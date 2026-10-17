import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-line-relation-order';
await mkdir(output, { recursive: true });
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 },
    extraHTTPHeaders: { 'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY } });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(`Browser build: ${build}`);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = name => page.getByRole('button', { name, exact: true }).click();
  const menu = async name => {
    await tool('Open application menu'); await page.getByRole('button', { name: 'File', exact: true }).hover(); await tool(name);
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async (conflict, text) => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    const buffer = text ? Buffer.from(text) : await readFile(new URL(`./fixtures/drag-matrix/line-relation-order-${conflict ? 'conflict' : 'free'}.dxf`, import.meta.url));
    await page.locator('input[type=file]').setInputFiles({ name: 'line-relation.dxf', mimeType: 'application/dxf', buffer });
    await expect(canvas).toHaveAttribute('data-entity-count', '2'); await tool('Fit extents');
  };
  const pick = async (point, id) => {
    const p = await canvas.evaluate((c, v) => {
      const d = c.dataset, r = c.getBoundingClientRect();
      return { x: r.left + v[0] * +d.scale + +d.offsetX, y: r.top - v[1] * +d.scale + +d.offsetY };
    }, point);
    await page.mouse.move(p.x, p.y); await page.waitForTimeout(100);
    assert.equal(await canvas.getAttribute('data-hovered-id'), id);
    await page.mouse.click(p.x, p.y); await page.waitForTimeout(180);
  };
  for (const kind of ['Parallel', 'Perpendicular']) for (const reverse of [false, true]) {
    await load(false); const original = native(await download()); await tool(kind);
    const picks = [[[0, 1.7], 'a'], [[4.9, 3.5], 'b']];
    for (const [point, id] of reverse ? picks.reverse() : picks) await pick(point, id);
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(`${kind}-${reverse}`), solved = native(text);
    assert.equal(solved.constraints.length, 2);
    assert.ok(solved.constraints.every(c => c.state === 'Satisfied'), JSON.stringify(solved.constraints));
    assert.deepEqual(solved.entities.find(e => e.id === 'a'), original.entities.find(e => e.id === 'a'));
    const b = solved.entities.find(e => e.id === 'b');
    assert.ok(Math.abs(Math.hypot(b.end.x-b.start.x, b.end.y-b.start.y) - Math.sqrt(34)) < 1e-8);
    await page.screenshot({path: `${output}/${kind}-${reverse}.png`});
    await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).entities, solved.entities);
    await load(false, text); const reopened = native(await download());
    assert.deepEqual(reopened.entities, solved.entities); assert.deepEqual(reopened.constraints, solved.constraints);
    console.log(`PASS ${kind} reverse=${reverse}, Vertical preserved, history/native reopen`);
  }
  for (const reverse of [false, true]) {
    await load(true); const original = native(await download()); await tool('Parallel');
    const picks = [[[0, 1.7], 'a'], [[4.9, 5], 'b']];
    for (const [point, id] of reverse ? picks.reverse() : picks) await pick(point, id);
    await canvas.focus(); await page.keyboard.press('Escape');
    await expect(page.locator('.dxfer-operation-feedback')).toContainText('Geometry unchanged');
    const text = await download(`conflict-${reverse}`), failed = native(text);
    assert.deepEqual(failed.entities, original.entities);
    assert.equal(failed.constraints.filter(c => c.state === 'Unsatisfied').length, 1);
    assert.equal(failed.constraints.find(c => c.kind === 'Parallel').state, 'Unsatisfied');
    await tool('Undo'); assert.deepEqual(native(await download()).constraints, original.constraints);
    await tool('Redo'); assert.deepEqual(native(await download()).constraints, failed.constraints);
    await load(true, text); assert.deepEqual(native(await download()).constraints, failed.constraints);
    console.log(`PASS conflicting axes reverse=${reverse}, stable failed intent/history/reopen`);
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
