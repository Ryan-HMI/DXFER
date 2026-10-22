import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url && ['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-ortho-precedence';
await mkdir(output, {recursive:true});
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i=0;i<lines.length;i+=2) if (lines[i].trim()==='310') hex+=lines[i+1].trim();
  assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixture = horizontal => ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
  '0','SECTION','2','ENTITIES','0','LINE','5','200','8','0',
  '10',horizontal?'2':'0','20',horizontal?'2':'3','11',horizontal?'2':'6','21',horizontal?'8':'3',
  '0','ENDSEC','0','EOF',''].join('\n');
const near = (a,b) => assert.ok(Math.abs(a-b)<=1e-6, `${a} != ${b}`);
const content = d => ({entities:d.entities, constraints:d.constraints, dimensions:d.dimensions});
const results = {passed:false, cases:[], errors:[]};
const browser = await chromium.launch({headless:true, executablePath:process.env.DXFER_CHROMIUM});
let page;
try {
  page = await browser.newPage({viewport:{width:1440,height:1000},
    extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(15000); page.on('dialog', d=>d.accept());
  page.on('pageerror', e=>results.errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => {await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(100);};
  const menu = async name => {await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const download = async name => {
    const pending=page.waitForEvent('download');await menu('Download DXF');
    const text=await readFile(await (await pending).path(),'utf8');
    await writeFile(`${output}/${name}.dxf`,text);return {text,doc:decode(text)};
  };
  const load = async (text,count=1) => {
    await canvas.press('Escape');await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count','0');
    await page.locator('input[type=file]').setInputFiles({name:'ortho.dxf',mimeType:'application/dxf',buffer:Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count',String(count));await tool('Fit extents');
    let last,stable=0;
    await expect.poll(async()=>{const next=await canvas.evaluate(c=>JSON.stringify([c.dataset.scale,c.dataset.offsetX,c.dataset.offsetY]));
      stable=next===last?stable+1:0;last=next;return stable;},{intervals:[150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = p => canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,
    y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
  const move = async p => {const s=await screen(p);await page.mouse.move(s.x,s.y);await page.waitForTimeout(180);return s;};
  const click = async p => {const s=await move(p);
    assert.ok(await canvas.evaluate((c,s)=>document.elementFromPoint(s.x,s.y)===c,s),'Creation click reaches canvas');
    await page.mouse.click(s.x,s.y);};
  for (const mode of ['guide','free']) for (const kind of ['Horizontal','Vertical']) for (const automatic of [true,false])
    for (const commit of mode==='guide'?['mouse','enter','keyed']:['mouse']) {
      if(process.env.DXFER_KEYED_ONLY==='1' && commit!=='keyed') continue;
      const name=`${mode}-${kind}-${automatic?'on':'off'}-${commit}`;results.currentCase=name;
      const horizontal=kind==='Horizontal';await load(fixture(horizontal));
      await menu('Settings');await page.getByRole('checkbox',{name:'Automatic snap constraints',exact:true}).setChecked(automatic);await tool('Close settings');
      const before=await download(`${name}-before`), source=before.doc.entities[0];
      const mid=horizontal?{x:2,y:5}:{x:3,y:3};
      const start=horizontal?{x:0,y:5.03}:{x:3.03,y:1};
      const end=horizontal?{x:4,y:5}:{x:3,y:5};
      await tool('Line');await click(start);
      if (mode==='guide') {
        await move(mid);await expect.poll(async()=>Number(await canvas.getAttribute('data-acquired-snap-count'))).toBeGreaterThan(0);
      }
      const p=await move(end);
      await page.screenshot({path:`${output}/${name}-preview.png`});
      if (mode==='guide' && process.env.DXFER_BASELINE !== '1') {
        await expect(canvas).toHaveAttribute('data-snap-cue',new RegExp(kind));
        assert.equal(await canvas.getAttribute('data-snap-cue-box'),null,'No visible snap information card');
      }
      if (commit==='keyed') {
        const input=page.locator('input[data-dimension-key="length"]');
        await input.fill('3');await input.press('Enter');
      } else if (commit==='mouse') await page.mouse.click(p.x,p.y);else await page.keyboard.press('Enter');
      await expect(canvas).toHaveAttribute('data-entity-count','2');await canvas.press('Escape');
      const saved=await download(`${name}-committed`), added=saved.doc.entities.find(e=>e.id!==source.id);
      assert.deepEqual(saved.doc.entities.find(e=>e.id===source.id),source,'Source stays unchanged');
      const axis=horizontal?'y':'x';
      if(commit==='keyed') {
        assert.ok(Math.abs(added.end[axis]-mid[axis])>1e-5,'Explicit length overrides the guide');
        assert.equal(saved.doc.dimensions.length,1);near(saved.doc.dimensions[0].value,3);
        assert.equal(saved.doc.dimensions[0].isDriving,true);
      } else if(mode==='guide') near(added.end[axis],mid[axis]);else near(added.end[axis],added.start[axis]);
      const relations=saved.doc.constraints.filter(c=>c.kind===kind);
      if(automatic && commit!=='keyed') {
        assert.equal(relations.length,1,'Exactly one corresponding permanent axis/alignment relation');
        assert.equal(relations[0].state,'Satisfied');
        if(mode==='guide') {
          assert.ok(relations[0].referenceKeys.includes(`${source.id}:mid`));
          assert.ok(relations[0].referenceKeys.includes(`${added.id}:end`));
        } else assert.deepEqual(relations[0].referenceKeys,[added.id]);
      } else assert.deepEqual(saved.doc.constraints,before.doc.constraints,'OFF preserves positioning but adds no constraints');
      await tool('Undo');assert.deepEqual(content((await download(`${name}-undo`)).doc),content(before.doc));
      await tool('Redo');assert.deepEqual(content((await download(`${name}-redo`)).doc),content(saved.doc));
      await load(saved.text,2);assert.deepEqual(content((await download(`${name}-reopened`)).doc),content(saved.doc));
      results.cases.push({name,passed:true});console.log(`PASS ${name}`);
    }
  assert.deepEqual(results.errors,[]);results.passed=true;delete results.currentCase;
} catch(error) {
  results.failure=error.stack;await page?.screenshot({path:`${output}/failure.png`});throw error;
} finally {
  await writeFile(`${output}/results.json`,JSON.stringify(results,null,2));await browser.close();
}
console.log(JSON.stringify({passed:true,cases:results.cases.length,build:results.build,output}));
