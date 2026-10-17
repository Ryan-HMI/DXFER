import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-equal-pick';
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
    await tool('Open application menu');
    await page.getByRole('button', { name: 'File', exact: true }).hover();
    await tool(name);
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async text => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    const buffer = Buffer.from(text ?? await readFile(new URL('./fixtures/drag-matrix/equal-arc-pick.dxf', import.meta.url)));
    await page.locator('input[type=file]').setInputFiles({ name: 'equal-arc-pick.dxf', mimeType: 'application/dxf', buffer });
    await expect(canvas).toHaveAttribute('data-entity-count', '2');
    await tool('Fit extents');
  };
  const pick = async (point, midpoint = false) => {
    const p = await canvas.evaluate((c, p) => {
      const d = c.dataset, r = c.getBoundingClientRect();
      return { x: r.left + p[0] * +d.scale + +d.offsetX, y: r.top - p[1] * +d.scale + +d.offsetY };
    }, point);
    await page.mouse.move(p.x, p.y); await page.waitForTimeout(100);
    if (midpoint) assert.match(await canvas.getAttribute('data-hovered-id'), /^arc\|point\|mid/);
    await page.mouse.click(p.x, p.y); await page.waitForTimeout(150);
  };
  for (const reversed of [false, true]) {
    await load(); const original = native(await download());
    await tool('Equal');
    const arc = [10 + 3 / Math.sqrt(2), 10 + 3 / Math.sqrt(2)];
    const circle = [20 + 3 * Math.cos(0.3), 10 + 3 * Math.sin(0.3)];
    if (reversed) { await pick(circle); await pick(arc, true); }
    else { await pick(arc, true); await pick(circle); }
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(`equal-${reversed}`), solved = native(text);
    assert.deepEqual(solved.entities, original.entities);
    assert.equal(solved.constraints.length, 1);
    assert.equal(solved.constraints[0].kind, 'Equal');
    assert.equal(solved.constraints[0].state, 'Satisfied');
    assert.deepEqual([...solved.constraints[0].referenceKeys].sort(), ['arc', 'circle']);
    await page.screenshot({ path: `${output}/equal-${reversed}.png` });
    await tool('Undo'); assert.equal(native(await download()).constraints.length, 0);
    await tool('Redo'); assert.deepEqual(native(await download()).constraints, solved.constraints);
    await load(text);
    assert.deepEqual(native(await download(`reopened-${reversed}`)).constraints, solved.constraints);
    console.log(`PASS midpoint Equal, reversed=${reversed}: unchanged geometry, satisfied entity refs, Undo/Redo/reopen`);
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
