import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-rectangle-tangent';
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
  const load = async text => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    const buffer = text ? Buffer.from(text) : await readFile(new URL('./fixtures/drag-matrix/rectangle-coupled-tangent.dxf', import.meta.url));
    await page.locator('input[type=file]').setInputFiles({ name: 'rectangle-coupled-tangent.dxf', mimeType: 'application/dxf', buffer });
    await expect(canvas).toHaveAttribute('data-entity-count', '6'); await tool('Fit extents');
  };
  const pick = async (point, id) => {
    const p = await canvas.evaluate((c, v) => {
      const d = c.dataset, r = c.getBoundingClientRect();
      return { x: r.left + v[0] * +d.scale + +d.offsetX, y: r.top - v[1] * +d.scale + +d.offsetY };
    }, point);
    await page.mouse.move(p.x, p.y); await page.waitForTimeout(100);
    assert.equal(await canvas.getAttribute('data-hovered-id'), id);
    await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
  };
  for (const reverse of [false, true]) {
    await load(); const original = native(await download()); await tool('Tangent');
    const selections = [[[-2.9, 7], 'bottom'], [[-4 - Math.SQRT2, 3 + Math.SQRT2], 'small']];
    for (const [point, id] of reverse ? selections.reverse() : selections) await pick(point, id);
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(`rectangle-${reverse}`), solved = native(text);
    assert.equal(solved.constraints.length, original.constraints.length + 1);
    assert.ok(solved.constraints.every(c => c.state === 'Satisfied'), JSON.stringify(solved.constraints));
    assert.deepEqual(solved.entities.find(e => e.id === 'large').center, {x: 0, y: 0});
    await page.screenshot({path: `${output}/rectangle-${reverse}.png`});
    await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).entities, solved.entities);
    await load(text); const reopened = native(await download());
    assert.deepEqual(reopened.entities, solved.entities); assert.deepEqual(reopened.constraints, solved.constraints);
    console.log(`PASS rectangle/two-circle Tangent reverse=${reverse}, all existing constraints, history and native reopen`);
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
