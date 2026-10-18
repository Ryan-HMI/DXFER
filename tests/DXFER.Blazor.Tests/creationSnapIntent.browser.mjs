import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/creation-snap-browser';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')hex+=a[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const baseline=await readFile(new URL('./fixtures/drag-matrix/implicit-snap-line.dxf',import.meta.url));
const browser=await chromium.launch({headless:true});
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  const canvas=page.locator('canvas.drawing-canvas');
  const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
  const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return {text,doc:native(text)};};
  const load=async(buffer,count=2)=>{await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');await page.locator('input[type=file]').setInputFiles({name:'snap-source.dxf',mimeType:'application/dxf',buffer:Buffer.from(buffer)});await expect(canvas).toHaveAttribute('data-entity-count',String(count));await tool('Fit extents');};
  const screen=p=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
  const move=async p=>{const s=await screen(p);await page.mouse.move(s.x,s.y);await page.waitForTimeout(150);return s;};
  const click=async p=>{const s=await move(p);await page.mouse.click(s.x,s.y);};
  const drag=async(a,b)=>{await move(a);await page.mouse.down();const s=await screen(b);await page.mouse.move(s.x,s.y,{steps:10});await page.mouse.up();await page.waitForTimeout(250);};
  for(const [label,kind,p,acquire,from,to] of [
    ['edge','PointOnLine',{x:3,y:3},null,{x:6,y:3},{x:6,y:5}],
    ['projection','Vertical',{x:2,y:4},{x:2,y:3},{x:2,y:3},{x:3,y:3}]
  ]) {
    await load(baseline);const original=(await download()).doc;
    await tool('Point');if(acquire)await move(acquire);await click(p);
    await expect(canvas).toHaveAttribute('data-entity-count','3');await canvas.focus();await page.keyboard.press('Escape');
    const saved=await download(`${label}-created`);
    const c=saved.doc.constraints.find(c=>c.kind===kind);assert.ok(c,`${label}: permanent relation missing`);assert.equal(c.state,'Satisfied');
    assert.deepEqual(saved.doc.entities.filter(e=>e.id==='source'||e.id==='line'),original.entities);
    await tool('Undo');assert.deepEqual((await download()).doc.entities,original.entities);
    await tool('Redo');assert.deepEqual((await download()).doc.constraints,saved.doc.constraints);
    await load(saved.text,3);await drag(from,to);
    const changed=(await download(`${label}-edited-after-reopen`)).doc;
    const added=changed.entities.find(e=>e.type==='Point'||e.kind==='Point'||e.location);
    const source=changed.entities.find(e=>e.id==='source');assert.notDeepEqual(source,original.entities.find(e=>e.id==='source'));
    assert.ok(added);const q=added.location;
    if(kind==='Vertical')assert.ok(Math.abs(q.x-source.start.x)<1e-8);
    else assert.ok(Math.abs((q.x-source.start.x)*(source.end.y-source.start.y)-(q.y-source.start.y)*(source.end.x-source.start.x))<1e-8);
    assert.equal(changed.constraints.find(x=>x.id===c.id).state,'Satisfied');
    await page.screenshot({path:`${output}/${label}.png`});
    console.log(`PASS ${label}: real click, unchanged source, permanent relation, Undo/Redo, native reopen and source edit`);
  }
  const crossing='0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n0\nLINE\n8\n0\n10\n3\n20\n-4\n11\n3\n21\n4\n0\nENDSEC\n0\nEOF\n';
  await load(crossing);await download();await tool('Point');await click({x:3,y:0});
  await expect(canvas).toHaveAttribute('data-entity-count','3');await canvas.focus();await page.keyboard.press('Escape');
  const intersected=await download('intersection-created');
  assert.equal(intersected.doc.constraints.filter(c=>c.kind==='PointOnLine' && c.state==='Satisfied').length,2);
  await tool('Undo');await expect(canvas).toHaveAttribute('data-entity-count','2');await tool('Redo');
  await download();await load(intersected.text,3);
  assert.deepEqual((await download('intersection-reopened')).doc.constraints,intersected.doc.constraints);
  console.log('PASS intersection: actual click retains both lines through Undo/Redo and native reopen');
  assert.deepEqual(errors,[]);
  const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));console.log(build);
} finally {await browser.close();}
