import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-circle-diameter';
await mkdir(output, {recursive: true});
// Disposable framing points keep circle and arc gestures away from origin/snap targets.
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
  '0','SECTION','2','ENTITIES','0','POINT','8','0','10','-5','20','-5',
  '0','POINT','8','0','10','10','20','10','0','ENDSEC','0','EOF',''].join('\n');
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'real download contains native drawing data');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (actual, expected, tolerance = 1e-8) => assert.ok(Math.abs(actual - expected) < tolerance, `${actual} != ${expected}`);
const results = [], errors = [];
const selectedCases = process.env.DXFER_CASES?.split(',').filter(Boolean);
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text());
  });
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    if (process.env.DXFER_EXPECTED_REVISION) await expect(page.locator('.app-brand')).toHaveAttribute('title', new RegExp(process.env.DXFER_EXPECTED_REVISION));
  };
  await ready();
  const canvas = page.locator('canvas.drawing-canvas');
  const draft = key => page.locator(`input[data-dimension-key="${key}"]`);
  const persistent = id => page.locator(`input[data-dimension-id="${id}"]`);
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (text = fixture, count = 2) => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'circle-diameter.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0; previous = view; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const move = async p => { const point = await screen(p); await page.mouse.move(point.x, point.y); };
  const click = async p => { const point = await screen(p); await page.mouse.click(point.x, point.y); };
  const typeNumber = async (input, value, label) => {
    await expect(input).toBeVisible(); await input.focus();
    const node = await input.elementHandle();
    await page.keyboard.press('ControlOrMeta+A');
    const trace = []; let prefix = '';
    for (const key of value) {
      await page.keyboard.press(key); await page.waitForTimeout(key === '.' ? 650 : 35); prefix += key;
      const sample = await node.evaluate(el => ({connected: el.isConnected, focused: document.activeElement === el,
        type: el.type, value: el.value, badInput: el.validity.badInput, selectionStart: el.selectionStart, selectionEnd: el.selectionEnd}));
      const expected = sample.type === 'number' ? prefix === '.' ? '' : prefix.replace(/\.$/, '') : prefix;
      trace.push({key, prefix, expected, ...sample});
      await writeFile(`${output}/${label}-typing.json`, JSON.stringify(trace, null, 2));
      assert.ok(sample.connected && sample.focused, 'typing keeps the original focused input');
      assert.equal(sample.value, expected, 'each decimal prefix survives');
    }
    await expect(input).toHaveValue(value);
  };
  const assertCircle = (doc, diameter, originalEntities) => {
    const circles = doc.entities.filter(e => e.kind === 'circle'); assert.equal(circles.length, 1);
    near(circles[0].radius, diameter / 2);
    // Center placement crosses browser pointer coordinates; keyed size remains exact above.
    near(circles[0].center.x, 2, 1e-4); near(circles[0].center.y, 2, 1e-4);
    assert.deepEqual(doc.entities.filter(e => e.kind !== 'circle'), originalEntities);
    const dimensions = doc.dimensions.filter(d => d.kind === 'Diameter'); assert.equal(dimensions.length, 1);
    assert.equal(dimensions[0].isDriving, true); near(dimensions[0].value, diameter);
    assert.ok(!doc.dimensions.some(d => d.kind === 'Radius'), 'circle creation must not silently retain radius intent');
    return dimensions[0];
  };
  const assertDiameterLabel = async (dimension, value) => {
    const input = persistent(dimension.id);
    await expect(input).toHaveAttribute('data-dimension-kind', /diameter/i);
    const numberText = Number.isInteger(value) ? `${value}(?:\\.0+)?` : `${String(value).replace('.', '\\.')}0*`;
    await expect(input).toHaveValue(new RegExp(`^\\u2300\\s*${numberText}$`));
  };
  const createKeyedCircle = async (value, label) => {
    await load(); const before = (await download(`${label}-before`)).doc;
    await tool('Center circle'); await click({x: 2, y: 2}); await move({x: 5, y: 2});
    await expect(draft('diameter')).toBeVisible(); await expect(draft('radius')).toHaveCount(0);
    await expect(page.locator('.drawing-dimension-symbol')).toBeVisible();
    await expect(page.locator('.drawing-dimension-symbol')).toHaveText('\u2300');
    await expect.poll(async () => Number(await draft('diameter').inputValue())).toBeCloseTo(6, 5);
    await typeNumber(draft('diameter'), value, label);
    const inputBox = await draft('diameter').boundingBox(), symbolBox = await page.locator('.drawing-dimension-symbol').boundingBox();
    const canvasBox = await canvas.boundingBox();
    await writeFile(`${output}/${label}-symbol-layout.json`, JSON.stringify({inputBox, symbolBox, canvasBox,
      background: await canvas.evaluate(c => getComputedStyle(c).backgroundColor)}, null, 2));
    assert.ok(symbolBox.width > 0 && symbolBox.height > 0);
    assert.ok(symbolBox.x + symbolBox.width <= inputBox.x, 'diameter symbol does not overlap the numeric input');
    near(symbolBox.y + symbolBox.height / 2, inputBox.y + inputBox.height / 2, 2);
    assert.ok(symbolBox.x >= canvasBox.x && symbolBox.y >= canvasBox.y
      && symbolBox.x + symbolBox.width <= canvasBox.x + canvasBox.width
      && symbolBox.y + symbolBox.height <= canvasBox.y + canvasBox.height, 'symbol stays inside the canvas');
    await page.screenshot({path: `${output}/${label}-preview.png`});
    await page.keyboard.press('Enter'); await expect(canvas).toHaveAttribute('data-entity-count', '3');
    await canvas.press('Escape');
    const saved = await download(`${label}-created`);
    const dimension = assertCircle(saved.doc, Number(value), before.entities);
    await assertDiameterLabel(dimension, Number(value));
    await page.screenshot({path: `${output}/${label}-committed.png`});
    return {before, saved, dimension};
  };
  const run = async (name, action) => {
    if (selectedCases && !selectedCases.includes(name)) return;
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, results, errors}, null, 2));
    await ready();
  };

  await run('diameter-create-edit-history-reopen', async () => {
    const {before, saved, dimension} = await createKeyedCircle('5', 'diameter-five');
    await tool('Undo'); assert.deepEqual((await download('diameter-create-undo')).doc.entities, before.entities);
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await tool('Redo'); assert.deepEqual((await download('diameter-create-redo')).doc, saved.doc);
    await persistent(dimension.id).dblclick(); await typeNumber(persistent(dimension.id), '6', 'diameter-edit-six');
    await page.keyboard.press('Enter');
    await expect(persistent(dimension.id)).toHaveAttribute('data-dimension-value', '6'); await canvas.press('Escape');
    const edited = await download('diameter-six-edited'); assertCircle(edited.doc, 6, before.entities);
    await assertDiameterLabel(dimension, 6); await page.screenshot({path: `${output}/diameter-six-edited.png`});
    await tool('Undo'); assert.deepEqual((await download('diameter-edit-undo')).doc, saved.doc);
    await tool('Redo'); assert.deepEqual((await download('diameter-edit-redo')).doc, edited.doc);
    await load(edited.text, 3);
    const reopened = (await download('diameter-six-reopened')).doc;
    assert.deepEqual(reopened, {...edited.doc, metadata: {...edited.doc.metadata, sourceSha256: createHash('sha256').update(edited.text).digest('hex')}});
    assertCircle(reopened, 6, before.entities); await assertDiameterLabel(dimension, 6);
  });

  await run('diameter-leading-decimal', async () => {
    await menu('Appearances');
    await page.getByLabel('Drawing background preset', {exact: true}).selectOption('neutral');
    await page.getByLabel('Drawing background color', {exact: true}).fill('#ffffff');
    await tool('Close appearances');
    await expect(canvas).toHaveCSS('background-color', 'rgb(255, 255, 255)');
    const {before, saved, dimension} = await createKeyedCircle('.25', 'diameter-quarter');
    await load(saved.text, 3); const reopened = (await download('diameter-quarter-reopened')).doc;
    assertCircle(reopened, .25, before.entities); await assertDiameterLabel(dimension, .25);
  });

  await run('click-only-circle-no-driving-dimension', async () => {
    await load(); const before = (await download('click-circle-before')).doc;
    await tool('Center circle'); await click({x: 2, y: 2}); await move({x: 5, y: 2});
    await expect(draft('diameter')).toBeVisible();
    await click({x: 5, y: 2}); await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const saved = await download('click-circle-created');
    const circle = saved.doc.entities.find(e => e.kind === 'circle'); near(circle.radius, 3, 1e-4);
    assert.deepEqual(saved.doc.dimensions, before.dimensions, 'pointer-only creation does not force a driving diameter');
    assert.deepEqual(saved.doc.entities.filter(e => e.kind !== 'circle'), before.entities);
    await page.screenshot({path: `${output}/click-circle-created.png`});
  });

  await run('center-point-arc-keeps-radius', async () => {
    await load(); const before = (await download('radius-arc-before')).doc;
    await tool('Center point arc'); await click({x: 2, y: 2}); await move({x: 5, y: 2});
    await expect(draft('radius')).toBeVisible(); await expect(draft('diameter')).toHaveCount(0);
    await expect(page.locator('.drawing-dimension-symbol')).toHaveCount(0);
    await typeNumber(draft('radius'), '5', 'arc-radius-five'); await page.keyboard.press('Enter');
    await move({x: 2, y: 7}); await expect(draft('sweep')).toBeVisible();
    await typeNumber(draft('sweep'), '90', 'arc-sweep'); await page.keyboard.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const saved = await download('radius-arc-created');
    near(saved.doc.entities.find(e => e.kind === 'arc').radius, 5);
    assert.deepEqual(saved.doc.entities.filter(e => e.kind !== 'arc'), before.entities);
    const radius = saved.doc.dimensions.find(d => d.kind === 'Radius'); assert.ok(radius?.isDriving); near(radius.value, 5);
    assert.ok(!saved.doc.dimensions.some(d => d.kind === 'Diameter'));
    await expect(persistent(radius.id)).toHaveValue(/^R\s*5(?:\.0+)?$/);
    await page.screenshot({path: `${output}/radius-arc-created.png`});
  });
  const build = await page.locator('.app-brand').getAttribute('title');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  assert.deepEqual(errors, []); assert.ok(results.length > 0 && results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
