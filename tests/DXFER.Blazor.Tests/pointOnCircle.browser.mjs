import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com']
  .includes(new URL(url).hostname), 'Isolated local/dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-point-on-circle';
const only = process.env.DXFER_CASES?.split(',');
await mkdir(output, {recursive: true});

const native = text => {
  const lines = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'Download contains a native document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const withNative = (text, doc) => {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/,
      bytes.toString('hex').match(/.{1,240}/g).map(chunk => `310\n${chunk}\n`).join(''));
};
const fixture = centerControl => {
  // Move only the center-center control away from the selectable origin marker.
  const offset = centerControl ? 2 : 0;
  return ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
    '0', 'SECTION', '2', 'ENTITIES',
    '0', 'CIRCLE', '5', '100', '8', '0', '10', String(offset), '20', String(offset), '40', '5',
    '0', 'CIRCLE', '5', '200', '8', '0', '10', String(8 + offset), '20', String(3 + offset), '40', '1',
    '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
};
const close = (actual, expected, message, tolerance = 1e-7) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${message}: expected ${expected}, got ${actual}`);
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const onCircle = (circle, angle) => ({x: circle.center.x + circle.radius * Math.cos(angle),
  y: circle.center.y + circle.radius * Math.sin(angle)});
const semanticCenterReference = key => {
  const canvasCenter = /^(.+)\|point\|center\|([^|]+)\|([^|]+)$/.exec(key);
  return canvasCenter && Number.isFinite(Number(canvasCenter[2])) && Number.isFinite(Number(canvasCenter[3]))
    ? `${canvasCenter[1]}:center` : key;
};
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
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async text => {
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'point-on-circle.dxf',
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
  const freshHover = async (p, matches, message) => {
    const bounds = await canvas.boundingBox();
    assert.ok(bounds, 'Canvas is visible');
    const away = {x: p.x + (p.x < bounds.x + bounds.width / 2 ? 24 : -24),
      y: p.y + (p.y < bounds.y + bounds.height / 2 ? 24 : -24)};
    let key;
    // Escape/cancel can clear hover while the pointer stays put. Re-enter with real mouse events.
    await expect.poll(async () => {
      await page.mouse.move(away.x, away.y);
      await page.mouse.move(p.x, p.y, {steps: 2});
      key = await canvas.getAttribute('data-hovered-id');
      return matches(key);
    }, {timeout: 3000, intervals: [100, 150, 250], message}).toBe(true);
    return key;
  };
  const pick = async ({point, id, body}, name) => {
    const p = await screen(point);
    const key = await freshHover(p, key => body ? key === id : key?.startsWith(`${id}|point|center|`),
      `${name}: expected ${body ? 'whole-circle body' : 'circle center'} hover for ${id}`);
    await capture(`${name}-${body ? 'body' : `center-${id}`}-hover`);
    if (body) assert.equal(key, id, 'Off-quadrant circumference must select the whole circle, not a snap point');
    else assert.ok(key?.startsWith(`${id}|point|center|`), `Expected circle center; got ${key}`);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(180);
    return key;
  };
  const applyCoincident = async (target, moving, centerControl, workflow, reverse, name) => {
    await canvas.press('Escape');
    if (workflow === 'tool-first') {
      await tool('Coincident');
      await expect(canvas).toHaveAttribute('data-active-tool', 'coincident');
    }
    const picks = [{id: moving.id, point: moving.center, body: false},
      {id: target.id, point: centerControl ? target.center : onCircle(target, 0.73), body: !centerControl}];
    if (reverse) picks.reverse();
    const keys = [];
    for (const [index, targetPick] of picks.entries()) {
      keys.push(await pick(targetPick, name));
      if (index === 0 || workflow === 'preselect') {
        await expect(canvas).toHaveAttribute('data-selected-count', String(index + 1));
        assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), [...keys].sort());
      }
    }
    if (workflow === 'preselect') await tool('Coincident');
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
  for (const mode of ['free', 'fixed', 'center-control'])
    for (const workflow of ['preselect', 'tool-first']) for (const reverse of [false, true])
      cases.push({mode, workflow, reverse});

  for (const {mode, workflow, reverse} of cases) {
    const centerControl = mode === 'center-control';
    const name = `${mode}-${workflow}-${reverse ? 'target-first' : 'center-first'}`;
    if (only && !only.includes(name)) continue;
    try {
      await load(fixture(centerControl));
      let baseline = await download(`${name}-seed`);
      const target = baseline.doc.entities.find(e => e.kind === 'circle' && e.radius === 5);
      const moving = baseline.doc.entities.find(e => e.kind === 'circle' && e.radius === 1);
      assert.ok(target && moving);
      if (mode === 'fixed') {
        // Both peers are fixed: no alternate target movement/resizing can legitimately satisfy this request.
        const doc = structuredClone(baseline.doc);
        doc.constraints.push({id: 'fixed-target', kind: 'Fix', referenceKeys: [target.id], state: 'Satisfied'},
          {id: 'fixed-moving', kind: 'Fix', referenceKeys: [moving.id], state: 'Satisfied'});
        await load(withNative(baseline.text, doc));
        baseline = await download(`${name}-fixed-baseline`);
        assert.deepEqual(baseline.doc.constraints, doc.constraints, 'Fixed fixture intent loads');
      }
      await applyCoincident(target, moving, centerControl, workflow, reverse, name);
      const saved = await download(`${name}-applied`);
      const added = saved.doc.constraints.filter(c => !baseline.doc.constraints.some(old => old.id === c.id));
      const expectedKind = centerControl ? 'Coincident' : 'PointOnCircle';
      const expectedRefs = [`${moving.id}:center`, centerControl ? `${target.id}:center` : target.id].sort();
      assert.deepEqual(saved.doc.entities.map(e => e.id), baseline.doc.entities.map(e => e.id), 'Entity identities preserved');
      assert.deepEqual(saved.doc.dimensions, baseline.doc.dimensions);
      assert.deepEqual(saved.doc.constraints.filter(c => baseline.doc.constraints.some(old => old.id === c.id)), baseline.doc.constraints);
      for (const original of [target, moving])
        close(saved.doc.entities.find(e => e.id === original.id).radius, original.radius, 'Circle radius preserved');
      for (const relation of added) {
        assert.equal(relation.kind, expectedKind);
        const references = centerControl ? relation.referenceKeys.map(semanticCenterReference) : relation.referenceKeys;
        assert.deepEqual([...references].sort(), expectedRefs, 'Native point/whole-circle reference semantics');
      }
      if (mode === 'fixed') {
        assert.deepEqual(saved.doc.entities, baseline.doc.entities, 'Failed command cannot partially alter geometry');
        assert.ok(added.length <= 1, 'At most one failed relation');
        assert.ok(added.every(c => c.state === 'Unsatisfied'), 'Explicit failed intent or safe refusal');
        if (added.length) await historyAndReopen(baseline.doc, saved, name);
        else {
          await load(saved.text);
          assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(baseline.doc));
        }
      } else {
        assert.equal(added.length, 1, 'One relation must be created');
        const relation = added[0];
        assert.equal(relation.state, 'Satisfied');
        const solvedTarget = saved.doc.entities.find(e => e.id === target.id);
        const solvedMoving = saved.doc.entities.find(e => e.id === moving.id);
        if (centerControl) close(distance(solvedTarget.center, solvedMoving.center), 0, 'Centers coincide');
        else {
          assert.deepEqual(solvedTarget, target, 'Point-on-circle must preserve the target circle');
          close(distance(solvedTarget.center, solvedMoving.center), target.radius, 'Selected center lies on target circumference');
          assert.ok(distance(solvedMoving.center, moving.center) > 1, 'Selected center must actually move');
        }
        assert.ok(saved.doc.constraints.every(c => c.state === 'Satisfied'));
        await historyAndReopen(baseline.doc, saved, name);

        if (!centerControl) {
          const from = await screen(solvedMoving.center);
          const destination = onCircle(target, 1.12);
          const to = await screen(destination);
          await freshHover(from, key => key?.startsWith(`${moving.id}|point|center|`),
            `Reopened circle center is draggable: ${moving.id}`);
          await page.mouse.down();
          await page.mouse.move(to.x, to.y, {steps: 12});
          await page.mouse.up();
          await page.waitForTimeout(300);
          const changed = await download(`${name}-moved-again`);
          const moved = changed.doc.entities.find(e => e.id === moving.id);
          assert.deepEqual(changed.doc.entities.find(e => e.id === target.id), target, 'Repeat edit leaves target unchanged');
          close(moved.radius, moving.radius, 'Repeat edit preserves moving radius');
          close(distance(moved.center, target.center), target.radius, 'Repeat edit retains circumference incidence');
          assert.ok(distance(moved.center, solvedMoving.center) > 0.5, 'Repeat drag must actually change the center');
          close(distance(moved.center, destination), 0, 'Center reaches the requested circumference location', 0.04);
          const persisted = changed.doc.constraints.find(c => c.id === relation.id);
          assert.equal(persisted.state, 'Satisfied');
          assert.deepEqual(persisted.referenceKeys, relation.referenceKeys);
          assert.ok(changed.doc.constraints.every(c => c.state === 'Satisfied'));
          assert.deepEqual(changed.doc.dimensions, saved.doc.dimensions);
        }
      }
      await capture(`${name}-final`);
      results.push({name, passed: true});
      console.log(`PASS ${name}: actual picks, native ${expectedKind}, radii, history/reopen${mode === 'free' ? ', repeat movement' : ''}`);
    } catch (error) {
      results.push({name, passed: false, error: error.stack});
      console.error(`FAIL ${name}: ${error.message}`);
      await capture(`${name}-failure`);
      await ready();
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors,
      limits: ['Off-quadrant whole-circle picks only; quadrant point-snap semantics are not asserted.']}, null, 2));
  }
  assert.ok(results.length, 'At least one matching case must run');
  assert.deepEqual(errors, [], 'No browser/circuit errors');
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
  console.log(`PASS ${results.length} point-on-circle cases`);
} finally {
  await browser.close();
}
