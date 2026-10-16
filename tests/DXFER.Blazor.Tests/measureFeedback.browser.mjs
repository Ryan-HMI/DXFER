import assert from 'node:assert/strict';
import {mkdir,readFile} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
await mkdir('/tmp/dxfer-measure-evidence',{recursive:true});
const dxf=[0,'SECTION',2,'HEADER',9,'$INSUNITS',70,1,0,'ENDSEC',0,'SECTION',2,'ENTITIES',
 0,'POINT',8,'0',10,0,20,0,30,0,0,'POINT',8,'0',10,3,20,4,30,0,
 0,'LINE',8,'0',10,10,20,0,30,0,11,40,21,0,31,0,
 0,'LINE',8,'0',10,10,20,5,30,0,11,20,21,5,31,0,
 0,'CIRCLE',8,'0',10,30,20,8,30,0,40,3,0,'ENDSEC',0,'EOF'].join('\n')+'\n';
const native=text=>{let h='';const l=text.trim().split(/\r?\n/);for(let i=0;i<l.length;i+=2)if(l[i].trim()==='310')h+=l[i+1].trim();return JSON.parse(Buffer.from(h,'hex'));};
const browser=await chromium.launch({headless:true});
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(url);await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
 const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
 const canvas=page.locator('canvas.drawing-canvas'),feedback=page.getByRole('status',{name:'Viewport measurement'});
 const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(200);};
 const download=async()=>{const pending=page.waitForEvent('download');await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool('Download DXF');return native(await readFile(await(await pending).path(),'utf8'));};
 await page.locator('input[type=file]').setInputFiles({name:'measure.dxf',mimeType:'application/dxf',buffer:Buffer.from(dxf)});await expect(canvas).toHaveAttribute('data-entity-count','5');await tool('Fit extents');const before=await download();
 await tool('Measure');await tool('Fit extents');
 const screen=p=>canvas.evaluate((c,p)=>{const r=c.getBoundingClientRect(),d=c.dataset;return{x:r.left+p[0]*+d.scale+ +d.offsetX,y:r.top-p[1]*+d.scale+ +d.offsetY};},p);
 const pick=async p=>{const s=await screen(p);await page.mouse.click(s.x,s.y);await page.waitForTimeout(250);};
 await pick([0,0]);const second=await screen([3,4]);await page.mouse.move(second.x,second.y);
 await expect(feedback).toContainText('Delta X3 in');await expect(feedback).toContainText('Delta Y4 in');await expect(feedback).toContainText('Shortest5 in');
 await pick([3,4]);await page.mouse.move(10,10);await expect(feedback).toContainText('Shortest5 in');
 await page.screenshot({path:'/tmp/dxfer-measure-evidence/points.png'});
 const b=await canvas.boundingBox();await page.mouse.click(b.x+b.width*.6,b.y+40);await expect(feedback).not.toBeVisible();
 await pick([12,0]);await pick([12,5]);await page.mouse.move(10,10);await expect(feedback).toContainText('Shortest5 in');await expect(feedback).toContainText('Delta X0 in');
 await page.screenshot({path:'/tmp/dxfer-measure-evidence/parallel-lines.png'});
 await page.mouse.click(b.x+b.width*.6,b.y+40);await pick([12,0]);await pick([30+3/Math.sqrt(2),8+3/Math.sqrt(2)]);await page.mouse.move(10,10);
 await expect(feedback).toContainText('Shortest5 in');await expect(feedback).toContainText('Delta Y5 in');
 await page.screenshot({path:'/tmp/dxfer-measure-evidence/line-circle.png'});
 const p=await feedback.boundingBox();assert.ok(p.x>=b.x&&p.x+p.width<=b.x+b.width+1&&p.y+p.height<=b.y+b.height+1);
 await canvas.focus();await page.keyboard.press('Escape');await expect(feedback).not.toBeVisible();
 await expect(page.getByRole('button',{name:'Undo',exact:true,includeHidden:true})).toBeDisabled();
 const after=await download();assert.deepEqual(after.entities,before.entities);assert.deepEqual(after.dimensions,before.dimensions);assert.deepEqual(after.constraints,before.constraints);
 assert.deepEqual(errors,[]);console.log('PASS point hover/locked deltas, parallel-line and circle boundary shortest distances, viewport placement, exit, unchanged native geometry/dimensions/constraints/history. Build:',build);
}finally{await browser.close();}
