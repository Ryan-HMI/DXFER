import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated candidate URL; this test never selects a host implicitly');
assert.ok(['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-line-slot';
await mkdir(output, {recursive: true});
const fixture = '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n5\n100\n8\n0\n10\n2\n20\n3\n11\n14\n21\n3\n0\nENDSEC\n0\nEOF\n';
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'native document payload');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  metadata: doc.metadata});
const close = (a, b, label) => assert.ok(Math.abs(a - b) < 1e-6, `${label}: ${a} != ${b}`);
const pointClose = (a, b, label) => { close(a.x, b.x, `${label} X`); close(a.y, b.y, `${label} Y`); };
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const owner = reference => reference.split(/[:|]/)[0];
const midpoint = line => ({x: (line.start.x + line.end.x) / 2, y: (line.start.y + line.end.y) / 2});

function assertSlot(doc, sourceId, width, originalSource, keyedWidth = true, sourceTolerance = 0) {
  const source = doc.entities.find(e => e.id === sourceId);
  assert.ok(source, 'original centerline ID retained');
  assert.equal(source.kind, 'line'); assert.equal(source.isConstruction, true);
  if (originalSource) {
    for (const end of ['start', 'end']) for (const axis of ['x', 'y'])
      assert.ok(Math.abs(source[end][axis] - originalSource[end][axis]) <= sourceTolerance, `source ${end}.${axis} unchanged within ${sourceTolerance}`);
    assert.deepEqual({...source, start: originalSource.start, end: originalSource.end},
      {...originalSource, isConstruction: true}, 'source ID and metadata retained');
  }
  assert.equal(doc.entities.length, 5, 'one retained source plus two sides and two arcs');
  const arcs = doc.entities.filter(e => e.kind === 'arc');
  const sides = doc.entities.filter(e => e.kind === 'line' && e.id !== sourceId);
  assert.equal(arcs.length, 2); assert.equal(sides.length, 2);
  const tangent = doc.constraints.filter(c => c.kind.toLowerCase() === 'tangent');
  assert.equal(tangent.length, 4, 'all four permanent tangencies');
  const axis = {x: source.end.x - source.start.x, y: source.end.y - source.start.y};
  const length = Math.hypot(axis.x, axis.y), normal = {x: -axis.y / length, y: axis.x / length};
  assert.ok(length > 1e-6);
  const offsetSigns = [];
  for (const side of sides) {
    assert.equal(side.layer, source.layer); assert.ok(!side.isConstruction);
    const start = distance(side.start, source.start) < distance(side.end, source.start) ? side.start : side.end;
    const end = start === side.start ? side.end : side.start;
    const signed = (start.x - source.start.x) * normal.x + (start.y - source.start.y) * normal.y;
    close(Math.abs(signed), width / 2, 'side offset is half full width'); offsetSigns.push(Math.sign(signed));
    pointClose(start, {x: source.start.x + normal.x * signed, y: source.start.y + normal.y * signed}, 'side start');
    pointClose(end, {x: source.end.x + normal.x * signed, y: source.end.y + normal.y * signed}, 'side end');
    for (const arc of arcs) assert.ok(tangent.some(c => c.referenceKeys.some(r => owner(r) === side.id)
      && c.referenceKeys.some(r => owner(r) === arc.id)), 'each side tangent to each cap');
  }
  assert.deepEqual(offsetSigns.sort(), [-1, 1]);
  for (const endName of ['start', 'end']) {
    const arc = arcs.find(e => distance(e.center, source[endName]) < 1e-6);
    assert.ok(arc, `arc center follows source ${endName}`); close(arc.radius, width / 2, 'cap radius');
    close(((arc.endAngleDegrees - arc.startAngleDegrees) % 360 + 360) % 360, 180, 'semicircular cap sweep');
    assert.equal(arc.layer, source.layer); assert.ok(!arc.isConstruction);
    assert.ok(doc.constraints.some(c => c.kind.toLowerCase() === 'coincident'
      && c.referenceKeys.some(r => r === `${sourceId}:${endName}` || r === `${sourceId}|point|${endName}`)
      && c.referenceKeys.some(r => r === `${arc.id}:center` || r === `${arc.id}|point|center`)),
    `persistent source ${endName}/cap center attachment`);
  }
  const driving = doc.dimensions.filter(d => d.isDriving);
  assert.equal(driving.length, keyedWidth ? 1 : 0, 'only keyed width creates a permanent dimension');
  if (keyedWidth) close(driving[0].value, width, 'stored full width');
  return {source, dimension: driving[0]};
}

