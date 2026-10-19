import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/axis-recovery';
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
const fixture = vertical => ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', '0', 'LINE', '5', '100', '8', '0', '10', '0', '20', '0',
  '11', vertical ? '0' : '10', '21', vertical ? '10' : '0',
  '0', 'CIRCLE', '5', '200', '8', '0', '10', '7', '20', '7', '40', '.6', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-8, `${a} != ${b}`);
const lineOf = doc => doc.entities.find(e => e.kind === 'line');
function unchangedPeers(before, after, id) {
  assert.deepEqual(after.entities.map(e => [e.id, e.kind]), before.entities.map(e => [e.id, e.kind]));
  assert.deepEqual(after.entities.filter(e => e.id !== id), before.entities.filter(e => e.id !== id));
  assert.deepEqual(after.dimensions, before.dimensions);
  assert.deepEqual(after.metadata.entityStyles, before.metadata.entityStyles);
}
function aligned(doc, axis) {
  const line = lineOf(doc); assert.ok(line);
  near(line.start[axis === 'Horizontal' ? 'y' : 'x'], line.end[axis === 'Horizontal' ? 'y' : 'x']);
  near(Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y), 10);
  assert.ok(doc.constraints.some(c => c.kind === axis && c.state === 'Satisfied'), 'requested axis is satisfied');
}
const results = [], errors = [], observations = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  // Passive render observation supplies real badge positions; selection/deletion still use pointer and keyboard.
  await page.addInitScript(() => {
    window.__axisRecoveryGlyphs = [];
    const clear = CanvasRenderingContext2D.prototype.clearRect, rect = CanvasRenderingContext2D.prototype.rect;
    CanvasRenderingContext2D.prototype.clearRect = function (...args) {
      if (this.canvas.classList.contains('drawing-canvas')) window.__axisRecoveryGlyphs = [];
      return clear.apply(this, args);
    };
    CanvasRenderingContext2D.prototype.rect = function (x, y, w, h) {
      if (this.canvas.classList.contains('drawing-canvas') && w === 16 && h === 16) {
        const p = new DOMPoint(x + 8, y + 8).matrixTransform(this.getTransform()), b = this.canvas.getBoundingClientRect();
        window.__axisRecoveryGlyphs.push({x: b.left + p.x * b.width / this.canvas.width, y: b.top + p.y * b.height / this.canvas.height});
      }
      return rect.call(this, x, y, w, h);
    };
  });
  page.setDefaultTimeout(10000); page.on('dialog', d => d.accept()); page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text()); });
  const ready = async () => { await page.goto(url); await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined); };
  await ready();
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(160); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8'); await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async text => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'axis-recovery.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '2'); await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(300);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const applyAxis = async (line, name) => {
    await canvas.press('Escape'); await page.waitForTimeout(250); let s;
    for (const t of [.35, .7, .2, .8]) {
      s = await screen({x: line.start.x * (1 - t) + line.end.x * t, y: line.start.y * (1 - t) + line.end.y * t});
      await page.mouse.move(s.x, s.y); await page.waitForTimeout(100);
      if (await canvas.getAttribute('data-hovered-id') === line.id) break;
    }
    assert.equal(await canvas.getAttribute('data-hovered-id'), line.id, 'pick actual line body, not an endpoint or glyph');
    await page.mouse.click(s.x, s.y); await expect(canvas).toHaveAttribute('data-selected-keys', line.id);
    await tool(name); await page.waitForTimeout(200); await canvas.press('Escape');
  };
  const history = async (before, after, name) => {
    await tool('Undo'); assert.deepEqual((await download(`${name}-undo`)).doc, before);
    await tool('Redo'); assert.deepEqual((await download(`${name}-redo`)).doc, after);
  };
  const reopen = async (saved, name) => {
    await load(saved.text); const doc = (await download(`${name}-reopened`)).doc;
    assert.deepEqual(doc.entities, saved.doc.entities); assert.deepEqual(doc.constraints, saved.doc.constraints); assert.deepEqual(doc.dimensions, saved.doc.dimensions);
  };
  const deleteGlyph = async id => {
    if (await canvas.getAttribute('data-show-all-constraints') !== 'true') await tool('Show constraints');
    await page.waitForTimeout(150);
    const glyphs = await page.evaluate(() => window.__axisRecoveryGlyphs); let found = false;
    for (const p of glyphs) {
      await page.mouse.move(p.x, p.y); await page.waitForTimeout(100);
      if (await canvas.getAttribute('data-hovered-id') === `constraint:${id}`) {
        await page.mouse.click(p.x, p.y); found = true; break;
      }
    }
    assert.ok(found, `actual glyph selectable: ${id}`);
    await expect(canvas).toHaveAttribute('data-selected-keys', `constraint:${id}`);
    await canvas.press('Delete'); await page.waitForTimeout(300);
  };
  const run = async (name, action) => {
    if (only && !only.includes(name)) return;
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (e) { results.push({name, passed: false, error: e.stack}); console.error(`FAIL ${name}: ${e.message}`); await page.screenshot({path: `${output}/${name}-failure.png`}); }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, observations, errors}, null, 2)); await ready();
  };
  for (const vertical of [true, false]) {
    const requested = vertical ? 'Horizontal' : 'Vertical', existing = vertical ? 'Vertical' : 'Horizontal';
    const tag = vertical ? 'vertical-to-horizontal' : 'horizontal-to-vertical';
    await run(tag, async () => {
      await load(fixture(vertical)); const before = (await download(`${tag}-before`)).doc, line = lineOf(before);
      await applyAxis(line, requested); const after = await download(`${tag}-after`);
      unchangedPeers(before, after.doc, line.id);
      observations.push({name: tag, geometryUnchanged: JSON.stringify(before.entities) === JSON.stringify(after.doc.entities), constraints: after.doc.constraints});
      aligned(after.doc, requested); await page.screenshot({path: `${output}/${tag}-aligned.png`});
      await history(before, after.doc, tag); await reopen(after, tag);
    });
    await run(`${tag}-delete-blocker`, async () => {
      await load(fixture(vertical)); const seed = (await download(`${tag}-recovery-seed`)).doc, line = lineOf(seed);
      await applyAxis(line, existing); const withBlocker = (await download(`${tag}-with-blocker`)).doc;
      assert.ok(withBlocker.constraints.some(c => c.kind === existing && c.state === 'Satisfied'));
      await applyAxis(line, requested); const conflict = (await download(`${tag}-conflict`)).doc;
      assert.deepEqual(conflict.entities, seed.entities, 'conflict must not collapse or move geometry');
      const blocker = conflict.constraints.find(c => c.kind === existing), failed = conflict.constraints.find(c => c.kind === requested);
      assert.ok(blocker && failed); assert.equal(blocker.state, 'Satisfied'); assert.equal(failed.state, 'Unsatisfied');
      await page.screenshot({path: `${output}/${tag}-conflict.png`});
      await deleteGlyph(blocker.id); const after = await download(`${tag}-blocker-deleted`);
      assert.ok(!after.doc.constraints.some(c => c.id === blocker.id));
      assert.equal(after.doc.constraints.find(c => c.id === failed.id)?.kind, requested, 'remaining intent retains identity');
      unchangedPeers(conflict, after.doc, line.id);
      observations.push({name: `${tag}-delete-blocker`, geometryUnchanged: JSON.stringify(conflict.entities) === JSON.stringify(after.doc.entities), remaining: after.doc.constraints});
      await page.screenshot({path: `${output}/${tag}-after-delete.png`});
      await history(conflict, after.doc, `${tag}-delete`); await reopen(after, `${tag}-delete`);
      // Recovery is intentionally a separate assertion: baseline artifacts retain the existing deletion behavior.
      aligned(after.doc, requested);
    });
    await run(`${tag}-delete-failed`, async () => {
      await load(fixture(vertical)); const seed = (await download(`${tag}-negative-seed`)).doc, line = lineOf(seed);
      await applyAxis(line, existing); await applyAxis(line, requested);
      const before = (await download(`${tag}-negative-conflict`)).doc;
      const failed = before.constraints.find(c => c.kind === requested), satisfied = before.constraints.find(c => c.kind === existing);
      assert.equal(failed?.state, 'Unsatisfied'); assert.equal(satisfied?.state, 'Satisfied');
      await deleteGlyph(failed.id); const after = await download(`${tag}-failed-deleted`);
      assert.deepEqual(after.doc.entities, seed.entities, 'deleting failed intent must not move geometry');
      unchangedPeers(before, after.doc, line.id);
      assert.deepEqual(after.doc.constraints, [satisfied], 'retain the already-satisfied opposite relation');
      await page.screenshot({path: `${output}/${tag}-failed-deleted.png`});
      await history(before, after.doc, `${tag}-negative-delete`); await reopen(after, `${tag}-negative-delete`);
    });
    for (const mode of ['fixed', 'driving']) await run(`${tag}-${mode}`, async () => {
      await load(fixture(vertical)); const saved = await download(`${tag}-${mode}-seed`), doc = structuredClone(saved.doc), line = lineOf(doc);
      if (mode === 'fixed') doc.constraints.push({id: 'fixed-line', kind: 'Fix', referenceKeys: [line.id], state: 'Satisfied'});
      else doc.dimensions.push({id: 'axis-distance', kind: vertical ? 'VerticalDistance' : 'HorizontalDistance', referenceKeys: [`${line.id}:start`, `${line.id}:end`], value: 10, anchor: {x: 3, y: 3}, isDriving: true});
      await load(withNative(saved.text, doc)); const before = (await download(`${tag}-${mode}-before`)).doc;
      await applyAxis(line, requested); const after = await download(`${tag}-${mode}-after`);
      assert.deepEqual(after.doc.entities, before.entities); assert.deepEqual(after.doc.dimensions, before.dimensions);
      assert.deepEqual(after.doc.constraints.filter(c => before.constraints.some(b => b.id === c.id)), before.constraints);
      const added = after.doc.constraints.filter(c => !before.constraints.some(b => b.id === c.id));
      assert.ok(added.every(c => c.kind === requested && c.state === 'Unsatisfied'));
      if (added.length) await history(before, after.doc, `${tag}-${mode}`);
      else await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      await reopen(after, `${tag}-${mode}`);
    });
  }
  assert.ok(results.length); assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
