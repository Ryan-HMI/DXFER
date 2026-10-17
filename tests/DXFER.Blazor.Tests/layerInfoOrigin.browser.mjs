import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-layer-info-origin';
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
    const buffer = Buffer.from(text ?? await readFile(new URL('./fixtures/drag-matrix/ui-origin-tangent.dxf', import.meta.url)));
    await page.locator('input[type=file]').setInputFiles({ name: 'ui-origin-tangent.dxf', mimeType: 'application/dxf', buffer });
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await tool('Fit extents');
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return { x: r.left + p[0] * +d.scale + +d.offsetX, y: r.top - p[1] * +d.scale + +d.offsetY };
  }, point);
  const pick = async (point, expected) => {
    const p = await screen(point); await page.mouse.move(p.x, p.y); await page.waitForTimeout(100);
    if (expected) assert.match(await canvas.getAttribute('data-hovered-id'), expected);
    await page.mouse.click(p.x, p.y); await page.waitForTimeout(180);
  };
  await load();
  await tool('Add layer');
  const name = page.getByLabel('Layer name', { exact: true });
  for (const text of ['Rapid_LAYER_typing_0123456789', 'Counterbore_and_Form_Detail']) {
    await name.fill(''); await name.pressSequentially(text, { delay: 3 });
    await page.waitForTimeout(500); await expect(name).toHaveValue(text);
  }
  await tool('Rename'); await expect(page.getByRole('button', { name: 'Layer Counterbore_and_Form_Detail', exact: true })).toBeVisible();
  await tool('Undo'); await expect(page.getByRole('button', { name: 'Layer Layer 1', exact: true })).toBeVisible();
  await tool('Redo'); await expect(page.getByRole('button', { name: 'Layer Counterbore_and_Form_Detail', exact: true })).toBeVisible();
  const svgWidths = await page.locator('.dxfer-layers svg').evaluateAll(els => els.map(e => e.getBoundingClientRect().width));
  assert.ok(svgWidths.length > 5 && svgWidths.every(w => w === 18), JSON.stringify(svgWidths));
  await tool('Line'); await tool('Exit tool');
  await expect(page.locator('.dxfer-operation-inspector')).toBeVisible();
  await tool('Hide operation inspector'); await tool('Show operation inspector');
  await expect(page.locator('.dxfer-operation-inspector')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Hide operation inspector' }).locator('circle')).toHaveCount(2);
  const savedLayers = await download('layers');
  await load(savedLayers); await expect(page.getByRole('button', { name: 'Layer Counterbore_and_Form_Detail', exact: true })).toBeVisible();
  console.log('PASS rapid layer typing, rename/history/reopen, compact icons, idle info reopening');
  for (const [width, height] of [[1440, 1000], [1100, 705]]) {
    await page.setViewportSize({ width, height });
    await tool('Open application menu'); await tool('Tool hotkeys');
    const dialog = page.getByRole('dialog', { name: 'Tool hotkey settings' });
    await expect(dialog).toBeVisible(); await page.waitForTimeout(200);
    const d = await dialog.boundingBox(), c = await canvas.boundingBox();
    assert.ok(Math.abs(d.x + d.width / 2 - c.x - c.width / 2) < 2, JSON.stringify({ d, c }));
    assert.ok(Math.abs(d.y + d.height / 2 - c.y - c.height / 2) < 2, JSON.stringify({ d, c }));
    assert.ok(d.width <= 600 && d.height <= 560 && d.x >= c.x && d.y >= c.y);
    assert.equal(await page.locator('.dxfer-hotkey-grid').evaluate(e => getComputedStyle(e).scrollbarWidth), 'thin');
    await page.getByLabel('Search hotkeys').fill('chamfer');
    await expect(page.locator('.dxfer-hotkey-row')).toHaveCount(1);
    await page.getByLabel('Search hotkeys').fill('');
    await expect(page.locator('.dxfer-hotkey-row')).not.toHaveCount(1);
    await page.locator('.dxfer-hotkey-grid').evaluate(e => e.scrollTop = e.scrollHeight);
    await expect.poll(() => page.locator('.dxfer-hotkey-grid').evaluate(e => e.scrollTop)).toBeGreaterThan(0);
    await page.waitForTimeout(100);
    await expect(page.getByRole('button', { name: 'Close', exact: true })).toBeVisible();
    await page.screenshot({ path: `${output}/hotkeys-${width}.png` });
    await page.getByLabel('Search hotkeys').press('Escape'); await expect(dialog).toHaveCount(0);
  }
  console.log('PASS centered field-bounded Hotkeys, search, scrolling, Escape at both desktop sizes');
  await page.setViewportSize({ width: 1440, height: 1000 });
  for (const reversed of [false, true]) {
    await load(); const original = native(await download());
    await tool('Tangent');
    if (reversed) { await pick([14, 8], /^b\|point\|quadrant/); await pick([4, 8], /^a\|point\|quadrant/); }
    else { await pick([4, 8], /^a\|point\|quadrant/); await pick([14, 8], /^b\|point\|quadrant/); }
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(`tangent-${reversed}`), solved = native(text);
    assert.equal(solved.constraints[0].kind, 'Tangent'); assert.equal(solved.constraints[0].state, 'Satisfied');
    assert.deepEqual([...solved.constraints[0].referenceKeys].sort(), ['a', 'b']);
    assert.deepEqual(solved.entities.find(e => e.id === 'guide'), original.entities.find(e => e.id === 'guide'));
    await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).entities, solved.entities);
    await load(text); assert.deepEqual(native(await download()).constraints, solved.constraints);
    console.log(`PASS circle quadrant Tangent reversed=${reversed}, history/reopen`);
  }
  for (const reversed of [false, true]) {
    await load(); const original = native(await download()); await tool('Concentric');
    if (reversed) { await pick([0, 0], /origin/); await pick([4, 5], /^a\|point\|center/); }
    else { await pick([4, 5], /^a\|point\|center/); await pick([0, 0], /origin/); }
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(`origin-${reversed}`), solved = native(text);
    assert.equal(solved.constraints[0].kind, 'Concentric'); assert.equal(solved.constraints[0].state, 'Satisfied');
    assert.deepEqual(solved.entities.find(e => e.id === 'a').center, { x: 0, y: 0 });
    assert.equal(solved.entities.find(e => e.id === 'a').radius, 3);
    assert.deepEqual(solved.entities.filter(e => e.id !== 'a'), original.entities.filter(e => e.id !== 'a'));
    await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).entities, solved.entities);
    await load(text); assert.deepEqual(native(await download()).constraints, solved.constraints);
    const from = await screen([0, 0]), to = await screen([2, 2]);
    await page.mouse.move(from.x, from.y); await page.mouse.down(); await page.mouse.move(to.x, to.y, { steps: 10 });
    await page.screenshot({ path: `${output}/origin-held-${reversed}.png` });
    await page.mouse.up(); await page.waitForTimeout(250);
    assert.deepEqual(native(await download()).entities, solved.entities);
    await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
    const radiusFrom = await screen([3, 0]), radiusTo = await screen([4, 0]);
    await page.mouse.move(radiusFrom.x, radiusFrom.y); await page.mouse.down();
    await page.mouse.move(radiusTo.x, radiusTo.y, { steps: 10 }); await page.mouse.up(); await page.waitForTimeout(250);
    const resized = native(await download(`origin-radius-${reversed}`));
    assert.deepEqual(resized.entities.find(e => e.id === 'a').center, { x: 0, y: 0 });
    assert.ok(Math.abs(resized.entities.find(e => e.id === 'a').radius - 4) < 0.05);
    assert.equal(resized.constraints[0].state, 'Satisfied');
    console.log(`PASS Concentric origin reversed=${reversed}, unchanged peers/radius, history/reopen, refused center drag`);
  }
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
