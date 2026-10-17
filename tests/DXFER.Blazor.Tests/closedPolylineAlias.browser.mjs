import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-closed-alias-browser';
await mkdir(output, { recursive: true });
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex').toString('utf8'));
};
const close = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
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
  const menu = async name => {
    await page.getByRole('button', { name: 'Open application menu' }).click();
    await page.getByRole('button', { name: 'File', exact: true }).hover();
    await page.getByRole('button', { name, exact: true }).click();
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async (name, text) => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    const buffer = Buffer.from(text ?? await readFile(new URL(`./fixtures/drag-matrix/${name}.dxf`, import.meta.url)));
    await page.locator('input[type=file]').setInputFiles({ name: `${name}.dxf`, mimeType: 'application/dxf', buffer });
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
    await page.getByRole('button', { name: 'Fit extents', exact: true }).click();
    const box = await canvas.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.wheel(0, 300); await page.waitForTimeout(200);
  };
  const drag = async (from, to, expected, name) => {
    const screen = p => canvas.evaluate((c, p) => {
      const d = c.dataset, r = c.getBoundingClientRect();
      return { x: r.left + p[0] * Number(d.scale) + Number(d.offsetX), y: r.top - p[1] * Number(d.scale) + Number(d.offsetY) };
    }, p);
    const a = await screen(from), b = await screen(to);
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(100);
    assert.match(await canvas.getAttribute('data-hovered-id'), /^poly\|point\|vertex-/);
    await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 12 }); await page.waitForTimeout(150);
    const painted = await canvas.evaluate((c, p) => {
      const d = c.dataset, ratio = c.width / c.clientWidth;
      const x = Math.round((p[0] * Number(d.scale) + Number(d.offsetX)) * ratio);
      const y = Math.round((-p[1] * Number(d.scale) + Number(d.offsetY)) * ratio);
      const pixels = c.getContext('2d').getImageData(x - 3, y - 3, 7, 7).data;
      for (let i = 0; i < pixels.length; i += 4) if (pixels[i + 1] > 100 && pixels[i + 3] > 80) return true;
      return false;
    }, [(1 + expected[0]) / 2, (4 + expected[1]) / 2]);
    assert.ok(painted, 'closing edge missing from held preview');
    await page.screenshot({ path: `${output}/${name}-held.png` });
    await page.mouse.up(); await page.waitForTimeout(250);
  };
  for (const mode of ['free', 'fixed', 'driving']) {
    const name = `closed-alias-${mode}`, expected = mode === 'free' ? [-1, 1] : mode === 'fixed' ? [0, 0] : [0, 1];
    await load(name); const original = native(await download());
    await drag([0, 0], [-1, 1], expected, name);
    await expect(page.locator('[aria-label^="Contours:"]')).toHaveAttribute('aria-label', /^Contours: 1\./);
    const text = await download(name), next = native(text), points = next.entities[0].vertices;
    close(points[0].x, expected[0]); close(points[0].y, expected[1]); assert.deepEqual(points[0], points.at(-1));
    assert.deepEqual(points.slice(1, -1), original.entities[0].vertices.slice(1, -1));
    if (mode === 'driving') { close(next.dimensions[0].value, 6); assert.equal(next.dimensions[0].isDriving, true); }
    if (mode === 'fixed') {
      assert.deepEqual(next.entities, original.entities);
      await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
    } else {
      await page.getByRole('button', { name: 'Undo', exact: true }).click();
      assert.deepEqual(native(await download()).entities, original.entities);
      await page.getByRole('button', { name: 'Redo', exact: true }).click();
      assert.deepEqual(native(await download()).entities, next.entities);
    }
    await load(`${name}-reopened`, text);
    assert.deepEqual(native(await download()).entities, next.entities);
    if (mode !== 'fixed') {
      await drag(expected, [0, 0], [0, 0], `${name}-reedit`);
      const edited = native(await download(`${name}-reedit`));
      const restored = edited.entities[0].vertices;
      close(restored[0].x, 0); close(restored[0].y, 0); assert.deepEqual(restored[0], restored.at(-1));
    }
    console.log(`PASS ${name}: held/release, closure, history, native reopen${mode === 'fixed' ? ', refusal/no Undo' : ', reedit'}`);
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
