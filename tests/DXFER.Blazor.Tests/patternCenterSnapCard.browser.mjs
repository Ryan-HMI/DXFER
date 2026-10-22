// Disposable drawing fixtures only. The private QA wrapper supplies auth.
import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated QA URL explicitly');
const host = new URL(url).hostname;
assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(host)
  || host === 'dxferdev.sync.harrisonmetals.com' && process.env.DXFER_ALLOW_PUBLIC_DEV === '1',
'Public Dev requires explicit DXFER_ALLOW_PUBLIC_DEV=1; Production/Test are never allowed');
assert.ok(process.env.DXFER_QA_API_KEY, 'Use the private QA auth wrapper');
const output = process.env.DXFER_SCREENSHOTS ?? `/tmp/dxfer-pattern-center-snap-card-${Date.now()}`;
await mkdir(output, {recursive: true, mode: 0o700});
const requested = process.env.DXFER_CASES?.split(',').filter(Boolean);
const near = (a, b, label, tolerance = 1e-6) => assert.ok(Number.isFinite(a) && Number.isFinite(b)
  && Math.abs(a - b) <= tolerance, `${label}: ${a} != ${b}`);
const pointNear = (a, b, label, tolerance) => {
  near(a.x, b.x, `${label} X`, tolerance); near(a.y, b.y, `${label} Y`, tolerance);
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  layers: doc.metadata.layers, entityStyles: doc.metadata.entityStyles, patterns: doc.metadata.patterns});
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'Native editable document retained in DXF'); return JSON.parse(Buffer.from(hex, 'hex'));
};
const owner = key => key.split(/[:|]/)[0];
const semantic = key => {
  const match = /^(.*?)\|point\|([^|]+)(?:\|.*)?$/.exec(key);
  return match ? `${match[1]}${match[2] === 'point' ? '' : `:${match[2]}`}` : key;
};
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  '0', 'CIRCLE', '5', '100', '8', '0', '10', '10', '20', '10', '40', '1',
  '0', 'LINE', '5', '200', '8', '0', '10', '-4', '20', '4', '11', '4', '21', '4',
  '0', 'CIRCLE', '5', '300', '8', '0', '10', '6', '20', '-3', '40', '.8',
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const constructionFixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  '0', 'LINE', '5', '200', '8', '0', '10', '-4', '20', '4', '11', '4', '21', '4',
  '0', 'ARC', '5', '400', '8', '0', '10', '0', '20', '0', '40', '3', '50', '20', '51', '160',
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const targets = [
  {name: 'origin', point: {x: 0, y: 0}, kind: 'Coincident', ref: '$origin:center'},
  {name: 'endpoint', point: {x: -4, y: 4}, kind: 'Coincident', ref: 'line-200:start'},
  {name: 'midpoint', point: {x: 0, y: 4}, kind: 'Midpoint', ref: 'line-200'},
  {name: 'center', point: {x: 6, y: -3}, kind: 'Coincident', ref: 'circle-300:center'},
  {name: 'edge', point: {x: -1.4, y: 4}, kind: 'PointOnLine', ref: 'line-200', edge: true}
];
function assertPattern(doc, baseline, center) {
  const pattern = doc.metadata.patterns[0], before = baseline.metadata.patterns[0];
  assert.equal(doc.metadata.patterns.length, 1); assert.equal(pattern.kind, 'Radial');
  const parameters = ({center, centerPointId, ...values}) => values;
  assert.deepEqual(parameters(pattern), parameters(before),
    'Center edit preserves pattern parameters, identity and membership');
  pointNear(pattern.center, center, 'Pattern center');
  const source = doc.entities.find(e => e.id === pattern.sourceEntityIds[0]);
  assert.deepEqual(source, baseline.entities.find(e => e.id === source.id), 'Fixed source remains exact');
  const generated = new Set(pattern.members.map(m => m.entityId));
  for (const old of baseline.entities.filter(e => !generated.has(e.id) && e.id !== before.centerPointId))
    assert.deepEqual(doc.entities.find(e => e.id === old.id), old, 'Snap cannot move unrelated geometry');
  if (pattern.centerPointId) {
    const point = doc.entities.find(e => e.id === pattern.centerPointId);
    assert.equal(point?.kind, 'point'); assert.equal(point.isConstruction, true);
    pointNear(point.location, center, 'Exposed point follows metadata');
  }
  for (const member of pattern.members) {
    const copy = doc.entities.find(e => e.id === member.entityId);
    const angle = member.column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) * Math.PI / 180;
    const dx = source.center.x - center.x, dy = source.center.y - center.y;
    assert.equal(copy?.kind, 'circle'); near(copy.radius, source.radius, 'Member radius');
    pointNear(copy.center, {x: center.x + dx * Math.cos(angle) - dy * Math.sin(angle),
      y: center.y + dx * Math.sin(angle) + dy * Math.cos(angle)}, 'Regenerated member');
  }
  assert.ok(doc.constraints.every(c => c.state === 'Satisfied'), 'All persisted relations remain satisfied');
}

