// Disposable canvas-only QA. Run through the private QA wrapper; never a live drawing.
import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated isolated QA URL explicitly');
const target = new URL(url);
assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(target.hostname)
  || (target.origin === 'https://dxferdev.sync.harrisonmetals.com'
    && process.env.DXFER_ALLOW_PUBLIC_DEV === '1' && process.env.DXFER_EXPECTED_REVISION),
  'Loopback QA or explicitly enabled revision-checked DXFer Dev only');
assert.ok(process.env.DXFER_QA_API_KEY, 'Run through the private QA auth wrapper');
const output = process.env.DXFER_SCREENSHOTS ?? `/tmp/dxfer-normal-symmetry-${Date.now()}`;
await mkdir(output, {recursive: true, mode: 0o700});
const near = (a, b, label) => assert.ok(Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) < 1e-6,
  `${label}: ${a} != ${b}`);
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const owner = key => key.split(/[:|]/)[0];
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  layers: doc.metadata.layers, entityStyles: doc.metadata.entityStyles, patterns: doc.metadata.patterns});
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'DXF contains the native editable document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const circle = (handle, x, y, radius) => `0\nCIRCLE\n5\n${handle}\n8\n0\n10\n${x}\n20\n${y}\n40\n${radius}\n`;
const line = (handle, x, y, ex, ey) => `0\nLINE\n5\n${handle}\n8\n0\n10\n${x}\n20\n${y}\n11\n${ex}\n21\n${ey}\n`;
const fixture = kind => '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n'
  + (kind === 'Symmetric'
    ? circle('100', 6, 10, 2) + circle('101', 15, 11, 2.5) + line('102', 10, 2, 10, 18)
    : circle('100', 10, 10, 3) + line('101', 13.4, 10.3, 18, 11))
  + '0\nENDSEC\n0\nEOF\n';
const body = entity => entity.kind === 'circle'
  ? {x: entity.center.x + entity.radius * Math.cos(.63), y: entity.center.y + entity.radius * Math.sin(.63)}
  : {x: entity.start.x * .69 + entity.end.x * .31, y: entity.start.y * .69 + entity.end.y * .31};
const entity = (doc, id) => { const found = doc.entities.find(e => e.id === id); assert.ok(found, `Entity ${id} retained`); return found; };
function assertRelation(doc, kind, ids, previous) {
  const matches = doc.constraints.filter(c => c.kind === kind);
  assert.equal(matches.length, 1, `Exactly one persistent ${kind}`);
  const relation = matches[0];
  if (previous) {
    assert.equal(relation.id, previous.id, 'Relation identity survives edits/reopen');
    assert.deepEqual(relation.referenceKeys, previous.referenceKeys, 'Endpoint correspondence survives edits/reopen');
  }
  assert.equal(relation.state, 'Satisfied');
  assert.deepEqual(relation.referenceKeys.map(owner), ids, 'Stored participant order');
  assert.ok(doc.constraints.every(c => c.state === 'Satisfied'), 'No hidden failed existing constraint');
  if (kind === 'Symmetric') {
    const [first, second, axis] = ids.map(id => entity(doc, id));
    const dx = axis.end.x - axis.start.x, dy = axis.end.y - axis.start.y, length = Math.hypot(dx, dy);
    assert.ok(length > 1e-7);
    const ux = dx / length, uy = dy / length;
    const signed = -(first.center.x - axis.start.x) * uy + (first.center.y - axis.start.y) * ux;
    near(second.center.x, first.center.x + 2 * signed * uy, 'Mirrored center X');
    near(second.center.y, first.center.y - 2 * signed * ux, 'Mirrored center Y');
    near(second.radius, first.radius, 'Mirrored circle size');
  } else {
    const [source, curve] = ids.map(id => entity(doc, id));
    const key = relation.referenceKeys[0];
    const end = /(?:\:|\|)(start|end)$/.exec(key)?.[1];
    assert.ok(end, 'Normal persists the selected line endpoint, not just its owner');
    const contact = source[end], dx = source.end.x - source.start.x, dy = source.end.y - source.start.y;
    const length = Math.hypot(dx, dy); assert.ok(length > 1e-7);
    near(distance(contact, curve.center), curve.radius, 'Contact lies on circle');
    near(((curve.center.x - source.start.x) * dy - (curve.center.y - source.start.y) * dx) / length, 0,
      'Line is radial, not tangent');
  }
  for (const d of doc.dimensions.filter(d => d.isDriving)) {
    const curve = entity(doc, owner(d.referenceKeys[0]));
    assert.ok(['Radius', 'Diameter'].includes(d.kind));
    near(curve.radius * (d.kind === 'Diameter' ? 2 : 1), d.value, 'Driving circle size satisfied');
  }
  return relation;
}

