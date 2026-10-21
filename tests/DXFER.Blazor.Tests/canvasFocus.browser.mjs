import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url && ['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-canvas-focus';
await mkdir(output, {recursive:true});
const results = {samples:[], errors:[], passed:false};
const browser = await chromium.launch({headless:true, executablePath:process.env.DXFER_CHROMIUM});
let page;
try {
  page = await browser.newPage({viewport:{width:1440,height:1000}, extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => results.errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) results.errors.push(message.text()); });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title'); console.log(results.build);
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  const snapshot = () => canvas.evaluate(c => {
    const style = getComputedStyle(c), active = document.activeElement, rect = c.getBoundingClientRect();
    return {focused:active === c, active:{tag:active?.tagName, label:active?.getAttribute('aria-label'), dimensionKey:active?.dataset?.dimensionKey},
      programmatic:c.dataset.programmaticFocus ?? null, focusVisible:c.matches(':focus-visible'),
      outline:{style:style.outlineStyle, width:style.outlineWidth, color:style.outlineColor, offset:style.outlineOffset},
      view:{scale:+c.dataset.scale,x:+c.dataset.offsetX,y:+c.dataset.offsetY,width:rect.width,height:rect.height},
      entities:c.dataset.entityCount};
  });
  const record = async name => { const value = await snapshot(); results.samples.push({name,...value});
    await page.screenshot({path:`${output}/${name}.png`}); console.log(name, JSON.stringify(value)); return value; };
  const sameCamera = (a,b) => {
    for (const key of ['scale','x','y']) assert.equal(b.view[key],a.view[key],`Tab/Enter/focus must preserve camera ${key}`);
  };
  const hasOutline = state => !['none','hidden'].includes(state.outline.style) && parseFloat(state.outline.width) > 0
    && state.outline.color !== 'rgba(0, 0, 0, 0)';
  const quietCanvas = async name => {
    await expect(canvas).toBeFocused(); await page.waitForTimeout(250);
    const state = await record(name);
    assert.equal(hasOutline(state),false,'programmatic return must not draw a canvas focus ring');
    assert.equal(state.programmatic,'true','application focus return carries its explicit marker');
    return state;
  };
  const tool = async name => { await page.getByRole('button',{name,exact:true}).click(); await page.waitForTimeout(200); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button',{name:'File',exact:true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(),'utf8'); await writeFile(`${output}/${name}.dxf`,text);
    const lines=text.trim().split(/\r?\n/); let hex='';
    for(let i=0;i<lines.length;i+=2) if(lines[i].trim()==='310') hex+=lines[i+1].trim();
    assert.ok(hex); return JSON.parse(Buffer.from(hex,'hex'));
  };
  const keyboardEnterCanvas = async name => {
    // Start on the real preceding tab stop, then use a native Tab key. Never focus
    // the canvas through DOM APIs or synthesize its focus event for this assertion.
    const previous = await canvas.evaluateHandle(c => {
      const stops = [...document.querySelectorAll('a[href],button,input,select,textarea,[tabindex]')]
        .filter(e => e.tabIndex >= 0 && !e.disabled && e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden');
      const index = stops.indexOf(c); if(index <= 0) throw new Error('Canvas has no preceding native tab stop');
      return stops[index-1];
    });
    try { await previous.asElement().focus(); await page.keyboard.press('Tab'); }
    finally { await previous.dispose(); }
    await expect(canvas).toBeFocused();
    const state = await record(name);
    assert.equal(state.programmatic,null,'blur clears the programmatic-focus marker before native re-entry');
    assert.equal(state.focusVisible,true,'native Tab entry matches focus-visible');
    assert.equal(hasOutline(state),true,'native keyboard navigation retains a visible focus indicator');
    return state;
  };

  await expect(canvas).toHaveAttribute('data-entity-count','0');
  await tool('Two-point rectangle');
  await page.mouse.click(350,450); await page.mouse.move(600,650,{steps:5});
  const width=page.locator('input[data-dimension-key="width"]'), height=page.locator('input[data-dimension-key="height"]');
  await expect(width).toBeVisible(); await expect(height).toBeVisible(); await page.waitForTimeout(300);
  await width.fill('15'); const beforeTab = await record('rectangle-width-before-tab');
  await width.press('Tab'); await expect(height).toBeFocused(); await page.waitForTimeout(250);
  sameCamera(beforeTab, await record('rectangle-height-after-tab'));
  await height.fill('10'); const beforeEnter = await record('rectangle-height-before-enter');
  await height.press('Enter'); await expect(canvas).toHaveAttribute('data-entity-count','4');
  sameCamera(beforeEnter, await quietCanvas('rectangle-committed-programmatic-focus'));
  await expect(page.locator('input[data-dimension-key]')).toHaveCount(0);
  await expect(page.locator('input[data-dimension-id]')).toHaveCount(2);
  await page.keyboard.press('Escape'); await page.waitForTimeout(200);
  const created = await download('rectangle-15x10');
  assert.deepEqual(created.dimensions.map(d=>d.value).sort((a,b)=>a-b),[10,15]);
  const points = created.entities.flatMap(e=>[e.start,e.end]);
  assert.equal(points.length,8);
  assert.ok(Math.abs(Math.max(...points.map(p=>p.x))-Math.min(...points.map(p=>p.x))-15)<1e-6);
  assert.ok(Math.abs(Math.max(...points.map(p=>p.y))-Math.min(...points.map(p=>p.y))-10)<1e-6);
  await keyboardEnterCanvas('native-tab-entry-before-dimension-edit');

  const dimensionId = created.dimensions.find(d=>d.value===15).id;
  const dimension = page.locator(`input[data-dimension-id="${dimensionId}"]`);
  await dimension.dblclick(); await dimension.fill('16');
  const beforeDimensionEnter = await record('persistent-dimension-before-enter');
  await dimension.press('Enter'); await expect(dimension).toHaveAttribute('data-dimension-value','16');
  sameCamera(beforeDimensionEnter, await quietCanvas('persistent-dimension-programmatic-return'));
  sameCamera(beforeDimensionEnter, await keyboardEnterCanvas('native-tab-reentry-after-programmatic-return'));

  await tool('Undo'); await expect(dimension).toHaveAttribute('data-dimension-value','15');
  sameCamera(beforeDimensionEnter, await record('undo-button-focus'));
  // Closing the context menu schedules DrawingCanvas.FocusAsync from C#.
  const box = await canvas.boundingBox();
  await page.mouse.click(box.x + box.width * .5, box.y + box.height * .15, {button:'right'});
  const context = page.getByRole('dialog',{name:'Drawing context commands'});
  await expect(context).toBeVisible(); await context.getByRole('button',{name:'Close',exact:true}).click();
  sameCamera(beforeDimensionEnter, await quietCanvas('context-close-csharp-programmatic-return'));
  sameCamera(beforeDimensionEnter, await keyboardEnterCanvas('native-tab-reentry-after-context-close'));
  assert.deepEqual(results.errors,[]); results.passed=true;
  console.log('PASS rectangle 15x10 Tab/Enter, unchanged camera, quiet programmatic return, persistent dimensions and repeated native keyboard focus indicators');
} catch(error) {
  results.failure=error.stack; await page?.screenshot({path:`${output}/failure.png`}).catch(()=>{}); throw error;
} finally {
  await writeFile(`${output}/results.json`,JSON.stringify(results,null,2)); await browser.close();
}
