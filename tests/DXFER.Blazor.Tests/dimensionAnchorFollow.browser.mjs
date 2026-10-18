import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dimension-anchor-follow';
await mkdir(output,{recursive:true});
const native = text => {
  const lines=text.trim().split(/\r?\n/); let hex='';
  for(let i=0;i<lines.length;i+=2) if(lines[i].trim()==='310') hex+=lines[i+1].trim();
  return JSON.parse(Buffer.from(hex,'hex'));
};
const browser=await chromium.launch({headless:true});
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  const canvas=page.locator('canvas.drawing-canvas');
  const label=page.locator('input[data-dimension-id="length"]');
  const tool=name=>page.getByRole('button',{name,exact:true}).click();
  const download=async name=>{
    const pending=page.waitForEvent('download'); await tool('Open application menu');
    await page.getByRole('button',{name:'File',exact:true}).hover(); await tool('Download DXF');
    const text=await readFile(await (await pending).path(),'utf8');
    if(name) await writeFile(`${output}/${name}.dxf`,text);
    return text;
  };
  const screen=p=>canvas.evaluate((c,p)=>{const b=c.getBoundingClientRect(); return {x:b.x+(+c.dataset.offsetX)+p.x*(+c.dataset.scale),y:b.y+(+c.dataset.offsetY)-p.y*(+c.dataset.scale)};},p);
  const labelWorld=()=>label.evaluate(e=>{const c=document.querySelector('canvas.drawing-canvas'); return {x:(parseFloat(e.style.left)-Number(c.dataset.offsetX))/Number(c.dataset.scale),y:(Number(c.dataset.offsetY)-parseFloat(e.style.top))/Number(c.dataset.scale)};});
  const near=(a,b,why)=>assert.ok(Math.hypot(a.x-b.x,a.y-b.y)<0.001,`${why}: ${JSON.stringify(a)} != ${JSON.stringify(b)}`);
  const visibleAt=async expected=>expect.poll(async()=>{const a=await labelWorld();return Math.hypot(a.x-expected.x,a.y-expected.y);}).toBeLessThan(0.001);
  await page.locator('input[type=file]').setInputFiles(new URL('./fixtures/corner-driving10.dxf',import.meta.url).pathname);
  await expect(label).toBeVisible(); await tool('Fit extents');
  const original=native(await download('original'));
  const originalVisible=await labelWorld(); // Initial label may be viewport-clamped.
  const box=await label.boundingBox(), destination=await screen({x:6,y:2});
  await page.mouse.move(box.x+box.width/2,box.y+box.height/2); await page.mouse.down();
  await page.mouse.move(destination.x,destination.y,{steps:8}); await page.mouse.up();
  await expect(page.getByRole('status')).toHaveText('Moved dimension.');
  const positioned=native(await download('label-positioned'));
  assert.deepEqual(positioned.entities,original.entities);
  near(await labelWorld(),positioned.dimensions[0].anchor,'label reposition');
  const start=await screen({x:5,y:0}),end=await screen({x:7,y:1});
  await page.mouse.move(start.x,start.y); await page.mouse.down(); await page.mouse.move(end.x,end.y,{steps:10});
  await page.screenshot({path:`${output}/held.png`});
  const expected={x:positioned.dimensions[0].anchor.x+2,y:positioned.dimensions[0].anchor.y+1};
  const held=await labelWorld();
  await page.mouse.up(); await page.waitForTimeout(250);
  const text=await download('moved'),moved=native(text),visible=await labelWorld();
  console.log('LABEL FOLLOW',JSON.stringify({positioned:positioned.dimensions[0].anchor,held,visible,saved:moved.dimensions[0].anchor,expected}));
  near(moved.dimensions[0].anchor,expected,'saved anchor follows geometry');
  near(held,expected,'held preview follows repositioned label');
  near(visible,expected,'released label matches saved anchor');
  await tool('Undo'); await visibleAt(positioned.dimensions[0].anchor);
  await tool('Undo'); await visibleAt(originalVisible);
  assert.deepEqual(native(await download('undone')).dimensions,original.dimensions);
  await tool('Redo'); await visibleAt(positioned.dimensions[0].anchor);
  await tool('Redo'); await visibleAt(expected);
  assert.deepEqual(native(await download('redone')).dimensions,moved.dimensions);
  await page.locator('input[type=file]').setInputFiles({name:'reopen.dxf',mimeType:'application/dxf',buffer:Buffer.from(text)});
  await page.waitForFunction(()=>document.querySelector('.document-filename')?.textContent==='reopen.dxf');
  await visibleAt(expected);
  assert.deepEqual(native(await download('reopened')).dimensions,moved.dimensions);
  // A cancelled label gesture must not leave another invisible position override.
  for (const cancellation of ['pointercancel','Escape']) {
    const reopenedBox=await label.boundingBox();
    await page.mouse.move(reopenedBox.x+reopenedBox.width/2,reopenedBox.y+reopenedBox.height/2);
    await page.mouse.down(); await page.mouse.move(reopenedBox.x+70,reopenedBox.y-40,{steps:5});
    if (cancellation==='Escape') await page.keyboard.press('Escape');
    else await label.dispatchEvent('pointercancel',{pointerId:1});
    await page.mouse.up();
    await visibleAt(expected);
    await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
  }
  const againStart=await screen({x:7,y:1}),againEnd=await screen({x:8,y:2});
  await page.mouse.move(againStart.x,againStart.y);await page.mouse.down();await page.mouse.move(againEnd.x,againEnd.y,{steps:8});await page.mouse.up();
  const againExpected={x:expected.x+1,y:expected.y+1};
  await visibleAt(againExpected);
  near(native(await download('edited-again')).dimensions[0].anchor,againExpected,'edit after reopen');
  await page.screenshot({path:`${output}/after-reopen-edit.png`});
  assert.deepEqual(errors,[]);
  const build=await page.locator('.app-brand').getAttribute('title');
  if(process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
  console.log('PASS label reposition then geometry drag: held/release, Undo/Redo, native reopen/edit again, pointer cancellation.',build);
} finally {await browser.close();}
