import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-snap-cue-occlusion';
const baseline = process.env.DXFER_OCCLUSION_BASELINE === '1';
await mkdir(output, {recursive: true});
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC','0','SECTION','2','ENTITIES',
  '0','LINE','8','0','10','2','20','2','11','8','21','5',
  '0','POINT','8','0','10','-4','20','-4','0','POINT','8','0','10','14','20','12','0','ENDSEC','0','EOF',''].join('\n');
const browser = await chromium.launch({headless: true});
const results = [], errors = [], exports = [], sourceReads = [];
let build, canvasUrl, canvasSha256;
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', d => d.accept()); page.on('pageerror', e => errors.push(e.message));
  page.on('response', response => {
    if (/\/drawingCanvas\.js(?:\?|$)/.test(response.url()) && response.status() === 200)
      sourceReads.push(response.body().then(b => { canvasUrl = response.url(); canvasSha256 = createHash('sha256').update(b).digest('hex'); }));
  });
  if (baseline) await page.addInitScript(() => {
    const original = CanvasRenderingContext2D.prototype.fillRect;
    CanvasRenderingContext2D.prototype.fillRect = function(x, y, width, height) {
      if (this.canvas.matches('.drawing-canvas') && height === 44 && width > 50)
        this.canvas.__observedSnapCueBox = {x, y, width, height};
      return original.apply(this, arguments);
    };
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  const originalStorage = await page.evaluate(() => Object.fromEntries(Object.entries(localStorage)));
  const canvas = page.locator('canvas.drawing-canvas');
  const grain = page.locator('.dxfer-tool-group').filter({has: page.locator('.dxfer-section-title', {hasText: /^Grain$/})});
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  let originalAuto;
  const setting = async enabled => {
    await menu('Settings'); const checkbox = page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true});
    originalAuto ??= await checkbox.isChecked(); await checkbox.setChecked(enabled); await tool('Close settings');
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8'); await writeFile(`${output}/${name}.dxf`, text);
    const lines = text.trim().split(/\r?\n/), checksums = []; let hex = '';
    for (let i = 0; i < lines.length; i += 2) {
      if (lines[i].trim() === '310') hex += lines[i + 1].trim();
      if (lines[i].trim() === '1') checksums.push(lines[i + 1].trim());
    }
    const bytes = Buffer.from(hex, 'hex');
    assert.ok(hex && checksums.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()));
    exports.push({name, checksumValid: true}); return JSON.parse(bytes.toString('utf8'));
  };
  const screen = (x, y) => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().x + +c.dataset.offsetX + p.x * +c.dataset.scale,
    y: c.getBoundingClientRect().y + +c.dataset.offsetY - p.y * +c.dataset.scale}), {x, y});
  const hover = async enabled => {
    const p = await screen(8, 5); await page.mouse.move(p.x, p.y);
    await expect(canvas).toHaveAttribute('data-snap-cue', enabled ? /Endpoint.*Coincident/i : /Endpoint.*Snap only/i);
    await page.waitForTimeout(100);
  };
  const drag = async (x, y) => {
    const header = grain.getByRole('button', {name: 'Drag Grain tools', exact: true});
    const b = await header.boundingBox(); await page.mouse.move(b.x + 16, b.y + 12); await page.mouse.down();
    try { await page.waitForTimeout(80); await page.mouse.move(x + 16, y + 12, {steps: 10}); await page.waitForTimeout(100); }
    finally { await page.mouse.up(); }
    await page.waitForTimeout(150);
  };
  const geometry = async () => canvas.evaluate((c, baseline) => {
    const local = baseline ? c.__observedSnapCueBox : JSON.parse(c.dataset.snapCueBox || 'null');
    if (!local) throw new Error('No observed/rendered snap cue box');
    const bounds = c.getBoundingClientRect();
    const box = {...local, x: local.x + bounds.x, y: local.y + bounds.y};
    const obstacles = [...document.querySelectorAll('.dxfer-tool-group, .dxfer-sync-control-bar, input[data-dimension-key], .drawing-dimension-input')]
      .filter(e => e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden')
      .map(e => { const r = e.getBoundingClientRect(); return {name: e.getAttribute('aria-label') || e.textContent.trim().slice(0, 60) || e.className,
        x: r.x, y: r.y, width: r.width, height: r.height}; }).filter(r => r.width > 0 && r.height > 0);
    const collisions = obstacles.filter(r => box.x < r.x + r.width && box.x + box.width > r.x && box.y < r.y + r.height && box.y + box.height > r.y);
    return {cue: c.dataset.snapCue, local, box, obstacles, collisions, canvas: bounds.toJSON()};
  }, baseline);
  const capture = async name => {
    const state = await geometry(); await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify(state, null, 2)); return state;
  };
  try {
    for (const [width, height] of baseline ? [[1188, 761]] : [[1188, 761], [1100, 705]]) {
      for (const enabled of baseline ? [false] : [false, true]) {
        const name = `${width}-${enabled ? 'on' : 'off'}`;
        try {
          await page.setViewportSize({width, height}); await canvas.press('Escape'); await menu('New drawing');
          await page.locator('input[type=file]').setInputFiles({name: 'snap-occlusion.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture)});
          await expect(canvas).toHaveAttribute('data-entity-count', '3'); await tool('Fit extents'); await page.waitForTimeout(300);
          await setting(enabled); const before = await download(`${name}-before`);
          await tool('Line'); await hover(enabled);
          const preferred = (await geometry()).box;
          // Leave the endpoint exposed while placing Grain over the preferred cue rectangle.
          await drag(preferred.x + 8, preferred.y + 4);
          await expect(grain).toHaveClass(/dxfer-tool-group-floating/);
          await hover(enabled); const blocked = await capture(`${name}-floating`);
          const violations = [...blocked.collisions];
          if (!baseline) {
            const anchor = await screen(8, 5);
            await drag(anchor.x - 170, anchor.y + 30); await hover(enabled);
            violations.push(...(await capture(`${name}-relocated`)).collisions);
            const bounds = await canvas.boundingBox(); await drag(bounds.x + bounds.width - 25, 240);
            await expect(grain).toHaveClass(/dxfer-tool-group-right/);
            await grain.getByRole('button', {name: 'Compact Grain tools', exact: true}).click();
            await hover(enabled); violations.push(...(await capture(`${name}-compact`)).collisions);
            await grain.getByRole('button', {name: 'Expand Grain tools', exact: true}).click();
            await canvas.press('Escape'); await tool('Line');
            const start = await screen(2, 2); await page.mouse.click(start.x, start.y);
            await hover(enabled);
            await expect(page.locator('input[data-dimension-key="length"]')).toBeVisible();
            violations.push(...(await capture(`${name}-dimension-input`)).collisions);
          }
          await canvas.press('Escape'); await expect(canvas).toHaveAttribute('data-snap-cue', '');
          await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
          const after = await download(`${name}-after`);
          for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(after[key], before[key], `${key} unchanged`);
          assert.deepEqual(violations, [], 'snap cue must not overlap palettes, sync bar or dimension inputs');
          results.push({name, passed: true}); console.log(`PASS ${name}`);
        } catch (error) {
          results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
          await page.screenshot({path: `${output}/${name}-failure.png`});
        }
        await Promise.all(sourceReads);
        await writeFile(`${output}/results.json`, JSON.stringify({url, build, baseline, canvasUrl, canvasSha256, results, errors, exports}, null, 2));
      }
    }
  } finally {
    await canvas.press('Escape');
    if (originalAuto !== undefined) await setting(originalAuto);
    // This is an isolated browser context; restore its original preferences before disposal as well.
    await page.evaluate(saved => { localStorage.clear(); for (const [key, value] of Object.entries(saved)) localStorage.setItem(key, value); }, originalStorage);
  }
  assert.deepEqual(errors, []); assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
