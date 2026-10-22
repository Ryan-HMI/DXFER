import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url && ['127.0.0.1','localhost','[::1]','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-radial-pattern-center';
await mkdir(output,{recursive:true});
const fixture='0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
const near=(a,b,name)=>assert.ok(Math.abs(a-b)<1e-6,`${name}: ${a} != ${b}`);
const content=doc=>({entities:doc.entities,constraints:doc.constraints,dimensions:doc.dimensions,patterns:doc.metadata.patterns});
const decode=text=>{const lines=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<lines.length;i+=2)if(lines[i].trim()==='310')hex+=lines[i+1].trim();assert.ok(hex);return JSON.parse(Buffer.from(hex,'hex'));};
const results={passed:false,cases:[],drags:[],errors:[]};
const browser=await chromium.launch({headless:true,executablePath:process.env.DXFER_CHROMIUM});
let page;
try {
  page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);page.on('dialog',d=>d.accept());
  page.on('pageerror',e=>results.errors.push(e.message));
  page.on('console',m=>{if(m.type()==='error'&&/unhandled|circuit|exception/i.test(m.text()))results.errors.push(m.text());});
  await page.goto(url);await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  results.build=await page.locator('.app-brand').getAttribute('title');console.log(results.build);
  if(process.env.DXFER_EXPECTED_REVISION)assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas=page.locator('canvas.drawing-canvas');
  const widget=page.getByRole('region',{name:'Viewport pattern parameters',exact:true});
  const editor=page.getByRole('region',{name:'Pattern settings',exact:true});
  const handle=widget.locator('[data-pattern-center-handle]');
  const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
  const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');await writeFile(`${output}/${name}.dxf`,text);return {text,doc:decode(text)};};
  const screen=point=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),point);
  const clickWorld=async(point,shift=false)=>{const p=await screen(point);await page.mouse.move(p.x,p.y);if(shift)await page.keyboard.down('Shift');try{await page.mouse.click(p.x,p.y);}finally{if(shift)await page.keyboard.up('Shift');}await page.waitForTimeout(150);};
  const load=async(text,count)=>{await canvas.press('Escape');await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');await page.locator('input[type=file]').setInputFiles({name:'radial-center.dxf',mimeType:'application/dxf',buffer:Buffer.from(text)});await expect(canvas).toHaveAttribute('data-entity-count',String(count));await tool('Fit extents');await canvas.press('Escape');
    let previous,stable=0;await expect.poll(async()=>{const view=await canvas.evaluate(c=>JSON.stringify([c.dataset.scale,c.dataset.offsetX,c.dataset.offsetY,c.getBoundingClientRect().toJSON()]));stable=view===previous?stable+1:0;previous=view;return stable;},{intervals:[100,150,150]}).toBeGreaterThanOrEqual(2);
  };
  const showControls=async doc=>{
    if(await handle.isVisible())return;
    const p=doc.metadata.patterns[0];
    const at=await canvas.evaluate((c,entities)=>{
      const r=c.getBoundingClientRect(),s=+c.dataset.scale;
      for(const e of entities)for(let i=0;i<16;i++){
        const a=.23+i*Math.PI/8,x=r.left+(e.center.x+e.radius*Math.cos(a))*s+ +c.dataset.offsetX,
          y=r.top-(e.center.y+e.radius*Math.sin(a))*s+ +c.dataset.offsetY;
        if(x>r.left+8&&x<r.right-8&&y>r.top+8&&y<r.bottom-8&&document.elementFromPoint(x,y)===c)return {x,y,id:e.id};
      }throw new Error('No unobscured pattern circumference');
    },p.sourceEntityIds.map(id=>doc.entities.find(e=>e.id===id)));
    await page.mouse.move(at.x+10,at.y+10);await page.mouse.move(at.x,at.y);
    await expect.poll(async()=>(await canvas.getAttribute('data-hovered-id')??'').startsWith(at.id)).toBe(true);
    await page.mouse.click(at.x,at.y);
    await expect(handle).toBeVisible();
  };
  const centerPoint=doc=>{
    const pattern=doc.metadata.patterns[0];assert.ok(pattern.centerPointId,'exposed center has a persisted point owner');
    const point=doc.entities.find(e=>e.id===pattern.centerPointId);
    assert.equal(point?.kind,'point');assert.equal(point.isConstruction,true,'center is construction geometry');
    near(point.location.x,pattern.center.x,'point/pattern center X');near(point.location.y,pattern.center.y,'point/pattern center Y');
    return point;
  };
  const assertPattern=(doc,sourceBefore)=>{
    const pattern=doc.metadata.patterns[0],point=centerPoint(doc);
    const source=doc.entities.find(e=>e.id===pattern.sourceEntityIds[0]);
    assert.deepEqual(source,sourceBefore,'center intent never rewrites or moves source circle');
    assert.equal(pattern.members.length,pattern.count-1);
    for(const m of pattern.members){const entity=doc.entities.find(e=>e.id===m.entityId);assert.equal(entity?.kind,'circle');
      const a=m.column*(pattern.fullCircle?360/pattern.count:pattern.stepAngleDegrees)*Math.PI/180;
      const dx=source.center.x-point.location.x,dy=source.center.y-point.location.y;
      near(entity.center.x,point.location.x+dx*Math.cos(a)-dy*Math.sin(a),'simultaneous member X');
      near(entity.center.y,point.location.y+dx*Math.sin(a)+dy*Math.cos(a),'simultaneous member Y');near(entity.radius,source.radius,'member radius');}
  };
  const historyRoundtrip=async(before,after,name)=>{
    await tool('Undo');assert.deepEqual(content((await download(`${name}-undo`)).doc),content(before.doc));
    await tool('Redo');assert.deepEqual(content((await download(`${name}-redo`)).doc),content(after.doc));
    await load(after.text,after.doc.entities.length);assert.deepEqual(content((await download(`${name}-reopen`)).doc),content(after.doc));
  };
  await load(fixture,1);await clickWorld({x:10.6,y:10.8});await tool('Fix');await canvas.press('Escape');
  const seed=await download('source-before-pattern');
  assert.ok(seed.doc.constraints.some(c=>c.kind==='Fix'&&c.state==='Satisfied'&&c.referenceKeys.includes(seed.doc.entities[0].id)),
    'whole source circle is explicitly Fixed before pattern creation');
  await clickWorld({x:10.6,y:10.8});await tool('Radial pattern');
  const count=widget.getByRole('spinbutton',{name:'Viewport pattern instances',exact:true});await count.fill('6');await count.press('Enter');
  for(const [axis,value] of [['X','2'],['Y','3']]){const input=editor.getByRole('spinbutton',{name:`Pattern center ${axis}`,exact:true});await input.fill(value);await input.press('Tab');await expect(widget).toHaveAttribute(`data-center-${axis.toLowerCase()}`,value);}
  await expect(handle).toHaveAccessibleName('Move pattern center');
  await expect(handle).toHaveAttribute('title','Select or drag pattern center');
  await handle.click();await expect(canvas).toHaveAttribute('data-pattern-editing','true');
  await expect(page.locator('.dxfer-command-bar')).toContainText('Confirm the pattern before constraining its center.');
  assert.deepEqual(content((await download('draft-center-click')).doc),content(seed.doc),'draft center click creates no persistent point or geometry');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();await expect(canvas).toHaveAttribute('data-pattern-editing','false');
  const created=await download('created');const source=created.doc.entities.find(e=>e.id===created.doc.metadata.patterns[0].sourceEntityIds[0]);
  assert.ok(!created.doc.metadata.patterns[0].centerPointId,'Confirm alone does not eagerly create a center point');
  assert.equal(created.doc.entities.length,6,'only the source and five radial members exist before exposure');
  // First committed click lazily exposes a point without starting a drag draft.
  await showControls(created.doc);await handle.click();await expect(canvas).toHaveAttribute('data-pattern-editing','false');
  const exposed=await download('center-exposed');const point=centerPoint(exposed.doc);assertPattern(exposed.doc,source);
  assert.equal(exposed.doc.entities.length,created.doc.entities.length+1,'first click adds exactly one construction point');
  assert.deepEqual(exposed.doc.entities.filter(e=>e.id!==point.id),created.doc.entities,'exposure preserves existing circles exactly');
  const hasPointRef=key=>key===point.id||key.startsWith(`${point.id}:`)||key.startsWith(`${point.id}|`);
  await expect.poll(async()=>(await canvas.getAttribute('data-selected-keys')).split(',').some(hasPointRef),{message:'first click selects the exposed real point after render'}).toBe(true);
  for(const commit of [false,true]){
    const name=`free-center-drag-${commit?'confirm':'cancel'}`;
    await load(exposed.text,exposed.doc.entities.length);await showControls(exposed.doc);
    const startBox=await handle.boundingBox(),scale=Number(await canvas.getAttribute('data-scale'));
    const grab={x:startBox.x+startBox.width/2+2,y:startBox.y+startBox.height/2-1};
    const delta={x:1.25,y:-.75},original=exposed.doc.metadata.patterns[0].center;
    const initialScreen=await screen(original),grabOffset={x:(grab.x-initialScreen.x)/scale,y:-(grab.y-initialScreen.y)/scale};
    const camera=()=>canvas.evaluate(c=>({scale:+c.dataset.scale,offsetX:+c.dataset.offsetX,offsetY:+c.dataset.offsetY}));
    const beforeCamera=await camera();
    await page.mouse.move(grab.x,grab.y);await page.mouse.down();
    try{
      await page.mouse.move(grab.x+4,grab.y);
      await expect(canvas).toHaveAttribute('data-pattern-editing','true');
      await expect(widget).toHaveAttribute('data-center-dragging','true');
      let previous,stable=0;await expect.poll(async()=>{const current=JSON.stringify(await camera());stable=current===previous?stable+1:0;previous=current;return stable;},{intervals:[100,150,150]}).toBeGreaterThanOrEqual(2);
      for(const fraction of [.35,.7,1]){
        const targetScreen=await screen({x:original.x+delta.x*fraction+grabOffset.x,y:original.y+delta.y*fraction+grabOffset.y});
        await page.mouse.move(targetScreen.x,targetScreen.y,{steps:6});
        await expect(canvas).toHaveAttribute('data-pattern-editing','true');
        await expect(widget).toHaveAttribute('data-center-dragging','true');
        await expect(widget.getByRole('button',{name:'Confirm',exact:true})).toBeDisabled();
        try{
          await expect.poll(async()=>Math.hypot(Number(await widget.getAttribute('data-center-x'))-original.x-delta.x*fraction,
            Number(await widget.getAttribute('data-center-y'))-original.y-delta.y*fraction),{message:'held center coordinates update continuously'}).toBeLessThan(1e-6);
        }finally{
          results.drags.push({name,fraction,grab,beforeCamera,camera:await camera(),expected:{x:original.x+delta.x*fraction,y:original.y+delta.y*fraction},
            actual:{x:Number(await widget.getAttribute('data-center-x')),y:Number(await widget.getAttribute('data-center-y'))}});
        }
        near(Number(await widget.getAttribute('data-source-x')),source.center.x,'held source X');
        near(Number(await widget.getAttribute('data-source-y')),source.center.y,'held source Y');
      }
      await page.screenshot({path:`${output}/${name}-held.png`});
    }finally{await page.mouse.up();}
    await expect(widget).not.toHaveAttribute('data-center-dragging','true');
    await expect(canvas).toHaveAttribute('data-pattern-editing','true');
    await expect(widget.getByRole('button',{name:'Confirm',exact:true})).toBeEnabled();
    await expect(page.locator('.dxfer-command-bar')).not.toContainText('Confirm the pattern before constraining its center.');
    assert.deepEqual(content((await download(`${name}-staged`)).doc),content(exposed.doc),'drag release stages only and must not also expose/select a new point');
    if(commit){
      await widget.getByRole('button',{name:'Confirm',exact:true}).click();await expect(canvas).toHaveAttribute('data-pattern-editing','false');
      const changed=await download(name);assertPattern(changed.doc,source);
      const center=centerPoint(changed.doc);assert.equal(center.id,point.id,'drag preserves exposed center identity');
      near(center.location.x,original.x+delta.x,'committed drag X');near(center.location.y,original.y+delta.y,'committed drag Y');
      await historyRoundtrip(exposed,changed,name);
    }else{
      await widget.getByRole('button',{name:'Cancel pattern changes',exact:true}).click();await expect(canvas).toHaveAttribute('data-pattern-editing','false');
      assert.deepEqual(content((await download(name)).doc),content(exposed.doc));
      await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
      await expect(page.getByRole('button',{name:'Redo',exact:true})).toBeDisabled();
    }
    results.cases.push(name);
  }
  for(const command of ['Coincident','Horizontal'])for(const workflow of ['selection-first','tool-first'])for(const originFirst of [false,true]){
    const name=`${command.toLowerCase()}-${workflow}-${originFirst?'origin-first':'center-first'}`;
    await load(exposed.text,exposed.doc.entities.length);await showControls(exposed.doc);
    if(workflow==='tool-first'){await tool(command);await expect(handle,'normal tool activation retains the center handle').toBeVisible();}
    const picks=originFirst?['origin','center']:['center','origin'];
    for(const [index,pick] of picks.entries()){
      const additive=workflow==='selection-first'&&index===1;
      if(pick==='center')await handle.click({modifiers:additive?['Shift']:[]});
      else await clickWorld({x:0,y:0},additive);
      await expect(canvas).toHaveAttribute('data-pattern-editing','false');
      if(index===0||workflow==='selection-first'){
        const matches=pick==='center'?hasPointRef:key=>key==='$origin:center';
        await expect.poll(async()=>(await canvas.getAttribute('data-selected-keys')).split(',').some(matches),{message:`${name}: ${pick} selection completes after render`}).toBe(true);
      }
    }
    if(workflow==='selection-first'){await expect(canvas).toHaveAttribute('data-selected-count','2');await tool(command);}
    await expect.poll(async()=>{const y=await widget.getAttribute('data-center-y');return y===null?Infinity:Math.abs(Number(y));},{message:`${name}: solved center is rendered before export`}).toBeLessThan(1e-6);
    await canvas.press('Escape');const changed=await download(name);assertPattern(changed.doc,source);
    const added=changed.doc.constraints.filter(c=>!exposed.doc.constraints.some(old=>old.id===c.id));
    assert.equal(added.length,1);assert.equal(added[0].kind,command);assert.equal(added[0].state,'Satisfied');
    assert.ok(added[0].referenceKeys.includes('$origin:center')&&added[0].referenceKeys.some(hasPointRef));
    const center=centerPoint(changed.doc).location;near(center.y,0,'origin alignment Y');if(command==='Coincident')near(center.x,0,'origin alignment X');
    await page.screenshot({path:`${output}/${name}.png`});await historyRoundtrip(exposed,changed,name);results.cases.push(name);
  }
  await load(exposed.text,exposed.doc.entities.length);await showControls(exposed.doc);await tool('Dimension');await handle.click();
  await expect.poll(async()=>(await canvas.getAttribute('data-dimension-draft-keys')??'').split(',').some(hasPointRef),{message:'center enters dimension draft after render'}).toBe(true);
  await clickWorld({x:0,y:0});
  await expect(canvas).toHaveAttribute('data-dimension-draft-complete','true');await clickWorld({x:4,y:1});await canvas.press('Escape');
  const placed=await download('center-dimension-placed');const dimension=placed.doc.dimensions.find(d=>!exposed.doc.dimensions.some(old=>old.id===d.id));
  assert.ok(dimension?.referenceKeys.includes('$origin:center')&&dimension.referenceKeys.some(hasPointRef));
  const editDimension=async(value,name)=>{
    const before=await download(`${name}-before`),input=page.locator(`input[data-dimension-id="${dimension.id}"]`);
    await input.dblclick();await input.fill(String(value));await input.press('Enter');await expect(input).toHaveAttribute('data-dimension-value',String(value));await canvas.press('Escape');
    const changed=await download(name);assertPattern(changed.doc,source);const d=changed.doc.dimensions.find(d=>d.id===dimension.id);
    assert.equal(d.isDriving,true);near(d.value,value,'driving center position');const center=centerPoint(changed.doc).location;
    if(d.kind==='LinearDistance')near(Math.hypot(center.x,center.y),value,'center distance to origin');
    else if(d.kind==='HorizontalDistance')near(Math.abs(center.x),value,'center X distance');
    else if(d.kind==='VerticalDistance')near(Math.abs(center.y),value,'center Y distance');
    else assert.fail(`Unexpected center-position dimension: ${d.kind}`);
    await historyRoundtrip(before,changed,name);results.cases.push(name);
  };
  await editDimension(5,'center-driving-five');await editDimension(6,'reopened-center-driving-six');
  await load(exposed.text,exposed.doc.entities.length);await showControls(exposed.doc);await handle.click();
  await expect.poll(async()=>(await canvas.getAttribute('data-selected-keys')).split(',').some(hasPointRef),{message:'center selected before Fix'}).toBe(true);
  await tool('Fix');await canvas.press('Escape');
  const fixed=await download('center-fixed');assert.ok(fixed.doc.constraints.some(c=>c.kind==='Fix'&&c.state==='Satisfied'&&c.referenceKeys.some(hasPointRef)));
  await showControls(fixed.doc);const box=await handle.boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();
  try{await page.mouse.move(box.x+box.width/2+65,box.y+box.height/2-45,{steps:12});}finally{await page.mouse.up();}
  if(await widget.getByRole('button',{name:'Confirm',exact:true}).count()){
    const confirm=widget.getByRole('button',{name:'Confirm',exact:true});if(await confirm.isEnabled())await confirm.click();else await widget.getByRole('button',{name:'Cancel pattern changes',exact:true}).click();
  }
  await canvas.press('Escape');assert.deepEqual(content((await download('fixed-center-drag-refused')).doc),content(fixed.doc),'incompatible center drag is atomic and preserves Fixed geometry');
  await tool('Undo');assert.deepEqual(content((await download('fixed-center-undo')).doc),content(exposed.doc),'refused drag adds no history entry');
  results.cases.push('fixed-center-drag-refused');await page.screenshot({path:`${output}/fixed-center-drag-refused.png`});
  assert.deepEqual(results.errors,[]);results.passed=true;console.log('PASS radial center constraints, driving position, pattern equations, native/history and Fixed refusal');
}catch(error){results.failure=error.stack;await page?.screenshot({path:`${output}/failure.png`}).catch(()=>{});throw error;}
finally{await writeFile(`${output}/results.json`,JSON.stringify(results,null,2));await browser.close();}