const specs = targets.flatMap(target => [true, false].map(automatic => ({
  name: `${target.name}-auto-${automatic ? 'on' : 'off'}`, target, automatic, mode: 'snap'
})));
specs.push(...[true, false].map(automatic => ({name: `cancel-auto-${automatic ? 'on' : 'off'}`,
  target: targets[1], automatic, mode: 'cancel'})),
{name: 'exclude-generated-member', automatic: true, mode: 'exclude-member'},
{name: 'exclude-own-center', automatic: true, mode: 'exclude-center'},
{name: 'fixed-center-refusal', automatic: true, target: targets[0], mode: 'fixed'},
...[1440, 1100].map(width => ({name: `floating-card-${width}`, width, mode: 'card'})));
if (process.env.DXFER_CHECK_CONSTRUCTION === '1') specs.push({name: 'construction-dash-spacing', mode: 'construction'});
const selected = requested ? specs.filter(s => requested.includes(s.name)) : specs;
assert.ok(selected.length && (!requested || selected.length === new Set(requested).size), 'DXFER_CASES must list existing cases');
const evidence = {passed: false, requested: selected.map(s => s.name), cases: [], drags: [], layouts: [], exports: [], errors: []};
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
let page, failure;
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(12000); page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => evidence.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) evidence.errors.push(message.text());
  });
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const build = await page.locator('.app-brand').getAttribute('title');
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION));
    if (evidence.build) assert.equal(build, evidence.build, 'Candidate must not change during replay');
    evidence.build = build;
  };
  await ready();
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  const widget = page.getByRole('region', {name: 'Viewport pattern parameters', exact: true});
  const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const handle = widget.locator('[data-pattern-center-handle]');
  const card = widget.locator('[data-pattern-card]');
  const header = card.getByRole('button', {name: 'Move pattern card', exact: true});
  const command = name => page.getByRole('button', {name, exact: true});
  const tool = async name => { await command(name).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await command('File').hover(); await tool(name); };
  const camera = () => canvas.evaluate(c => ({scale: +c.dataset.scale, x: +c.dataset.offsetX, y: +c.dataset.offsetY}));
  const historyState = async () => ({undo: await command('Undo').isEnabled(), redo: await command('Redo').isEnabled()});
  const stableCamera = async () => {
    let previous, stable = 0;
    await expect.poll(async () => {
      const next = JSON.stringify(await camera()); stable = previous === next ? stable + 1 : 0; previous = next; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); evidence.exports.push(name); return {text, doc: decode(text)};
  };
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify({canvas: await canvas.evaluate(c => ({...c.dataset})),
      widget: await widget.count() ? await widget.evaluate(c => ({...c.dataset})) : null,
      status: await page.locator('.dxfer-command-bar').innerText()}, null, 2));
  };
  const load = async saved => {
    if (await context.isVisible()) await context.getByRole('button', {name: 'Close', exact: true}).click();
    if (await editor.isVisible()) await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'pattern-center-snap-card.dxf', mimeType: 'application/dxf',
      buffer: Buffer.from(saved.text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(saved.doc?.entities.length ?? saved.entityCount ?? 3));
    await tool('Fit extents'); await canvas.press('Escape'); await stableCamera();
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const click = async (point, button = 'left') => {
    const p = await screen(point);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'Unobscured canvas target');
    await page.mouse.move(p.x, p.y); await page.mouse.click(p.x, p.y, {button}); await page.waitForTimeout(150);
  };
  const openPatternContext = async doc => {
    await stableCamera();
    const source = doc.entities.find(e => e.id === doc.metadata.patterns[0].sourceEntityIds[0]);
    const p = await canvas.evaluate((c, source) => {
      const r = c.getBoundingClientRect();
      if (c.dataset.patternEditing === 'true') {
        for (let y = r.top + 100; y < r.bottom - 40; y += 40)
          for (let x = r.left + 180; x < r.right - 40; x += 40)
            if (document.elementFromPoint(x, y) === c) return {x, y};
      }
      for (let i = 0; i < 24; i++) {
        const angle = .19 + i * Math.PI / 12;
        const p = {x: r.left + (source.center.x + source.radius * Math.cos(angle)) * +c.dataset.scale + +c.dataset.offsetX,
          y: r.top - (source.center.y + source.radius * Math.sin(angle)) * +c.dataset.scale + +c.dataset.offsetY};
        if (document.elementFromPoint(p.x, p.y) === c) return p;
      }
      throw new Error('No unobscured source circumference');
    }, source);
    await page.mouse.move(p.x, p.y); await page.waitForTimeout(150);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'Context gesture reaches canvas');
    await page.mouse.click(p.x, p.y, {button: 'right'}); await expect(context).toBeVisible();
  };
  const show = async doc => {
    if (await card.isVisible()) return;
    await openPatternContext(doc); await context.getByRole('button', {name: 'Show pattern controls', exact: true}).click();
    await expect(handle).toBeVisible(); await expect(card).toBeVisible();
  };
  const automatic = async enabled => {
    await menu('Settings');
    const input = page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true});
    await input.setChecked(enabled); await expect(input).toBeChecked({checked: enabled}); await tool('Close settings');
  };
  const history = async (before, after, name) => {
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before.doc), 'Single atomic Undo');
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after.doc), 'Exact Redo');
  };
  const reopen = async (saved, name) => {
    await load(saved); assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc), 'Native reopen is lossless');
  };
  const moveCard = async (dx, dy, escape = false) => {
    await expect(header).toBeVisible(); const b = await header.boundingBox();
    if (escape) await header.focus();
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await page.mouse.down();
    try {
      await page.mouse.move(b.x + b.width / 2 + dx, b.y + b.height / 2 + dy, {steps: 12});
      if (escape) await page.keyboard.press('Escape');
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(180);
  };
  const parkCard = async () => {
    const c = await canvas.boundingBox(), b = await card.boundingBox();
    await moveCard(c.x + c.width - b.width - 20 - b.x, c.y + c.height - b.height - 20 - b.y);
  };
  const cardLayout = async name => {
    const c = await canvas.boundingBox(), b = await card.boundingBox();
    assert.ok(b && b.x >= c.x + 7 && b.y >= c.y + 7 && b.x + b.width <= c.x + c.width - 7
      && b.y + b.height <= c.y + c.height - 7, `${name}: entire card clamped inside canvas`);
    const overflow = await card.evaluate(node => ({scroll: node.scrollWidth, client: node.clientWidth}));
    assert.ok(overflow.scroll <= overflow.client + 1, 'No horizontal card overflow');
    assert.ok(await header.evaluate(h => {
      const r = h.getBoundingClientRect();
      return h.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2));
    }), `${name}: card move handle stays reachable above palettes`);
    evidence.layouts.push({name, viewport: page.viewportSize(), canvas: c, card: b});
    return b;
  };
  const draftCenter = async () => ({x: Number(await widget.getAttribute('data-center-x')), y: Number(await widget.getAttribute('data-center-y'))});
  const renderedCenter = () => handle.evaluate(h => {
    const c = document.querySelector('canvas.drawing-canvas'), r = c.getBoundingClientRect(), b = h.getBoundingClientRect();
    return {x: (b.x + b.width / 2 - r.x - +c.dataset.offsetX) / +c.dataset.scale,
      y: -(b.y + b.height / 2 - r.y - +c.dataset.offsetY) / +c.dataset.scale};
  });
  const dragCenter = async (target, name, excluded = false) => {
    const b = await handle.boundingBox(), start = {x: b.x + b.width / 2, y: b.y + b.height / 2};
    const beforeCamera = await camera();
    const exactStart = await screen(await draftCenter());
    const grabOffset = {x: (start.x - exactStart.x) / beforeCamera.scale,
      y: -(start.y - exactStart.y) / beforeCamera.scale};
    await page.mouse.move(start.x, start.y); await page.mouse.down();
    let expected, pointer, raw;
    try {
      await page.mouse.move(start.x + 10, start.y - 6, {steps: 3});
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'true'); await stableCamera();
      assert.deepEqual(await camera(), beforeCamera, 'Starting a drag does not refit the camera');
      const p = await screen(target.point), scale = (await camera()).scale;
      // Deliberately miss the exact target by 2 pixels. Edge snaps preserve X.
      pointer = {x: p.x + (target.edge ? 0 : 2), y: p.y + 2};
      raw = {x: target.point.x + (target.edge ? 0 : 2 / scale) - grabOffset.x,
        y: target.point.y - 2 / scale - grabOffset.y};
      expected = excluded ? raw : target.edge ? {x: raw.x, y: target.point.y} : target.point;
      await page.mouse.move(pointer.x, pointer.y, {steps: 16});
      await expect(widget).toHaveAttribute('data-center-dragging', 'true');
      await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
      await expect.poll(async () => {
        const center = await renderedCenter(); return Math.hypot(center.x - expected.x, center.y - expected.y) * scale;
      }, {message: `${name}: held center ${excluded ? 'does not self-snap' : 'acquires exact snap'}`}).toBeLessThan(.1);
      const h = await handle.boundingBox(), wanted = await screen(expected);
      pointNear({x: h.x + h.width / 2, y: h.y + h.height / 2}, wanted, 'Rendered center matches draft', 1);
      await capture(`${name}-held`);
    } finally { await page.mouse.up(); }
    await expect(widget).not.toHaveAttribute('data-center-dragging', 'true');
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
    await expect.poll(async () => {
      const center = await draftCenter(); return Math.hypot(center.x - expected.x, center.y - expected.y);
    }, {message: `${name}: server draft accepts released snapped center`}).toBeLessThan(1e-5);
    evidence.drags.push({name, target, excluded, pointer, raw, expected, released: await draftCenter()});
    return expected;
  };

  await load({text: fixture}); await automatic(false);
  await tool('Fix'); await click({x: 10.6, y: 10.8}); await canvas.press('Escape');
  const fixedSource = await download('source-fixed');
  assert.ok(fixedSource.doc.constraints.some(c => c.kind === 'Fix' && c.state === 'Satisfied'
    && c.referenceKeys.includes('circle-100')), 'Whole source is Fixed before creating pattern');
  await load(fixedSource);
  const sourcePick = await screen({x: 10.6, y: 10.8});
  await page.mouse.move(sourcePick.x, sourcePick.y);
  await expect(canvas).toHaveAttribute('data-hovered-id', 'circle-100');
  await page.mouse.click(sourcePick.x, sourcePick.y);
  await expect(canvas).toHaveAttribute('data-selected-keys', 'circle-100');
  await tool('Radial pattern');
  await expect(widget).toBeVisible();
  const count = widget.getByRole('spinbutton', {name: 'Viewport pattern instances', exact: true});
  await count.fill('4'); await count.press('Enter');
  for (const [axis, value] of [['X', '2'], ['Y', '3']]) {
    const input = editor.getByRole('spinbutton', {name: `Pattern center ${axis}`, exact: true});
    await input.fill(value); await input.press('Tab');
    await expect(widget).toHaveAttribute(`data-center-${axis.toLowerCase()}`, value);
  }
  await widget.getByRole('checkbox', {name: 'Full Circle', exact: true}).check();
  await widget.getByRole('button', {name: 'Confirm', exact: true}).click();
  await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
  const seed = await download('seed-unexposed');
  assert.equal(seed.doc.metadata.patterns[0].members.length, 3);
  assert.ok(!seed.doc.metadata.patterns[0].centerPointId, 'Seed has no eagerly exposed point');
  await load(seed); await show(seed.doc); await handle.click();
  const exposed = await download('seed-exposed');
  const centerId = exposed.doc.metadata.patterns[0].centerPointId; assert.ok(centerId);
  assertPattern(exposed.doc, seed.doc, {x: 2, y: 3});

  for (const spec of selected) {
    evidence.currentCase = spec.name;
    if (spec.mode === 'construction') {
      await page.setViewportSize({width: 1440, height: 1000}); await load({text: constructionFixture, entityCount: 2});
      const before = await download(`${spec.name}-before`);
      await canvas.evaluate(c => {
        const ctx = c.getContext('2d'), stroke = ctx.stroke;
        window.__patternQaConstructionStrokes = [];
        ctx.stroke = function (...args) {
          const dash = this.getLineDash();
          if (dash.length) window.__patternQaConstructionStrokes.push(dash);
          return stroke.apply(this, args);
        };
      });
      await tool('Construction'); await click({x: -1.4, y: 4});
      await click({x: 3 * Math.cos(.8), y: 3 * Math.sin(.8)}); await canvas.press('Escape');
      await tool('Fit extents'); await stableCamera();
      const changed = await download(`${spec.name}-construction`);
      assert.equal(changed.doc.entities.length, 2);
      for (const e of changed.doc.entities) {
        assert.equal(e.isConstruction, true, 'UI marks each disposable fixture entity as construction');
        assert.deepEqual({...e, isConstruction: false}, before.doc.entities.find(old => old.id === e.id),
          'Construction styling preserves geometry, identity and every other entity property');
      }
      const strokes = await page.evaluate(() => window.__patternQaConstructionStrokes);
      assert.ok(strokes.some(dash => dash.length === 2 && dash[0] === 8 && dash[1] === 9),
        'Actual canvas rendering uses 8px strokes with 9px gaps');
      await capture(spec.name);
      evidence.cases.push({name: spec.name, passed: true, observedWiderDash: [8, 9]});
      console.log(`PASS ${spec.name}`); continue;
    }
    if (spec.mode === 'card') {
      await page.setViewportSize({width: spec.width, height: spec.width === 1440 ? 1000 : 705});
      await ready(); await load(exposed); await show(exposed.doc);
      const initial = await cardLayout(`${spec.name}-initial`), c = await canvas.boundingBox();
      const cardCenter = {x: initial.x + initial.width / 2, y: initial.y + initial.height / 2};
      assert.ok(cardCenter.x > c.x + c.width * .25 && cardCenter.x < c.x + c.width * .8
        && cardCenter.y > c.y + c.height * .2 && cardCenter.y < c.y + c.height * .8,
      'Card initializes in the central visible viewport, not at a canvas edge or geometry anchor');
      const oldHistory = await historyState(), oldCenter = await draftCenter();
      await moveCard(-110, -65); const moved = await cardLayout(`${spec.name}-moved`);
      assert.ok(Math.hypot(moved.x - initial.x, moved.y - initial.y) > 50, 'Header drag actually moves the card');
      pointNear(await draftCenter(), oldCenter, 'Card movement cannot move pattern center');
      assert.deepEqual(await historyState(), oldHistory, 'Card drag adds no history');
      await moveCard(55, 35, true);
      pointNear(await card.boundingBox(), moved, 'Escape restores pre-drag card position', 1);
      await expect(card).toBeVisible();
      const clear = await canvas.evaluate(c => {
        const r = c.getBoundingClientRect();
        for (let y = r.top + 80; y < r.bottom - 80; y += 40)
          for (let x = r.left + 180; x < r.right - 180; x += 40)
            if (document.elementFromPoint(x, y) === c) return {x, y};
        throw new Error('No unobscured camera gesture location');
      });
      const beforeWheel = await camera(); await page.mouse.move(clear.x, clear.y); await page.mouse.wheel(0, 120);
      await expect.poll(async () => (await camera()).scale).not.toBe(beforeWheel.scale); await stableCamera();
      pointNear(await card.boundingBox(), moved, 'Zoom does not move card', 1);
      const beforePan = await camera(); await page.mouse.move(clear.x, clear.y); await page.mouse.down({button: 'middle'});
      try { await page.mouse.move(clear.x + 40, clear.y + 25, {steps: 10}); }
      finally { await page.mouse.up({button: 'middle'}); }
      await expect.poll(async () => (await camera()).x).not.toBe(beforePan.x); await stableCamera();
      pointNear(await card.boundingBox(), moved, 'Pan does not move card', 1);
      await expect(widget.getByRole('button', {name: 'Edit pattern', exact: true})).toHaveCount(0);
      await count.fill('5'); await count.press('Enter'); await expect(count).toHaveValue('5');
      pointNear(await card.boundingBox(), moved, 'Editing an input does not drag/reanchor card', 1);
      await openPatternContext(exposed.doc);
      await context.getByRole('button', {name: 'Hide pattern controls', exact: true}).click(); await expect(widget).toHaveCount(0);
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
      assert.deepEqual(content((await download(`${spec.name}-hidden-draft`)).doc), content(exposed.doc), 'Hidden draft remains uncommitted');
      await show(exposed.doc); await expect(count).toHaveValue('5');
      pointNear(await card.boundingBox(), moved, 'Hide/show preserves card position', 1);
      assert.deepEqual(await historyState(), oldHistory, 'Camera/card/draft controls add no commit history');
      await moveCard(2000, 2000); await cardLayout(`${spec.name}-clamped-bottom-right`); await capture(`${spec.name}-bottom-right`);
      await moveCard(-2000, -2000); await cardLayout(`${spec.name}-clamped-top-left`); await capture(`${spec.name}-top-left`);
      await widget.getByRole('button', {name: 'Cancel pattern changes', exact: true}).click();
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
      assert.deepEqual(content((await download(`${spec.name}-cancelled`)).doc), content(exposed.doc));
      assert.deepEqual(await historyState(), oldHistory);
      evidence.cases.push({name: spec.name, passed: true}); console.log(`PASS ${spec.name}`); continue;
    }

    await page.setViewportSize({width: 1440, height: 1000});
    // ON begins without a point to verify exposure+relation+geometry share one commit.
    const original = spec.mode === 'exclude-center' || spec.mode === 'fixed' ? exposed : seed;
    await load(original); await automatic(spec.automatic); await show(original.doc); await parkCard();
    let baseline = await download(`${spec.name}-before`);
    if (spec.mode === 'fixed') {
      await handle.click();
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
      await expect(canvas).toHaveAttribute('data-selected-keys', centerId);
      await tool('Fix'); await canvas.press('Escape');
      baseline = await download(`${spec.name}-fixed`);
      assert.ok(baseline.doc.constraints.some(c => c.kind === 'Fix' && c.state === 'Satisfied'
        && c.referenceKeys.some(k => owner(k) === centerId)), 'Center Fixed through real UI');
      await load(baseline); await show(baseline.doc); await parkCard();
      const b = await handle.boundingBox(), destination = await screen(spec.target.point);
      await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await page.mouse.down();
      try { await page.mouse.move(destination.x + 2, destination.y + 2, {steps: 16}); }
      finally { await page.mouse.up(); }
      await page.waitForTimeout(300);
      const confirm = widget.getByRole('button', {name: 'Confirm', exact: true});
      if (await confirm.count() && await confirm.isEnabled()) await confirm.click();
      await capture(spec.name);
      assert.deepEqual(content((await download(`${spec.name}-refused`)).doc), content(baseline.doc), 'Fixed refusal preserves all document content');
      if (await widget.getByRole('button', {name: 'Cancel pattern changes', exact: true}).isVisible()) {
        await widget.getByRole('button', {name: 'Cancel pattern changes', exact: true}).click();
        await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
        await expect(editor).toBeHidden();
      }
      assert.deepEqual(await historyState(), {undo: false, redo: false}, 'Refusal adds no history');
      await reopen(baseline, spec.name);
    } else {
      const excluded = spec.mode.startsWith('exclude-');
      let target = spec.target;
      if (excluded) {
        const p = baseline.doc.metadata.patterns[0];
        target = {point: spec.mode === 'exclude-center' ? p.center
          : baseline.doc.entities.find(e => e.id === p.members[0].entityId).center};
      }
      const beforeHistory = await historyState();
      const expected = await dragCenter(target, spec.name, excluded);
      assert.deepEqual(content((await download(`${spec.name}-staged`)).doc), content(baseline.doc), 'Pointer release stages only');
      assert.deepEqual(await historyState(), beforeHistory, 'Preview creates no history');
      if (spec.mode === 'cancel') {
        await widget.getByRole('button', {name: 'Cancel pattern changes', exact: true}).click();
        await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
        assert.deepEqual(content((await download(`${spec.name}-cancelled`)).doc), content(baseline.doc));
        assert.deepEqual(await historyState(), beforeHistory, 'Cancel adds no history or exposed point');
      } else {
        await widget.getByRole('button', {name: 'Confirm', exact: true}).click();
        await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
        const changed = await download(`${spec.name}-committed`); assertPattern(changed.doc, baseline.doc, expected);
        const additions = changed.doc.constraints.filter(c => !baseline.doc.constraints.some(old => old.id === c.id));
        for (const old of baseline.doc.constraints) assert.deepEqual(changed.doc.constraints.find(c => c.id === old.id), old);
        if (spec.automatic && !excluded) {
          assert.equal(additions.length, 1, 'ON persists exactly one snap relation');
          const relation = additions[0], pointId = changed.doc.metadata.patterns[0].centerPointId;
          assert.ok(pointId, 'Persistent relation uses a real construction point'); assert.equal(relation.state, 'Satisfied');
          assert.equal(relation.kind, target.kind);
          const refs = relation.referenceKeys.map(semantic);
          assert.ok(refs.some(k => owner(k) === pointId));
          assert.ok(refs.includes(target.ref), `Snap relation refers to ${target.ref}: ${refs.join(', ')}`);
          assert.equal(changed.doc.entities.length, baseline.doc.entities.length + (baseline.doc.metadata.patterns[0].centerPointId ? 0 : 1));
        } else {
          assert.deepEqual(additions, [], 'OFF/excluded targets create no inferred relation');
          const oldPoint = baseline.doc.metadata.patterns[0].centerPointId, newPoint = changed.doc.metadata.patterns[0].centerPointId;
          if (excluded || oldPoint) assert.equal(newPoint, oldPoint, 'Existing center owner is preserved; excluded targets do not expose one');
          assert.equal(changed.doc.entities.length, baseline.doc.entities.length + (!oldPoint && newPoint ? 1 : 0),
            'Snap-only validation can expose at most one real construction point, never extra geometry');
        }
        await capture(spec.name); await history(baseline, changed, spec.name); await reopen(changed, spec.name);
      }
    }
    evidence.cases.push({name: spec.name, passed: true}); console.log(`PASS ${spec.name}`);
    await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2));
  }
  assert.deepEqual(evidence.errors, []); evidence.passed = true; delete evidence.currentCase;
} catch (error) {
  failure = error; evidence.failure = error.stack;
  await page?.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2)); await browser.close();
}
if (failure) throw failure;
console.log(JSON.stringify({passed: true, cases: evidence.cases.length, build: evidence.build, output}));
