import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-interactive-corners-evidence';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1188, height: 761 }, extraHTTPHeaders: { 'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY } });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const canvas = page.locator('canvas.drawing-canvas');
  const input = page.locator('.dxfer-corner-size-control input');
  const undo = page.getByRole('button', { name: 'Undo', exact: true });
  const apply = page.getByRole('button', { name: 'Confirm', exact: true });
  const cancel = page.getByRole('button', { name: 'Cancel', exact: true });
  const native = text => {
    const a = text.trim().split(/\r?\n/); let h = '';
    for (let i = 0; i < a.length; i += 2) if (a[i].trim() === '310') h += a[i + 1].trim();
    return JSON.parse(Buffer.from(h, 'hex'));
  };
  const download = async () => {
    const pending = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Open application menu' }).click();
    await page.getByRole('button', { name: 'File', exact: true }).hover();
    await page.getByRole('button', { name: 'Download DXF', exact: true }).click();
    return readFile(await (await pending).path(), 'utf8');
  };
  const point = async (x, y) => canvas.evaluate((c, p) => {
    const b = c.getBoundingClientRect();
    return { x: b.x + Number(c.dataset.offsetX) + p.x * Number(c.dataset.scale), y: b.y + Number(c.dataset.offsetY) - p.y * Number(c.dataset.scale) };
  }, { x, y });
  const click = async (x, y) => { const p = await point(x, y); await page.mouse.click(p.x, p.y); await page.waitForTimeout(200); };
  const twoLines = ['0','LINE','8','0','10','0','20','0','11','10','21','0','0','LINE','8','0','10','0','20','0','11','0','21','10'];
  const polyline = ['0','LWPOLYLINE','8','0','90','4','70','1','10','0','20','0','10','10','20','0','10','10','20','10','10','0','20','10'];
  const openPolyline = ['0','LWPOLYLINE','8','0','90','3','70','0','10','10','20','0','10','0','20','0','10','0','20','10'];
  const load = async (geometry, name) => {
    const text = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC','0','SECTION','2','ENTITIES',...geometry,'0','ENDSEC','0','EOF',''].join('\n');
    await page.locator('input[type=file]').setInputFiles({ name, mimeType: 'application/dxf', buffer: Buffer.from(text) });
    await page.waitForFunction(n => document.querySelector('.document-filename')?.textContent === n, name);
    await page.getByRole('button', { name: 'Fit extents', exact: true }).click();
    await page.waitForTimeout(250);
  };
  for (const tool of ['Chamfer', 'Fillet']) for (const kind of ['lines', 'open', 'closed']) {
    await load(kind === 'closed' ? polyline : kind === 'open' ? openPolyline : twoLines, `${tool}-${kind}.dxf`);
    const original = native(await download());
    await page.getByRole('button', { name: 'Hide operation inspector', exact: true }).click();
    await page.waitForFunction(() => !document.querySelector('.dxfer-operation-inspector'));
    await page.getByRole('button', { name: tool, exact: true }).click();
    await page.waitForFunction(() => document.querySelector('.dxfer-operation-inspector'));
    assert.equal(await apply.isDisabled(), true);
    await input.fill('2');
    await click(0, 0);
    await page.waitForFunction(() => document.querySelector('canvas').dataset.cornerPreview === 'true');
    assert.match(await page.locator('.dxfer-command-bar').innerText(), new RegExp(`${tool} preview`));
    assert.equal(await undo.isDisabled(), true);
    assert.equal(await page.locator('.document-change-state').textContent(), 'Unchanged');
    assert.deepEqual(native(await download()).entities, original.entities, 'preview must not leak into export');
    const beforePixels = await canvas.screenshot();
    const handle = await canvas.evaluate(c => {
      const b = c.getBoundingClientRect();
      return { x: b.x + Number(c.dataset.cornerHandleX), y: b.y + Number(c.dataset.cornerHandleY), scale: Number(c.dataset.scale) };
    });
    await page.mouse.move(handle.x, handle.y);
    await page.mouse.down();
    await page.mouse.move(handle.x + handle.scale, handle.y, { steps: 6 });
    await page.waitForFunction(() => Math.abs(Number(document.querySelector('.dxfer-corner-size-control input').value) - 3) < 0.001);
    assert.equal(await undo.isDisabled(), true, 'held preview must not create history');
    assert.notDeepEqual(await canvas.screenshot(), beforePixels, 'drag must visibly update geometry');
    await page.mouse.up();
    assert.equal(await undo.isDisabled(), true, 'release must only finish size adjustment');
    for (const bad of ['0', '-1', '', '100']) {
      await input.fill(bad);
      await page.waitForFunction(() => document.querySelector('canvas').dataset.cornerPreview === 'false');
      assert.equal(await apply.isDisabled(), true);
      await input.press('Enter');
      assert.equal(await undo.isDisabled(), true);
    }
    await input.fill('2');
    await page.waitForFunction(() => document.querySelector('canvas').dataset.cornerPreview === 'true');
    await page.screenshot({ path: `${output}/${tool}-${kind}-preview.png` });
    if (kind === 'lines') await cancel.press('Enter');
    else await input.press('Escape');
    await page.waitForFunction(() => !document.querySelector('.dxfer-corner-size-control'));
    assert.deepEqual(native(await download()).entities, original.entities);
    assert.equal(await undo.isDisabled(), true);
    await page.getByRole('button', { name: tool, exact: true }).click();
    await click(0, 0);
    await page.waitForFunction(() => document.querySelector('canvas').dataset.cornerPreview === 'true');
    if (kind === 'closed') {
      await page.getByRole('button', {name: 'Hide operation inspector'}).click();
      await page.waitForFunction(() => !document.querySelector('.dxfer-operation-inspector'));
      await page.screenshot({path:`${output}/${tool}-collapsed.png`});
      await input.press('Enter');
      await page.getByRole('button', {name: 'Show operation inspector'}).click();
      await page.waitForFunction(() => document.querySelector('.dxfer-operation-inspector'));
    } else if (kind === 'open') {
      await canvas.focus();
      await page.keyboard.press('Enter');
    } else await apply.click();
    await page.waitForFunction(() => document.querySelector('.document-change-state')?.textContent === 'Modified');
    assert.equal(await undo.isDisabled(), false);
    await page.waitForFunction(() => !document.querySelector('.dxfer-corner-size-control'));
    assert.match(await page.locator('.dxfer-command-bar').innerText(), new RegExp(`${tool} applied`));
    const saved = await download(), changed = native(saved);
    await writeFile(`${output}/${tool}-${kind}.dxf`, saved);
    assert.notDeepEqual(changed.entities, original.entities);
    await undo.click();
    assert.deepEqual(native(await download()).entities, original.entities);
    await page.getByRole('button', { name: 'Redo', exact: true }).click();
    assert.deepEqual(native(await download()).entities, changed.entities);
    await page.locator('input[type=file]').setInputFiles({ name: 'reopen.dxf', mimeType: 'application/dxf', buffer: Buffer.from(saved) });
    await page.waitForFunction(() => document.querySelector('.document-filename')?.textContent === 'reopen.dxf');
    const reopened = native(await download());
    assert.deepEqual(reopened.entities, changed.entities);
    assert.deepEqual(reopened.dimensions, changed.dimensions);
  }
  // Shorter desktop: controls and feedback remain visible, including rapid key-in.
  await page.setViewportSize({width:1100,height:705});
  await load(twoLines, 'compact-desktop.dxf');
  await page.getByRole('button', { name: 'Chamfer', exact: true }).click();
  await input.fill('');
  await input.pressSequentially('1.23456789', {delay:30});
  assert.equal(await input.inputValue(), '1.23456789');
  await input.fill('2');
  await click(0, 0);
  await page.waitForFunction(() => document.querySelector('canvas').dataset.cornerPreview === 'true');
  for (const locator of [input, apply, cancel]) {
    const b = await locator.boundingBox();
    assert.ok(b && b.x >= 0 && b.y >= 0 && b.x+b.width <= 1100 && b.y+b.height <= 705);
    assert.equal(await locator.evaluate(e => e.scrollWidth <= e.clientWidth), true);
  }
  await page.screenshot({path:`${output}/compact-desktop.png`});
  await cancel.click();
  assert.equal(await undo.isDisabled(), true);
  await page.locator('input[type=file]').setInputFiles(new URL('./fixtures/corner-driving10.dxf', import.meta.url).pathname);
  await page.waitForFunction(() => document.querySelector('.document-filename')?.textContent === 'corner-driving10.dxf');
  await page.getByRole('button', {name:'Fit extents',exact:true}).click();
  const driving = native(await download());
  for (const tool of ['Chamfer','Fillet']) {
    await page.getByRole('button', {name:tool,exact:true}).click();
    await input.fill('2');
    await click(0,0);
    assert.equal(await apply.isDisabled(), true);
    assert.match(await page.getByRole('status').innerText(), /unchanged/i);
    await input.press('Enter');
    assert.equal(await undo.isDisabled(), true);
    await page.screenshot({path:`${output}/${tool}-driving-refusal.png`});
    const refused = native(await download());
    assert.deepEqual(refused.entities, driving.entities);
    assert.deepEqual(refused.dimensions, driving.dimensions);
    await cancel.click();
  }
  assert.deepEqual(errors, []);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
  console.log('PASS interactive Fillet/Chamfer: lines/open/closed polylines, key-in/held drag/release, invalid recovery, Cancel/Escape, Confirm/Enter exits (canvas/collapsed inspector), history, native reopen, 1188/1100 desktop. Build:', build);
} finally { await browser.close(); }
