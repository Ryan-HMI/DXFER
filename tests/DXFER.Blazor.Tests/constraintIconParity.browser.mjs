import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output='/tmp/dxfer-constraint-icon-evidence';await mkdir(output,{recursive:true});
const native=text=>{const lines=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<lines.length;i+=2)if(lines[i].trim()==='310')hex+=lines[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const source=await readFile(new URL('./fixtures/drag-matrix/tangent-slide-rectangle.dxf',import.meta.url),'utf8');
const fixture=native(source);
fixture.entities.push({id:'middle',kind:'point',isConstruction:false,location:{x:10,y:0}},{id:'inner',kind:'circle',isConstruction:false,center:{x:10,y:17.5},radius:3});
for(const [kind,keys] of [['Midpoint',['middle','edge0']],['Concentric',['inner','circle']],['Equal',['edge0','edge2']],['Vertical',['edge1']],['Fix',['frame-low']]])
 fixture.constraints.push({id:kind,kind,referenceKeys:keys,state:'Satisfied'});
const bytes=Buffer.from(JSON.stringify(fixture)),chunks=bytes.toString('hex').match(/.{1,240}/g);
const dxf=source.replace(/(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/,'$1'+createHash('sha256').update(bytes).digest('hex').toUpperCase())
 .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/,chunks.map(c=>`310\n${c}\n`).join(''));
const browser=await chromium.launch({headless:true});
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
 // Observe the actual Path2D strokes on the app canvas, not a second renderer.
 await page.addInitScript(()=>{
  const paths=new WeakMap(),NativePath=window.Path2D;
  window.Path2D=class extends NativePath{constructor(...args){super(...args);if(typeof args[0]==='string')paths.set(this,args[0]);}};
  window.iconStrokes=[];
  for(const op of ['stroke','fill']){
   const original=CanvasRenderingContext2D.prototype[op];
   CanvasRenderingContext2D.prototype[op]=function(...args){
    if(this.canvas.classList.contains('drawing-canvas')&&paths.has(args[0])){
     const t=this.getTransform();window.iconStrokes.push({op,d:paths.get(args[0]),scale:t.a,color:this.strokeStyle});
     if(window.iconStrokes.length>4000)window.iconStrokes.splice(0,2000);
    }
    return original.apply(this,args);
   };
  }
 });
 const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
 await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
 const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
 const canvas=page.locator('canvas.drawing-canvas');
 const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(200);};
 const download=async()=>{const pending=page.waitForEvent('download');await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool('Download DXF');return native(await readFile(await(await pending).path(),'utf8'));};
 await page.locator('input[type=file]').setInputFiles({name:'constraint-symbols.dxf',mimeType:'application/dxf',buffer:Buffer.from(dxf)});
 await expect(canvas).toHaveAttribute('data-entity-count','9');await tool('Fit extents');
 const before=await download();await tool('Show constraints');await page.mouse.move(10,10);
 const names=['Coincident','Concentric','Parallel','Tangent','Horizontal','Vertical','Perpendicular','Equal','Midpoint','Fix'];
 for(const name of names){
  const paths=await page.getByRole('button',{name,exact:true}).locator('svg path').evaluateAll(nodes=>nodes.map(n=>({d:n.getAttribute('d'),op:n.getAttribute('fill')==='currentColor'?'fill':'stroke'})));
  assert.ok(paths.length,name);
  for(const path of paths)await page.waitForFunction(p=>window.iconStrokes.some(s=>s.d===p.d&&s.op===p.op&&s.scale===.5),path);
 }
 const mid=page.getByRole('button',{name:'Midpoint',exact:true}).locator('svg path');await expect(mid).toHaveCount(1);
 const values=(await mid.getAttribute('d')).match(/-?\d+(?:\.\d+)?/g).map(Number);
 const [x,y,right,bottom,left]=values;assert.ok(Math.abs(Math.hypot(right-x,bottom-y)-(right-left))<1e-8);
 await expect(page.getByRole('button',{name:'Coincident',exact:true}).locator('path[fill=currentColor]')).toHaveCount(1);
 await page.screenshot({path:`${output}/installed-icons.png`});
 await expect(page.getByRole('button',{name:'Undo',exact:true,includeHidden:true})).toBeDisabled();
 const after=await download();assert.deepEqual(after.entities,before.entities);assert.deepEqual(after.constraints,before.constraints);assert.deepEqual(after.dimensions,before.dimensions);
 assert.deepEqual(errors,[]);console.log('PASS all ten toolbar/canvas paths identical including filled details, equilateral Midpoint, heavy Coincident dot, visible badges and unchanged native document/history. Build:',build);
}finally{await browser.close();}
