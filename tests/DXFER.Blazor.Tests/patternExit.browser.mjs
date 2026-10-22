import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated QA URL explicitly');
assert.ok(['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-pattern-exit';
await mkdir(output, {recursive: true});
const fixture = '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'native document is present');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  patterns: doc.metadata.patterns});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const evidence = {cases: [], errors: [], exports: []};
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
  const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const command = name => page.getByRole('button', {name, exact: true});
  const menu = async name => {
    await command('Open application menu').click();
    await command('File').hover(); await command(name).click();
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); evidence.exports.push(name);
    return {text, doc: decode(text)};
  };
  const settled = async () => {
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = previous === view ? stable + 1 : 0; previous = view; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const load = async (text, count) => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'pattern-exit.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await command('Fit extents').click(); await settled();
    await expect(command('Undo')).toBeDisabled(); await expect(command('Redo')).toBeDisabled();
  };
  const hitCircle = async (entity, button = 'left') => {
    await settled();
    for (const angle of [.7, 2.2, 3.7, 5.2, 0, Math.PI]) {
      const p = await canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
        y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}),
      {x: entity.center.x + Math.cos(angle) * entity.radius, y: entity.center.y + Math.sin(angle) * entity.radius});
      if (!await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p)) continue;
      await page.mouse.move(p.x, p.y);
      await expect.poll(() => canvas.getAttribute('data-hovered-id')).toMatch(new RegExp(`^${entity.id}`));
      await page.mouse.click(p.x, p.y, {button}); return;
    }
    assert.fail('No unobscured circle hit target');
  };
  const set = async (scope, label, value) => {
    const input = scope.getByLabel(label, {exact: true});
    await input.fill(String(value)); await input.press('Tab'); await expect(input).toHaveValue(String(value));
  };
  const absent = async () => {
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    await expect(widget).toHaveCount(0); await expect(editor).toHaveCount(0);
    await expect(canvas).toHaveAttribute('data-corner-preview', 'false');
  };
  const exit = async (surface, accept) => {
    const scope = surface === 'viewport' ? widget : editor;
    await scope.getByRole('button', {name: accept ? 'Confirm' : surface === 'viewport' ? 'Cancel pattern changes' : 'Cancel', exact: true}).click();
    await absent();
  };
  const start = async (kind, source) => {
    await hitCircle(source); await expect(canvas).toHaveAttribute('data-selected-keys', source.id);
    await command(kind === 'radial' ? 'Radial pattern' : 'Rectangular / staggered pattern').click();
    await expect(editor).toBeVisible(); await expect(widget).toBeVisible();
    if (kind === 'radial') {
      await set(editor, 'Pattern center X', 2); await set(editor, 'Pattern center Y', 3);
      await set(widget, 'Viewport pattern instances', 6);
    } else {
      await set(widget, 'Viewport pattern U count', 3); await set(widget, 'Viewport pattern V count', 2);
      await set(widget, 'Viewport pattern U pitch', 3); await set(widget, 'Viewport pattern V pitch', 4);
    }
    await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
  };
  const reopen = async doc => {
    const pattern = doc.metadata.patterns[0];
    let picked = false;
    for (const id of [...pattern.members.map(m => m.entityId).reverse(), ...pattern.sourceEntityIds]) {
      try { await hitCircle(doc.entities.find(e => e.id === id), 'right'); picked = true; break; }
      catch (error) { if (error.message !== 'No unobscured circle hit target') throw error; }
    }
    assert.ok(picked, 'A pattern instance must be reachable for its context menu');
    await context.getByRole('button', {name: 'Show pattern controls', exact: true}).click();
    await expect(widget).toBeVisible();
    await expect(widget.getByRole('button', {name: 'Edit pattern', exact: true})).toHaveCount(0);
    await expect(editor).toBeVisible(); await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
    await expect(widget.locator('input[readonly]')).toHaveCount(0);
  };
  const validPattern = (doc, source, kind, count) => {
    assert.equal(doc.metadata.patterns.length, 1);
    const pattern = doc.metadata.patterns[0];
    assert.deepEqual(doc.entities.find(e => e.id === source.id), source, 'source unchanged');
    assert.equal(doc.entities.length, count); assert.equal(pattern.members.length, count - 1);
    for (const member of pattern.members) {
      const entity = doc.entities.find(e => e.id === member.entityId);
      assert.equal(entity.kind, 'circle'); near(entity.radius, source.radius);
      if (kind === 'radial') {
        const a = member.column * 2 * Math.PI / pattern.count;
        near(entity.center.x, pattern.center.x + (source.center.x - pattern.center.x) * Math.cos(a) - (source.center.y - pattern.center.y) * Math.sin(a));
        near(entity.center.y, pattern.center.y + (source.center.x - pattern.center.x) * Math.sin(a) + (source.center.y - pattern.center.y) * Math.cos(a));
      } else {
        near(entity.center.x, source.center.x + member.column * pattern.xPitch);
        near(entity.center.y, source.center.y + member.row * pattern.yPitch);
      }
    }
  };
  const oneHistory = async (before, after, name) => {
    await command('Undo').click();
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await expect(command('Undo')).toBeDisabled();
    await command('Redo').click();
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
    await expect(command('Redo')).toBeDisabled();
  };
  const unchanged = async (before, name) => {
    assert.deepEqual(content((await download(name)).doc), content(before));
    await expect(command('Undo')).toBeDisabled(); await expect(command('Redo')).toBeDisabled();
  };
  const baselines = {};
  for (const kind of ['radial', 'linear']) {
    for (const surface of ['viewport']) {
      await page.setViewportSize(surface === 'viewport' ? {width: 1440, height: 1000} : {width: 1100, height: 705});
      for (const accept of [false, true]) {
        const name = `${kind}-create-${surface}-${accept ? 'confirm' : 'cancel'}`; evidence.current = name;
        await load(fixture, 1); const before = await download(`${name}-before`), source = before.doc.entities[0];
        await start(kind, source); await exit(surface, accept);
        await page.screenshot({path: `${output}/${name}.png`});
        const after = await download(`${name}-after`);
        if (accept) {
          validPattern(after.doc, source, kind, 6); baselines[kind] = after;
          await oneHistory(before.doc, after.doc, name);
        } else {
          await unchanged(before.doc, `${name}-unchanged`);
          await start(kind, source); await exit(surface, false);
          await unchanged(before.doc, `${name}-restart-discarded`);
        }
        evidence.cases.push(name); console.log(`PASS ${name}`);
      }
    }
    for (const surface of ['sidebar']) {
      await page.setViewportSize(surface === 'viewport' ? {width: 1440, height: 1000} : {width: 1100, height: 705});
      for (const accept of [false, true]) {
        const name = `${kind}-edit-${surface}-${accept ? 'confirm' : 'cancel'}`; evidence.current = name;
        const baseline = baselines[kind]; await load(baseline.text, 6); await reopen(baseline.doc);
        const scope = surface === 'viewport' ? widget : editor;
        const label = `${surface === 'viewport' ? 'Viewport pattern' : 'Pattern'} ${kind === 'radial' ? 'instances' : 'U count'}`;
        await set(scope, label, kind === 'radial' ? 8 : 4); await exit(surface, accept);
        await page.screenshot({path: `${output}/${name}.png`});
        const after = await download(`${name}-after`);
        if (accept) {
          validPattern(after.doc, baseline.doc.entities.find(e => e.id === baseline.doc.metadata.patterns[0].sourceEntityIds[0]), kind, 8);
          await oneHistory(baseline.doc, after.doc, name);
        } else await unchanged(baseline.doc, `${name}-unchanged`);
        // Explicit Show still works after the hidden-on-exit state, and Cancel discards edited fields.
        await load(after.text, accept ? 8 : 6); await reopen(after.doc);
        await expect(widget.getByLabel(`Viewport pattern ${kind === 'radial' ? 'instances' : 'U count'}`, {exact: true}))
          .toHaveValue(String(kind === 'radial' ? accept ? 8 : 6 : accept ? 4 : 3));
        await exit(surface, false); await unchanged(after.doc, `${name}-reopen-cancel`);
        evidence.cases.push(name); console.log(`PASS ${name}`);
      }
    }
    const name = `${kind}-invalid-confirm`; evidence.current = name;
    await load(baselines[kind].text, 6); await reopen(baselines[kind].doc);
    await set(widget, `Viewport pattern ${kind === 'radial' ? 'instances' : 'U count'}`, 0);
    await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
    await expect(editor.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
    await expect(widget).toBeVisible(); await expect(editor).toBeVisible();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
    await page.screenshot({path: `${output}/${name}.png`});
    await exit('viewport', false); await unchanged(baselines[kind].doc, name);
    evidence.cases.push(name); console.log(`PASS ${name}`);
  }
  for (const axis of ['U', 'V']) for (const surface of ['sidebar']) {
    const name = `linear-${axis}-picker-${surface}-cancel`; evidence.current = name;
    await load(baselines.linear.text, 6); await reopen(baselines.linear.doc);
    await set(widget, 'Viewport pattern U count', 5);
    const scope = surface === 'viewport' ? widget : editor;
    await scope.getByRole('button', {name: `Pick ${axis} direction`, exact: true}).click();
    await expect(widget).toHaveAttribute('data-pattern-direction-picking', axis);
    await exit(surface, false); await unchanged(baselines.linear.doc, name);
    await reopen(baselines.linear.doc);
    await expect(widget).not.toHaveAttribute('data-pattern-direction-picking', /[UV]/);
    await expect(widget.getByRole('button', {name: `Pick ${axis} direction`, exact: true})).toHaveAttribute('aria-pressed', 'false');
    await expect(widget.getByLabel('Viewport pattern U count', {exact: true})).toHaveValue('3');
    await exit(surface, false);
    evidence.cases.push(name); console.log(`PASS ${name}`);
  }
  if (process.env.DXFER_PATTERN_COUNT_FIXTURES) {
    for (const kind of ['Radial', 'Rectangular', 'Staggered']) {
      const text = await readFile(`${process.env.DXFER_PATTERN_COUNT_FIXTURES}/${kind}.dxf`, 'utf8');
      const native = decode(text);
      for (const count of [2, 6]) for (const accept of [false, true]) {
        const name = `${kind}-count-${count}-${accept ? 'accept' : 'cancel'}-member-intent`;
        evidence.current = name;
        await load(text, native.entities.length);
        const baseline = await download(`${name}-before`);
        await reopen(baseline.doc);
        await set(widget, kind === 'Radial' ? 'Viewport pattern instances' : 'Viewport pattern U count', count);
        await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
        assert.deepEqual(content((await download(`${name}-draft`)).doc), content(baseline.doc), 'Preview cannot delete saved intent');
        await page.screenshot({path: `${output}/${name}-editable.png`});
        await exit('viewport', accept);
        if (!accept) await unchanged(baseline.doc, `${name}-unchanged`);
        else {
          const after = await download(`${name}-after`);
          assert.deepEqual(after.doc.dimensions.map(d => d.id), ['source-radius', 'other-radius']);
          assert.deepEqual(after.doc.constraints.map(c => c.id), ['source-fixed', 'other-fixed', 'center-fixed']);
          assert.ok(after.doc.constraints.every(c => c.state === 'Satisfied'));
          assert.deepEqual(after.doc.entities.filter(e => ['seed', 'other', 'center'].includes(e.id)),
            baseline.doc.entities.filter(e => ['seed', 'other', 'center'].includes(e.id)), 'Source, center and unrelated geometry remain exact');
          assert.equal(after.doc.metadata.patterns[0][kind === 'Radial' ? 'count' : 'columns'], count);
          await oneHistory(baseline.doc, after.doc, name);
          await load(after.text, after.doc.entities.length);
          assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(after.doc));
          await reopen(after.doc);
          await set(widget, kind === 'Radial' ? 'Viewport pattern instances' : 'Viewport pattern U count', 3);
          await exit('viewport', true);
          const again = await download(`${name}-edited-again`);
          assert.equal(again.doc.metadata.patterns[0][kind === 'Radial' ? 'count' : 'columns'], 3);
        }
        evidence.cases.push(name); console.log(`PASS ${name}`);
      }
    }
  }
  assert.deepEqual(evidence.errors, []); evidence.passed = true;
} catch (error) {
  failure = error; evidence.passed = false; evidence.failure = error.stack;
  if (page) await page.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2));
  await browser.close();
}
if (failure) throw failure;
console.log(JSON.stringify({passed: true, cases: evidence.cases.length, build: evidence.build, output}));
