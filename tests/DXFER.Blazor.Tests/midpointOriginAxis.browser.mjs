import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-midpoint-origin-axis';
const only = process.env.DXFER_CASES?.split(',');
const workflows = process.env.DXFER_WORKFLOW ? [process.env.DXFER_WORKFLOW] : ['tool-first', 'preselect'];
assert.ok(workflows.every(workflow => ['tool-first', 'preselect'].includes(workflow)));
await mkdir(output, {recursive: true});
const points = [[-4, -2, -4, 8], [-4, 8, 18, 8], [18, 8, 18, -2], [18, -2, -4, -2]];
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', ...points.flatMap(([x, y, ex, ey], i) =>
    ['0', 'LINE', '5', String(100 + i), '8', '0', '10', x, '20', y, '11', ex, '21', ey]),
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex);
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const withNative = (text, doc) => {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/, bytes.toString('hex').match(/.{1,240}/g).map(h => `310\n${h}\n`).join(''));
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const mid = line => ({x: (line.start.x + line.end.x) / 2, y: (line.start.y + line.end.y) / 2});
const near = (a, b, label) => assert.ok(Math.abs(a - b) < 1e-8, `${label}: ${a} != ${b}`);
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
  const load = async text => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'midpoint-origin-axis.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const pick = async (point, expected, name) => {
    const p = await screen(point);
    await page.mouse.move(p.x + 25, p.y + 25);
    await page.mouse.move(p.x, p.y, {steps: 2});
    await expect.poll(async () => expected.test(await canvas.getAttribute('data-hovered-id') ?? '')).toBe(true);
    const key = await canvas.getAttribute('data-hovered-id');
    if (key === '$origin:center') assert.deepEqual(point, {x: 0, y: 0}, 'Origin is picked at exact world zero');
    await page.screenshot({path: `${output}/${name}-hover.png`});
    await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
    return key;
  };
  await load(fixture);
  const seed = await download('seed');
  const cases = [];
  for (const kind of ['Horizontal', 'Vertical']) {
    for (const reverse of [false, true])
      for (const sized of [false, true]) cases.push({kind, reverse, fixed: false, sized});
    cases.push({kind, reverse: false, fixed: true});
  }
  for (const workflow of workflows) for (const {kind, reverse, fixed, sized} of cases) {
    const name = `${workflow}-${kind.toLowerCase()}-${reverse ? 'origin-first' : 'mid-first'}-${fixed ? 'fixed' : sized ? 'dimensioned' : 'free'}`;
    if (only && !only.includes(name)) continue;
    let evidence;
    try {
      const doc = structuredClone(seed.doc), edges = doc.entities, source = edges[0];
      for (const [i, edge] of edges.entries()) {
        doc.constraints.push({id: `corner-${i}`, kind: 'Coincident', state: 'Satisfied',
          referenceKeys: [`${edge.id}:end`, `${edges[(i + 1) % 4].id}:start`]},
        {id: `axis-${i}`, kind: i % 2 ? 'Horizontal' : 'Vertical', state: 'Satisfied', referenceKeys: [edge.id]});
        if (fixed) doc.constraints.push({id: `fixed-${i}`, kind: 'Fix', state: 'Satisfied', referenceKeys: [edge.id]});
      }
      if (sized) {
        for (const i of [0, 1]) doc.dimensions.push({
          id: `size-${i}`, kind: 'LinearDistance',
          referenceKeys: [`${edges[i].id}:start`, `${edges[i].id}:end`],
          value: i === 0 ? 10 : 22,
          anchor: i === 0 ? {x: -7, y: 3} : {x: 7, y: 11}, isDriving: true
        });
      }
      await load(withNative(seed.text, doc));
      const before = (await download(`${name}-before`)).doc;
      assert.deepEqual(content(before), content(doc), 'Native rectangle intent loads exactly');
      if (workflow === 'tool-first') await tool(kind);
      const picks = [{point: mid(source), expected: new RegExp(`^${source.id}\\|point\\|mid\\|`), label: 'mid'},
        {point: {x: 0, y: 0}, expected: /^\$origin:center$/, label: 'origin'}];
      if (reverse) picks.reverse();
      const keys = [];
      for (const [i, target] of picks.entries()) {
        keys.push(await pick(target.point, target.expected, `${name}-${target.label}`));
        if (i === 0) await expect(canvas).toHaveAttribute('data-selected-keys', keys[0]);
        if (workflow === 'preselect') {
          await expect(canvas).toHaveAttribute('data-selected-count', String(i + 1));
          assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), [...keys].sort(),
            'Preselection retains exact midpoint/origin references');
        }
      }
      if (workflow === 'preselect') await tool(kind);
      await page.waitForTimeout(250);
      const status = await page.locator('.dxfer-command-bar').innerText();
      await page.screenshot({path: `${output}/${name}-applied.png`});
      await canvas.press('Escape');
      const saved = await download(`${name}-applied`);
      const added = saved.doc.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
      evidence = {name, build, workflow, keys, status, added, beforeMidpoint: mid(source),
        afterMidpoint: mid(saved.doc.entities.find(e => e.id === source.id))};
      console.log(`OBSERVED ${name}: ${JSON.stringify(evidence)}`);
      assert.equal(saved.doc.entities.length, 4, 'Origin is virtual, not a new entity');
      assert.deepEqual(saved.doc.entities.map(e => e.id), before.entities.map(e => e.id),
        'Only the original four rectangle edges persist; origin remains virtual');
      assert.deepEqual(saved.doc.dimensions, before.dimensions);
      assert.deepEqual(saved.doc.constraints.filter(c => before.constraints.some(old => old.id === c.id)), before.constraints);
      const expectedRefs = [`${source.id}:mid`, '$origin:center'].sort();
      if (fixed) {
        assert.deepEqual(saved.doc.entities, before.entities, 'Fixed refusal cannot partially move the rectangle');
        assert.ok(added.length <= 1);
        for (const relation of added) {
          assert.equal(relation.kind, kind);
          assert.equal(relation.state, 'Unsatisfied');
          assert.deepEqual([...relation.referenceKeys].sort(), expectedRefs);
        }
      } else {
        assert.equal(added.length, 1, 'Exactly one midpoint/origin alignment intent');
        assert.equal(added[0].kind, kind);
        assert.deepEqual([...added[0].referenceKeys].sort(), expectedRefs);
        assert.equal(added[0].state, 'Satisfied');
        const alignedAxis = kind === 'Horizontal' ? 'y' : 'x';
        const freeAxis = kind === 'Horizontal' ? 'x' : 'y';
        assert.equal(evidence.afterMidpoint[alignedAxis], 0, 'Selected midpoint aligns with the exact virtual origin');
        assert.equal(evidence.afterMidpoint[freeAxis], evidence.beforeMidpoint[freeAxis],
          'Source midpoint retains its unconstrained coordinate');
        for (const [i, edge] of saved.doc.entities.entries()) {
          const original = before.entities.find(e => e.id === edge.id);
          const next = saved.doc.entities[(i + 1) % 4];
          near(edge.end.x, next.start.x, 'Corner x stays welded');
          near(edge.end.y, next.start.y, 'Corner y stays welded');
          near(edge.start[i % 2 ? 'y' : 'x'], edge.end[i % 2 ? 'y' : 'x'], 'Rectangle axis stays aligned');
          if (sized || i === 0)
            assert.equal(Math.hypot(edge.end.x - edge.start.x, edge.end.y - edge.start.y),
              Math.hypot(original.end.x - original.start.x, original.end.y - original.start.y),
              `Driven or selected side length is preserved exactly: ${edge.id}`);
        }
      }
      if (JSON.stringify(content(saved.doc)) !== JSON.stringify(content(before))) {
        await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
        await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
      } else await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      await load(saved.text);
      assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
      results.push({...evidence, passed: true});
      console.log(`PASS ${name}`);
    } catch (error) {
      results.push({...evidence, name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  }
  assert.ok(results.length);
  assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
