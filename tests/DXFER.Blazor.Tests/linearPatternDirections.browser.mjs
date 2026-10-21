import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
// Deliberately require the coordinated QA host; never default to an installed environment.
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated local DXFER_QA_URL before running this replay');
assert.ok(['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-linear-pattern-directions';
await mkdir(output, {recursive: true});

// The direction references are separate from the repeated circle and from each other.
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  '0', 'CIRCLE', '5', '100', '8', '0', '10', '0', '20', '0', '40', '.4',
  '0', 'LINE', '5', '200', '8', '0', '10', '-7', '20', '-6', '11', '-3', '21', '-5',
  '0', 'LWPOLYLINE', '5', '300', '8', '0', '90', '3', '70', '0',
  '10', '6', '20', '-6', '10', '8', '20', '-2', '10', '10', '20', '-3',
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const lines = text.trim().split(/\r?\n/), strings = []; let hex = '';
  for (let i = 0; i < lines.length; i += 2) {
    if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    if (lines[i].trim() === '1') strings.push(lines[i + 1].trim());
  }
  assert.ok(hex, 'export retains native document');
  const bytes = Buffer.from(hex, 'hex');
  assert.ok(strings.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()), 'native checksum');
  return JSON.parse(bytes);
};
function withNative(text, doc) {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/, bytes.toString('hex').match(/.{1,240}/g).map(chunk => `310\n${chunk}\n`).join(''));
}
const close = (actual, expected, label, tolerance = 1e-5) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${label}: ${actual} != ${expected}`);
const geometry = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  patterns: doc.metadata.patterns, layers: doc.metadata.layers, entityStyles: doc.metadata.entityStyles});
const midpoint = (a, b, t = .4) => ({x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t});
function direction(doc, reference) {
  const [id, segment] = reference.split('|segment|');
  const entity = doc.entities.find(item => item.id === id);
  assert.ok(entity, `direction owner exists: ${reference}`);
  assert.equal(entity.kind, segment === undefined ? 'line' : 'polyline');
  const a = segment === undefined ? entity.start : entity.vertices[+segment];
  const b = segment === undefined ? entity.end : entity.vertices[+segment + 1];
  const length = Math.hypot(b.x - a.x, b.y - a.y);
  assert.ok(length > 1e-7);
  return {x: (b.x - a.x) / length, y: (b.y - a.y) / length};
}
function assertPattern(doc, originalSource) {
  assert.equal(doc.metadata.patterns.length, 1);
  const p = doc.metadata.patterns[0], angle = p.angleDegrees * Math.PI / 180;
  const u = p.uDirectionReference ? direction(doc, p.uDirectionReference) : {x: Math.cos(angle), y: Math.sin(angle)};
  const v = p.vDirectionReference ? direction(doc, p.vDirectionReference) : {x: -u.y, y: u.x};
  const source = doc.entities.find(e => e.id === p.sourceEntityIds[0]);
  assert.deepEqual(source, originalSource, 'reference edits/settings never rewrite the pattern source');
  assert.equal(p.members.length, p.columns * p.rows - 1);
  assert.equal(doc.entities.length, p.columns * p.rows + 2);
  assert.equal(new Set(doc.entities.map(e => e.id)).size, doc.entities.length);
  for (const member of p.members) {
    const entity = doc.entities.find(e => e.id === member.entityId);
    assert.equal(member.sourceEntityId, source.id);
    const dx = (member.column + (p.kind === 'Staggered' && member.row % 2 ? p.staggerFraction : 0)) * p.xPitch;
    const dy = member.row * p.yPitch;
    close(entity.center.x, source.center.x + dx * u.x + dy * v.x, 'member X follows linked basis');
    close(entity.center.y, source.center.y + dx * u.y + dy * v.y, 'member Y follows linked basis');
    close(entity.radius, source.radius, 'member radius');
    assert.equal(entity.layer, source.layer, 'member layer');
  }
  return p;
}

// Layout contract: independent transparent-overlay groups stay inside the canvas,
// clear each other and tool palettes at both viewport sizes, and retain usable targets.
const evidence = {layouts: [], cases: [], exports: [], errors: []};
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
let page, failure;
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => evidence.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) evidence.errors.push(message.text());
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  evidence.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(evidence.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  const widget = page.getByRole('region', {name: 'Viewport pattern parameters', exact: true});
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const field = name => widget.getByLabel(`Viewport pattern ${name}`, {exact: true});
  const button = name => widget.getByRole('button', {name, exact: true});
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(160); };
  const menu = async name => {
    await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    const doc = native(text); evidence.exports.push(name); return {text, doc};
  };
  const load = async (text, count) => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'linear-directions.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(300);
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const click = async (point, mouseButton = 'left') => {
    const p = await screen(point);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'canvas target is not obscured');
    await page.mouse.click(p.x, p.y, {button: mouseButton}); await page.waitForTimeout(200);
  };
  const set = async (name, value) => { await field(name).fill(value); await field(name).press('Enter'); await expect(field(name)).toHaveValue(value); };
  const decimal = async (input, value) => {
    await input.focus(); await input.press('ControlOrMeta+A');
    for (const char of value) { await page.keyboard.press(char); await page.waitForTimeout(80); }
    await expect(input).toHaveValue(value); await input.press('Enter'); await expect(input).toHaveValue(value);
  };
  const confirm = async () => {
    await expect(button('Confirm')).toBeEnabled(); await button('Confirm').click();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
  };
  const layout = async name => {
    await expect(widget).toBeVisible(); const bounds = await canvas.boundingBox();
    const style = await widget.evaluate(e => ({background: getComputedStyle(e).backgroundColor, pointerEvents: getComputedStyle(e).pointerEvents}));
    assert.deepEqual(style, {background: 'rgba(0, 0, 0, 0)', pointerEvents: 'none'});
    const overlaps = (a, b) => a.x < b.x + b.width - .5 && a.x + a.width > b.x + .5
      && a.y < b.y + b.height - .5 && a.y + a.height > b.y + .5;
    const obstacles = [];
    for (const item of await page.locator('.dxfer-tool-group, .dxfer-sync-control-bar').all())
      if (await item.isVisible()) obstacles.push({name: 'palette', box: await item.boundingBox()});
    const groups = [];
    for (const item of await widget.locator('[data-pattern-anchor]').all()) {
      const anchor = await item.getAttribute('data-pattern-anchor'), box = await item.boundingBox();
      await expect(item).toBeVisible(); assert.ok(box.width > 0 && box.height > 0);
      assert.ok(box.x >= bounds.x - 1 && box.y >= bounds.y - 1 && box.x + box.width <= bounds.x + bounds.width + 1
        && box.y + box.height <= bounds.y + bounds.height + 1, `${anchor} within canvas`);
      for (const other of [...obstacles, ...groups]) assert.ok(!overlaps(box, other.box), `${anchor} clears ${other.name}`);
      groups.push({name: anchor, box});
    }
    assert.ok(groups.length >= 3);
    evidence.layouts.push({name, viewport: page.viewportSize(), groups});
    await page.screenshot({path: `${output}/${name}.png`});
  };
  const pick = async (axis, point, reference) => {
    await button(`Pick ${axis} direction`).click();
    await expect(widget).toHaveAttribute('data-pattern-direction-picking', axis);
    await expect(button(`Pick ${axis} direction`)).toHaveAttribute('aria-pressed', 'true');
    await expect(button('Confirm')).toBeDisabled();
    const target = await screen(point);
    await page.mouse.move(target.x, target.y); await page.waitForTimeout(250);
    evidence.cases.push({phase: 'direction-pick-hover', axis, reference, point, target,
      hovered: await canvas.getAttribute('data-hovered-id'), selected: await canvas.getAttribute('data-selected-keys')});
    await expect(canvas).toHaveAttribute('data-hovered-id', reference);
    await click(point);
    await expect(widget).toHaveAttribute(`data-${axis.toLowerCase()}-reference`, reference);
    await expect.poll(async () => [reference, `${axis} direction linked; pick a replacement edge`]
      .includes(await button(`Pick ${axis} direction`).getAttribute('title'))).toBe(true);
    await expect(button(`Pick ${axis} direction`)).toHaveAttribute('aria-pressed', 'false');
    await expect(button(`Clear ${axis} direction reference`)).toBeEnabled();
  };
  const show = async (doc, action = 'Show pattern controls') => {
    await canvas.press('Escape');
    const member = doc.entities.find(e => e.id === doc.metadata.patterns[0].members.at(-1).entityId);
    await click({x: member.center.x + member.radius * .6, y: member.center.y + member.radius * .8}, 'right');
    await context.getByRole('button', {name: action, exact: true}).click();
    if (action.startsWith('Hide')) await expect(widget).toHaveCount(0); else await expect(widget).toBeVisible();
  };
  const dragEnd = async (line, delta, name, expectPreview = true) => {
    await canvas.press('Escape'); await page.waitForTimeout(250);
    let selectionPoint;
    for (const fraction of [.4, .65, .25, .8]) {
      const candidate = midpoint(line.start, line.end, fraction), target = await screen(candidate);
      await page.mouse.move(target.x, target.y); await page.waitForTimeout(150);
      if (await canvas.getAttribute('data-hovered-id') === line.id) { selectionPoint = candidate; break; }
    }
    assert.ok(selectionPoint, `${name}: an unobscured line body target is available`);
    await click(selectionPoint);
    await expect(canvas).toHaveAttribute('data-selected-count', '1');
    const a = await screen(line.end), b = await screen({x: line.end.x + delta.x, y: line.end.y + delta.y});
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(150);
    await expect(canvas).toHaveAttribute('data-hovered-id', new RegExp(`^${line.id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\|point\\|end`));
    const before = await canvas.screenshot(); await page.mouse.down();
    try {
      await page.mouse.move(b.x, b.y, {steps: 12}); await page.waitForTimeout(600);
      if (expectPreview) assert.notDeepEqual(await canvas.screenshot(), before, 'held reference drag paints preview');
      await page.screenshot({path: `${output}/${name}-held.png`});
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(400); await canvas.press('Escape');
  };

  for (const viewport of [{width: 1440, height: 1000}, {width: 1100, height: 705}]) {
    await page.setViewportSize(viewport); const tag = String(viewport.width);
    await load(fixture, 3);
    const initial = (await download(`${tag}-initial`)).doc;
    const source = initial.entities.find(e => e.kind === 'circle'), line = initial.entities.find(e => e.kind === 'line');
    const poly = initial.entities.find(e => e.kind === 'polyline'), polyKey = `${poly.id}|segment|0`;
    const sourcePick = {x: source.center.x + source.radius * .6, y: source.center.y + source.radius * .8};
    await canvas.press('Escape'); await page.waitForTimeout(250);
    for (let attempt = 0; attempt < 4; attempt++) {
      const target = await screen(sourcePick); await page.mouse.move(target.x, target.y); await page.waitForTimeout(150);
      if (await canvas.getAttribute('data-hovered-id') === source.id) break;
      await page.mouse.wheel(0, -250); await page.waitForTimeout(250);
    }
    await expect(canvas).toHaveAttribute('data-hovered-id', source.id);
    await click(sourcePick); await expect(canvas).toHaveAttribute('data-selected-keys', source.id);
    await tool('Rectangular / staggered pattern');
    await set('U count', '3'); await set('V count', '2');
    await decimal(field('U pitch'), '2.75'); await decimal(field('V pitch'), '3.125');
    await decimal(page.getByRole('region', {name: 'Pattern settings', exact: true}).getByLabel('Pattern rotation', {exact: true}), '17.5');
    await layout(`${tag}-default-basis-draft`); await confirm();
    const defaults = await download(`${tag}-default-basis`);
    const defaultPattern = assertPattern(defaults.doc, source);
    assert.equal(defaultPattern.columns, 3); assert.equal(defaultPattern.rows, 2);
    close(defaultPattern.angleDegrees, 17.5, 'unlinked U uses fallback angle');
    assert.ok(defaultPattern.uDirectionReference == null && defaultPattern.vDirectionReference == null);
    close(defaultPattern.staggerFraction, .5, 'default stagger fraction');

    await button('Edit pattern').click();
    await pick('U', midpoint(line.start, line.end), line.id); await confirm();
    const linkedU = await download(`${tag}-linked-u`);
    const uPattern = assertPattern(linkedU.doc, source);
    assert.equal(uPattern.uDirectionReference, line.id); assert.ok(uPattern.vDirectionReference == null);
    await button('Edit pattern').click();
    await pick('V', midpoint(poly.vertices[0], poly.vertices[1]), polyKey);
    await button('Flip U direction').click(); await expect(field('U pitch')).toHaveValue('-2.75');
    await button('Flip V direction').click(); await expect(field('V pitch')).toHaveValue('-3.125');
    await widget.getByRole('checkbox', {name: 'Viewport stagger rows', exact: true}).check();
    await decimal(widget.getByLabel('Viewport stagger fraction', {exact: true}), '0.375');
    await layout(`${tag}-skew-flipped-stagger-draft`); await confirm();
    const linked = await download(`${tag}-linked-skew`), p = assertPattern(linked.doc, source);
    assert.equal(p.kind, 'Staggered'); assert.equal(p.uDirectionReference, line.id); assert.equal(p.vDirectionReference, polyKey);
    close(p.xPitch, -2.75, 'signed U'); close(p.yPitch, -3.125, 'signed V'); close(p.staggerFraction, .375, 'fraction persisted');
    await tool('Undo'); assert.deepEqual(geometry((await download(`${tag}-settings-undo`)).doc), geometry(linkedU.doc));
    await tool('Redo'); assert.deepEqual(geometry((await download(`${tag}-settings-redo`)).doc), geometry(linked.doc));

    await button('Edit pattern').click();
    await button('Clear U direction reference').click(); await button('Clear V direction reference').click();
    await expect(button('Clear U direction reference')).toBeDisabled();
    await expect(button('Clear V direction reference')).toBeDisabled();
    await button('Cancel pattern changes').click();
    assert.deepEqual(geometry((await download(`${tag}-clear-cancel`)).doc), geometry(linked.doc));
    await button('Edit pattern').click();
    await widget.getByRole('checkbox', {name: 'Viewport stagger rows', exact: true}).uncheck();
    await expect(widget.getByLabel('Viewport stagger fraction', {exact: true})).toHaveCount(0);
    await confirm();
    const unstaggered = (await download(`${tag}-stagger-off`)).doc;
    assert.equal(assertPattern(unstaggered, source).kind, 'Rectangular');
    assert.equal(unstaggered.metadata.patterns[0].uDirectionReference, line.id);
    assert.equal(unstaggered.metadata.patterns[0].vDirectionReference, polyKey);
    await tool('Undo'); assert.deepEqual(geometry((await download(`${tag}-stagger-restored`)).doc), geometry(linked.doc));

    await show(linked.doc, 'Hide pattern controls');
    await expect(page.locator('[data-pattern-anchor], [data-pattern-center-handle]')).toHaveCount(0);
    await show(linked.doc); await layout(`${tag}-shown`);
    assert.deepEqual(geometry((await download(`${tag}-visibility-unchanged`)).doc), geometry(linked.doc));
    await show(linked.doc, 'Hide pattern controls');
    await dragEnd(line, {x: 0, y: 1.25}, `${tag}-linked-reference`);
    const moved = await download(`${tag}-reference-moved`);
    const movedLine = moved.doc.entities.find(e => e.id === line.id);
    close(movedLine.end.y, line.end.y + 1.25, 'reference endpoint actually moved');
    close(movedLine.end.x, line.end.x, 'reference endpoint X');
    assert.deepEqual(movedLine.start, line.start, 'other reference endpoint stays');
    assertPattern(moved.doc, source);
    assert.notDeepEqual(moved.doc.entities.filter(e => p.members.some(m => m.entityId === e.id)),
      linked.doc.entities.filter(e => p.members.some(m => m.entityId === e.id)), 'dependent members actually regenerate');
    assert.deepEqual(moved.doc.entities.find(e => e.id === poly.id), poly, 'other direction reference stays');
    await tool('Undo'); assert.deepEqual(geometry((await download(`${tag}-reference-undo`)).doc), geometry(linked.doc));
    await tool('Redo'); assert.deepEqual(geometry((await download(`${tag}-reference-redo`)).doc), geometry(moved.doc));
    await load(moved.text, moved.doc.entities.length);
    assert.deepEqual(geometry((await download(`${tag}-reopened`)).doc), geometry(moved.doc));
    await dragEnd(movedLine, {x: -.75, y: 0}, `${tag}-reopened-reference`);
    const reedited = await download(`${tag}-reopened-edited`);
    close(reedited.doc.entities.find(e => e.id === line.id).end.x, movedLine.end.x - .75, 'reopened reference still editable');
    assertPattern(reedited.doc, source);

    // A real native Fix on a generated member must survive a refused pitch edit.
    const fixedDoc = structuredClone(linked.doc);
    const fixedMember = p.members.find(m => m.column === 1 && m.row === 0);
    fixedDoc.constraints.push({id: 'qa-fixed-pattern-member', kind: 'Fix', state: 'Satisfied', referenceKeys: [fixedMember.entityId]});
    await load(withNative(linked.text, fixedDoc), fixedDoc.entities.length);
    const fixedBefore = await download(`${tag}-fixed-before`);
    assert.ok(fixedBefore.doc.constraints.some(c => c.id === 'qa-fixed-pattern-member'), 'fixed fixture survived import');
    await show(fixedBefore.doc); await button('Edit pattern').click();
    await field('U pitch').fill('-4.25'); await field('U pitch').press('Tab');
    await expect(button('Confirm')).toBeDisabled(); await expect(widget.getByRole('alert')).toBeVisible();
    await layout(`${tag}-fixed-member-refused`); await button('Cancel pattern changes').click();
    assert.deepEqual(geometry((await download(`${tag}-fixed-after`)).doc), geometry(fixedBefore.doc));

    await load(linked.text, linked.doc.entities.length); await show(linked.doc);
    await tool('Lock layer 0'); const locked = await download(`${tag}-locked-before`);
    await expect(button('Edit pattern')).toBeDisabled(); await layout(`${tag}-locked-refused`);
    assert.deepEqual(geometry((await download(`${tag}-locked-after`)).doc), geometry(locked.doc));
    await tool('Undo'); await expect(button('Edit pattern')).toBeEnabled();
    assert.deepEqual(geometry((await download(`${tag}-unlocked-recovery`)).doc), geometry(linked.doc));
    evidence.cases.push({viewport, status: 'passed', checks: ['defaults', 'decimal typing', 'canvas line U', 'canvas segment V',
      'signed flips', 'stagger fraction', 'hide/show', 'reference regeneration', 'undo/redo', 'native reopen/reedit', 'fixed member', 'locked recovery']});
    console.log(`PASS linear directions ${tag}: linked regeneration, native/history, fixed/locked refusal`);
  }
  assert.deepEqual(evidence.errors, []);
} catch (error) {
  failure = error; evidence.failure = error.stack ?? String(error);
  if (page) await page.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/linear-pattern-directions-results.json`, JSON.stringify(evidence, null, 2));
  await browser.close();
}
if (failure) throw failure;