// Contract: only temporary browser fixtures; normal pointer/key input for edits.
// Existing-line slots expose a full Width, not the old three-point Radius input.
const widthSelector = 'input[data-dimension-key="width"]';
const results = {cases: [], previews: [], exports: [], errors: [], passed: false};
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
let page, failure;
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => results.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) results.errors.push(message.text());
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas'), width = page.locator(widthSelector);
  const command = name => page.getByRole('button', {name, exact: true});
  const menu = async name => {
    await command('Open application menu').click(); await command('File').hover(); await command(name).click();
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); results.exports.push(name);
    return {text, doc: decode(text)};
  };
  const settle = async () => {
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0; previous = view; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const load = async (text, count) => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'line-slot.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await command('Fit extents').click(); await settle();
    await expect(command('Undo')).toBeDisabled(); await expect(command('Redo')).toBeDisabled();
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const move = async point => {
    const p = await screen(point);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'canvas target is unobscured');
    await page.mouse.move(p.x, p.y, {steps: 6}); return p;
  };
  const pick = async source => {
    const p = await move({x: source.start.x + (source.end.x - source.start.x) * .4,
      y: source.start.y + (source.end.y - source.start.y) * .4});
    await expect.poll(() => canvas.getAttribute('data-hovered-id')).toMatch(new RegExp(`^${source.id}`));
    await page.mouse.click(p.x, p.y);
  };
  const start = async (source, selectionFirst) => {
    if (selectionFirst) {
      await pick(source); await expect(canvas).toHaveAttribute('data-selected-keys', source.id);
      await command('Slot').click();
    } else { await command('Slot').click(); await pick(source); }
    await move({x: midpoint(source).x, y: midpoint(source).y + 1});
    await expect(width).toBeVisible(); await expect(width).toHaveAttribute('aria-label', 'Width');
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
  };
  const decimal = async (input, value) => {
    await input.focus(); await input.press('ControlOrMeta+A'); let typed = '';
    for (const char of value) {
      await page.keyboard.press(char); typed += char;
      // Native number inputs omit a trailing decimal point from their DOM value.
      // Keep real sequential keys and assert every complete number plus the final value.
      if (typed !== '-' && !typed.endsWith('.')) await expect(input).toHaveValue(typed);
      await page.waitForTimeout(80);
    }
    await expect(input).toHaveValue(value);
  };
  const unchanged = async (before, name) => {
    assert.deepEqual(content((await download(name)).doc), content(before));
    await expect(command('Undo')).toBeDisabled(); await expect(command('Redo')).toBeDisabled();
  };
  const history = async (before, after, name) => {
    await command('Undo').click();
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await expect(command('Undo')).toBeDisabled();
    await command('Redo').click();
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
    await expect(command('Redo')).toBeDisabled();
  };
  const record = name => { results.cases.push(name); console.log(`PASS ${name}`); };
  const previewInk = async (source, fullWidth) => {
    // Sample both expected side strokes, away from the cursor and dimension input.
    // This observes actual canvas pixels, not a DOM flag or an internal preview DTO.
    return canvas.evaluate((c, {source, fullWidth}) => {
      const context = c.getContext('2d'), box = c.getBoundingClientRect();
      return [-1, 1].map(sign => {
        const x = source.start.x + (source.end.x - source.start.x) * .28;
        const y = source.start.y + sign * fullWidth / 2;
        const px = (x * +c.dataset.scale + +c.dataset.offsetX) * c.width / box.width;
        const py = (-y * +c.dataset.scale + +c.dataset.offsetY) * c.height / box.height;
        const pixels = context.getImageData(Math.round(px) - 4, Math.round(py) - 4, 9, 9).data;
        let ink = 0;
        for (let i = 0; i < pixels.length; i += 4)
          if (Math.max(pixels[i], pixels[i + 1], pixels[i + 2]) > 130 && pixels[i + 3] > 100) ink++;
        return ink;
      });
    }, {source, fullWidth});
  };

  let committed;
  for (const selectionFirst of [true, false]) {
    const name = selectionFirst ? 'selection-first-decimal-enter' : 'tool-first-pointer-click'; results.current = name;
    await load(fixture, 1); const before = await download(`${name}-before`), source = before.doc.entities[0];
    await start(source, selectionFirst);
    if (!selectionFirst) {
      const samples = [];
      for (let i = 0; i < 10; i++) {
        const fullWidth = 1.6 + i * .12;
        const p = await screen({x: midpoint(source).x, y: source.start.y + fullWidth / 2});
        await page.mouse.move(p.x, p.y); await page.waitForTimeout(20);
        samples.push({fullWidth, at: await page.evaluate(() => performance.now()), ink: await previewInk(source, fullWidth)});
      }
      results.continuousPreview = samples;
      assert.ok(samples.filter(s => s.ink.every(n => n > 3)).length >= 8,
        'at least 8/10 intermediate motion samples paint both current slot sides before stopping');
      await expect(canvas).toHaveAttribute('data-entity-count', '1');
    }
    let lastPixels;
    for (const fullWidth of [1.5, 2.5, 3.25]) {
      await move({x: midpoint(source).x, y: source.start.y + fullWidth / 2});
      await expect.poll(async () => Number(await width.inputValue())).toBeCloseTo(fullWidth, 3);
      await expect.poll(async () => (await previewInk(source, fullWidth)).every(n => n > 3)).toBe(true);
      const pixels = await canvas.screenshot();
      if (lastPixels) assert.notDeepEqual(pixels, lastPixels, 'sustained pointer motion repaints slot geometry');
      lastPixels = pixels;
      await expect(canvas).toHaveAttribute('data-entity-count', '1'); await expect(command('Undo')).toBeDisabled();
      results.previews.push({name, fullWidth, ink: await previewInk(source, fullWidth)});
    }
    await page.screenshot({path: `${output}/${name}-preview.png`});
    const fullWidth = selectionFirst ? 1.25 : 3.25;
    if (selectionFirst) { await decimal(width, '1.25'); await width.press('Enter'); }
    else {
      const p = await screen({x: midpoint(source).x, y: source.start.y + fullWidth / 2});
      await page.mouse.click(p.x, p.y);
    }
    await expect(canvas).toHaveAttribute('data-entity-count', '5'); await expect(width).toHaveCount(0);
    const after = await download(`${name}-after`); const slot = assertSlot(after.doc, source.id, fullWidth, source, selectionFirst);
    if (selectionFirst) await expect(page.locator(`input[data-dimension-id="${slot.dimension.id}"]`)).toBeVisible();
    await page.screenshot({path: `${output}/${name}-committed.png`});
    await history(before.doc, after.doc, name);
    if (!selectionFirst) record(name);
    if (selectionFirst) committed = {before, after, source, slot};
  }

  results.current = 'width-edit-native-history-source-edit';
  await load(committed.after.text, 5);
  const reopened = await download('native-reopened');
  assertSlot(reopened.doc, committed.source.id, 1.25, committed.source);
  const permanent = page.locator(`input[data-dimension-id="${committed.slot.dimension.id}"]`);
  await permanent.dblclick(); await decimal(permanent, '3.625'); await permanent.press('Enter');
  await expect(permanent).toHaveAttribute('data-dimension-value', '3.625');
  const resized = await download('width-edited'); assertSlot(resized.doc, committed.source.id, 3.625, committed.source, true, 1e-7);
  await history(reopened.doc, resized.doc, 'width-edit');
  await load(resized.text, 5);
  const sourceEditBefore = await download('source-edit-before');
  const source = resized.doc.entities.find(e => e.id === committed.source.id);
  await pick(source); await expect(canvas).toHaveAttribute('data-selected-keys', source.id);
  await canvas.evaluate(c => {
    window.__lineSlotPointerTrace = [];
    const observe = event => {
      const box = c.getBoundingClientRect();
      window.__lineSlotPointerTrace.push({type: event.type, clientX: event.clientX, clientY: event.clientY,
        x: (event.clientX - box.left - +c.dataset.offsetX) / +c.dataset.scale,
        y: -(event.clientY - box.top - +c.dataset.offsetY) / +c.dataset.scale, scale: +c.dataset.scale});
    };
    for (const type of ['pointerdown', 'pointerup']) c.addEventListener(type, observe, {once: true, capture: true});
  });
  await move(source.end); const target = {x: source.end.x + 1.5, y: source.end.y + 1};
  await expect.poll(() => canvas.getAttribute('data-hovered-id')).toMatch(/(?:\|point\||:)end(?:\||$)/);
  await page.mouse.down();
  try { const to = await screen(target); await page.mouse.move(to.x, to.y, {steps: 12}); await page.waitForTimeout(250); }
  finally { await page.mouse.up(); }
  await page.waitForTimeout(300);
  const dragged = await download('source-end-edited'), moved = assertSlot(dragged.doc, source.id, 3.625).source;
  const pointer = await page.evaluate(() => window.__lineSlotPointerTrace);
  const down = pointer.find(p => p.type === 'pointerdown'), up = pointer.find(p => p.type === 'pointerup');
  assert.ok(down && up, 'actual browser pointer events captured');
  const actualTarget = {x: source.end.x + up.x - down.x, y: source.end.y + up.y - down.y};
  const errorPixels = distance(moved.end, actualTarget) * up.scale;
  results.sourceDrag = {requestedTarget: target, pointer, actualTarget, endpoint: moved.end, errorPixels};
  assert.equal(down.scale, up.scale, 'camera scale unchanged during source drag');
  assert.ok(errorPixels <= 1, `pixel gesture follows actual pointer delta within one screen pixel: ${errorPixels}`);
  assert.ok(distance(moved.end, source.end) > 1, 'source edit actually changed geometry');
  await history(sourceEditBefore.doc, dragged.doc, 'source-edit'); record('selection-first-keyed-native-width-source-history');

  for (const value of [null, '0', '-1']) {
    const name = value === null ? 'escape-discards-preview' : `invalid-width-${value}`; results.current = name;
    await load(fixture, 1); const before = await download(`${name}-before`);
    await start(before.doc.entities[0], true);
    if (value !== null) {
      await decimal(width, value); await width.press('Enter');
      await expect(width).toBeVisible(); await expect(canvas).toHaveAttribute('data-entity-count', '1');
      await expect(command('Undo')).toBeDisabled();
    }
    await page.keyboard.press('Escape'); await expect(width).toHaveCount(0);
    await unchanged(before.doc, `${name}-after`);
  }
  record('escape-and-invalid-width-discard-without-history');

  results.current = 'locked-source-refused';
  await load(fixture, 1); await command('Lock layer 0').click();
  const locked = await download('locked-source-before'); await load(locked.text, 1);
  const lockedReopened = await download('locked-source-reopened');
  await command('Slot').click();
  const lockPoint = await move(midpoint(locked.doc.entities[0]));
  await page.mouse.click(lockPoint.x, lockPoint.y); await page.keyboard.press('Enter');
  await expect(width).toHaveCount(0); await expect(canvas).toHaveAttribute('data-entity-count', '1');
  await page.keyboard.press('Escape'); await unchanged(lockedReopened.doc, 'locked-source-after'); record('locked-source-refused');
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  failure = error; results.failure = error.stack;
  if (page) await page.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2)); await browser.close();
}
if (failure) throw failure;
console.log(JSON.stringify({passed: true, cases: results.cases.length, build: results.build, output}));
