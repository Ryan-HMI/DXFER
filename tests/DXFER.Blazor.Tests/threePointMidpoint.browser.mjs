import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-three-point-midpoint';
const only = process.env.DXFER_CASES?.split(',');
await mkdir(output, {recursive: true});
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex, 'Download contains native intent');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixture = type => {
  const point = (id, x, y) => ['0', 'POINT', '5', id, '8', '0', '10', x, '20', y];
  const line = (id, x, y, ex, ey) => ['0', 'LINE', '5', id, '8', '0', '10', x, '20', y, '11', ex, '21', ey];
  const circle = ['0', 'CIRCLE', '5', '100', '8', '0', '10', '7', '20', '5', '40', '1'];
  const entities = type === 'legacy' ? [...circle, ...line('200', 2, 3, 12, 3)]
    : type === 'ambiguous' ? [...point('100', 2, 3), ...point('200', 8, 3), ...point('300', 5, 9)]
    : type === 'mixed' ? [...circle, ...point('200', 2, 3), ...line('300', 12, 3, 14, 7)]
    : [...point('100', 7, 5), ...point('200', 2, 3), ...point('300', 12, 3)];
  return ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
    '0', 'SECTION', '2', 'ENTITIES', ...entities, '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const location = e => e.kind === 'circle' ? e.center : e.kind === 'line' ? e.start : e.location;
const reference = e => e.kind === 'circle' ? `${e.id}:center` : e.kind === 'line' ? `${e.id}:start` : e.id;
const semanticRef = key => key.replace(/\|point\|(center|start|end)\|[^|]+\|[^|]+$/, ':$1');
const nearPoint = (a, b, label, tolerance = 1e-8) => assert.ok(a && Math.hypot(a.x - b.x, a.y - b.y) <= tolerance,
  `${label}: ${JSON.stringify(a)} != ${JSON.stringify(b)}`);
const cases = [
  {name: 'points-preselect-middle-first', type: 'points', workflow: 'preselect', order: [0, 1, 2]},
  {name: 'points-tool-first-middle-last', type: 'points', workflow: 'tool-first', order: [2, 1, 0]},
  {name: 'mixed-preselect-middle-second', type: 'mixed', workflow: 'preselect', order: [1, 0, 2]},
  {name: 'mixed-tool-first-middle-second', type: 'mixed', workflow: 'tool-first', order: [2, 0, 1], follow: true},
  {name: 'ambiguous-preselect', type: 'ambiguous', workflow: 'preselect', order: [0, 1, 2]},
  {name: 'ambiguous-tool-first', type: 'ambiguous', workflow: 'tool-first', order: [2, 1, 0]},
  {name: 'legacy-tool-first-line-first', type: 'legacy', workflow: 'tool-first', order: [1, 0]},
  {name: 'legacy-tool-first-point-first', type: 'legacy', workflow: 'tool-first', order: [0, 1]}
];
const results = [], errors = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(8000);
  page.on('dialog', d => d.accept());
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (text, count) => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'three-point-midpoint.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify({dataset: await canvas.evaluate(c => ({...c.dataset})),
      status: await page.locator('.dxfer-command-bar').innerText()}, null, 2));
  };
  const hover = async (entity, name, body = false) => {
    const p = await screen(body ? {x: 4, y: 3} : location(entity));
    let key;
    await expect.poll(async () => {
      await page.mouse.move(p.x + 24, p.y + 24);
      await page.mouse.move(p.x, p.y, {steps: 2});
      key = await canvas.getAttribute('data-hovered-id');
      if (body) return key === entity.id;
      if (entity.kind === 'point') return key === entity.id || key?.startsWith(`${entity.id}|point|point|`);
      return key?.startsWith(`${entity.id}|point|${entity.kind === 'circle' ? 'center' : 'start'}|`) ?? false;
    }, {timeout: 3000, message: `${name}: actual ${body ? 'line body' : 'point'} pick`}).toBe(true);
    await capture(`${name}-hover`);
    return {p, key};
  };
  const historyReopen = async (before, saved, name) => {
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
    await load(saved.text, saved.doc.entities.length);
    assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
  };
  const follow = async (before, endpoint, target, name) => {
    await canvas.press('Escape');
    const {p} = await hover(endpoint, name), end = await screen(target);
    const middle = before.entities[0], other = before.entities[2];
    const expected = {x: (target.x + location(other).x) / 2, y: (target.y + location(other).y) / 2};
    let held;
    await page.mouse.down();
    try {
      for (let i = 1; i <= 12; i++) {
        await page.mouse.move(p.x + (end.x - p.x) * i / 12, p.y + (end.y - p.y) * i / 12);
        await page.waitForTimeout(35);
      }
      await page.waitForTimeout(400);
      const center = (await canvas.getAttribute('data-first-center')).split(',').map(Number);
      held = {x: center[0], y: center[1]};
      await capture(`${name}-held`);
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(300);
    await capture(`${name}-released`);
    await canvas.press('Escape');
    const saved = await download(`${name}-released`);
    nearPoint(held, expected, 'Middle follows source during held-pointer preview', 1e-4);
    nearPoint(location(saved.doc.entities.find(e => e.id === endpoint.id)), target, 'Source endpoint reaches pointer', 1e-4);
    const moved = saved.doc.entities.find(e => e.id === middle.id);
    const movedSource = saved.doc.entities.find(e => e.id === endpoint.id);
    nearPoint(moved.center, {x: (location(movedSource).x + location(other).x) / 2,
      y: (location(movedSource).y + location(other).y) / 2}, 'Released midpoint is exact');
    assert.equal(moved.radius, middle.radius);
    assert.deepEqual(saved.doc.entities.find(e => e.id === other.id), other);
    assert.deepEqual(saved.doc.entities.map(e => e.id), before.entities.map(e => e.id));
    assert.deepEqual(saved.doc.constraints, before.constraints, 'Source movement retains stable refs and satisfied state');
    assert.deepEqual(saved.doc.dimensions, before.dimensions);
    return saved;
  };
  for (const {name, type, workflow, order, follow: sourceFollow} of cases) {
    if (only && !only.includes(name)) continue;
    let evidence;
    try {
      await load(fixture(type), type === 'legacy' ? 2 : 3);
      const before = (await download(`${name}-before`)).doc;
      assert.equal(before.constraints.length, 0);
      if (workflow === 'tool-first') await tool('Midpoint');
      const keys = [];
      for (const [i, index] of order.entries()) {
        const entity = before.entities[index];
        const {p, key} = await hover(entity, `${name}-pick-${i}`, type === 'legacy' && entity.kind === 'line');
        keys.push(key);
        await page.mouse.click(p.x, p.y); await page.waitForTimeout(180);
        if (workflow === 'preselect' || i < order.length - 1) {
          await expect(canvas).toHaveAttribute('data-selected-count', String(i + 1));
          assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), [...keys].sort());
        }
        if (workflow === 'tool-first' && type !== 'legacy' && i === 1)
          await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      }
      if (workflow === 'preselect') await tool('Midpoint');
      await page.waitForTimeout(250);
      const status = await page.locator('.dxfer-command-bar').innerText();
      await capture(`${name}-applied`);
      await canvas.press('Escape');
      const saved = await download(`${name}-applied`);
      evidence = {name, build, keys, status, constraints: saved.doc.constraints};
      console.log(`OBSERVED ${name}: ${JSON.stringify(evidence)}`);
      assert.deepEqual(saved.doc.entities.map(e => e.id), before.entities.map(e => e.id), 'No construction line or other entity is inserted');
      assert.deepEqual(saved.doc.dimensions, before.dimensions);
      if (type === 'ambiguous') {
        assert.match(status, /unique middle point/i, 'Ambiguous choice has visible feedback');
        assert.deepEqual(content(saved.doc), content(before), 'Ambiguous middle must reject without choosing a candidate');
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      } else {
        assert.equal(saved.doc.constraints.length, 1);
        const relation = saved.doc.constraints[0];
        assert.equal(relation.kind, 'Midpoint');
        assert.equal(relation.state, 'Satisfied');
        const [middle, a, b] = before.entities;
        if (type === 'legacy') {
          assert.equal(relation.referenceKeys.length, 2, 'Legacy line-plus-point remains a two-reference relation');
          assert.deepEqual(relation.referenceKeys.map(semanticRef).sort(), [a.id, reference(middle)].sort());
        } else {
          assert.equal(relation.referenceKeys.length, 3);
          assert.equal(relation.referenceKeys[0], reference(middle), 'Canonical middle identity is first, independent of pick order');
          assert.deepEqual(relation.referenceKeys.slice(1).sort(), [reference(a), reference(b)].sort());
        }
        nearPoint(location(saved.doc.entities[0]), {x: 7, y: 3}, 'Free middle reaches endpoint average');
        if (middle.kind === 'circle') assert.equal(saved.doc.entities[0].radius, middle.radius);
        assert.deepEqual(saved.doc.entities.slice(1), before.entities.slice(1), 'Applying moves only the chosen free middle');
        await historyReopen(before, saved, `${name}-creation`);
        if (sourceFollow) {
          const moved = await follow(saved.doc, saved.doc.entities[1], {x: 4, y: 5}, `${name}-source-edit`);
          await historyReopen(saved.doc, moved, `${name}-source-edit`);
          await follow(moved.doc, moved.doc.entities[1], {x: 5, y: 6}, `${name}-reopened-edit`);
        }
      }
      results.push({...evidence, passed: true}); console.log(`PASS ${name}`);
    } catch (error) {
      results.push({...evidence, name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`); await capture(`${name}-failure`);
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  }
  assert.ok(results.length);
  assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
