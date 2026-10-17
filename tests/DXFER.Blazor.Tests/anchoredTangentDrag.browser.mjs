import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-anchored-tangent';
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
  const load = async buffer => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({ name: 'anchored-tangent.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer) });
    await expect(canvas).toHaveAttribute('data-entity-count', '2'); await tool('Fit extents');
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return { x: r.left + p[0] * +d.scale + +d.offsetX, y: r.top - p[1] * +d.scale + +d.offsetY };
  }, point);
  const drag = async (start, end, target, screenshot) => {
    const from = await screen(start), to = await screen(end);
    await page.mouse.move(from.x, from.y); await page.waitForTimeout(100);
    assert.match(await canvas.getAttribute('data-hovered-id'), target);
    await page.mouse.down(); await page.mouse.move(to.x, to.y, { steps: 10 });
    if (screenshot) await page.screenshot({ path: `${output}/${screenshot}.png` });
    await page.mouse.up(); await page.waitForTimeout(250);
  };
  for (const anchor of ['fix', 'coincident', 'concentric', 'driving']) for (const inside of [false, true]) {
    const fixture = await readFile(new URL(`./fixtures/drag-matrix/anchored-tangent-${anchor}-${inside ? 'internal' : 'external'}.dxf`, import.meta.url));
    const x = inside ? 7 : 13;
    for (const center of [false, true]) {
      // Center movement with a fixed radius may legitimately project along tangency.
      if (anchor === 'driving' && center) continue;
      await load(fixture); const original = native(await download());
      const name = `${anchor}-${inside ? 'internal' : 'external'}-${center ? 'center' : 'perimeter'}`;
      const start = center ? [x, 0] : [x, 3], end = center ? [x + 1, 0] : [x, 4];
      await drag(start, end, center ? /^small\|point\|center/ : /^small\|point\|quadrant/, name);
      const text = await download(name), changed = native(text);
      if (anchor === 'driving') {
        assert.deepEqual(changed.entities, original.entities); assert.deepEqual(changed.dimensions, original.dimensions);
        await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
        console.log(`PASS ${name} refuses unchanged with no history`); continue;
      }
      const large = changed.entities.find(e => e.id === 'large'), small = changed.entities.find(e => e.id === 'small');
      assert.deepEqual(large.center, {x: 0, y: 0});
      assert.ok(Math.abs(large.radius - (inside || center ? 11 : 9)) < 0.05, JSON.stringify(changed));
      assert.ok(Math.abs(small.center.x - (center ? x + 1 : x)) < 0.05);
      assert.ok(Math.abs(small.radius - (center ? 3 : 4)) < 0.05);
      assert.ok(changed.constraints.every(c => c.state === 'Satisfied'));
      await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
      await tool('Redo'); assert.deepEqual(native(await download()).entities, changed.entities);
      await load(text); assert.deepEqual(native(await download()).entities, changed.entities);
      const nextStart = [small.center.x, small.center.y + small.radius];
      await drag(nextStart, [nextStart[0], nextStart[1] + 0.5], /^small\|point\|quadrant/);
      const editedAgain = native(await download());
      assert.ok(editedAgain.constraints.every(c => c.state === 'Satisfied'));
      assert.deepEqual(editedAgain.entities.find(e => e.id === 'large').center, {x: 0, y: 0});
      assert.notDeepEqual(editedAgain.entities, changed.entities);
      console.log(`PASS ${name} held/release, history, native reopen and edit again`);
    }
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