const specs = ['Symmetric', 'Normal'].flatMap(kind => [
  {name: `${kind.toLowerCase()}-tool-first`, kind, preselect: false},
  {name: `${kind.toLowerCase()}-preselect`, kind, preselect: true},
  {name: `${kind.toLowerCase()}-fixed-refusal`, kind, preselect: false, fixed: true}
]);
const selected = process.env.DXFER_NORMAL_SYMMETRY_CASE
  ? specs.filter(s => s.name === process.env.DXFER_NORMAL_SYMMETRY_CASE) : specs;
assert.ok(selected.length, 'DXFER_NORMAL_SYMMETRY_CASE must name an existing case');
const results = {passed: false, requested: selected.map(s => s.name), cases: [], exports: [], errors: []};
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
let page, failure;
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(15000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => results.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) results.errors.push(message.text());
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build?.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  const command = name => page.getByRole('button', {name, exact: true});
  const tool = async name => { await command(name).click(); await page.waitForTimeout(150); };
  const menu = async name => { await tool('Open application menu'); await command('File').hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); results.exports.push(name);
    return {text, doc: decode(text)};
  };
  const load = async (text, count) => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'normal-symmetry-qa.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0; previous = view; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const move = async point => {
    const p = await screen(point);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'Canvas target is unobscured');
    await page.mouse.move(p.x, p.y); return p;
  };
  const pick = async (point, expectedKey, shift = false) => {
    const p = await move(point);
    if (expectedKey) await expect.poll(async () => await canvas.getAttribute('data-hovered-id')).toMatch(expectedKey);
    if (shift) await page.keyboard.down('Shift');
    try { await page.mouse.click(p.x, p.y); }
    finally { if (shift) await page.keyboard.up('Shift'); }
    await page.waitForTimeout(180);
  };
  const keyPattern = (id, endpoint) => new RegExp(`^${id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}${endpoint ? `(?::|\\|point\\|)${endpoint}(?:\\|[^|]+\\|[^|]+)?` : ''}$`);
  const history = async (before, after, name) => {
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before.doc), 'Atomic Undo');
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after.doc), 'Exact Redo');
  };
  const reopen = async (saved, name) => {
    await load(saved.text, saved.doc.entities.length);
    const next = await download(`${name}-reopened`);
    assert.deepEqual(content(next.doc), content(saved.doc), 'Native DXF retains geometry, intent, IDs and styles'); return next;
  };
  const addSize = async curve => {
    await tool('Dimension'); await pick(body(curve), keyPattern(curve.id));
    await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
    await pick({x: curve.center.x - curve.radius * .4, y: curve.center.y - curve.radius * .5});
    await expect(page.locator('input[data-dimension-id]')).toHaveCount(1);
    await canvas.press('Escape');
  };
  const changeSize = async (doc, value) => {
    const d = doc.dimensions[0]; assert.ok(d?.isDriving, 'UI-created circle dimension is driving');
    const input = page.locator(`input[data-dimension-id="${d.id}"]`);
    await input.dblclick(); await input.fill(String(value)); await input.press('Enter');
    await expect(input).toHaveAttribute('data-dimension-value', String(value)); await canvas.press('Escape');
  };
  const applyRelation = async (spec, doc, ids) => {
    await canvas.press('Escape');
    const points = spec.kind === 'Symmetric' ? ids.map(id => body(entity(doc, id)))
      : [entity(doc, ids[0]).start, body(entity(doc, ids[1]))];
    if (!spec.preselect) await tool(spec.kind);
    for (let i = 0; i < ids.length; i++) {
      await pick(points[i], keyPattern(ids[i], spec.kind === 'Normal' && i === 0 ? 'start' : null), spec.preselect && i > 0);
    }
    if (spec.preselect) await tool(spec.kind);
    await page.waitForTimeout(300);
  };
  const dragCenter = async (curve, delta) => {
    await canvas.press('Escape');
    const start = await move(curve.center);
    await expect.poll(async () => await canvas.getAttribute('data-hovered-id')).toMatch(keyPattern(curve.id, 'center'));
    const end = await screen({x: curve.center.x + delta.x, y: curve.center.y + delta.y});
    await page.mouse.down();
    try { await page.mouse.move(end.x, end.y, {steps: 16}); await page.waitForTimeout(200); }
    finally { await page.mouse.up(); }
    await page.waitForTimeout(350);
    return {from: start, to: end};
  };

  await menu('Settings');
  await page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true}).setChecked(false);
  await tool('Close settings');
  for (const spec of selected) {
    const name = spec.name, count = spec.kind === 'Symmetric' ? 3 : 2;
    await load(fixture(spec.kind), count);
    let baseline = await download(`${name}-imported`);
    const circles = baseline.doc.entities.filter(e => e.kind === 'circle');
    const source = circles[0], axisOrLine = baseline.doc.entities.find(e => e.kind === 'line');
    const ids = spec.kind === 'Symmetric' ? [source.id, circles[1].id, axisOrLine.id] : [axisOrLine.id, source.id];
    // Fix only the symmetry axis in positive cases, so source translation remains free.
    const fixed = spec.fixed ? baseline.doc.entities : spec.kind === 'Symmetric' ? [axisOrLine] : [];
    for (const e of fixed) { await canvas.press('Escape'); await tool('Fix'); await pick(body(e), keyPattern(e.id)); await canvas.press('Escape'); }
    if (!spec.fixed) await addSize(source);
    baseline = await download(`${name}-before`);
    for (const e of fixed) assert.ok(baseline.doc.constraints.some(c => c.kind === 'Fix' && c.state === 'Satisfied'
      && c.referenceKeys.includes(e.id)), 'Fixed fixture established through UI');
    await applyRelation(spec, baseline.doc, ids);
    if (spec.fixed) {
      const status = page.locator('.dxfer-command-bar');
      await expect(status).toContainText(/fail|conflict|cannot|could not|inconsistent|unsatisfied|over.?constrain|fixed|reject/i);
      const refusal = await status.innerText();
      await page.screenshot({path: `${output}/${name}.png`}); await canvas.press('Escape');
      const rejected = await download(`${name}-rejected`);
      assert.deepEqual(rejected.doc.entities, baseline.doc.entities, 'Rejected intent never moves Fixed geometry');
      assert.deepEqual(rejected.doc.dimensions, baseline.doc.dimensions, 'Rejected intent preserves dimensions');
      for (const c of baseline.doc.constraints) assert.deepEqual(rejected.doc.constraints.find(next => next.id === c.id), c);
      const additions = rejected.doc.constraints.filter(c => !baseline.doc.constraints.some(old => old.id === c.id));
      assert.ok(additions.length <= 1);
      for (const c of additions) {
        assert.equal(c.kind, spec.kind);
        assert.equal(c.state, 'Unsatisfied', 'Retained failed intent cannot claim success');
      }
      if (additions.length) await history(baseline, rejected, name);
      await reopen(rejected, name);
      results.cases.push({name, passed: true, refusal, retainedFailedIntent: additions.length === 1});
    } else {
      await canvas.press('Escape');
      let saved = await download(`${name}-applied`);
      const relation = assertRelation(saved.doc, spec.kind, ids);
      assert.equal(saved.doc.entities.length, count, 'Constraint adds no replacement geometry');
      await history(baseline, saved, `${name}-creation`);
      await changeSize(saved.doc, saved.doc.dimensions[0].value * 1.2);
      let edited = await download(`${name}-size-edited`);
      assertRelation(edited.doc, spec.kind, ids, relation);
      assert.ok(Math.abs(entity(edited.doc, source.id).radius - entity(saved.doc, source.id).radius) > .1, 'Radius actually changed');
      await history(saved, edited, `${name}-size`);
      saved = await reopen(edited, `${name}-size`);
      const beforeDrag = entity(saved.doc, source.id), gesture = await dragCenter(beforeDrag, {x: -.7, y: .6});
      edited = await download(`${name}-source-moved`);
      assertRelation(edited.doc, spec.kind, ids, relation);
      assert.ok(distance(entity(edited.doc, source.id).center, beforeDrag.center) > .2, 'Source center drag actually moved geometry');
      if (spec.kind === 'Symmetric') assert.deepEqual(entity(edited.doc, axisOrLine.id), axisOrLine, 'Fixed axis remains exact');
      await history(saved, edited, `${name}-drag`);
      saved = await reopen(edited, `${name}-drag`);
      await changeSize(saved.doc, saved.doc.dimensions[0].value * .9);
      edited = await download(`${name}-reopened-edited`);
      assertRelation(edited.doc, spec.kind, ids, relation);
      await page.screenshot({path: `${output}/${name}.png`});
      results.cases.push({name, passed: true, relationId: relation.id, gesture});
    }
    console.log(`PASS ${name}`);
  }
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  failure = error; results.failure = error.stack;
  await page?.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2)); await browser.close();
}
if (failure) throw failure;
console.log(JSON.stringify({passed: true, cases: results.cases.length, build: results.build, output}));
