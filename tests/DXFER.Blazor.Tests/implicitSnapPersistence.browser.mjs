import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-implicit-snaps';
await mkdir(output, {recursive: true});
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = name => page.getByRole('button', {name, exact: true}).click();
  const menu = async name => {await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);};
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async (buffer, count = 2) => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'implicit-snaps.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count)); await tool('Fit extents');
    await page.waitForTimeout(250);
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return {x: r.left + p.x * +d.scale + +d.offsetX, y: r.top - p.y * +d.scale + +d.offsetY};
  }, point);
  const click = async point => {const p = await screen(point); await page.mouse.click(p.x, p.y);};
  const drag = async (start, end, label) => {
    const a = await screen(start), b = await screen(end);
    // Real pointer coordinates need not hit the mathematical grip exactly.
    if (process.env.DXFER_OFFSET_DRAG === '1') {
      a.x = Math.round(a.x) + 2; a.y = Math.round(a.y) - 2;
      b.x = Math.round(b.x); b.y = Math.round(b.y);
    }
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(120);
    console.log('GRAB',label,await canvas.getAttribute('data-hovered-id'));
    await page.mouse.down(); await page.mouse.move(b.x, b.y, {steps: process.env.DXFER_OFFSET_DRAG === '1' ? 1 : 12});
    await page.waitForTimeout(120);
    console.log('HELD',label,await canvas.getAttribute('data-hovered-id'));
    await page.screenshot({path: `${output}/${label}.png`});
    await page.mouse.up(); await page.waitForTimeout(250);
  };
  const close = (a, b) => assert.ok(Math.hypot(a.x - b.x, a.y - b.y) < 0.04, JSON.stringify({a, b}));
  for (const kind of ['line', 'ellipse', 'spline', 'polyline']) {
    const baseline = await readFile(new URL(`./fixtures/drag-matrix/implicit-snap-${kind}.dxf`, import.meta.url));
    const midpoint = kind === 'line' || kind === 'polyline';
    const target = {x: midpoint ? 4 : 2, y: 3};
    await load(baseline);
    const original = native(await download());
    await drag({x: -2, y: 1}, target, `${kind}-snap`);
    const text = await download(`${kind}-snap`), snapped = native(text);
    const relation = midpoint ? 'Midpoint' : 'Coincident';
    assert.ok(snapped.constraints.some(c => c.kind === relation && c.state === 'Satisfied' && c.referenceKeys.includes('line:start')));
    const endpoint=snapped.entities.find(e => e.id === 'line').start;
    assert.ok(Math.hypot(endpoint.x-target.x,endpoint.y-target.y)<1e-9,'Snapped endpoint must be exact, not merely near the target');
    assert.deepEqual(snapped.entities.find(e => e.id === 'source'), original.entities.find(e => e.id === 'source'));
    await tool('Undo'); assert.deepEqual(native(await download()).constraints, original.constraints);
    await tool('Redo'); assert.deepEqual(native(await download()).constraints, snapped.constraints);
    await load(text);
    const from = midpoint ? {x: 6, y: 3} : target;
    const to = midpoint ? {x: 8, y: 3} : {x: 3, y: 4};
    await drag(from, to, `${kind}-follow`);
    const changed = native(await download(`${kind}-follow`));
    close(changed.entities.find(e => e.id === 'line').start, midpoint ? {x: 5, y: 3} : to);
    assert.ok(changed.constraints.every(c => c.state === 'Satisfied'));
    console.log(`PASS ${kind}: drag snap, source unchanged, history, native reopen, subsequent coupled edit`);

    await load(baseline); await tool('Line'); await expect(canvas).toHaveAttribute('data-active-tool', 'line');
    await click(target); await click({x: -1, y: 6});
    await expect(canvas).toHaveAttribute('data-entity-count', '3');
    await page.keyboard.press('Escape');
    const created = native(await download(`${kind}-created`));
    assert.ok(created.constraints.some(c => c.kind === relation && c.state === 'Satisfied'));
    console.log(`PASS ${kind}: creation snap stores ${relation}`);
  }
  assert.deepEqual(errors, []);
} finally {await browser.close();}
