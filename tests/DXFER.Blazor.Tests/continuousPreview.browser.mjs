import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/continuous-preview';
await mkdir(output, {recursive: true});
const fixture = await readFile(new URL('./fixtures/selection-offset/rectangle.dxf', import.meta.url));
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-8, `${a} != ${b}`);
const results = [], errors = [];
const previewDelayMs = Number(process.env.DXFER_PREVIEW_DELAY_MS ?? 180);
assert.ok(Number.isFinite(previewDelayMs) && previewDelayMs >= 0 && previewDelayMs <= 5000);
const baselineRevision = process.env.DXFER_CANVAS_REVISION;
if (baselineRevision) assert.match(baselineRevision, /^[a-f0-9]{7,40}$/);
const baselineSource = baselineRevision ? execFileSync('git', ['show', `${baselineRevision}:src/DXFER.Blazor/wwwroot/drawingCanvas.js`], {encoding: 'utf8', maxBuffer: 4 * 1024 * 1024}) : null;
let canvasSha256;
const sourceReads = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', d => d.accept());
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text()); });
  // Test-only response latency at the interop boundary. Real requests/results and all rendering logic are unchanged.
  if (previewDelayMs > 0 || baselineSource) await page.route('**/drawingCanvas.js*', async route => {
    const response = await route.fetch();
    const source = baselineSource ?? await response.text();
    canvasSha256 = createHash('sha256').update(source).digest('hex');
    const signature = 'export function createDrawingCanvas(canvas, dotnetRef, dimensionOverlay = null, constraintIcons = {}) {';
    assert.equal(source.split(signature).length, 2, 'latency hook must match exactly one canvas factory');
    const hook = `
      window.__continuousPreview = {requests: 0, responses: 0};
      const qaInvoke = dotnetRef.invokeMethodAsync.bind(dotnetRef);
      dotnetRef = new Proxy(dotnetRef, {get(target, key) {
        if (key !== 'invokeMethodAsync') return Reflect.get(target, key);
        return async (method, ...args) => {
          if (method !== 'GetOffsetPreview') return qaInvoke(method, ...args);
          window.__continuousPreview.requests++;
          const result = await qaInvoke(method, ...args);
          await new Promise(resolve => setTimeout(resolve, ${previewDelayMs}));
          window.__continuousPreview.responses++;
          return result;
        };
      }});
    `;
    await route.fulfill({response, body: previewDelayMs > 0 ? source.replace(signature, signature + hook) : source});
  });
  else page.on('response', response => {
    if (/\/drawingCanvas\.js(?:\?|$)/.test(response.url())) sourceReads.push(response.body().then(body => {
      canvasSha256 = createHash('sha256').update(body).digest('hex');
    }));
  });
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  };
  await ready();
  const canvas = page.locator('canvas.drawing-canvas');
  const field = key => page.locator(`input[data-dimension-key="${key}"]`);
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); return {text, doc: native(text)};
  };
  const load = async (buffer = fixture, count = 4) => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'continuous-preview.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
    let previousView, stableSamples = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY,
        c.getBoundingClientRect().toJSON()]));
      stableSamples = view === previousView ? stableSamples + 1 : 0;
      previousView = view;
      return stableSamples;
    }, {message: 'Fit and viewport reflow settle before computing pointer coordinates', intervals: [80, 100, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const move = async p => { const s = await screen(p); await page.mouse.move(s.x, s.y); };
  const click = async p => { const s = await screen(p); await page.mouse.click(s.x, s.y); };
  const beginOffset = async (through = {x: 5, y: -.75}) => {
    await load();
    const a = await screen({x: -.5, y: 6.5}), b = await screen({x: 10.5, y: -.5});
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(100); await page.mouse.down();
    try { await page.mouse.move(b.x, b.y, {steps: 12}); } finally { await page.mouse.up(); }
    await expect(canvas).toHaveAttribute('data-selected-count', '4');
    await tool('Offset'); await move(through);
    await expect(field('offset')).toBeVisible();
  };
  const run = async (name, action) => {
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (e) {
      results.push({name, passed: false, error: e.stack}); console.error(`FAIL ${name}: ${e.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, results, errors}, null, 2));
    await ready();
  };
  const typeExact = async (key, value, label) => {
    const input = field(key); await expect(input).toBeVisible(); await input.focus();
    const node = await input.elementHandle();
    await page.keyboard.press('ControlOrMeta+A'); await page.keyboard.press('Backspace');
    await expect(input).toHaveValue(''); await expect(input).toBeFocused();
    const trace = [];
    for (const character of value) {
      await page.keyboard.type(character); await page.waitForTimeout(35);
      const sample = await node.evaluate(el => ({connected: el.isConnected, focused: document.activeElement === el, value: el.value}));
      trace.push({character, ...sample});
      await writeFile(`${output}/${label}-keystrokes.json`, JSON.stringify(trace, null, 2));
      assert.ok(sample.connected && sample.focused, 'each keystroke retains the original focused input');
    }
    await page.waitForTimeout(450);
    const state = await node.evaluate(el => ({connected: el.isConnected, focused: document.activeElement === el, value: el.value}));
    await writeFile(`${output}/${label}-typing.json`, JSON.stringify(state, null, 2));
    assert.deepEqual(state, {connected: true, focused: true, value}, 'typing preserves the same focused input and every decimal character');
    await expect(input).toHaveValue(value);
  };

  await run('offset-continuous-motion', async () => {
    await beginOffset();
    const samples = [];
    for (let i = 0; i < 30; i++) {
      await move({x: 4 + i * .055, y: -.8 - i * .025});
      await page.waitForTimeout(25);
      samples.push(await canvas.evaluate(c => {
        // Look away from the cursor/dimension marker, outside the original rectangle.
        const ratio = c.width / c.getBoundingClientRect().width;
        const x = Math.round((1.5 * +c.dataset.scale + +c.dataset.offsetX) * ratio);
        const y1 = Math.round((.55 * +c.dataset.scale + +c.dataset.offsetY) * ratio);
        const y2 = Math.round((1.8 * +c.dataset.scale + +c.dataset.offsetY) * ratio);
        const pixels = c.getContext('2d').getImageData(x - 2, y1, 5, y2 - y1).data;
        let ink = 0;
        for (let j = 0; j < pixels.length; j += 4) if (Math.max(pixels[j], pixels[j + 1], pixels[j + 2]) > 160) ink++;
        return {ink, input: Boolean(document.querySelector('input[data-dimension-key="offset"]')), ...window.__continuousPreview};
      }));
    }
    await writeFile(`${output}/continuous-samples.json`, JSON.stringify(samples, null, 2));
    await page.screenshot({path: `${output}/continuous-motion.png`});
    if (previewDelayMs > 0) assert.ok(samples.at(-1).responses >= 3, 'multiple delayed responses arrive during motion');
    assert.ok(samples.slice(5).every(s => s.ink > 0 && s.input), 'last valid preview and dimension remain visible throughout ongoing motion');
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
  });

  await run('offset-decimal-history', async () => {
    await beginOffset();
    await typeExact('offset', '1.25', 'offset-first');
    await typeExact('offset', '.75', 'offset-leading-decimal');
    await typeExact('offset', '2.5', 'offset-retype');
    await typeExact('offset', '0', 'offset-invalid-zero');
    await field('offset').press('Enter'); await page.waitForTimeout(400);
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await page.screenshot({path: `${output}/offset-invalid-unchanged.png`});
    await typeExact('offset', '1.25', 'offset-final');
    await page.screenshot({path: `${output}/offset-keyed.png`});
    await field('offset').press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape');
    const saved = await download('offset-committed');
    const dimension = saved.doc.dimensions.find(d => d.kind === 'Offset');
    assert.ok(dimension?.isDriving); assert.equal(dimension.value, 1.25);
    const polyline = saved.doc.entities.at(-1);
    near(Math.min(...polyline.vertices.map(p => p.x)), -1.25);
    near(Math.max(...polyline.vertices.map(p => p.x)), 11.25);
    near(Math.min(...polyline.vertices.map(p => p.y)), -1.25);
    near(Math.max(...polyline.vertices.map(p => p.y)), 7.25);
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await tool('Redo'); assert.deepEqual((await download('offset-redo')).doc, saved.doc);
    await load(saved.text, 5);
    const reopened = (await download('offset-reopened')).doc;
    assert.deepEqual(reopened, {...saved.doc, metadata: {...saved.doc.metadata, sourceSha256: createHash('sha256').update(saved.text).digest('hex')}});
  });

  await run('offset-collapsed-recovery', async () => {
    await beginOffset({x: 5, y: 1});
    await typeExact('offset', '4', 'collapsed-inset');
    const input = await field('offset').elementHandle();
    await field('offset').press('Enter'); await page.waitForTimeout(450);
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    assert.deepEqual(await input.evaluate(el => ({connected: el.isConnected, focused: document.activeElement === el, value: el.value})),
      {connected: true, focused: true, value: '4'}, 'collapsed inset keeps the editable input alive');
    await page.screenshot({path: `${output}/collapsed-inset-unchanged.png`});
    await typeExact('offset', '1.25', 'collapsed-recovery');
    await field('offset').press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape');
    const saved = await download('inset-recovered');
    assert.equal(saved.doc.dimensions.find(d => d.kind === 'Offset').value, 1.25);
    near(Math.min(...saved.doc.entities.at(-1).vertices.map(p => p.x)), 1.25);
    near(Math.max(...saved.doc.entities.at(-1).vertices.map(p => p.y)), 4.75);
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
  });

  for (const kind of ['line', 'rectangle']) await run(`${kind}-decimal-typing`, async () => {
    await load();
    await tool(kind === 'line' ? 'Line' : 'Two-point rectangle');
    await click({x: 3, y: 3}); await move({x: 6, y: 4.5});
    const key = kind === 'line' ? 'length' : 'width';
    await typeExact(key, '2.75', `${kind}-first`);
    await typeExact(key, '1.25', `${kind}-retype`);
    await page.screenshot({path: `${output}/${kind}-keyed.png`});
    await field(key).press('Enter');
    if (kind === 'rectangle') {
      await expect(field('height')).toBeFocused();
      await typeExact('height', '.75', 'rectangle-height-first');
      await typeExact('height', '1.5', 'rectangle-height-retype');
      await field('height').press('Enter');
    }
    const count = kind === 'rectangle' ? 8 : 5;
    await expect(canvas).toHaveAttribute('data-entity-count', String(count)); await canvas.press('Escape');
    const saved = await download(`${kind}-committed`);
    assert.ok(saved.doc.dimensions.some(d => d.isDriving && d.value === 1.25));
    if (kind === 'rectangle') assert.ok(saved.doc.dimensions.some(d => d.isDriving && d.value === 1.5));
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await tool('Redo'); assert.deepEqual((await download(`${kind}-redo`)).doc, saved.doc);
    await load(saved.text, count);
    const reopened = (await download(`${kind}-reopened`)).doc;
    assert.deepEqual(reopened.entities, saved.doc.entities); assert.deepEqual(reopened.dimensions, saved.doc.dimensions);
  });
  const build = await page.locator('.app-brand').getAttribute('title');
  await Promise.all(sourceReads);
  assert.ok(canvasSha256, 'capture the actual tested canvas source hash');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, baselineRevision, previewDelayMs, canvasSha256, results, errors}, null, 2));
  assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `Continuous-preview regression failed; see ${output}/results.json`);
} finally { await browser.close(); }
