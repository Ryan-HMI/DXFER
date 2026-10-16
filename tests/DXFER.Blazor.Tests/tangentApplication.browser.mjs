import assert from 'node:assert/strict';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output='/tmp/dxfer-tangent-application-evidence';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let h='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')h+=a[i+1].trim();return JSON.parse(Buffer.from(h,'hex'));};
const source=await readFile(new URL('./fixtures/drag-matrix/tangent-slide-rectangle.dxf',import.meta.url),'utf8');
const fixture=native(source);fixture.constraints=fixture.constraints.filter(c=>c.kind!=='Tangent');fixture.entities.find(e=>e.id==='circle').center.y=20.5;
const bytes=Buffer.from(JSON.stringify(fixture));
const dxf=source.replace(/(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/,'$1'+createHash('sha256').update(bytes).digest('hex').toUpperCase())
 .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/,bytes.toString('hex').match(/.{1,240}/g).map(c=>`310\n${c}\n`).join(''));
const close=(a,b)=>assert.ok(Math.abs(a-b)<1e-6,`${a} != ${b}`);
const browser=await chromium.launch({headless:true});
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
 await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
 const canvas=page.locator('canvas.drawing-canvas');const build=await page.locator('.app-brand').getAttribute('title');
 const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(180);};
 const download=async name=>{const pending=page.waitForEvent('download');await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool('Download DXF');const text=await readFile(await(await pending).path(),'utf8');await writeFile(`${output}/${name}.dxf`,text);return{text,doc:native(text)};};
 const screen=p=>canvas.evaluate((c,p)=>{const d=c.dataset,r=c.getBoundingClientRect();return{x:r.left+p[0]*+d.scale+ +d.offsetX,y:r.top-p[1]*+d.scale+ +d.offsetY};},p);
 const pick=async p=>{const s=await screen(p);await page.mouse.click(s.x,s.y);await page.waitForTimeout(200);};
 await page.locator('input[type=file]').setInputFiles({name:'tangent-application.dxf',mimeType:'application/dxf',buffer:Buffer.from(dxf)});await expect(canvas).toHaveAttribute('data-entity-count','7');await tool('Fit extents');
 const before=(await download('before')).doc;
 await tool('Tangent');await tool('Fit extents');await pick([14,10]);await pick([10+7.5/Math.sqrt(2),20.5+7.5/Math.sqrt(2)]);
 await canvas.focus();await page.keyboard.press('Escape');
 const applied=(await download('applied')).doc;const tangent=applied.constraints.find(c=>c.kind==='Tangent');assert.ok(tangent);assert.equal(tangent.state,'Satisfied');
 let circle=applied.entities.find(e=>e.id==='circle');close(circle.center.y,17.5);close(circle.radius,7.5);
 assert.deepEqual(applied.entities.filter(e=>e.id!=='circle'),before.entities.filter(e=>e.id!=='circle'));
 await tool('Undo');assert.deepEqual((await download('undo')).doc.entities,before.entities);await tool('Redo');assert.deepEqual((await download('redo')).doc.entities,applied.entities);
 const from=await screen([10,17.5]),to=await screen([13,19.5]);await page.mouse.move(from.x,from.y);await page.mouse.down();await page.mouse.move(to.x,to.y,{steps:12});await page.waitForTimeout(200);await page.screenshot({path:`${output}/held.png`});await page.mouse.up();await page.waitForTimeout(200);
 const saved=await download('released');circle=saved.doc.entities.find(e=>e.id==='circle');assert.ok(Math.abs(circle.center.x-13)<0.001,'mouse-selected horizontal movement');close(circle.center.y,17.5);close(circle.radius,7.5);assert.ok(saved.doc.constraints.every(c=>c.state==='Satisfied'));
 await page.locator('input[type=file]').setInputFiles({name:'reopened.dxf',mimeType:'application/dxf',buffer:Buffer.from(saved.text)});await page.waitForTimeout(300);
 const reopened=(await download('reopened')).doc;assert.deepEqual(reopened.entities,saved.doc.entities);assert.ok(reopened.constraints.every(c=>c.state==='Satisfied'));assert.deepEqual(errors,[]);
 console.log('PASS new Tangent application on diameter15 circle/dimensioned rectangle, unchanged peers, Undo/Redo, held drag/release and native reopen. Build:',build);
}finally{await browser.close();}
