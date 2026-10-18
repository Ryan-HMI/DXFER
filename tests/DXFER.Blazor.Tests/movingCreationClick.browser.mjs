import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/moving-creation';await mkdir(output,{recursive:true});
const browser=await chromium.launch({headless:true});
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  const canvas=page.locator('canvas.drawing-canvas');
  const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
  const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const native=text=>{const a=text.trim().split(/\r?\n/);let h='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')h+=a[i+1].trim();return JSON.parse(Buffer.from(h,'hex'));};
  const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return {text,doc:native(text)};};
  const shape=doc=>JSON.parse(JSON.stringify(doc.entities.map(({id,...geometry})=>geometry),(_,value)=>typeof value==='number'?Math.round(value*1e9)/1e9:value));
  const fresh=async()=>{await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');};
  const pick=async(x,y,moving)=>{
    await page.mouse.move(x-10,y-7);await page.mouse.move(x,y);await page.mouse.down();
    if(moving)await page.mouse.move(x+18,y+13,{steps:3});
    await page.mouse.up();
  };
  const pickWorld=async(x,y,moving)=>{
    const p=await canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),{x,y});
    await pick(p.x,p.y,moving);
  };
  for(const [name,points] of [
    ['Point',[[-8,4]]],['Line',[[-8,4],[2,-2]]],
    ['Two-point rectangle',[[-8,4],[2,-2]]],['Center circle',[[-8,4],[2,-2]]],
    ['Three-point arc',[[-8,4],[2,4],[-3,-3]]],['Ellipse',[[-8,4],[2,4],[-3,-3]]]
  ]) {
    let baseline;
    for(const moving of [false,true]) {
      await fresh();await tool(name);
      for(const [x,y] of points)await pickWorld(x,y,moving);
      await expect(canvas).not.toHaveAttribute('data-entity-count','0');
      await canvas.focus();await page.keyboard.press('Escape');
      const saved=await download(`${name.replaceAll(' ','-')}-${moving?'moving':'stationary'}`);
      if(!moving){baseline=shape(saved.doc);continue;}
      assert.deepEqual(shape(saved.doc),baseline,`${name}: point placement must match the press, not release`);
      await tool('Undo');await expect(canvas).toHaveAttribute('data-entity-count','0');
      await tool('Redo');assert.deepEqual(shape((await download()).doc),baseline);
      await page.locator('input[type=file]').setInputFiles({name:'reopened.dxf',mimeType:'application/dxf',buffer:Buffer.from(saved.text)});
      await expect(page.locator('.document-filename')).toHaveText('reopened.dxf');
      assert.deepEqual(shape((await download()).doc),baseline);
      await page.screenshot({path:`${output}/${name.replaceAll(' ','-')}.png`});
    }
    console.log(`PASS ${name}: moving click, exact geometry, Undo/Redo and native reopen`);
  }
  await fresh();await tool('Line');
  const origin=await canvas.evaluate(c=>({x:c.getBoundingClientRect().left+ +c.dataset.offsetX,y:c.getBoundingClientRect().top+ +c.dataset.offsetY}));
  await pick(origin.x,origin.y,true);await pick(origin.x+150,origin.y-100,true);
  await expect(canvas).toHaveAttribute('data-entity-count','1');await canvas.focus();await page.keyboard.press('Escape');
  const snapped=(await download('origin-moving')).doc;
  assert.ok(snapped.constraints.some(c=>c.kind==='Coincident' && c.state==='Satisfied'));
  assert.deepEqual(errors,[]);
  const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
  console.log('PASS moving origin snap retains permanent Coincident.',build);
}finally{await browser.close();}
