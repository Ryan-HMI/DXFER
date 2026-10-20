import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com']
  .includes(new URL(url).hostname), 'Isolated local/dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-circle-offset-dimension';
const only = process.env.DXFER_CASES?.split(',').filter(Boolean);
await mkdir(output, {recursive: true});
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', '0', 'CIRCLE', '5', '100', '8', '0',
  '10', '10', '20', '10', '40', '3', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const independentFixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  '0', 'CIRCLE', '5', '100', '8', '0', '10', '10', '20', '10', '40', '3',
  '0', 'CIRCLE', '5', '200', '8', '0', '10', '10', '20', '10', '40', '4',
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const lines = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'Download must contain a native document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (actual, expected, message, tolerance = 1e-7) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${message}: expected ${expected}, got ${actual}`);
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const belongsTo = (key, id) => key === id || key.startsWith(`${id}|`) || key.startsWith(`${id}:`);
const pointOn = (circle, quadrant) => quadrant ? {x: circle.center.x, y: circle.center.y + circle.radius}
  : {x: circle.center.x + circle.radius * Math.cos(0.63), y: circle.center.y + circle.radius * Math.sin(0.63)};
const results = [], errors = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text());
  });
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  };
  await ready();
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const persistent = id => page.locator(`input[data-dimension-id="${id}"]`);
  const tool = async name => {
    await page.getByRole('button', {name, exact: true}).click();
    await page.waitForTimeout(150);
  };
  const menu = async name => {
    await tool('Open application menu');
    await page.getByRole('button', {name: 'File', exact: true}).hover();
    await tool(name);
  };
  const download = async name => {
    const pending = page.waitForEvent('download');
    await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const fit = async () => {
    await tool('Fit extents');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX,
        c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = previous === view ? stable + 1 : 0;
      previous = view;
      return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const load = async (text = fixture, count = 1) => {
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'circle-offset-dimension.dxf',
      mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await fit();
    await canvas.press('Escape');
  };
  const screen = point => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), point);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify(await canvas.evaluate(c => ({...c.dataset})), null, 2));
  };
  const pickCircle = async (circle, quadrant, name) => {
    const p = await screen(pointOn(circle, quadrant));
    const bounds = await canvas.boundingBox();
    assert.ok(bounds);
    const away = {x: p.x + (p.x < bounds.x + bounds.width / 2 ? 24 : -24),
      y: p.y + (p.y < bounds.y + bounds.height / 2 ? 24 : -24)};
    await expect.poll(async () => {
      await page.mouse.move(away.x, away.y);
      await page.mouse.move(p.x, p.y, {steps: 2});
      const key = await canvas.getAttribute('data-hovered-id');
      return quadrant ? Boolean(key?.startsWith(`${circle.id}|point|`) && !key.includes('|center|')) : key === circle.id;
    }, {timeout: 3000, intervals: [100, 150, 250], message: `Actual ${quadrant ? 'quadrant' : 'body'} pick: ${circle.id}`}).toBe(true);
    await capture(`${name}-hover`);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(150);
  };
  const click = async point => {
    const p = await screen(point);
    await page.mouse.move(p.x, p.y);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(180);
  };
  const draftKeys = async () => (await canvas.getAttribute('data-dimension-draft-keys')).split(',').filter(Boolean);
  const dismissPlacementEditor = async (name, newlyCreated, clickAway = false) => {
    const inputs = page.locator('input[data-dimension-id]');
    await expect(inputs).toHaveCount(1);
    if (newlyCreated) await expect(inputs).toBeFocused();
    const before = await inputs.evaluate(input => ({
      id: input.dataset.dimensionId, value: input.value, storedValue: input.dataset.dimensionValue,
      editing: input.dataset.dimensionEditing, focused: document.activeElement === input
    }));
    await writeFile(`${output}/${name}-before-dismiss.json`, JSON.stringify({
      dismissal: clickAway ? 'untouched-click-away' : 'focused-escape', ...before
    }, null, 2));
    if (clickAway) {
      assert.ok(before.focused && before.editing === 'true', 'Blur control starts in the untouched auto-open editor');
      // Real empty-canvas click must not commit rounded display text that the user never edited.
      await click({x: 10, y: 8});
      await expect(inputs).not.toBeFocused();
      await expect(inputs).toHaveAttribute('data-dimension-value', before.storedValue);
    } else {
      // Send Escape to the editor itself before changing focus.
      await page.keyboard.press('Escape');
    }
    await canvas.press('Escape');
  };
  const edit = async (id, value) => {
    const input = persistent(id);
    await expect(input).toBeVisible();
    await input.dblclick();
    await input.fill(String(value));
    await input.press('Enter');
    await expect(input).toHaveAttribute('data-dimension-value', String(value));
    await canvas.press('Escape');
  };
  const history = async (before, after, name) => {
    await tool('Undo');
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo');
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
  };
  const assertGap = (doc, source, derivedId, value, dimensionId) => {
    assert.equal(doc.entities.length, 2);
    assert.deepEqual(doc.entities.find(e => e.id === source.id), source, 'Source R3 geometry unchanged');
    const derived = doc.entities.find(e => e.id === derivedId);
    assert.equal(derived.kind, 'circle');
    assert.deepEqual(derived.center, source.center, 'Concentric centers remain unchanged');
    near(derived.radius, 3 + value, 'Derived radius', value === 1 ? 1e-4 : 1e-7);
    assert.equal(doc.dimensions.length, 1, 'No competing Diameter/Radius or duplicate Offset dimension');
    const gap = doc.dimensions[0];
    assert.equal(gap.kind, 'Offset');
    assert.equal(gap.isDriving, true);
    near(gap.value, value, 'Gap value', value === 1 ? 1e-4 : 1e-7);
    assert.deepEqual(gap.referenceKeys, [source.id, derivedId], 'Canonical source-to-derived refs');
    if (dimensionId) assert.equal(gap.id, dimensionId);
    assert.equal(doc.constraints.filter(c => c.kind === 'Offset').length, 1);
    assert.ok(doc.constraints.every(c => c.state === 'Satisfied'));
    return gap;
  };

  const cases = [
    {name: 'pointer-body-source-first', clickAway: true},
    {name: 'pointer-body-derived-first', reverse: true},
    {name: 'keyed-body-source-first', keyed: true},
    {name: 'keyed-body-derived-first', keyed: true, reverse: true},
    {name: 'pointer-quadrant-source-first', quadrant: true},
    {name: 'keyed-quadrant-derived-first', keyed: true, quadrant: true, reverse: true},
    {name: 'single-circle-body-diameter', control: true},
    {name: 'single-circle-quadrant-diameter', control: true, quadrant: true},
    {name: 'independent-body-gap-cancel', independent: true}
  ];
  for (const {name, keyed = false, quadrant = false, reverse = false, control = false, independent = false, clickAway = false} of cases) {
    if (only && !only.includes(name)) continue;
    try {
      await load(independent ? independentFixture : fixture, independent ? 2 : 1);
      const seed = await download(`${name}-seed`);
      const source = seed.doc.entities[0];
      assert.equal(source.kind, 'circle');
      near(source.radius, 3, 'Fixture radius');
      assert.deepEqual(source.center, {x: 10, y: 10});
      if (independent) {
        const other = seed.doc.entities.find(e => e.id !== source.id);
        assert.equal(other.kind, 'circle');
        assert.deepEqual(other.center, source.center);
        near(other.radius, 4, 'Independent second radius');
        assert.deepEqual(seed.doc.constraints, [], 'No linked Offset relationship in fixture');
        assert.deepEqual(seed.doc.dimensions, []);
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
        await tool('Dimension');
        await pickCircle(source, false, `${name}-first`);
        assert.equal((await draftKeys()).length, 1);
        await pickCircle(other, false, `${name}-second`);
        assert.equal((await draftKeys()).length, 2, 'Independent curves now stage a perimeter gap');
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(0);
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
        await capture(`${name}-refusal`);
        await canvas.press('Escape');
        const refused = await download(`${name}-refused`);
        assert.deepEqual(content(refused.doc), content(seed.doc), 'Cancelling the perimeter draft changes neither geometry, dimensions nor constraints');
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
      } else if (control) {
        await tool('Dimension');
        await pickCircle(source, quadrant, `${name}-first`);
        assert.equal((await draftKeys()).length, 1);
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(0);
        await click({x: 14.5, y: 8});
        await dismissPlacementEditor(name, true);
        const placed = await download(`${name}-placed`);
        assert.deepEqual(placed.doc.entities, seed.doc.entities);
        assert.equal(placed.doc.dimensions.length, 1);
        const diameter = placed.doc.dimensions[0];
        assert.equal(diameter.kind, 'Diameter');
        assert.equal(diameter.isDriving, true);
        near(diameter.value, 6, 'Single-circle diameter');
        assert.equal(diameter.referenceKeys.length, 1);
        assert.ok(belongsTo(diameter.referenceKeys[0], source.id));
        await edit(diameter.id, 8);
        const changed = await download(`${name}-edited`);
        near(changed.doc.entities[0].radius, 4, 'Diameter edit radius');
        assert.deepEqual(changed.doc.entities[0].center, source.center);
        assert.equal(changed.doc.dimensions.length, 1);
        assert.equal(changed.doc.dimensions[0].kind, 'Diameter');
        assert.equal(changed.doc.dimensions[0].id, diameter.id);
        near(changed.doc.dimensions[0].value, 8, 'Edited diameter');
        await history(placed.doc, changed.doc, name);
        await load(changed.text);
        assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(changed.doc));
      } else {
        await pickCircle(source, false, `${name}-offset-source`);
        await expect(canvas).toHaveAttribute('data-selected-count', '1');
        await tool('Offset');
        const side = await screen({x: 14, y: 10});
        await page.mouse.move(side.x, side.y);
        const offset = page.locator('input[data-dimension-key="offset"]');
        await expect(offset).toBeVisible();
        if (keyed) {
          await offset.fill('1');
          await offset.press('Enter');
        } else await page.mouse.click(side.x, side.y);
        await expect(canvas).toHaveAttribute('data-entity-count', '2');
        await canvas.press('Escape');
        await fit();
        const linked = await download(`${name}-linked`);
        assert.deepEqual(linked.doc.entities.find(e => e.id === source.id), source);
        const derived = linked.doc.entities.find(e => e.id !== source.id);
        assert.equal(derived.kind, 'circle');
        near(derived.radius, 4, 'Actual Offset creates R4', 1e-4);
        assert.deepEqual(derived.center, source.center);
        const relation = linked.doc.constraints.filter(c => c.kind === 'Offset');
        assert.equal(relation.length, 1, 'Actual Offset retains linked intent');
        assert.deepEqual(relation[0].referenceKeys, [source.id, derived.id]);
        assert.equal(relation[0].state, 'Satisfied');
        assert.equal(linked.doc.dimensions.length, keyed ? 1 : 0);
        if (keyed) assertGap(linked.doc, source, derived.id, 1);

        await tool('Dimension');
        const picks = reverse ? [derived, source] : [source, derived];
        await pickCircle(picks[0], quadrant, `${name}-first`);
        assert.equal((await draftKeys()).length, 1);
        await pickCircle(picks[1], quadrant, `${name}-second`);
        const keys = await draftKeys();
        assert.equal(keys.length, 2, 'Second circle extends draft instead of committing a Diameter');
        for (const circle of picks) assert.equal(keys.filter(key => belongsTo(key, circle.id)).length, 1);
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(keyed ? 1 : 0);
        await capture(`${name}-pair-draft`);
        await click({x: 15.5, y: 8});
        await dismissPlacementEditor(name, !keyed, clickAway);
        const placed = await download(`${name}-placed`);
        assert.deepEqual(placed.doc.entities, linked.doc.entities, 'Placing a gap does not alter geometry');
        const gap = assertGap(placed.doc, source, derived.id, 1, keyed ? linked.doc.dimensions[0].id : undefined);
        await edit(gap.id, 2);
        const changed = await download(`${name}-edited`);
        assertGap(changed.doc, source, derived.id, 2, gap.id);
        await history(placed.doc, changed.doc, name);
        await load(changed.text, 2);
        const reopened = await download(`${name}-reopened`);
        assert.deepEqual(content(reopened.doc), content(changed.doc));
        assertGap(reopened.doc, source, derived.id, 2, gap.id);
      }
      await capture(`${name}-final`);
      results.push({name, passed: true});
      console.log(`PASS ${name}: real picks, native dimension, edit, history/reopen`);
    } catch (error) {
      results.push({name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`);
      await capture(`${name}-failure`);
      await ready();
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  }
  assert.ok(results.length, 'At least one matching case must run');
  assert.deepEqual(errors, [], 'No browser/circuit errors');
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
  console.log(`PASS ${results.length} circle offset dimension cases`);
} finally {
  await browser.close();
}
