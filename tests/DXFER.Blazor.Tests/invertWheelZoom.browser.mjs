import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url && ['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-invert-wheel-zoom';
await mkdir(output, {recursive:true});
const fixture = await readFile(new URL('./fixtures/drag-matrix/implicit-snap-line.dxf', import.meta.url));
const results = {passed:false, wheels:[], settings:[], history:[], errors:[]};
const geometry = doc => ({entities:doc.entities, constraints:doc.constraints, dimensions:doc.dimensions, patterns:doc.metadata?.patterns});
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i=0; i<lines.length; i+=2) if (lines[i].trim()==='310') hex+=lines[i+1].trim();
  assert.ok(hex, 'download contains native document'); return JSON.parse(Buffer.from(hex,'hex'));
};
const browser = await chromium.launch({headless:true, executablePath:process.env.DXFER_CHROMIUM});
let page;
try {
  // A new context has no saved preference; do not preload or overwrite storage.
  page = await browser.newPage({viewport:{width:1440,height:1000}, extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog=>dialog.accept());
  page.on('pageerror', error=>results.errors.push(error.message));
  page.on('console', message=>{if(message.type()==='error' && /unhandled|circuit|exception/i.test(message.text()))results.errors.push(message.text());});
  const ready = ()=>page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  await page.goto(url); await ready();
  results.build = await page.locator('.app-brand').getAttribute('title'); console.log(results.build);
  if(process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  const settings = page.getByRole('dialog',{name:'Settings',exact:true});
  const invert = settings.getByRole('checkbox',{name:/^Invert (?:mouse )?wheel zoom$/i});
  const automatic = settings.getByRole('checkbox',{name:'Automatic snap constraints',exact:true});
  const tool = async name=>{await page.getByRole('button',{name,exact:true}).click(); await page.waitForTimeout(150);};
  const menu = async name=>{await tool('Open application menu'); await page.getByRole('button',{name:'File',exact:true}).hover(); await tool(name);};
  const history = async ()=>({undoDisabled:await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),
    redoDisabled:await page.getByRole('button',{name:'Redo',exact:true}).isDisabled()});
  const view = ()=>canvas.evaluate(c=>({scale:+c.dataset.scale,x:+c.dataset.offsetX,y:+c.dataset.offsetY}));
  const openSettings = async (expectedInvert, expectedAuto, name)=>{
    await menu('Settings'); await expect(invert).toBeEnabled(); await expect(automatic).toBeEnabled();
    await expect(invert).toBeChecked({checked:expectedInvert});
    await expect(automatic).toBeChecked({checked:expectedAuto});
    if(name) {
      results.settings.push({name,invert:await invert.isChecked(),automatic:await automatic.isChecked(),viewport:page.viewportSize()});
      await page.screenshot({path:`${output}/${name}.png`});
    }
  };
  const changeSetting = async (which, value, previousInvert, previousAuto)=>{
    const beforeHistory=await history(), beforeView=await view();
    await openSettings(previousInvert,previousAuto);
    await (which==='invert'?invert:automatic).setChecked(value);
    await expect(which==='invert'?automatic:invert).toBeChecked({checked:which==='invert'?previousAuto:previousInvert});
    await tool('Close settings');
    assert.deepEqual(await history(),beforeHistory,'settings add no undo/redo entry');
    assert.deepEqual(await view(),beforeView,'changing settings does not zoom or recenter');
  };
  const download = async name=>{
    const pending=page.waitForEvent('download'); await menu('Download DXF');
    const text=await readFile(await (await pending).path(),'utf8'); await writeFile(`${output}/${name}.dxf`,text);
    return decode(text);
  };
  const load = async ()=>{
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count','0');
    await page.locator('input[type=file]').setInputFiles({name:'wheel-zoom-source.dxf',mimeType:'application/dxf',buffer:fixture});
    await expect(canvas).toHaveAttribute('data-entity-count','2'); await tool('Fit extents'); await canvas.press('Escape');
  };
  const createSnapPoint = async (enabled,name)=>{
    await load(); const before=await download(`${name}-before`);
    await tool('Point');
    const p=await canvas.evaluate(c=>({x:c.getBoundingClientRect().left+4*+c.dataset.scale+ +c.dataset.offsetX,
      y:c.getBoundingClientRect().top-3*+c.dataset.scale+ +c.dataset.offsetY}));
    await page.mouse.move(p.x,p.y); await page.waitForTimeout(150); await page.mouse.click(p.x,p.y);
    await expect(canvas).toHaveAttribute('data-entity-count','3'); await canvas.press('Escape');
    const after=await download(`${name}-created`);
    const point=after.entities.find(e=>!before.entities.some(old=>old.id===e.id));
    assert.ok(point?.location && Math.hypot(point.location.x-4,point.location.y-3)<1e-8,'existing midpoint snap position unchanged');
    assert.equal(after.constraints.some(c=>c.kind==='Midpoint'),enabled,'Auto Constraints retains its own creation policy');
    assert.deepEqual(after.entities.filter(e=>before.entities.some(old=>old.id===e.id)),before.entities);
    return {before,after};
  };
  const wheel = async (name,inverted,delta=-120)=>{
    const beforeHistory=await history();
    const cursor=await canvas.evaluate(c=>{
      const r=c.getBoundingClientRect();
      for(const [fx,fy] of [[.63,.43],[.55,.6],[.45,.35]]) {
        // Chromium wheel events use integer client coordinates.
        const p={x:Math.round(r.left+r.width*fx),y:Math.round(r.top+r.height*fy)};
        if(document.elementFromPoint(p.x,p.y)===c)return p;
      }
      throw new Error('No unobscured canvas wheel target');
    });
    const sample=()=>canvas.evaluate((c,p)=>{
      const r=c.getBoundingClientRect(),s=+c.dataset.scale;
      return {scale:s,world:{x:(p.x-r.left- +c.dataset.offsetX)/s,y:-(p.y-r.top- +c.dataset.offsetY)/s}};
    },cursor);
    await page.mouse.move(cursor.x,cursor.y); const before=await sample();
    await canvas.evaluate(c=>c.addEventListener('wheel',event=>{window.__wheelZoomQaEvent={x:event.clientX,y:event.clientY};},{once:true,passive:true}));
    await page.mouse.wheel(0,delta);
    await expect.poll(async()=>(await sample()).scale).not.toBe(before.scale);
    await page.waitForTimeout(150); const after=await sample();
    const deliveredCursor=await page.evaluate(()=>window.__wheelZoomQaEvent);
    results.wheels.push({name,inverted,delta,cursor,deliveredCursor,before,after});
    assert.deepEqual(deliveredCursor,cursor,'anchor is measured at the actual wheel event position');
    const zoomIn=(delta<0)!==inverted;
    assert.ok(zoomIn?after.scale>before.scale:after.scale<before.scale,`${name}: wheel direction`);
    assert.ok(Math.hypot(after.world.x-before.world.x,after.world.y-before.world.y)<1e-8,`${name}: cursor world anchor unchanged`);
    assert.deepEqual(await history(),beforeHistory,`${name}: wheel does not touch history`);
  };
  const historyUnchanged = async (baseline,name)=>{
    assert.deepEqual(geometry(await download(`${name}-after-wheel`)),geometry(baseline.after),'wheel/settings preserve all native geometry and constraints');
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count','2');
    assert.deepEqual(geometry(await download(`${name}-undo`)),geometry(baseline.before),'one Undo still reverses the original point creation');
    await tool('Redo'); await expect(canvas).toHaveAttribute('data-entity-count','3');
    assert.deepEqual(geometry(await download(`${name}-redo`)),geometry(baseline.after),'Redo restores exact creation');
    results.history.push(name);
  };

  await openSettings(false,true,'settings-default-1440'); await tool('Close settings');
  const original=await createSnapPoint(true,'auto-on-normal');
  await wheel('default-wheel-up-in',false); await wheel('default-wheel-down-out',false,120);
  await changeSetting('invert',true,false,true);
  await openSettings(true,true,'settings-inverted-1440'); await tool('Close settings');
  await wheel('inverted-wheel-up-out',true); await wheel('inverted-wheel-down-in',true,120);
  await historyUnchanged(original,'inverted-auto-on');
  await changeSetting('automatic',false,true,true);
  await page.reload(); await ready();
  await openSettings(true,false,'settings-reloaded-inverted'); await tool('Close settings');
  const noAuto=await createSnapPoint(false,'auto-off-inverted');
  await wheel('reloaded-wheel-up-out',true);
  await changeSetting('invert',false,true,false);
  await wheel('off-restores-wheel-up-in',false); await wheel('off-restores-wheel-down-out',false,120);
  await historyUnchanged(noAuto,'restored-auto-off');
  await changeSetting('automatic',true,false,false);
  await page.reload(); await ready(); await page.setViewportSize({width:1100,height:705});
  await openSettings(false,true,'settings-restored-reloaded-1100');
  const box=await settings.boundingBox();
  assert.ok(box.x>=0 && box.y>=0 && box.x+box.width<=1100 && box.y+box.height<=705,'settings stay within viewport');
  await tool('Close settings');
  const restored=await createSnapPoint(true,'auto-on-restored');
  await wheel('persisted-off-wheel-up-in',false); await historyUnchanged(restored,'persisted-off-auto-on');
  assert.deepEqual(results.errors,[]); results.passed=true;
  console.log('PASS normal/inverted wheel directions, cursor anchor, reload persistence, independent Auto Constraints, exact native geometry and Undo/Redo');
} catch(error) {
  results.failure=error.stack; await page?.screenshot({path:`${output}/failure.png`}).catch(()=>{}); throw error;
} finally {
  await writeFile(`${output}/results.json`,JSON.stringify(results,null,2)); await browser.close();
}
