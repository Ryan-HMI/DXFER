import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-radial-dimension-context';
await mkdir(output, {recursive: true});
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
  '0','SECTION','2','ENTITIES','0','POINT','8','0','10','-5','20','-5',
  '0','POINT','8','0','10','10','20','10','0','ENDSEC','0','EOF',''].join('\n');
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-8, `${a} != ${b}`);
const native = text => {
  const pairs = text.trim().split(/\r?\n/); let hex = ''; const strings = [];
  for (let i = 0; i < pairs.length; i += 2) {
    if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
    if (pairs[i].trim() === '1') strings.push(pairs[i + 1].trim());
  }
  assert.ok(hex && hex.length % 2 === 0);
  const bytes = Buffer.from(hex, 'hex');
  assert.ok(strings.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()), 'native payload checksum');
  return JSON.parse(bytes);
};
const converted = (doc, id, kind, value) => ({...doc,
  dimensions: doc.dimensions.map(d => d.id === id ? {...d, kind, value} : d)});
const results = [], errors = [], exports = [];
const selectedCases = process.env.DXFER_CASES?.split(',').filter(Boolean);
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000); page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text());
  });
  const ready = async () => {
    await page.setViewportSize({width: 1188, height: 761}); await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    if (process.env.DXFER_EXPECTED_REVISION) await expect(page.locator('.app-brand')).toHaveAttribute('title', new RegExp(process.env.DXFER_EXPECTED_REVISION));
  };
  await ready();
  const canvas = page.locator('canvas.drawing-canvas');
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const dimension = id => page.locator(`input[data-dimension-id="${id}"]`);
  const draft = key => page.locator(`input[data-dimension-key="${key}"]`);
  const display = () => context.getByRole('group', {name: 'Display', exact: true});
  const displayChoice = kind => display().getByRole('radio', {name: kind === 'Radius' ? /^Radius\s*\(R\)$/ : /^Diameter\s*\(\u2300\)$/});
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    const doc = native(text); exports.push({name, bytes: Buffer.byteLength(text), checksumValid: true});
    return {text, doc};
  };
  const load = async (text = fixture, count = 2) => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'radial-dimension-context.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
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
  const click = async p => { const point = await screen(p); await page.mouse.click(point.x, point.y); };
  const move = async p => { const point = await screen(p); await page.mouse.move(point.x, point.y); };
  const typeNumber = async (input, value) => {
    await input.focus(); await page.keyboard.press('ControlOrMeta+A');
    for (const key of value) { await page.keyboard.press(key); await page.waitForTimeout(35); }
    await expect(input).toHaveValue(value); await page.keyboard.press('Enter');
  };
  const openDimension = async (id, name) => {
    await dimension(id).click({button: 'right'}); await expect(context).toBeVisible();
    const bounds = await context.boundingBox(), viewport = page.viewportSize();
    assert.ok(bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= viewport.width + 1
      && bounds.y + bounds.height <= viewport.height + 1, 'dimension menu fits the desktop viewport');
    if (name) await page.screenshot({path: `${output}/${name}.png`});
  };
  const toggle = async (before, id, kind, value, name) => {
    const old = before.dimensions.find(d => d.id === id);
    await openDimension(id);
    await expect(displayChoice(old.kind)).toBeChecked();
    await expect(context.getByRole('radio', {name: old.isDriving ? 'Driving' : 'Driven', exact: true})).toBeChecked();
    await page.screenshot({path: `${output}/${name}-menu.png`});
    await displayChoice(kind).check(); await expect(context).toBeHidden();
    await expect(dimension(id)).toHaveAttribute('data-dimension-kind', new RegExp(`^${kind}$`, 'i'));
    await expect(dimension(id)).toHaveAttribute('data-dimension-value', String(value));
    const saved = await download(name);
    assert.deepEqual(saved.doc, converted(before, id, kind, value), 'conversion changes only kind/value; geometry, ID, refs, anchor, mode and all other intent remain exact');
    await page.screenshot({path: `${output}/${name}.png`}); return saved;
  };
  const reopen = async (saved, name) => {
    await load(saved.text, saved.doc.entities.length);
    const again = await download(name);
    assert.deepEqual(again.doc, {...saved.doc, metadata: {...saved.doc.metadata, sourceSha256: createHash('sha256').update(saved.text).digest('hex')}});
    return again;
  };
  const createCircle = async name => {
    await load(); await tool('Center circle'); await click({x: 2, y: 2}); await move({x: 5, y: 2});
    await expect(draft('diameter')).toBeVisible(); await typeNumber(draft('diameter'), '5');
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const saved = await download(name), radial = saved.doc.dimensions.find(d => d.kind === 'Diameter');
    assert.ok(radial?.isDriving); near(radial.value, 5); assert.ok(radial.anchor && radial.referenceKeys.length);
    near(saved.doc.entities.find(e => e.kind === 'circle').radius, 2.5);
    return {saved, id: radial.id};
  };
  const run = async (name, action) => {
    if (selectedCases && !selectedCases.includes(name)) return;
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, results, errors, exports}, null, 2)); await ready();
  };

  await run('driving-circle-conversion-history-edit', async () => {
    const {saved, id} = await createCircle('driving-diameter-five');
    const radius = await toggle(saved.doc, id, 'Radius', 2.5, 'driving-radius-converted');
    await tool('Undo'); assert.deepEqual((await download('radius-conversion-undo')).doc, saved.doc);
    await tool('Redo'); assert.deepEqual((await download('radius-conversion-redo')).doc, radius.doc);
    const diameter = await toggle(radius.doc, id, 'Diameter', 5, 'driving-diameter-restored');
    await tool('Undo'); assert.deepEqual((await download('diameter-conversion-undo')).doc, radius.doc);
    await tool('Redo'); assert.deepEqual((await download('diameter-conversion-redo')).doc, diameter.doc);
    const opened = await reopen(diameter, 'driving-diameter-reopened');
    const editable = await toggle(opened.doc, id, 'Radius', 2.5, 'reopened-radius-converted');
    await dimension(id).dblclick(); await typeNumber(dimension(id), '3');
    await expect(dimension(id)).toHaveAttribute('data-dimension-value', '3'); await canvas.press('Escape');
    const edited = await download('converted-radius-edited');
    const originalCircle = editable.doc.entities.find(e => e.kind === 'circle'), circle = edited.doc.entities.find(e => e.id === originalCircle.id);
    near(circle.radius, 3); assert.deepEqual(circle.center, originalCircle.center);
    assert.deepEqual(edited.doc.entities.filter(e => e.id !== circle.id), editable.doc.entities.filter(e => e.id !== circle.id));
    const radial = edited.doc.dimensions.find(d => d.id === id);
    assert.equal(radial.kind, 'Radius'); assert.equal(radial.isDriving, true); near(radial.value, 3);
    assert.deepEqual(radial.referenceKeys, editable.doc.dimensions.find(d => d.id === id).referenceKeys);
    await page.screenshot({path: `${output}/converted-radius-edited.png`});
    await tool('Undo'); assert.deepEqual((await download('converted-radius-edit-undo')).doc, editable.doc);
    await tool('Redo'); assert.deepEqual((await download('converted-radius-edit-redo')).doc, edited.doc);
    await reopen(edited, 'edited-radius-reopened');
  });

  await run('driven-circle-conversion-retains-mode', async () => {
    const {saved, id} = await createCircle('driven-circle-initial');
    await openDimension(id); await context.getByRole('radio', {name: 'Driven', exact: true}).check();
    await expect(context).toBeHidden(); await expect(dimension(id)).toHaveAttribute('readonly', '');
    const driven = await download('driven-diameter-five');
    assert.deepEqual(driven.doc.entities, saved.doc.entities); assert.equal(driven.doc.dimensions.find(d => d.id === id).isDriving, false);
    const radius = await toggle(driven.doc, id, 'Radius', 2.5, 'driven-radius'); await expect(dimension(id)).toHaveAttribute('readonly', '');
    const diameter = await toggle(radius.doc, id, 'Diameter', 5, 'driven-diameter'); await expect(dimension(id)).toHaveAttribute('readonly', '');
    await tool('Undo'); assert.deepEqual((await download('driven-conversion-undo')).doc, radius.doc);
    await tool('Redo'); assert.deepEqual((await download('driven-conversion-redo')).doc, diameter.doc);
    await reopen(diameter, 'driven-diameter-reopened'); await expect(dimension(id)).toHaveAttribute('readonly', '');
  });

  await run('arc-radius-to-diameter-keeps-sweep', async () => {
    await load(); await tool('Center point arc'); await click({x: 2, y: 2}); await move({x: 5, y: 2});
    await typeNumber(draft('radius'), '5'); await move({x: 2, y: 7}); await typeNumber(draft('sweep'), '90');
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const before = await download('arc-radius-initial'), id = before.doc.dimensions.find(d => d.kind === 'Radius').id;
    const after = await toggle(before.doc, id, 'Diameter', 10, 'arc-diameter-converted');
    assert.deepEqual(after.doc.entities.find(e => e.kind === 'arc'), before.doc.entities.find(e => e.kind === 'arc'));
    assert.deepEqual(after.doc.dimensions.find(d => d.kind === 'Angle'), before.doc.dimensions.find(d => d.kind === 'Angle'));
    await tool('Undo'); assert.deepEqual((await download('arc-conversion-undo')).doc, before.doc);
    await tool('Redo'); assert.deepEqual((await download('arc-conversion-redo')).doc, after.doc);
    await reopen(after, 'arc-diameter-reopened');
  });

  await run('locked-layer-display-disabled', async () => {
    const {saved, id} = await createCircle('locked-circle-before');
    await tool('Lock layer 0'); const locked = await download('locked-circle');
    await page.setViewportSize({width: 1100, height: 705}); await tool('Fit extents');
    await openDimension(id, 'locked-display-menu-1100');
    await expect(displayChoice('Radius')).toBeDisabled(); await expect(displayChoice('Diameter')).toBeDisabled();
    await expect(displayChoice('Diameter')).toBeChecked();
    await context.press('Escape'); assert.deepEqual((await download('locked-menu-unchanged')).doc, locked.doc);
    await tool('Undo'); assert.deepEqual((await download('locked-menu-history-unchanged')).doc, saved.doc);
  });

  await run('nonradial-menu-no-display-choices', async () => {
    await load(await readFile(new URL('./fixtures/selection-offset/rectangle.dxf', import.meta.url)), 4);
    const before = await download('linear-before'), id = before.doc.dimensions[0].id;
    await openDimension(id, 'nonradial-menu'); await expect(display()).toHaveCount(0);
    await expect(context.getByRole('radio', {name: /^Radius\s*\(/})).toHaveCount(0);
    await expect(context.getByRole('radio', {name: /^Diameter\s*\(/})).toHaveCount(0);
    await expect(context.getByRole('radio', {name: 'Driving', exact: true})).toBeChecked();
    await context.press('Escape'); assert.deepEqual((await download('linear-menu-unchanged')).doc, before.doc);
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
  });
  const build = await page.locator('.app-brand').getAttribute('title');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors, exports}, null, 2));
  assert.deepEqual(errors, []); assert.ok(results.length > 0 && results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
