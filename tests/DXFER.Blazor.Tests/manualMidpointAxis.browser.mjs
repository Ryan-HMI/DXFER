import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com']
  .includes(new URL(url).hostname), 'Isolated local/dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-manual-midpoint-axis';
const only = process.env.DXFER_CASES?.split(',');
await mkdir(output, {recursive: true});

const native = text => {
  const lines = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'Download contains a native document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
// Add fixture constraints to an exported native payload without altering its DXF geometry checksum.
const withNative = (text, doc) => {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/,
      bytes.toString('hex').match(/.{1,240}/g).map(chunk => `310\n${chunk}\n`).join(''));
};
const fixture = kind => {
  const horizontal = kind === 'Horizontal';
  return ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
    '0', 'SECTION', '2', 'ENTITIES',
    '0', 'LINE', '5', '100', '8', '0', '10', '0', '20', '0',
    '11', horizontal ? '0' : '10', '21', horizontal ? '10' : '0',
    '0', 'CIRCLE', '5', '200', '8', '0', '10', horizontal ? '5' : '4',
    '20', horizontal ? '4' : '5', '40', '1', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
};
const close = (actual, expected, message, tolerance = 1e-7) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${message}: expected ${expected}, got ${actual}`);
const closePoint = (actual, expected, message, tolerance = 1e-7) => {
  assert.ok(actual, `${message}: missing point`);
  close(actual.x, expected.x, `${message} x`, tolerance);
  close(actual.y, expected.y, `${message} y`, tolerance);
};
const midpoint = line => ({x: (line.start.x + line.end.x) / 2, y: (line.start.y + line.end.y) / 2});
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const results = [], errors = [];
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
    await page.waitForTimeout(180);
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
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async text => {
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'manual-midpoint-axis.dxf',
      mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '2');
    await tool('Fit extents');
    await canvas.press('Escape');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX,
        c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = previous === view ? stable + 1 : 0;
      previous = view;
      return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = point => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), point);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify(await canvas.evaluate(c => ({...c.dataset})), null, 2));
  };
  const pick = async (point, id, label, name) => {
    const p = await screen(point);
    await page.mouse.move(p.x, p.y);
    await page.waitForTimeout(180);
    await capture(`${name}-${label}-hover`);
    const key = await canvas.getAttribute('data-hovered-id');
    assert.ok(key?.startsWith(`${id}|point|${label}|`),
      `${name}: expected ${label} point, not whole entity; actual ${key}`);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(200);
    return key;
  };
  const applyAxis = async (source, circle, kind, workflow, reverse, name) => {
    await canvas.press('Escape');
    if (workflow === 'tool-first') {
      await tool(kind);
      await expect(canvas).toHaveAttribute('data-active-tool', kind.toLowerCase());
    }
    const picks = [{point: circle.center, id: circle.id, label: 'center'},
      {point: midpoint(source), id: source.id, label: 'mid'}];
    if (reverse) picks.reverse();
    const keys = [];
    for (const [index, target] of picks.entries()) {
      keys.push(await pick(target.point, target.id, target.label, name));
      if (index === 0 || workflow === 'preselect') {
        await expect(canvas).toHaveAttribute('data-selected-count', String(index + 1));
        assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), [...keys].sort(),
          'Selection retains point identities');
      }
    }
    if (workflow === 'preselect') await tool(kind);
    await page.waitForTimeout(250);
    await canvas.press('Escape');
  };
  const historyAndReopen = async (before, saved, name) => {
    await tool('Undo');
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo');
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
    await load(saved.text);
    assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
  };

  const cases = [];
  for (const fixed of [false, true]) for (const kind of ['Horizontal', 'Vertical'])
    for (const workflow of ['preselect', 'tool-first']) for (const reverse of [false, true])
      cases.push({fixed, kind, workflow, reverse});

  for (const {fixed, kind, workflow, reverse} of cases) {
    const name = `${kind.toLowerCase()}-${workflow}-${reverse ? 'mid-first' : 'center-first'}-${fixed ? 'fixed' : 'free'}`;
    if (only && !only.includes(name)) continue;
    try {
      await load(fixture(kind));
      const seed = await download(`${name}-seed`);
      const doc = structuredClone(seed.doc);
      const source = doc.entities.find(e => e.kind === 'line');
      const circle = doc.entities.find(e => e.kind === 'circle');
      assert.ok(source && circle);
      doc.constraints.push({id: 'source-origin', kind: 'Coincident',
        referenceKeys: [`${source.id}:start`, '$origin:center'], state: 'Satisfied'});
      if (fixed) {
        // Lock both peers so refusal does not depend on solving a different free-peer configuration.
        doc.constraints.push({id: 'fixed-circle', kind: 'Fix', referenceKeys: [circle.id], state: 'Satisfied'},
          {id: 'fixed-source', kind: 'Fix', referenceKeys: [source.id], state: 'Satisfied'});
      }
      await load(withNative(seed.text, doc));
      const before = (await download(`${name}-before`)).doc;
      assert.deepEqual(before.constraints, doc.constraints, 'Native fixture constraints must load');
      await applyAxis(source, circle, kind, workflow, reverse, name);
      const saved = await download(`${name}-applied`);
      const added = saved.doc.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
      const expectedRefs = [`${source.id}:mid`, `${circle.id}:center`].sort();
      const changedCircle = saved.doc.entities.find(e => e.id === circle.id);
      assert.deepEqual(saved.doc.entities.find(e => e.id === source.id), source, 'Axis command must preserve the source line');
      close(changedCircle.radius, circle.radius, 'Circle radius is preserved');
      assert.deepEqual(saved.doc.dimensions, before.dimensions);
      assert.deepEqual(saved.doc.constraints.filter(c => before.constraints.some(old => old.id === c.id)), before.constraints);
      if (fixed) {
        assert.deepEqual(saved.doc.entities, before.entities, 'Refusal cannot partially change geometry');
        assert.ok(added.length <= 1, 'At most one failed axis intent');
        for (const relation of added) {
          assert.equal(relation.kind, kind);
          assert.equal(relation.state, 'Unsatisfied');
          assert.deepEqual([...relation.referenceKeys].sort(), expectedRefs);
        }
        if (added.length) await historyAndReopen(before, saved, name);
        else {
          await load(saved.text);
          assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(before));
        }
      } else {
        assert.equal(added.length, 1, 'Exactly one manual axis relation');
        const relation = added[0];
        assert.equal(relation.kind, kind);
        assert.deepEqual([...relation.referenceKeys].sort(), expectedRefs, 'Native refs must be midpoint and center, not whole line');
        assert.equal(relation.state, 'Satisfied');
        const axis = kind === 'Horizontal' ? 'y' : 'x';
        const other = axis === 'y' ? 'x' : 'y';
        close(changedCircle.center[axis], midpoint(source)[axis], 'Center aligns with source midpoint');
        close(changedCircle.center[other], circle.center[other], 'Unconstrained center coordinate is preserved');
        await historyAndReopen(before, saved, name);

        const from = await screen(source.end);
        const target = {...source.end, [axis]: source.end[axis] + 1};
        const to = await screen(target);
        await page.mouse.move(from.x, from.y);
        await page.waitForTimeout(180);
        const hovered = await canvas.getAttribute('data-hovered-id');
        assert.ok(hovered?.startsWith(`${source.id}|point|end|`), `Source endpoint must be picked; got ${hovered}`);
        await page.mouse.down();
        await page.mouse.move(to.x, to.y, {steps: 12});
        await page.mouse.up();
        await page.waitForTimeout(300);
        const followed = (await download(`${name}-source-follow`)).doc;
        const movedSource = followed.entities.find(e => e.id === source.id);
        const movedCircle = followed.entities.find(e => e.id === circle.id);
        closePoint(movedSource.start, source.start, 'Origin anchor is preserved');
        closePoint(movedSource.end, target, 'Source endpoint moves', 0.04);
        close(movedCircle.center[axis], midpoint(movedSource)[axis], 'Circle follows the live midpoint');
        assert.ok(Math.abs(movedCircle.center[axis] - changedCircle.center[axis]) > 0.25, 'Circle must actually follow');
        close(movedCircle.radius, circle.radius, 'Following preserves radius');
        const persisted = followed.constraints.find(c => c.id === relation.id);
        assert.equal(persisted.state, 'Satisfied');
        assert.deepEqual(persisted.referenceKeys, relation.referenceKeys);
        assert.ok(followed.constraints.every(c => c.state === 'Satisfied'));
      }
      await capture(`${name}-final`);
      results.push({name, passed: true});
      console.log(`PASS ${name}: midpoint point-pick, native refs, radius/source preservation, history/reopen${fixed ? ', guarded refusal' : ', source follow'}`);
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
  console.log(`PASS ${results.length} manual midpoint axis cases`);
} finally {
  await browser.close();
}
