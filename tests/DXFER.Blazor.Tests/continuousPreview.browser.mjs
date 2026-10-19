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
      window.__continuousPreview = {requests: 0, responses: 0, dragRequests: 0, dragResponses: 0};
      const qaInvoke = dotnetRef.invokeMethodAsync.bind(dotnetRef);
      dotnetRef = new Proxy(dotnetRef, {get(target, key) {
        if (key !== 'invokeMethodAsync') return Reflect.get(target, key);
        return async (method, ...args) => {
          if (!['GetOffsetPreview', 'GetOffsetDragPreview'].includes(method)) return qaInvoke(method, ...args);
          const isDrag = method === 'GetOffsetDragPreview';
          window.__continuousPreview[isDrag ? 'dragRequests' : 'requests']++;
          const result = await qaInvoke(method, ...args);
          await new Promise(resolve => setTimeout(resolve, ${previewDelayMs}));
          window.__continuousPreview[isDrag ? 'dragResponses' : 'responses']++;
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
    if (process.env.DXFER_EXPECTED_REVISION) {
      await expect(page.locator('.app-brand')).toHaveAttribute('title', new RegExp(process.env.DXFER_EXPECTED_REVISION));
    }
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
        // Span several dash periods so a gap cannot masquerade as a missing contour.
        const pixels = c.getContext('2d').getImageData(x - 16, y1, 32, y2 - y1).data;
        let ink = 0, rowSum = 0;
        const rows = [];
        for (let j = 0; j < pixels.length; j += 4) if (Math.max(pixels[j], pixels[j + 1], pixels[j + 2]) > 160) {
          const row = Math.floor(j / 4 / 32);
          ink++; rowSum += row; rows[row] = (rows[row] ?? 0) + 1;
        }
        const centroidRow = ink ? y1 + rowSum / ink : null;
        return {ink, centroidRow, worldY: ink ? (+c.dataset.offsetY - centroidRow / ratio) / +c.dataset.scale : null,
          rows: rows.map((count, row) => count ? {row: y1 + row, count} : null).filter(Boolean),
          time: performance.now(), input: Boolean(document.querySelector('input[data-dimension-key="offset"]')), ...window.__continuousPreview};
      }));
      samples.at(-1).pointerWorldY = -.8 - i * .025;
    }
    await writeFile(`${output}/continuous-samples.json`, JSON.stringify(samples, null, 2));
    await page.screenshot({path: `${output}/continuous-motion.png`});
    if (previewDelayMs > 0) assert.ok(samples.at(-1).responses >= 3, 'multiple delayed responses arrive during motion');
    assert.ok(samples.slice(5).every(s => s.ink > 0 && s.input), 'last valid preview and dimension remain visible throughout ongoing motion');
    const positions = samples.slice(5).map(s => s.worldY);
    assert.ok(positions[0] - positions.at(-1) > .3, 'preview edge advances at least 0.3 world units while the pointer is still moving, not just after it stops');
    assert.ok(new Set(positions.map(y => Math.round(y / .05))).size >= 4, 'ongoing motion produces at least four distinct preview edge positions, not a frozen visible frame');
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
  });

  for (const whileMoving of [false, true]) await run(whileMoving ? 'offset-direct-replace-while-moving' : 'offset-direct-replace-prefixes', async () => {
    const label = whileMoving ? 'offset-direct-replace-moving' : 'offset-direct-replace';
    await beginOffset();
    const input = field('offset');
    await input.focus();
    const node = await input.elementHandle();
    const readState = () => node.evaluate(el => ({connected: el.isConnected, focused: document.activeElement === el,
      sameNode: document.querySelector('input[data-dimension-key="offset"]') === el,
      type: el.type, value: el.value, selectionStart: el.selectionStart, selectionEnd: el.selectionEnd,
      selectionDirection: el.selectionDirection, selectedText: window.getSelection()?.toString(),
      badInput: el.validity.badInput, time: performance.now()}));
    const trace = [{phase: 'before', ...await readState()}];
    await page.keyboard.press('ControlOrMeta+A');
    trace.push({phase: 'select-all', ...await readState()});
    const failures = [];
    let prefix = '';
    for (const key of ['1', '.', '2', '5']) {
      await page.keyboard.press(key);
      if (whileMoving) await move({x: 5 + prefix.length * .2, y: -.9 - prefix.length * .15});
      await page.waitForTimeout(35);
      prefix += key;
      const sample = await readState();
      // Number inputs expose neither a caret API nor the editor's trailing decimal point.
      const expectedValue = sample.type === 'number' && prefix.endsWith('.') ? prefix.slice(0, -1) : prefix;
      const passed = sample.connected && sample.focused && sample.sameNode && sample.value === expectedValue;
      trace.push({phase: 'keypress', key, prefix, expectedValue, passed, ...sample});
      if (!passed) failures.push(`prefix ${prefix}: ${JSON.stringify(sample)}`);
      await writeFile(`${output}/${label}-trace.json`, JSON.stringify(trace, null, 2));
    }
    await page.waitForTimeout(450);
    const settled = await readState();
    trace.push({phase: 'settled', ...settled});
    await writeFile(`${output}/${label}-trace.json`, JSON.stringify(trace, null, 2));
    await page.screenshot({path: `${output}/${label}.png`});
    if (!(settled.connected && settled.focused && settled.sameNode && settled.value === '1.25')) failures.push('settled input lost exact 1.25 or its focused node');
    await page.keyboard.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape');
    const saved = await download(`${label}-committed`);
    const dimension = saved.doc.dimensions.find(d => d.kind === 'Offset');
    if (!(dimension?.isDriving && dimension.value === 1.25)) failures.push(`committed offset was ${JSON.stringify(dimension)}`);
    assert.deepEqual(failures, [], 'every direct Ctrl+A replacement prefix, original input node/focus and committed 1.25 must survive');
    const polyline = saved.doc.entities.at(-1);
    near(Math.min(...polyline.vertices.map(p => p.x)), -1.25);
    near(Math.min(...polyline.vertices.map(p => p.y)), -1.25);
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await tool('Redo'); assert.deepEqual((await download(`${label}-redo`)).doc, saved.doc);
    await load(saved.text, 5);
    const reopened = (await download(`${label}-reopened`)).doc;
    assert.deepEqual(reopened, {...saved.doc, metadata: {...saved.doc.metadata, sourceSha256: createHash('sha256').update(saved.text).digest('hex')}});
  });

  await run('linked-offset-held-source-motion', async () => {
    await load(await readFile(new URL('./fixtures/selection-offset/unsupported.dxf', import.meta.url)), 2);
    await click({x: 7, y: 0});
    await tool('Offset'); await move({x: 7, y: 1});
    await typeExact('offset', '1', 'held-drag-gap');
    await page.keyboard.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const linked = await download('held-drag-linked');
    assert.equal(linked.doc.constraints.find(c => c.kind === 'Offset')?.state, 'Satisfied');
    await canvas.press('Escape'); await page.waitForTimeout(250);
    await move({x: 2, y: 0});
    await expect(canvas).toHaveAttribute('data-hovered-id', 'line');
    const samples = [];
    await page.mouse.down();
    try {
      for (let i = 0; i < 30; i++) {
        const pointerWorldY = .05 + i * .05;
        await move({x: 2, y: pointerWorldY}); await page.waitForTimeout(25);
        const sample = await canvas.evaluate(c => {
          const ratio = c.width / c.getBoundingClientRect().width;
          const x = Math.round((8 * +c.dataset.scale + +c.dataset.offsetX) * ratio);
          const y1 = Math.round((-3 * +c.dataset.scale + +c.dataset.offsetY) * ratio);
          const y2 = Math.round((-.5 * +c.dataset.scale + +c.dataset.offsetY) * ratio);
          const pixels = c.getContext('2d').getImageData(x - 4, y1, 9, y2 - y1).data;
          const rows = [];
          for (let j = 0; j < pixels.length; j += 4) {
            if (pixels[j + 1] > 130 && pixels[j + 1] > pixels[j] + 70 && pixels[j + 1] > pixels[j + 2] + 50) {
              const row = Math.floor(j / 4 / 9);
              rows[row] = (rows[row] ?? 0) + 1;
            }
          }
          // The upper green line is the dependent offset, away from pointer, labels and source highlight.
          const firstRow = rows.findIndex(count => count > 0);
          return {time: performance.now(), topWorldY: firstRow < 0 ? null : (+c.dataset.offsetY - (y1 + firstRow) / ratio) / +c.dataset.scale,
            rows: rows.map((count, row) => count ? {row: y1 + row, count} : null).filter(Boolean), ...window.__continuousPreview};
        });
        samples.push({pointerWorldY, pointerHeld: true, ...sample});
      }
      await writeFile(`${output}/held-drag-samples.json`, JSON.stringify(samples, null, 2));
      await page.screenshot({path: `${output}/linked-offset-during-held-motion.png`});
      const positions = samples.slice(5).map(s => s.topWorldY);
      assert.ok(positions.every(y => y !== null), 'dependent offset remains visible throughout held source movement');
      assert.ok(positions.at(-1) - positions[0] > .7, 'dependent offset advances during held movement, before pointer-up');
      assert.ok(new Set(positions.map(y => Math.round(y / .1))).size >= 4, 'dependent offset has at least four distinct held preview positions');
      if (previewDelayMs > 0) assert.ok(samples.at(-1).dragResponses >= 3, 'multiple delayed shared drag previews arrive while pointer is held');
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(450);
    const moved = await download('held-drag-released');
    near(moved.doc.entities.find(e => e.id === 'line').start.y, 1.5);
    near(moved.doc.entities.at(-1).start.y, 2.5);
    assert.deepEqual(moved.doc.entities.find(e => e.id === 'point'), linked.doc.entities.find(e => e.id === 'point'));
    assert.equal(moved.doc.constraints.find(c => c.kind === 'Offset')?.state, 'Satisfied');
    await tool('Undo'); assert.deepEqual((await download('held-drag-undo')).doc.entities, linked.doc.entities);
    await tool('Redo'); assert.deepEqual((await download('held-drag-redo')).doc, moved.doc);
    await load(moved.text, 3);
    assert.deepEqual((await download('held-drag-reopened')).doc.entities, moved.doc.entities);
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
