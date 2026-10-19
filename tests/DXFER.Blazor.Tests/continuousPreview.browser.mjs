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
const selectedCases = process.env.DXFER_CASES?.split(',').filter(Boolean);
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
  const cdp = await page.context().newCDPSession(page);
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
    if (/\/drawingCanvas\.js(?:\?|$)/.test(response.url()) && response.status() === 200) sourceReads.push(response.body().then(body => {
      canvasSha256 = createHash('sha256').update(body).digest('hex');
    }).catch(error => errors.push(`Canvas source hash read: ${error.message}`)));
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
    if (selectedCases && !selectedCases.includes(name)) return;
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

  const pressDecimalSequence = async (input, keys, label, {pauseAfterDecimal = false, whileMoving = false} = {}) => {
    await expect(input).toBeVisible(); await input.focus();
    const node = await input.elementHandle();
    await node.evaluate(el => {
      window.__decimalEvents = [];
      for (const name of ['keydown', 'beforeinput', 'input', 'blur']) el.addEventListener(name, e => {
        window.__decimalEvents.push({event: name, key: e.key, code: e.code, location: e.location, data: e.data,
          value: el.value, focused: document.activeElement === el, time: performance.now()});
      });
    });
    const state = () => node.evaluate(el => ({value: el.value, type: el.type, connected: el.isConnected,
      focused: document.activeElement === el, selectionStart: el.selectionStart, selectionEnd: el.selectionEnd,
      selectedText: window.getSelection()?.toString(), badInput: el.validity.badInput, time: performance.now()}));
    await page.keyboard.press('ControlOrMeta+A');
    const trace = [{phase: 'select-all', ...await state()}];
    let prefix = '';
    for (const key of keys) {
      if (key === 'NumpadDecimal') {
        // Avoid Playwright's NumLock-off/Delete mapping: send the actual keypad decimal text.
        const event = {key: '.', code: 'NumpadDecimal', windowsVirtualKeyCode: 110, nativeVirtualKeyCode: 110, location: 3, isKeypad: true};
        await cdp.send('Input.dispatchKeyEvent', {type: 'keyDown', ...event, text: '.', unmodifiedText: '.'});
        await cdp.send('Input.dispatchKeyEvent', {type: 'keyUp', ...event});
      } else await page.keyboard.press(key);
      if (whileMoving) await move({x: 5 + trace.length * .1, y: -1 - trace.length * .1});
      const decimal = key === '.' || key === 'NumpadDecimal';
      await page.waitForTimeout(pauseAfterDecimal && decimal ? 650 : 35);
      prefix += decimal ? '.' : key;
      const sample = await state();
      const expectedValue = sample.type === 'number'
        ? ['-', '.', '-.'].includes(prefix) ? '' : prefix.replace(/\.$/, '') : prefix;
      trace.push({phase: 'keypress', key, prefix, expectedValue, prefixPassed: sample.value === expectedValue, ...sample});
      await writeFile(`${output}/${label}-trace.json`, JSON.stringify({trace, events: await page.evaluate(() => window.__decimalEvents)}, null, 2));
    }
    await page.waitForTimeout(450);
    trace.push({phase: 'settled', ...await state()});
    await writeFile(`${output}/${label}-trace.json`, JSON.stringify({trace, events: await page.evaluate(() => window.__decimalEvents)}, null, 2));
    await page.screenshot({path: `${output}/${label}.png`});
    return trace;
  };

  const inspectorVariants = [
    {suffix: 'decimal', keys: ['1', '.', '2', '5'], value: 1.25},
    {suffix: 'paused-decimal', keys: ['1', '.', '2', '5'], value: 1.25, pauseAfterDecimal: true},
    {suffix: 'paused-leading-decimal', keys: ['.', '2', '5'], value: .25, pauseAfterDecimal: true}
  ];
  await run('numpad-plain-input-control', async () => {
    const control = await browser.newPage({viewport: {width: 600, height: 200}});
    try {
      await control.setContent('<label>Standalone number input <input type="number" value="0.75" step="any"></label>');
      const session = await control.context().newCDPSession(control);
      const evidence = [];
      for (const method of ['playwright-shift', 'cdp-text']) {
        const input = control.locator('input'); await input.fill('0.75'); await input.focus();
        await input.evaluate(el => {
          window.__plainDecimalEvents = [];
          if (el.dataset.traced) return;
          el.dataset.traced = 'true';
          for (const name of ['keydown', 'beforeinput', 'input']) el.addEventListener(name, e => window.__plainDecimalEvents.push({
            event: name, key: e.key, code: e.code, location: e.location, data: e.data, value: el.value}));
        });
        await control.keyboard.press('ControlOrMeta+A'); await control.keyboard.press('1'); await control.waitForTimeout(35);
        if (method === 'playwright-shift') await control.keyboard.press('Shift+NumpadDecimal');
        else {
          const event = {key: '.', code: 'NumpadDecimal', windowsVirtualKeyCode: 110, nativeVirtualKeyCode: 110, location: 3, isKeypad: true};
          await session.send('Input.dispatchKeyEvent', {type: 'keyDown', ...event, text: '.', unmodifiedText: '.'});
          await session.send('Input.dispatchKeyEvent', {type: 'keyUp', ...event});
        }
        await control.waitForTimeout(650);
        await control.keyboard.press('2'); await control.waitForTimeout(35);
        await control.keyboard.press('5'); await control.waitForTimeout(450);
        evidence.push({method, value: await input.inputValue(), events: await control.evaluate(() => window.__plainDecimalEvents)});
        await control.screenshot({path: `${output}/plain-numpad-${method}.png`});
      }
      await writeFile(`${output}/plain-numpad-control.json`, JSON.stringify(evidence, null, 2));
      assert.equal(evidence[0].value, '125', 'the textless Shift keypad sequence also fails in a plain input outside DXFer');
      assert.ok(!evidence[0].events.some(e => e.event === 'beforeinput' && e.data === '.'));
      assert.equal(evidence[1].value, '1.25');
      assert.ok(evidence[1].events.some(e => e.event === 'keydown' && e.key === '.' && e.code === 'NumpadDecimal' && e.location === 3));
      assert.ok(evidence[1].events.some(e => e.event === 'beforeinput' && e.data === '.'));
    } finally { await control.close(); }
  });
  for (const name of ['Fillet', 'Chamfer']) for (const variant of inspectorVariants) await run(`${name.toLowerCase()}-inspector-${variant.suffix}`, async () => {
    const label = `${name.toLowerCase()}-inspector-${variant.suffix}`;
    const dxf = ['0','SECTION','2','ENTITIES','0','LINE','8','0','10','0','20','0','11','10','21','0',
      '0','LINE','8','0','10','0','20','0','11','0','21','10','0','ENDSEC','0','EOF',''].join('\n');
    await load(dxf, 2); await tool(name); await click({x: 0, y: 0});
    await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
    const input = page.locator('.dxfer-corner-size-control input');
    const trace = await pressDecimalSequence(input, variant.keys, label, variant);
    assert.equal(Number(trace.at(-1).value), variant.value, `${name} inspector must preserve decimal typing`);
    assert.ok(trace.every(s => s.connected && s.focused), 'inspector retains original focused node');
    await page.getByRole('button', {name: 'Confirm', exact: true}).click();
    const saved = await download(`${label}-committed`);
    if (name === 'Fillet') near(saved.doc.entities.find(e => e.kind === 'arc').radius, variant.value);
    else assert.ok(saved.doc.entities.some(e => e.kind === 'line' && Math.abs(e.start.x - variant.value) < 1e-8 && Math.abs(e.end.y - variant.value) < 1e-8));
  });

  const decimalVariants = [
    {suffix: 'leading-decimal', keys: ['.', '2', '5'], value: .25, pauseAfterDecimal: true},
    {suffix: 'zero-leading-decimal', keys: ['0', '.', '2', '5'], value: .25, pauseAfterDecimal: true},
    {suffix: 'paused-decimal', keys: ['1', '.', '2', '5'], value: 1.25, pauseAfterDecimal: true},
    {suffix: 'numpad-decimal', keys: ['1', 'NumpadDecimal', '2', '5'], value: 1.25, pauseAfterDecimal: true},
    {suffix: 'paused-decimal-moving', keys: ['1', '.', '2', '5'], value: 1.25, pauseAfterDecimal: true, whileMoving: true}
  ];
  for (const surface of ['offset', 'existing-dimension']) for (const variant of decimalVariants) {
    if (surface === 'existing-dimension' && variant.whileMoving) continue;
    const label = `${surface}-${variant.suffix}`;
    await run(label, async () => {
      let input, dimensionId;
      if (surface === 'offset') { await beginOffset(); input = field('offset'); }
      else {
        await load();
        const original = await download(`${label}-before`);
        dimensionId = original.doc.dimensions.find(d => d.isDriving).id;
        input = page.locator(`input[data-dimension-id="${dimensionId}"]`);
        await input.dblclick();
      }
      const trace = await pressDecimalSequence(input, variant.keys, label, variant);
      assert.ok(trace.filter(s => s.phase === 'keypress').every(s => s.prefixPassed), 'each prefix survives input sanitization and rerenders');
      assert.ok(trace.every(s => s.connected && s.focused), 'decimal typing keeps original node and focus');
      assert.equal(Number(trace.at(-1).value), variant.value);
      if (variant.suffix === 'numpad-decimal') {
        assert.ok(await page.evaluate(() => window.__decimalEvents.some(e => e.event === 'keydown' && e.code === 'NumpadDecimal' && e.key === '.' && e.location === 3)
          && window.__decimalEvents.some(e => e.event === 'beforeinput' && e.data === '.')), 'actual keypad decimal key and text events');
      }
      await page.keyboard.press('Enter');
      await page.waitForTimeout(350); await canvas.press('Escape');
      const saved = await download(`${label}-committed`);
      const dimension = saved.doc.dimensions.find(d => surface === 'offset' ? d.kind === 'Offset' : d.id === dimensionId);
      assert.equal(dimension.value, variant.value, 'native committed dimension matches typed decimal');
      assert.equal(dimension.isDriving, true);
    });
  }

  for (const negative of [false, true]) await run(negative ? 'pattern-center-negative-decimal' : 'pattern-pitch-paused-decimal', async () => {
    const label = negative ? 'pattern-center-negative-decimal' : 'pattern-pitch-paused-decimal';
    const circle = '0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
    await load(circle, 1); await click({x: 11, y: 10});
    const pick = await screen({x: 11, y: 10}); await page.mouse.click(pick.x, pick.y, {button: 'right'});
    await page.getByRole('dialog', {name: 'Drawing context commands'}).getByRole('button', {name: 'Pattern', exact: true}).click();
    const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
    await expect(editor).toBeVisible();
    if (negative) await editor.getByLabel('Pattern layout', {exact: true}).selectOption('Radial');
    const input = editor.getByLabel(negative ? 'Pattern center X' : 'Pattern X pitch', {exact: true});
    const trace = await pressDecimalSequence(input, negative ? ['-', '.', '2', '5'] : ['1', '.', '2', '5'], label, {pauseAfterDecimal: true});
    assert.ok(trace.every(s => s.connected && s.focused), 'pattern decimal entry retains node and focus');
    assert.equal(Number(trace.at(-1).value), negative ? -.25 : 1.25);
    await editor.getByRole('button', {name: 'Confirm', exact: true}).click(); await expect(editor).toBeHidden();
    const saved = await download(`${label}-committed`);
    const pattern = saved.doc.metadata.patterns[0];
    near(negative ? (pattern.center.x ?? pattern.center[0]) : pattern.xPitch, negative ? -.25 : 1.25);
  });

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
  assert.ok(results.length > 0, 'at least one requested case must run');
  assert.ok(results.every(r => r.passed), `Continuous-preview regression failed; see ${output}/results.json`);
} finally { await browser.close(); }
