// Observes actual canvas draw calls and real pointer input; never substitutes solver/interop results.
import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL;
assert.ok(url&&['127.0.0.1','localhost','[::1]','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS??'/tmp/dxfer-pattern-center-latency';await mkdir(output,{recursive:true});
const fixedCenter=process.env.DXFER_LATENCY_FIXED_CENTER==='1';
const evidence={completed:false,errors:[],runs:[],lagBudgetMs:Number(process.env.DXFER_PREVIEW_LAG_BUDGET_MS??50),serverFrameDelayMs:Number(process.env.DXFER_SERVER_FRAME_DELAY_MS??0),delayedFrames:0,delayedHttpPolls:0};
const fixture='0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
const native=text=>{const lines=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<lines.length;i+=2)if(lines[i].trim()==='310')hex+=lines[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const content=d=>({entities:d.entities,constraints:d.constraints,dimensions:d.dimensions,patterns:d.metadata.patterns});
const percentile=(values,p)=>{const sorted=[...values].sort((a,b)=>a-b);return sorted[Math.min(sorted.length-1,Math.floor(sorted.length*p))];};
const browser=await chromium.launch({headless:true,executablePath:process.env.DXFER_CHROMIUM});let page;
try{
  page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(12000);page.on('dialog',d=>d.accept());page.on('pageerror',e=>evidence.errors.push(e.message));
  if(evidence.serverFrameDelayMs>0){
    await page.route(/\/_blazor(?:\?|$)/,async route=>{
      if(route.request().method()!=='GET')return route.continue();
      const response=await route.fetch();evidence.delayedHttpPolls++;
      await new Promise(resolve=>setTimeout(resolve,evidence.serverFrameDelayMs));await route.fulfill({response});
    });
    await page.routeWebSocket(/\/_blazor(?:\?|$)/,socket=>{
    const server=socket.connectToServer();let closed=false;
    server.onMessage(message=>{evidence.delayedFrames++;setTimeout(()=>{if(!closed)socket.send(message);},evidence.serverFrameDelayMs);});
    socket.onClose(()=>{closed=true;server.close();});server.onClose((code,reason)=>{closed=true;socket.close({code,reason});});
    });
  }
  await page.addInitScript(()=>{
    const probe=window.__patternLatency={enabled:false,frame:null,frames:0,pointers:[],samples:[]};
    const clear=CanvasRenderingContext2D.prototype.clearRect,arc=CanvasRenderingContext2D.prototype.arc;
    CanvasRenderingContext2D.prototype.clearRect=function(...args){
      if(probe.enabled&&this.canvas.matches('canvas.drawing-canvas')){probe.frame={t:performance.now(),arcs:[]};probe.frames++;}
      return clear.apply(this,args);
    };
    CanvasRenderingContext2D.prototype.arc=function(...args){
      if(probe.enabled&&probe.frame&&this.canvas.matches('canvas.drawing-canvas')&&Math.abs(Math.abs(args[4]-args[3])-2*Math.PI)<.001)
        probe.frame.arcs.push({x:args[0],y:args[1],radius:args[2]});
      return arc.apply(this,args);
    };
  });
  await page.goto(url);await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  evidence.build=await page.locator('.app-brand').getAttribute('title');console.log(evidence.build);
  if(process.env.DXFER_EXPECTED_REVISION)assert.ok(evidence.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas=page.locator('canvas.drawing-canvas'),widget=page.getByRole('region',{name:'Viewport pattern parameters'});
  const handle=widget.locator('[data-pattern-center-handle]'),editor=page.getByRole('region',{name:'Pattern settings',exact:true});
  const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
  const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');await writeFile(`${output}/${name}.dxf`,text);return native(text);};
  const screen=p=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
  const hiddenControls=async()=>{
    await expect(widget).toHaveCount(0);await expect(editor).toBeHidden();
    await expect(page.locator('[data-pattern-center-handle]')).toHaveCount(0);
    await expect(canvas).toHaveAttribute('data-pattern-editing','false');
  };
  const showControls=async doc=>{
    await hiddenControls();
    const pattern=doc.metadata.patterns[0],ids=[...pattern.sourceEntityIds,...pattern.members.map(m=>m.entityId)];
    const at=await canvas.evaluate((c,entities)=>{
      const r=c.getBoundingClientRect(),s=+c.dataset.scale;
      for(const e of entities)for(let i=0;i<16;i++){
        const a=.23+i*Math.PI/8,x=r.left+(e.center.x+e.radius*Math.cos(a))*s+ +c.dataset.offsetX,
          y=r.top-(e.center.y+e.radius*Math.sin(a))*s+ +c.dataset.offsetY;
        if(x>r.left+8&&x<r.right-8&&y>r.top+8&&y<r.bottom-8&&document.elementFromPoint(x,y)===c)return {x,y,id:e.id};
      }throw new Error('No unobscured pattern circumference for Show pattern controls');
    },ids.map(id=>doc.entities.find(e=>e.id===id)));
    await page.mouse.move(at.x+10,at.y+10);await page.mouse.move(at.x,at.y);
    await expect.poll(async()=>(await canvas.getAttribute('data-hovered-id')??'').startsWith(at.id)).toBe(true);
    await page.mouse.click(at.x,at.y,{button:'right'});
    const context=page.getByRole('dialog',{name:'Drawing context commands'});
    await expect(context).toBeVisible();
    await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
    await expect(context).toBeHidden();await expect(handle).toBeVisible();
  };
  if(process.env.DXFER_PATTERN_LATENCY_FIXTURE){
    const text=await readFile(process.env.DXFER_PATTERN_LATENCY_FIXTURE,'utf8'),doc=native(text);
    await page.locator('input[type=file]').setInputFiles({name:'latency-pattern.dxf',mimeType:'application/dxf',buffer:Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count',String(doc.entities.length));await tool('Fit extents');
    await page.getByRole('region',{name:'Patterns',exact:true}).getByRole('button',{name:'Radial (6)',exact:true}).click();
    await expect(editor).toBeVisible();await widget.getByRole('button',{name:'Cancel pattern changes',exact:true}).click();
    await hiddenControls();await showControls(doc);
  }else{
  await page.locator('input[type=file]').setInputFiles({name:'latency-circle.dxf',mimeType:'application/dxf',buffer:Buffer.from(fixture)});
  await expect(canvas).toHaveAttribute('data-entity-count','1');await tool('Fit extents');await canvas.press('Escape');
  let p=await screen({x:10.6,y:10.8});await page.mouse.click(p.x,p.y);await expect(canvas).toHaveAttribute('data-selected-count','1');await tool('Fix');
  assert.ok((await download('fixed-source')).constraints.some(c=>c.kind==='Fix'&&c.state==='Satisfied'));
  await canvas.press('Escape');await expect(canvas).toHaveAttribute('data-selected-count','0');
  p=await screen({x:10.6,y:10.8});await page.mouse.click(p.x,p.y);await expect(canvas).toHaveAttribute('data-selected-count','1');await tool('Radial pattern');
  for(const [axis,value]of[['X','2'],['Y','3']]){const input=editor.getByRole('spinbutton',{name:`Pattern center ${axis}`,exact:true});await input.fill(value);await input.press('Tab');await expect(widget).toHaveAttribute(`data-center-${axis.toLowerCase()}`,value);}
  await expect(widget.getByRole('spinbutton',{name:'Viewport pattern instances',exact:true})).toHaveValue('6');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();await expect(canvas).toHaveAttribute('data-pattern-editing','false');
  await hiddenControls();await showControls(await download('created-pattern'));
  await handle.click();await expect(canvas).toHaveAttribute('data-selected-count','1');
  }
  const baseline=await download('baseline');
  const pattern=baseline.metadata.patterns[0];assert.ok(pattern.centerPointId);const source=baseline.entities.find(e=>e.id===pattern.sourceEntityIds[0]);
  if(fixedCenter)assert.ok(baseline.constraints.some(c=>c.kind==='Fix'&&c.referenceKeys.some(key=>key===pattern.centerPointId||key.startsWith(`${pattern.centerPointId}:`))),'negative fixture fixes the actual center point');
  const opposite=baseline.entities.find(e=>e.id===pattern.members.find(m=>m.column===3).entityId);assert.equal(opposite.kind,'circle');
  for(let run=1;run<=2;run++){
    if(run>1)await showControls(baseline);
    await expect(canvas).toHaveAttribute('data-pattern-editing','false');
    await expect.poll(async()=>{const box=await handle.boundingBox(),p=await screen(pattern.center);return Math.hypot(box.x+box.width/2-p.x,box.y+box.height/2-p.y);},{message:'committed center handle has settled before the next gesture'}).toBeLessThan(1);
    const beforeCamera=await canvas.evaluate(c=>({scale:+c.dataset.scale,x:+c.dataset.offsetX,y:+c.dataset.offsetY}));
    const initial=await screen(pattern.center),scale=Number(await canvas.getAttribute('data-scale'));
    const handleBox=await handle.boundingBox();
    const grab={x:handleBox.x+handleBox.width/2+2,y:handleBox.y+handleBox.height/2-1};
    const offset={x:(grab.x-initial.x)/scale,y:-(grab.y-initial.y)/scale};
    await canvas.evaluate(c=>{
      const q=window.__patternLatency;q.monitorCamera=true;q.cameraTrace=[];
      const sample=()=>{if(!q.monitorCamera)return;q.cameraTrace.push({t:performance.now(),scale:+c.dataset.scale,x:+c.dataset.offsetX,y:+c.dataset.offsetY});q.cameraRaf=requestAnimationFrame(sample);};
      sample();
    });
    await page.mouse.move(grab.x,grab.y);await page.mouse.down();
    try{
      await page.mouse.move(grab.x+4,grab.y);await expect(canvas).toHaveAttribute('data-pattern-editing','true');
      await expect(widget).toHaveAttribute('data-center-dragging','true');await page.waitForTimeout(600);
      await page.evaluate(({offset,source,opposite})=>{
        const q=window.__patternLatency,c=document.querySelector('canvas.drawing-canvas'),w=document.querySelector('.pattern-viewport-controls');
        q.enabled=true;q.frame=null;q.frames=0;q.pointers=[];q.samples=[];q.source=source;q.opposite=opposite;
        const world=p=>{const r=c.getBoundingClientRect(),s=+c.dataset.scale;return{x:(p.x-r.left- +c.dataset.offsetX)/s,y:-(p.y-r.top- +c.dataset.offsetY)/s};};
        q.listener=e=>{const p=world({x:e.clientX,y:e.clientY});q.pointers.push({t:performance.now(),x:p.x-offset.x,y:p.y-offset.y});};
        w.addEventListener('pointermove',q.listener,{capture:true});
        const sample=()=>{
          if(!q.enabled)return;
          if(q.frame&&q.pointers.length){
            const s=+c.dataset.scale,r=c.getBoundingClientRect(),h=w.querySelector('[data-pattern-center-handle]').getBoundingClientRect();
            const handle=world({x:h.x+h.width/2,y:h.y+h.height/2}),target=q.pointers.at(-1);
            const circles=q.frame.arcs.filter(a=>Math.abs(a.radius-source.radius*s)<.01).map(a=>({x:(a.x- +c.dataset.offsetX)/s,y:-(a.y- +c.dataset.offsetY)/s}));
            // For member 3 of six (180 degrees), member = 2*center - source.
            const candidates=circles.filter(a=>Math.hypot(a.x-opposite.center.x,a.y-opposite.center.y)<8);
            const member=candidates.sort((a,b)=>Math.hypot(a.x-(2*target.x-source.center.x),a.y-(2*target.y-source.center.y))-Math.hypot(b.x-(2*target.x-source.center.x),b.y-(2*target.y-source.center.y)))[0];
            q.samples.push({t:performance.now(),frameAt:q.frame.t,target,handle,server:{x:+w.dataset.centerX,y:+w.dataset.centerY},
              rendered:member?{x:(member.x+source.center.x)/2,y:(member.y+source.center.y)/2}:null,circleDraws:circles.length,scale:s,
              circles,camera:{scale:s,x:+c.dataset.offsetX,y:+c.dataset.offsetY}});
          }
          // Observe after the animation-frame draw callbacks, not before the
          // application's queued local-preview paint in the same frame.
          q.raf=requestAnimationFrame(()=>{q.sampleTimer=setTimeout(sample,0);});
        };q.raf=requestAnimationFrame(()=>{q.sampleTimer=setTimeout(sample,0);});
      },{offset,source,opposite});
      const begin=Date.now();
      for(let i=0;i<=120;i++){
        const f=i/120,target={x:pattern.center.x+3*f,y:pattern.center.y+.5*Math.sin(f*Math.PI*2)};
        const at=await screen({x:target.x+offset.x,y:target.y+offset.y});await page.mouse.move(at.x,at.y);
        if(i===60){
          const pixels=await canvas.evaluate(c=>{
            const q=window.__patternLatency,s=q.samples.at(-1),scale=+c.dataset.scale,ratio=c.width/c.clientWidth,ctx=c.getContext('2d');
            if(!s?.rendered)return null;
            const center={x:2*s.rendered.x-q.source.center.x,y:2*s.rendered.y-q.source.center.y};
            const ink=Array.from({length:8},(_,i)=>{
              const a=.23+i*Math.PI/4,x=Math.round(((center.x+q.source.radius*Math.cos(a))*scale+ +c.dataset.offsetX)*ratio),
                y=Math.round((-(center.y+q.source.radius*Math.sin(a))*scale+ +c.dataset.offsetY)*ratio);
              if(x<3||y<3||x+3>=c.width||y+3>=c.height)return false;
              const data=ctx.getImageData(x-3,y-3,7,7).data;let min=255,max=0;for(let j=0;j<data.length;j+=4){min=Math.min(min,data[j]);max=Math.max(max,data[j]);}return max-min>35;
            });return {center,ink};
          });
          assert.ok(pixels&&pixels.ink.filter(Boolean).length>=5,'observed member circumference is actually painted, not just a path or handle');
          await writeFile(`${output}/run-${run}-pixels.json`,JSON.stringify(pixels,null,2));
          await page.screenshot({path:`${output}/run-${run}-sustained.png`});
        }
        const delay=begin+i*30-Date.now();if(delay>0)await page.waitForTimeout(delay);
      }
      await page.waitForTimeout(500);
      const trace=await page.evaluate(()=>{const q=window.__patternLatency;q.enabled=false;cancelAnimationFrame(q.raf);clearTimeout(q.sampleTimer);document.querySelector('.pattern-viewport-controls').removeEventListener('pointermove',q.listener,true);return {pointers:q.pointers,samples:q.samples,frames:q.frames,cameraTrace:q.cameraTrace};});
      assert.ok(trace.pointers.length>=100&&trace.samples.length>=60,'sustained real pointer/frame coverage');
      const active=trace.samples.filter(s=>s.rendered&&s.t>=trace.pointers[3].t&&s.t<=trace.pointers.at(-1).t&&s.t-s.target.t<50);
      assert.ok(active.length>50,'actual opposite-member circle was observed');
      const lags=active.map(s=>{const prior=trace.pointers.filter(p=>p.t<=s.t);const nearest=prior.reduce((a,b)=>Math.hypot(a.x-s.rendered.x,a.y-s.rendered.y)<Math.hypot(b.x-s.rendered.x,b.y-s.rendered.y)?a:b);return s.t-nearest.t;});
      const handleErrors=active.map(s=>Math.hypot(s.handle.x-s.target.x,s.handle.y-s.target.y)*s.scale);
      const memberErrors=active.map(s=>2*Math.hypot(s.rendered.x-s.target.x,s.rendered.y-s.target.y)*s.scale);
      let updates=0,last;for(const s of active){if(!last||Math.hypot(s.rendered.x-last.x,s.rendered.y-last.y)>1e-5){updates++;last=s.rendered;}}
      const metrics={run,pointerEvents:trace.pointers.length,sampledFrames:active.length,geometryUpdates:updates,
        geometryUpdatesPerSecond:updates/((active.at(-1).t-active[0].t)/1000),lagP50Ms:percentile(lags,.5),lagP95Ms:percentile(lags,.95),lagMaxMs:Math.max(...lags),
        handleP95ErrorPx:percentile(handleErrors,.95),memberP95ErrorPx:percentile(memberErrors,.95),beforeCamera,handleBox,grab,
        cameraMovedDuringHold:trace.cameraTrace.some(camera=>Object.keys(beforeCamera).some(k=>Math.abs(camera[k]-beforeCamera[k])>1e-6))};
      if(fixedCenter){
        const expected=baseline.entities.filter(e=>e.kind==='circle');
        metrics.fixedCenter=true;
        metrics.maxFalseGeometryPx=Math.max(...trace.samples.flatMap(s=>s.circles.map(circle=>Math.min(...expected.map(e=>Math.hypot(circle.x-e.center.x,circle.y-e.center.y)))*s.scale)));
        assert.ok(metrics.maxFalseGeometryPx<.1,'constrained negative never paints a false moved copy');
      }
      evidence.runs.push(metrics);await writeFile(`${output}/run-${run}-trace.json`,JSON.stringify(trace));console.log(JSON.stringify(metrics));
    }finally{await page.mouse.up();await page.evaluate(()=>{const q=window.__patternLatency;q.enabled=false;q.monitorCamera=false;cancelAnimationFrame(q.cameraRaf);});}
    await expect(widget).not.toHaveAttribute('data-center-dragging','true');
    if(!fixedCenter){
      await expect(widget.getByRole('button',{name:'Confirm',exact:true})).toBeEnabled();
      await expect.poll(async()=>Number(await widget.getAttribute('data-center-x'))).toBeCloseTo(pattern.center.x+3,5);
    }
    assert.deepEqual(content(await download(`run-${run}-uncommitted`)),content(baseline));
    await widget.getByRole('button',{name:'Cancel pattern changes',exact:true}).click();
    await hiddenControls();
    assert.deepEqual(content(await download(`run-${run}-cancelled`)),content(baseline));
  }
  assert.deepEqual(evidence.errors,[]);evidence.completed=true;
  if(evidence.serverFrameDelayMs>0)assert.ok(evidence.delayedFrames+evidence.delayedHttpPolls>0,'real Blazor transport responses passed through the delay route');
  evidence.responsive=fixedCenter?evidence.runs.every(r=>r.maxFalseGeometryPx<.1):evidence.runs.every(r=>r.lagP95Ms<evidence.lagBudgetMs&&r.memberP95ErrorPx<1&&!r.cameraMovedDuringHold);
  if(process.env.DXFER_REQUIRE_RESPONSIVE==='1')assert.ok(evidence.responsive,`Rendered member p95 lag exceeds ${evidence.lagBudgetMs}ms; see trace`);
}catch(error){evidence.failure=error.stack;await page?.screenshot({path:`${output}/failure.png`}).catch(()=>{});throw error;}
finally{await writeFile(`${output}/results.json`,JSON.stringify(evidence,null,2));await browser.close();}
