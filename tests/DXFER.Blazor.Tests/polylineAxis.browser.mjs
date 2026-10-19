import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/polyline-axis';
await mkdir(output, {recursive: true});
const only = process.env.DXFER_CASES?.split(',');
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex'));
};
function withNative(text, doc) {
  const bytes = Buffer.from(JSON.stringify(doc));
  return text.replace(/(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/, bytes.toString('hex').match(/.{1,240}/g).map(c => `310\n${c}\n`).join(''));
}
const fixture = ({closed = true, horizontal = false} = {}) => {
  const points = [[0, 0], [8, 1], [7, 7], [0, horizontal ? 7 : 6]];
  return ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
    '0', 'SECTION', '2', 'ENTITIES', '0', 'LWPOLYLINE', '5', '100', '8', '0', '90', '4', '70', closed ? '1' : '0',
    ...points.flatMap(([x, y]) => ['10', String(x), '20', String(y)]),
    '0', 'CIRCLE', '5', '200', '8', '0', '10', '11', '20', '3', '40', '.6', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
};
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-8, `${a} != ${b}`);
function preserved(before, after, id, segment, geometryUnchanged = false) {
  assert.deepEqual(after.entities.map(e => e.id), before.entities.map(e => e.id), 'entity IDs/order preserved');
  const a = before.entities.find(e => e.id === id), b = after.entities.find(e => e.id === id);
  assert.equal(b.kind, 'polyline', 'selected segment must not replace its parent with a line');
  assert.equal(b.vertices.length, a.vertices.length, 'vertex topology preserved');
  assert.equal(b.isConstruction, a.isConstruction);
  const closed = JSON.stringify(a.vertices[0]) === JSON.stringify(a.vertices.at(-1));
  assert.equal(JSON.stringify(b.vertices[0]) === JSON.stringify(b.vertices.at(-1)), closed, 'closure preserved');
  const affected = new Set([segment, segment + 1]);
  if (closed && (affected.has(0) || affected.has(a.vertices.length - 1))) { affected.add(0); affected.add(a.vertices.length - 1); }
  for (let i = 0; i < a.vertices.length; i++) if (geometryUnchanged || !affected.has(i)) assert.deepEqual(b.vertices[i], a.vertices[i], `unaffected vertex ${i}`);
  assert.deepEqual(after.entities.filter(e => e.id !== id), before.entities.filter(e => e.id !== id), 'unrelated geometry unchanged');
  assert.deepEqual(after.metadata.entityStyles, before.metadata.entityStyles);
  assert.deepEqual(after.dimensions, before.dimensions, 'existing driving values/references preserved');
}
const results = [], errors = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  // Observe rendered badge rectangles to click their real canvas targets, without selecting via JS.
  await page.addInitScript(() => {
    window.__axisGlyphs = [];
    const clear = CanvasRenderingContext2D.prototype.clearRect;
    CanvasRenderingContext2D.prototype.clearRect = function (...args) {
      if (this.canvas.classList.contains('drawing-canvas')) window.__axisGlyphs = [];
      return clear.apply(this, args);
    };
    const rect = CanvasRenderingContext2D.prototype.rect;
    CanvasRenderingContext2D.prototype.rect = function (x, y, width, height) {
      if (this.canvas.classList.contains('drawing-canvas') && width === 16 && height === 16) {
        const t = this.getTransform(), p = new DOMPoint(x + 8, y + 8).matrixTransform(t), bounds = this.canvas.getBoundingClientRect();
        window.__axisGlyphs.push({x: bounds.left + p.x * bounds.width / this.canvas.width, y: bounds.top + p.y * bounds.height / this.canvas.height});
      }
      return rect.call(this, x, y, width, height);
    };
  });
  page.setDefaultTimeout(10000); page.on('dialog', d => d.accept());
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text()); });
  const ready = async () => { await page.goto(url); await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined); };
  await ready();
  const initialBuild = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(initialBuild.includes(process.env.DXFER_EXPECTED_REVISION), initialBuild);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(160); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); return {text, doc: native(text)};
  };
  const load = async (text = fixture(), count = 2) => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'polyline-axis.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count)); await tool('Fit extents'); await canvas.press('Escape');
    await page.waitForTimeout(300);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const click = async p => { const s = await screen(p); await page.mouse.click(s.x, s.y); };
  const selectEdge = async (poly, index) => {
    await canvas.press('Escape');
    await page.waitForTimeout(250);
    const a = poly.vertices[index], b = poly.vertices[index + 1];
    let p, s;
    // Dimension labels may legitimately overlay an edge. Pick another interior point, never a different target.
    for (const t of [.35, .7, .2, .8]) {
      p = {x: a.x * (1 - t) + b.x * t, y: a.y * (1 - t) + b.y * t};
      s = await screen(p); await page.mouse.move(s.x, s.y); await page.waitForTimeout(120);
      if (await canvas.getAttribute('data-hovered-id') === `${poly.id}|segment|${index}`) break;
    }
    if (await canvas.getAttribute('data-hovered-id') !== `${poly.id}|segment|${index}`) {
      await writeFile(`${output}/pick-miss-${poly.id}-${index}.json`, JSON.stringify({p, s,
        dataset: await canvas.evaluate(c => ({...c.dataset})), hit: await page.evaluate(p => document.elementFromPoint(p.x, p.y)?.outerHTML, s)}, null, 2));
    }
    assert.equal(await canvas.getAttribute('data-hovered-id'), `${poly.id}|segment|${index}`, 'actual segment pick, not midpoint/vertex');
    await page.mouse.click(s.x, s.y);
    await expect(canvas).toHaveAttribute('data-selected-keys', `${poly.id}|segment|${index}`);
  };
  const axis = async (poly, index, name) => { await selectEdge(poly, index); await tool(name); await page.waitForTimeout(250); await canvas.press('Escape'); };
  const history = async (before, after, prefix) => {
    await tool('Undo'); assert.deepEqual((await download(`${prefix}-undo`)).doc, before);
    await tool('Redo'); assert.deepEqual((await download(`${prefix}-redo`)).doc, after);
  };
  const run = async (name, action) => {
    if (only && !only.includes(name)) return;
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (e) { results.push({name, passed: false, error: e.stack}); console.error(`FAIL ${name}: ${e.message}`); await page.screenshot({path: `${output}/${name}-failure.png`}); }
    await writeFile(`${output}/results.json`, JSON.stringify({results, errors}, null, 2)); await ready();
  };

  for (const [name, options, command, segment] of [
    ['closed-sloped-horizontal', {}, 'Horizontal', 2],
    ['closed-already-horizontal', {horizontal: true}, 'Horizontal', 2],
    ['open-sloped-horizontal', {closed: false}, 'Horizontal', 2],
    ['closed-sloped-vertical', {}, 'Vertical', 1]
  ]) await run(name, async () => {
    await load(fixture(options)); const before = await download(`${name}-before`);
    const poly = before.doc.entities.find(e => e.kind === 'polyline'); assert.ok(poly);
    await axis(poly, segment, command);
    const after = await download(`${name}-after`); preserved(before.doc, after.doc, poly.id, segment);
    await page.screenshot({path: `${output}/${name}-applied.png`});
    const relation = after.doc.constraints.find(c => c.kind === command && c.referenceKeys.includes(`${poly.id}|segment|${segment}`));
    assert.ok(relation, 'axis constraint recorded on actual segment'); assert.equal(relation.state, 'Satisfied');
    const vertices = after.doc.entities.find(e => e.id === poly.id).vertices;
    near(vertices[segment][command === 'Horizontal' ? 'y' : 'x'], vertices[segment + 1][command === 'Horizontal' ? 'y' : 'x']);
    if (options.horizontal) assert.deepEqual(after.doc.entities, before.doc.entities);
    await history(before.doc, after.doc, name);
    await load(after.text); const reopened = (await download(`${name}-reopened`)).doc;
    assert.deepEqual(reopened.entities, after.doc.entities); assert.deepEqual(reopened.constraints, after.doc.constraints);
    const other = command === 'Horizontal' ? 1 : 2, nextCommand = command === 'Horizontal' ? 'Vertical' : 'Horizontal';
    await axis(reopened.entities.find(e => e.id === poly.id), other, nextCommand);
    const again = (await download(`${name}-reedit`)).doc; preserved(reopened, again, poly.id, other);
    assert.ok(again.constraints.every(c => c.state === 'Satisfied'));
    assert.ok(again.constraints.some(c => c.kind === nextCommand));
  });

  for (const mode of ['fixed', 'driving']) await run(`${mode}-conflict`, async () => {
    await load(); const seed = await download(`${mode}-seed`), doc = structuredClone(seed.doc);
    const poly = doc.entities.find(e => e.kind === 'polyline'), key = `${poly.id}|segment|2`;
    if (mode === 'fixed') doc.constraints.push({id: 'fixed-poly', kind: 'Fix', referenceKeys: [poly.id], state: 'Satisfied'});
    else doc.dimensions.push({id: 'top-rise', kind: 'VerticalDistance', referenceKeys: [`${key}:start`, `${key}:end`], value: 1, anchor: {x: 4, y: 8}, isDriving: true});
    await load(withNative(seed.text, doc)); const before = (await download(`${mode}-before`)).doc;
    await axis(poly, 2, 'Horizontal'); const after = (await download(`${mode}-after`)).doc;
    preserved(before, after, poly.id, 2, true);
    assert.deepEqual(after.constraints.filter(c => before.constraints.some(old => old.id === c.id)), before.constraints);
    const added = after.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
    assert.ok(added.every(c => c.kind === 'Horizontal' && c.state === 'Unsatisfied'), 'incompatible intent is refused or explicitly failed');
    if (added.length) await history(before, after, mode);
    else await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await page.screenshot({path: `${output}/${mode}-unchanged.png`});
  });

  await run('linked-offset', async () => {
    await load(); const seed = (await download('offset-seed')).doc, source = seed.entities.find(e => e.kind === 'polyline');
    await selectEdge(source, 2);
    const edgePick = await screen({x: source.vertices[2].x * .65 + source.vertices[3].x * .35,
      y: source.vertices[2].y * .65 + source.vertices[3].y * .35});
    await page.mouse.click(edgePick.x, edgePick.y, {button: 'right'});
    await page.getByRole('dialog', {name: 'Drawing context commands'}).getByRole('button', {name: 'Offset', exact: true}).click();
    await expect(canvas).toHaveAttribute('data-active-tool', 'offset');
    await expect(canvas).toHaveAttribute('data-selected-keys', source.id);
    await page.waitForTimeout(200);
    const p = await screen({x: 4, y: 8}); await page.mouse.move(p.x, p.y);
    const input = page.locator('input[data-dimension-key="offset"]'); await expect(input).toBeVisible();
    await input.fill('1'); await input.press('Enter'); await expect(canvas).toHaveAttribute('data-entity-count', '3'); await canvas.press('Escape');
    const linked = await download('offset-linked'), copy = linked.doc.entities.find(e => e.kind === 'polyline' && e.id !== source.id);
    assert.ok(copy); assert.ok(linked.doc.constraints.some(c => c.kind === 'Offset'));
    // Offset may reverse winding: identify the upper sloped edge from the actual generated vertices.
    const edge = copy.vertices.slice(0, -1).map((a, index) => ({index, a, b: copy.vertices[index + 1]}))
      .filter(e => Math.abs(e.a.x - e.b.x) > 1e-6 && Math.abs(e.a.y - e.b.y) > 1e-6)
      .sort((a, b) => (b.a.y + b.b.y) - (a.a.y + a.b.y))[0];
    assert.ok(edge, 'generated top edge is sloped, nonvertical, and can geometrically become horizontal');
    await axis(copy, edge.index, 'Horizontal'); const attempted = await download('offset-axis-attempt');
    preserved(linked.doc, attempted.doc, copy.id, edge.index, true);
    assert.deepEqual(attempted.doc.constraints.filter(c => c.kind === 'Offset'), linked.doc.constraints.filter(c => c.kind === 'Offset'));
    assert.ok(attempted.doc.constraints.filter(c => c.kind === 'Horizontal').every(c => c.state === 'Unsatisfied'));
    await page.screenshot({path: `${output}/linked-offset-refusal.png`});
    // Isolate unlinking from any failed H intent retained by the constraint UI.
    await load(linked.text, 3);
    await tool('Show constraints');
    const constraint = linked.doc.constraints.find(c => c.kind === 'Offset');
    // Actual canvas hover hit testing finds the rendered glyph; no programmatic selection or document mutation.
    const glyphs = await page.evaluate(() => window.__axisGlyphs); let found = false;
    for (const p of glyphs) {
      await page.mouse.move(p.x, p.y); await page.waitForTimeout(80);
      if ((await canvas.getAttribute('data-hovered-id')) === `constraint:${constraint.id}`) { await page.mouse.click(p.x, p.y); found = true; break; }
    }
    assert.ok(found, 'Offset glyph is selectable for deletion');
    await canvas.press('Delete'); await page.waitForTimeout(250);
    const unlinked = (await download('offset-relation-deleted')).doc;
    assert.deepEqual(unlinked.entities, linked.doc.entities); assert.ok(!unlinked.constraints.some(c => c.kind === 'Offset'));
    await axis(copy, edge.index, 'Horizontal'); const changed = (await download('offset-unlinked-horizontal')).doc;
    preserved(unlinked, changed, copy.id, edge.index);
    assert.ok(changed.constraints.some(c => c.kind === 'Horizontal' && c.state === 'Satisfied'));
    const vertices = changed.entities.find(e => e.id === copy.id).vertices;
    near(vertices[edge.index].y, vertices[edge.index + 1].y);
    await history(unlinked, changed, 'offset-unlinked');
  });
  const build = await page.locator('.app-brand').getAttribute('title');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors,
    limits: ['DXF bulges are not represented by PolylineEntity; no curved-edge retention claim.']}, null, 2));
  assert.deepEqual(errors, []); assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
