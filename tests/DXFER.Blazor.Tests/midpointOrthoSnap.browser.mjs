import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname), 'Local or isolated Dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-midpoint-ortho-snap';
await mkdir(output, {recursive: true});

const native = text => {
  const lines = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'Download must contain the native document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixtures = Object.fromEntries(await Promise.all(['line', 'polyline'].map(async kind => [kind,
  await readFile(new URL(`./fixtures/drag-matrix/implicit-snap-${kind}.dxf`, import.meta.url))])));
const close = (actual, expected, message, tolerance = 1e-7) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${message}: expected ${expected}, got ${actual}`);
const closePoint = (actual, expected, message, tolerance = 1e-7) => {
  assert.ok(actual, `${message}: missing point`);
  close(actual.x, expected.x, `${message} x`, tolerance);
  close(actual.y, expected.y, `${message} y`, tolerance);
};
const ends = source => source.kind === 'polyline' ? source.vertices.slice(0, 2) : [source.start, source.end];
const midpoint = source => {
  const [a, b] = ends(source);
  return {x: (a.x + b.x) / 2, y: (a.y + b.y) / 2};
};
const geometry = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const results = [], errors = [], placedPositions = new Map();
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(15000);
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
  const tool = async name => {
    await page.getByRole('button', {name, exact: true}).click();
    await page.waitForTimeout(150);
  };
  const menu = async name => {
    await tool('Open application menu');
    await page.getByRole('button', {name: 'File', exact: true}).hover();
    await tool(name);
  };
  const automatic = async enabled => {
    await menu('Settings');
    const checkbox = page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true});
    await expect(checkbox).toBeEnabled();
    await checkbox.setChecked(enabled);
    await tool('Close settings');
  };
  const download = async name => {
    const pending = page.waitForEvent('download');
    await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (buffer, count = 2) => {
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'midpoint-ortho.dxf',
      mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX,
        c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0;
      previous = view;
      return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = point => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), point);
  const move = async point => {
    const p = await screen(point);
    await page.mouse.move(p.x, p.y);
    await page.waitForTimeout(180);
    return p;
  };
  const click = async point => {
    const p = await move(point);
    await page.mouse.click(p.x, p.y);
  };
  const drag = async (from, to) => {
    await move(from);
    await page.mouse.down();
    const p = await screen(to);
    await page.mouse.move(p.x, p.y, {steps: 12});
    await page.mouse.up();
    await page.waitForTimeout(300);
  };

  const cases = [];
  for (const sourceKind of ['line', 'polyline']) for (const kind of ['Horizontal', 'Vertical'])
    for (const enabled of [true, false]) cases.push({sourceKind, kind, enabled, creationTool: 'Point'});
  for (const kind of ['Horizontal', 'Vertical']) for (const enabled of [true, false])
    cases.push({sourceKind: 'line', kind, enabled, creationTool: 'Line'});

  for (const {sourceKind, kind, enabled, creationTool} of cases) {
    const name = `${sourceKind}-${creationTool.toLowerCase()}-${kind.toLowerCase()}-${enabled ? 'on' : 'off'}`;
    try {
      await load(fixtures[sourceKind]);
      await automatic(enabled);
      const original = (await download(`${name}-before`)).doc;
      const source = original.entities.find(e => e.id === 'source');
      assert.equal(source.kind, sourceKind);
      const mid = midpoint(source);
      closePoint(mid, {x: 4, y: 3}, 'Fixture midpoint');
      const reference = sourceKind === 'polyline' ? `${source.id}|segment|0:mid` : `${source.id}:mid`;
      const target = kind === 'Horizontal' ? {x: 1.25, y: mid.y} : {x: mid.x, y: 7};
      const axis = kind === 'Horizontal' ? 'y' : 'x';

      await tool(creationTool);
      await expect(canvas).toHaveAttribute('data-active-tool', creationTool.toLowerCase());
      await move(mid);
      await expect(canvas).toHaveAttribute('data-hovered-id', /^source\|point\|mid(?:-0)?\|/);
      await expect.poll(async () => Number(await canvas.getAttribute('data-acquired-snap-count'))).toBeGreaterThan(0);
      // Approach a few screen pixels off-axis: the guide must snap, not just coincide with an exact click.
      const guide = await screen(target);
      if (kind === 'Horizontal') guide.y += 3;
      else guide.x += 3;
      await page.mouse.move(guide.x, guide.y);
      await page.waitForTimeout(180);
      await page.screenshot({path: `${output}/${name}-guide.png`});
      await writeFile(`${output}/${name}-guide.json`, JSON.stringify(await canvas.evaluate(c => ({
        hovered: c.dataset.hoveredId, point: c.dataset.hoveredSnapPoint,
        acquired: c.dataset.acquiredSnapCount, cue: c.dataset.snapCue
      })), null, 2));
      await page.mouse.click(guide.x, guide.y);
      if (creationTool === 'Line') await click({x: 1, y: 6});
      await expect(canvas).toHaveAttribute('data-entity-count', '3');
      await canvas.press('Escape');
      const saved = await download(`${name}-created`);
      const added = saved.doc.entities.filter(e => !original.entities.some(old => old.id === e.id));
      assert.equal(added.length, 1, 'Exactly one new entity');
      const entity = added[0];
      assert.equal(entity.kind, creationTool.toLowerCase());
      const position = creationTool === 'Line' ? entity.start : entity.location;
      close(position[axis], target[axis], 'Exact acquired midpoint guide coordinate');
      closePoint(position, target, 'Placed location', 0.04);
      assert.deepEqual(saved.doc.entities.filter(e => original.entities.some(old => old.id === e.id)),
        original.entities, 'Creation must not alter either source entity');
      const createdReference = creationTool === 'Line' ? `${entity.id}:start` : entity.id;
      const relations = saved.doc.constraints.filter(c => c.kind === kind && c.referenceKeys.includes(reference)
        && c.referenceKeys.includes(createdReference));
      if (enabled) {
        assert.equal(relations.length, 1, `Permanent ${kind} relation must reference ${reference} and ${createdReference}`);
        assert.equal(relations[0].state, 'Satisfied');
        assert.ok(saved.doc.constraints.every(c => c.state === 'Satisfied'));
      } else {
        assert.deepEqual(saved.doc.constraints, original.constraints, 'Auto constraints OFF must not create any relation');
      }
      const comparisonKey = `${sourceKind}-${creationTool}-${kind}`;
      if (enabled) placedPositions.set(comparisonKey, position);
      else {
        assert.ok(placedPositions.has(comparisonKey), 'Enabled comparison case must have passed');
        closePoint(position, placedPositions.get(comparisonKey), 'Auto constraints OFF preserves the snapped location');
      }

      await tool('Undo');
      assert.deepEqual(geometry((await download()).doc), geometry(original), 'Undo restores pre-creation geometry and intent');
      await tool('Redo');
      assert.deepEqual(geometry((await download()).doc), geometry(saved.doc), 'Redo restores midpoint references');
      await load(saved.text, 3);
      assert.deepEqual(geometry((await download(`${name}-reopened`)).doc), geometry(saved.doc), 'Native reopen preserves intent');

      // Disable acquisition during edits; existing constraints must still follow the source midpoint.
      await automatic(false);
      const [start, end] = ends(source);
      const movedEnd = kind === 'Horizontal' ? {x: end.x, y: end.y + 2} : {x: end.x + 2, y: end.y};
      await drag(end, movedEnd);
      const changed = (await download(`${name}-source-edited`)).doc;
      const changedSource = changed.entities.find(e => e.id === source.id);
      assert.notDeepEqual(changedSource, source, 'Source endpoint drag must actually change the source');
      closePoint(ends(changedSource)[0], start, 'Other source endpoint remains in place');
      closePoint(ends(changedSource)[1], movedEnd, 'Dragged endpoint', 0.04);
      const changedEntity = changed.entities.find(e => e.id === entity.id);
      const followed = creationTool === 'Line' ? changedEntity.start : changedEntity.location;
      if (enabled) {
        close(followed[axis], midpoint(changedSource)[axis], 'Created reference follows the live midpoint');
        assert.ok(Math.abs(followed[axis] - position[axis]) > 0.5, 'Created reference must move, not retain an old coordinate');
        const persisted = changed.constraints.find(c => c.id === relations[0].id);
        assert.equal(persisted.state, 'Satisfied');
        assert.deepEqual(persisted.referenceKeys, relations[0].referenceKeys);
        assert.ok(changed.constraints.every(c => c.state === 'Satisfied'));
      } else {
        closePoint(followed, position, 'Unconstrained placement does not follow a later source edit');
        assert.deepEqual(changed.constraints, original.constraints);
      }
      await page.screenshot({path: `${output}/${name}-follow.png`});
      results.push({name, passed: true, reference, createdReference});
      console.log(`PASS ${name}: actual hover/click, stable midpoint ref, unchanged source, history/reopen, endpoint edit`);
    } catch (error) {
      results.push({name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
      await ready();
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  }
  assert.deepEqual(errors, [], 'No browser/circuit errors');
  assert.equal(results.filter(result => !result.passed).length, 0, `Failures recorded in ${output}/results.json`);
  console.log(`PASS ${results.length}/${cases.length} midpoint orthogonal acquisition cases`);
} finally {
  await browser.close();
}
