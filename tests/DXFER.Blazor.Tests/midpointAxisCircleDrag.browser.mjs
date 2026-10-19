import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-midpoint-axis-circle-drag';
const only = process.env.DXFER_CASES?.split(',');
const verify = process.env.DXFER_DRAG_MODE === 'verify';
await mkdir(output, {recursive: true});
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
const fixture = vertical => {
  const edges = [[0, 0, 0, 10], [0, 10, 22, 10], [22, 10, 22, 0], [22, 0, 0, 0]];
  return ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC', '0', 'SECTION', '2', 'ENTITIES',
    '0', 'CIRCLE', '5', '200', '8', '0', '10', '5', '20', '5', '40', '2',
    ...edges.flatMap(([x, y, ex, ey], i) => ['0', 'LINE', '5', String(100 + i), '8', '0',
      '10', vertical ? y : x, '20', vertical ? x : y, '11', vertical ? ey : ex, '21', vertical ? ex : ey]),
    '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const near = (actual, expected, label) => assert.ok(Math.abs(actual - expected) < 1e-4, `${label}: ${actual} != ${expected}`);
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
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
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
    await page.locator('input[type=file]').setInputFiles({name: 'midpoint-axis-circle-drag.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '5');
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const drag = async (circle, target, name) => {
    await canvas.press('Escape');
    const start = await screen(circle.center), end = await screen(target);
    await page.mouse.move(start.x + 25, start.y + 25);
    await page.mouse.move(start.x, start.y, {steps: 2});
    await expect.poll(() => canvas.getAttribute('data-hovered-id')).toMatch(new RegExp(`^${circle.id}\\|point\\|center\\|`));
    await page.screenshot({path: `${output}/${name}-hover.png`});
    await page.mouse.down();
    let held;
    try {
      for (let i = 1; i <= 12; i++) {
        await page.mouse.move(start.x + (end.x - start.x) * i / 12, start.y + (end.y - start.y) * i / 12);
        await page.waitForTimeout(35);
      }
      await page.waitForTimeout(450);
      held = {center: (await canvas.getAttribute('data-first-center')).split(',').map(Number),
        radius: Number(await canvas.getAttribute('data-first-radius')), dataset: await canvas.evaluate(c => ({...c.dataset}))};
      await page.screenshot({path: `${output}/${name}-held.png`});
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(350);
    const status = await page.locator('.dxfer-command-bar').innerText();
    await page.screenshot({path: `${output}/${name}-released.png`});
    await canvas.press('Escape');
    const saved = await download(`${name}-released`);
    await writeFile(`${output}/${name}-gesture.json`, JSON.stringify({target, held, status}, null, 2));
    return {saved, held, status};
  };
  const cases = [];
  for (const kind of ['Horizontal', 'Vertical']) {
    cases.push({kind, motion: 'pure', reverse: false}, {kind, motion: 'drift', reverse: false},
      {kind, motion: 'drift', reverse: true}, {kind, motion: 'fixed', reverse: false});
  }
  for (const {kind, motion, reverse} of cases) {
    const name = `${kind.toLowerCase()}-${motion}-${reverse ? 'mid-first' : 'center-first'}`;
    if (only && !only.includes(name)) continue;
    if (!only && (motion === 'pure' || (motion === 'fixed' && kind === 'Vertical'))) continue;
    let evidence;
    try {
      const vertical = kind === 'Vertical', free = vertical ? 'y' : 'x', constrained = vertical ? 'x' : 'y';
      await load(fixture(vertical));
      const seed = await download(`${name}-seed`), doc = structuredClone(seed.doc);
      const circle = doc.entities.find(e => e.kind === 'circle'), edges = doc.entities.filter(e => e.kind === 'line');
      assert.equal(doc.entities[0].id, circle.id, 'Held-preview debug center must refer to the circle');
      for (const [i, edge] of edges.entries()) doc.constraints.push(
        {id: `corner-${i}`, kind: 'Coincident', state: 'Satisfied', referenceKeys: [`${edge.id}:end`, `${edges[(i + 1) % 4].id}:start`]},
        {id: `axis-${i}`, kind: (i % 2 === 0) !== vertical ? 'Vertical' : 'Horizontal', state: 'Satisfied', referenceKeys: [edge.id]});
      const refs = [`${circle.id}:center`, `${edges[0].id}:mid`];
      doc.constraints.push({id: 'midpoint-axis', kind, state: 'Satisfied', referenceKeys: reverse ? [...refs].reverse() : refs});
      if (motion === 'fixed') doc.constraints.push({id: 'fixed-center', kind: 'Fix', state: 'Satisfied', referenceKeys: [`${circle.id}:center`]});
      await load(withNative(seed.text, doc));
      const before = (await download(`${name}-before`)).doc;
      assert.deepEqual(content(before), content(doc));
      const target = {...circle.center, [free]: 8, [constrained]: motion === 'pure' ? 5 : 5.6};
      const {saved, held, status} = await drag(circle, target, name);
      const moved = saved.doc.entities.find(e => e.id === circle.id);
      evidence = {name, build, target, heldCenter: held.center, releasedCenter: moved.center, status,
        freeAxisMoved: Math.abs(moved.center[free] - circle.center[free]) > 1};
      assert.equal(moved.radius, circle.radius);
      assert.equal(held.radius, circle.radius);
      assert.deepEqual(saved.doc.entities.filter(e => e.id !== circle.id), edges, 'Free rectangle remains unchanged');
      assert.deepEqual(saved.doc.constraints, before.constraints);
      assert.deepEqual(saved.doc.dimensions, before.dimensions);
      near(moved.center[constrained], 5, 'Axis remains satisfied');
      if (motion === 'fixed') {
        assert.deepEqual(content(saved.doc), content(before));
        assert.deepEqual(held.center, [5, 5]);
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        // Recover in a new disposable native fixture with only Fix removed, never bypass the solver.
        const recovered = structuredClone(before);
        recovered.constraints = recovered.constraints.filter(c => c.id !== 'fixed-center');
        await load(withNative(seed.text, recovered));
        const recovery = await drag(circle, {...circle.center, [free]: 8, [constrained]: verify ? 5.6 : 5}, `${name}-recovery`);
        const recoveredCircle = recovery.saved.doc.entities.find(e => e.id === circle.id);
        near(recoveredCircle.center[free], 8, 'Drag recovers after removing Fix');
        near(recoveredCircle.center[constrained], 5, 'Recovery preserves alignment');
        near(recovery.held.center[vertical ? 1 : 0], 8, 'Recovery preview moves before release');
        near(recovery.held.center[vertical ? 0 : 1], 5, 'Recovery preview preserves alignment');
        assert.equal(recoveredCircle.radius, 2);
        assert.deepEqual(recovery.saved.doc.entities.filter(e => e.id !== circle.id), edges);
        assert.deepEqual(recovery.saved.doc.constraints, recovered.constraints);
      } else if (verify || motion === 'pure') {
        near(held.center[vertical ? 1 : 0], 8, 'Held preview advances along free axis');
        near(held.center[vertical ? 0 : 1], 5, 'Held preview projects perpendicular pointer drift');
        near(moved.center[free], 8, 'Release advances along free axis');
        await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
        await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
        await load(saved.text);
        assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
        const again = await drag(moved, {...moved.center, [free]: 9, [constrained]: 5.4}, `${name}-reopened`);
        if (verify) {
          const againCircle = again.saved.doc.entities.find(e => e.id === circle.id);
          near(againCircle.center[free], 9, 'Reopened native relation still permits projected drag');
          near(againCircle.center[constrained], 5, 'Reopened drag preserves alignment');
          near(again.held.center[vertical ? 1 : 0], 9, 'Reopened held preview projects drift');
          near(again.held.center[vertical ? 0 : 1], 5, 'Reopened held preview preserves alignment');
          assert.equal(againCircle.radius, 2);
          assert.deepEqual(again.saved.doc.entities.filter(e => e.id !== circle.id), edges);
          assert.deepEqual(again.saved.doc.constraints, saved.doc.constraints);
        }
      }
      results.push({...evidence, passed: true});
      console.log(`${verify ? 'VERIFY' : 'BASELINE'} ${name}: ${JSON.stringify(evidence)}`);
    } catch (error) {
      results.push({...evidence, name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, verify, results, errors}, null, 2));
  }
  assert.ok(results.length);
  assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
