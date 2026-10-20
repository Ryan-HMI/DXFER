// Real HTTP/UI integration. No route interception, mock interop, or injected solver implementation.
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-plane-gcs-integration';
const only = process.env.DXFER_CASES?.split(',');
const startedAt = new Date().toISOString();
const browserModules = [];
await mkdir(output, {recursive: true});
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex, 'Download must contain native data');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const withNative = (text, doc) => {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/, bytes.toString('hex').match(/.{1,240}/g).map(h => `310\n${h}\n`).join(''));
};
const fixture = edges => ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', ...edges.flatMap(([x, y, ex, ey], i) =>
    ['0', 'LINE', '5', String(100 + i), '8', '0', '10', x, '20', y, '11', ex, '21', ey]),
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const rectangle = fixture([[-4, -2, -4, 8], [-4, 8, 18, 8], [18, 8, 18, -2], [18, -2, -4, -2]]);
const circleFixture = (radius, center = {x: 10, y: 10}) => ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', '0', 'CIRCLE', '5', '100', '8', '0', '10', center.x, '20', center.y, '40', radius,
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions, patterns: doc.metadata?.patterns ?? []});
const near = (a, b, message, tolerance = 1e-6) => assert.ok(Math.abs(a - b) <= tolerance, `${message}: ${a} != ${b}`);
const samePoint = (a, b, message, tolerance = 1e-6) => {
  near(a.x, b.x, `${message} x`, tolerance); near(a.y, b.y, `${message} y`, tolerance);
};
const length = line => Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y);
const middle = line => ({x: (line.start.x + line.end.x) / 2, y: (line.start.y + line.end.y) / 2});
const results = [], errors = [], consoleErrors = [], limitations = [];
let build;
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(12000);
  page.on('dialog', d => d.accept());
  page.on('pageerror', e => errors.push(e.message));
  page.on('response', async response => {
    if (!new URL(response.url()).pathname.endsWith('/drawingCanvas.js')) return;
    try {
      browserModules.push({url: response.url(), receivedAt: new Date().toISOString(),
        sha256: createHash('sha256').update(await response.body()).digest('hex')});
    } catch (error) { consoleErrors.push(`Could not fingerprint canvas module: ${error.message}`); }
  });
  page.on('console', message => {
    if (message.type() !== 'error') return;
    consoleErrors.push(message.text());
    console.error(`BROWSER ${message.text()}`);
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  console.log(`${startedAt}; ${build}; configured solver: PlaneGCS; real endpoint, no mocks`);
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (text, count) => {
    await page.keyboard.press('Escape'); await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'plane-gcs-integration.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const autoSnap = async enabled => {
    await menu('Settings');
    const input = page.getByRole('checkbox', {name: 'Automatic snap constraints'});
    await expect(input).toBeEnabled(); await input.setChecked(enabled);
    await tool('Close settings');
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    const evidence = {dataset: await canvas.evaluate(c => ({...c.dataset})), status: await page.locator('.dxfer-command-bar').innerText(),
      dimensions: await page.locator('input[data-dimension-id]').evaluateAll(inputs => inputs.map(input => ({
        dataset: {...input.dataset}, className: input.className, value: input.value, title: input.title
      })))};
    await writeFile(`${output}/${name}.json`, JSON.stringify(evidence, null, 2)); return evidence;
  };
  const hover = async (point, matches) => {
    const p = await screen(point);
    await expect.poll(async () => {
      await page.mouse.move(p.x + 24, p.y + 24); await page.mouse.move(p.x, p.y, {steps: 2});
      return matches(await canvas.getAttribute('data-hovered-id'));
    }, {message: 'Actual canvas geometry target is pickable'}).toBe(true);
    return p;
  };
  const pick = async (point, matches) => {
    const p = await hover(point, matches); await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
  };
  const firstStart = () => canvas.evaluate(c => {
    const [x, y] = c.dataset.firstScreenStart.split(',').map(Number);
    return {x: (x - +c.dataset.offsetX) / +c.dataset.scale, y: (+c.dataset.offsetY - y) / +c.dataset.scale};
  });
  const assertRectangle = (before, after) => {
    assert.deepEqual(after.entities.map(e => e.id), before.entities.map(e => e.id));
    assert.deepEqual(after.constraints, before.constraints);
    assert.deepEqual(after.dimensions, before.dimensions);
    after.entities.forEach((edge, i) => {
      const next = after.entities[(i + 1) % 4];
      samePoint(edge.end, next.start, 'Corner weld remains satisfied');
      near(edge.start[i % 2 ? 'y' : 'x'], edge.end[i % 2 ? 'y' : 'x'], 'H/V remains satisfied');
      assert.ok(length(edge) > 1e-6, 'Rectangle cannot collapse');
    });
  };
  const historyReopen = async (before, saved, name) => {
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
    await load(saved.text, saved.doc.entities.length);
    assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
  };
  const rectangleFixture = async (name, fixed = false) => {
    await load(rectangle, 4); const seed = await download(`${name}-seed`), doc = structuredClone(seed.doc);
    doc.entities.forEach((e, i) => {
      doc.constraints.push({id: `corner-${i}`, kind: 'Coincident', referenceKeys: [`${e.id}:end`, `${doc.entities[(i + 1) % 4].id}:start`], state: 'Satisfied'},
        {id: `axis-${i}`, kind: i % 2 ? 'Horizontal' : 'Vertical', referenceKeys: [e.id], state: 'Satisfied'});
      if (fixed) doc.constraints.push({id: `fixed-${i}`, kind: 'Fix', referenceKeys: [e.id], state: 'Satisfied'});
    });
    await load(withNative(seed.text, doc), 4);
    const loaded = (await download(`${name}-before`)).doc;
    assert.deepEqual(content(loaded), content(doc)); return loaded;
  };
  const drag = async (before, target, name, refusal = null, persistedSnap = null) => {
    const first = before.entities[0];
    const a = await hover(first.start, key => key?.startsWith(`${first.id}|point|start|`));
    const b = await screen(target);
    let held;
    await page.mouse.down();
    try {
      await page.mouse.move(b.x, b.y, {steps: 12});
      await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
      if (refusal) {
        await expect.poll(() => canvas.getAttribute('data-plane-gcs-preview-reason')).toMatch(refusal);
        samePoint(await firstStart(), first.start, 'Refused held preview stays unchanged');
      } else {
        await expect.poll(async () => {
          const p = await firstStart(); return Math.hypot(p.x - target.x, p.y - target.y);
        }, {message: 'Real PlaneGCS preview reaches the dragged vertex while held'}).toBeLessThan(1e-4);
        assert.ok(!/unavailable|not support|failed|not mapped/i.test(await canvas.getAttribute('data-plane-gcs-preview-reason') ?? ''));
      }
      held = await capture(`${name}-held`);
    } finally { await page.mouse.up(); }
    await expect(page.locator('.dxfer-command-bar')).toContainText(refusal ?? /Moved geometry with PlaneGCS/);
    if (!refusal) await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeEnabled();
    const released = await capture(`${name}-released`);
    await canvas.press('Escape');
    const saved = await download(`${name}-after`);
    if (refusal) {
      assert.deepEqual(content(saved.doc), content(before));
      await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      assert.match(released.status, refusal);
    } else {
      samePoint(saved.doc.entities[0].start, target, 'Released vertex follows requested pointer', 1e-4);
      const previewPoint = held.dataset.firstScreenStart.split(',').map(Number);
      samePoint(saved.doc.entities[0].start, {x: (previewPoint[0] - +held.dataset.offsetX) / +held.dataset.scale,
        y: (+held.dataset.offsetY - previewPoint[1]) / +held.dataset.scale}, 'Held/released solver geometry agrees');
      if (persistedSnap) {
        const added = saved.doc.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
        assert.equal(added.length, 1, 'AutoSnap must persist exactly one relation');
        assert.equal(added[0].kind, persistedSnap.kind);
        assert.equal(added[0].state, 'Satisfied');
        assert.deepEqual([...added[0].referenceKeys].sort(), [...persistedSnap.refs].sort());
        assertRectangle(before, {...saved.doc, constraints: saved.doc.constraints.filter(c => c.id !== added[0].id)});
      } else {
        assertRectangle(before, saved.doc);
      }
      assert.match(released.status, /PlaneGCS/);
    }
    return {saved, held, released};
  };
  const run = async (name, action) => {
    if (only && !only.includes(name)) return;
    try { const evidence = await action(); results.push({name, passed: true, evidence}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await capture(`${name}-failure`); await page.keyboard.press('Escape');
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, startedAt, updatedAt: new Date().toISOString(),
      browserModules, configuredEngine: await canvas.getAttribute('data-solver-engine'), results, errors, consoleErrors, limitations,
      evidenceScope: 'Actual Web configuration, endpoint-reported engine, native results and UI behavior. Engine binary execution is proved separately by CoreNodeRuntime tests.'}, null, 2));
  };
  for (const [suffix, cornerX, cornerY, targetX, targetY] of [
    ['', 650, 560, 690, 610],
    ['-top-left', 480, 450, 445, 420],
    ['-top-right', 650, 450, 690, 420],
    ['-bottom-left', 480, 560, 445, 610]
  ]) await run(`created-rectangle-corner-drag${suffix}`, async () => {
    const name = `created-rectangle${suffix}`;
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await tool('Two-point rectangle');
    await page.mouse.click(480, 450);
    await page.mouse.move(650, 560, {steps: 8});
    await page.mouse.click(650, 560);
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await canvas.press('Escape');
    if (await canvas.getAttribute('data-show-all-constraints') !== 'true') await tool('Show constraints');
    const before = await download(`${name}-before`);
    assert.equal(before.doc.constraints.filter(c => c.kind === 'Parallel').length, 2);
    assert.equal(before.doc.constraints.filter(c => c.kind === 'Perpendicular').length, 1);
    const xs = before.doc.entities.flatMap(e => [e.start.x, e.end.x]);
    const right = cornerX === 650, lower = cornerY === 560;
    const source = before.doc.entities.find(e => Math.abs(e.start.x - e.end.x) < 1e-7
      && e.start.x === (right ? Math.max(...xs) : Math.min(...xs)));
    assert.ok(source, 'Created rectangle has a vertical edge');
    const useStart = lower ? source.start.y < source.end.y : source.start.y > source.end.y;
    const corner = useStart ? source.start : source.end;
    const target = await canvas.evaluate((c, p) => ({
      x: (p.x - c.getBoundingClientRect().left - +c.dataset.offsetX) / +c.dataset.scale,
      y: (+c.dataset.offsetY - (p.y - c.getBoundingClientRect().top)) / +c.dataset.scale
    }), {x: targetX, y: targetY});
    const start = await screen(corner), end = await screen(target);
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    try {
      await page.mouse.move(end.x, end.y, {steps: 20});
      await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
      await expect.poll(async () => {
        const heldX = await canvas.evaluate((c, right) => {
          const [x] = c.dataset[right ? 'firstScreenEnd' : 'firstScreenStart'].split(',').map(Number);
          return (x - +c.dataset.offsetX) / +c.dataset.scale;
        }, right);
        return Math.abs(heldX - target.x);
      }, {message: 'Actual created rectangle preview follows the pointer while held'}).toBeLessThan(1e-4);
      await capture(`${name}-held`);
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(500);
    const feedback = await capture(`${name}-released`);
    await canvas.press('Escape');
    const after = await download(`${name}-after`);
    const moved = after.doc.entities.find(e => e.id === source.id);
    samePoint(useStart ? moved.start : moved.end, target,
      `Created corner follows pointer: ${feedback.status}`, 1e-4);
    for (const edge of after.doc.entities) {
      const joined = after.doc.entities.filter(peer => peer.id !== edge.id)
        .some(peer => Math.hypot(edge.end.x - peer.start.x, edge.end.y - peer.start.y) < 1e-6
          || Math.hypot(edge.end.x - peer.end.x, edge.end.y - peer.end.y) < 1e-6);
      assert.ok(joined, 'Created rectangle remains connected');
      assert.ok(Math.abs(edge.start.x - edge.end.x) < 1e-6 || Math.abs(edge.start.y - edge.end.y) < 1e-6,
        'Created rectangle stays axis-aligned');
    }
    assert.deepEqual(after.doc.constraints, before.doc.constraints);
    await historyReopen(before.doc, after, name);
    return {status: feedback.status, constraints: after.doc.constraints};
  });
  await run('auto-snap-origin-persisted', async () => {
    const before = await rectangleFixture('auto-snap-origin-persisted'); await autoSnap(true);
    const refs = ['$origin:center', `${before.entities[0].id}:start`];
    const moved = await drag(before, {x: 0, y: 0}, 'auto-snap-origin-persisted', null, {kind: 'Coincident', refs});
    samePoint(moved.saved.doc.entities[0].start, {x: 0, y: 0}, 'Persisted origin coincidence is satisfied');
    assert.match(moved.released.status, /added coincident constraint/i);
    await historyReopen(before, moved.saved, 'auto-snap-origin-persisted');
    return {previewEngine: moved.held.dataset.planeGcsPreviewEngine, autoSnap: true, refs, status: moved.released.status};
  });
  await run('hv-rectangle-vertex-drag', async () => {
    const before = await rectangleFixture('hv-rectangle-vertex-drag'); await autoSnap(false);
    const moved = await drag(before, {x: -3, y: -1}, 'hv-rectangle-vertex-drag');
    await historyReopen(before, moved.saved, 'hv-rectangle-vertex-drag');
    await drag(moved.saved.doc, {x: -2.5, y: -0.5}, 'hv-rectangle-reopened-drag');
    return {previewEngine: moved.held.dataset.planeGcsPreviewEngine, autoSnap: false};
  });
  await run('fixed-rectangle-refusal', async () => {
    const before = await rectangleFixture('fixed-rectangle-refusal', true); await autoSnap(false);
    const refused = await drag(before, {x: -3, y: -1}, 'fixed-rectangle-refusal', /fixed|cannot|could not|failed|conflict|constrained.*no drag movement/i);
    await load(refused.saved.text, 4);
    assert.deepEqual(content((await download('fixed-reopened')).doc), content(before));
    return {previewEngine: refused.held.dataset.planeGcsPreviewEngine, reason: refused.held.dataset.planeGcsPreviewReason};
  });
  for (const anchor of ['origin', 'line-midpoint', 'line-midpoint-redundant']) for (const dy of [0, 1]) for (const sized of [false, true]) await run(`circle-horizontal-${anchor}-drag-${dy}${sized ? '-sized' : ''}`, async () => {
    const name = `circle-horizontal-${anchor}-drag-${dy}${sized ? '-sized' : ''}`;
    let input = circleFixture(2, {x: 1, y: 0.5});
    if (anchor !== 'origin') input = input.replace('0\nENDSEC\n0\nEOF',
      '0\nLINE\n5\n101\n8\n0\n10\n-2\n20\n-2\n11\n-2\n21\n2\n0\nENDSEC\n0\nEOF');
    await load(input, anchor === 'origin' ? 1 : 2); await autoSnap(false);
    const seed = (await download(`${name}-seed`)).doc, circle = seed.entities[0];
    if (sized) {
      await tool('Dimension');
      await pick({x: circle.center.x + Math.sqrt(2), y: circle.center.y + Math.sqrt(2)}, key => key === circle.id);
      await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
      const label = await screen({x: circle.center.x + 1, y: circle.center.y + 0.5});
      await page.mouse.click(label.x, label.y);
      await expect(page.locator('input[data-dimension-id]')).toHaveCount(1);
      await page.keyboard.press('Escape'); await canvas.press('Escape');
    }
    if (anchor !== 'origin') {
      if (anchor.endsWith('-redundant')) {
        await tool('Vertical');
        await pick({x: -2, y: 1.2}, key => key === seed.entities[1].id);
        await canvas.press('Escape');
      }
      await tool('Fix');
      await pick({x: -2, y: 1.2}, key => key === seed.entities[1].id);
      await canvas.press('Escape');
    }
    await tool('Horizontal');
    await pick(circle.center, key => key?.startsWith(`${circle.id}|point|center|`));
    if (anchor === 'origin') await pick({x: 0, y: 0}, key => key === '$origin:center');
    else await pick({x: -2, y: 0}, key => key?.includes('|point|mid|'));
    await canvas.press('Escape'); await tool('Fit extents');
    const before = (await download(`${name}-before`)).doc;
    assert.equal(before.constraints.length, anchor === 'origin' ? 1 : anchor.endsWith('-redundant') ? 3 : 2);
    assert.ok(before.constraints.every(c => c.state === 'Satisfied'));
    const initial = before.entities[0].center;
    const start = await hover(initial, key => key?.startsWith(`${circle.id}|point|center|`));
    const target = await screen({x: initial.x + 1, y: initial.y + dy});
    await page.mouse.down();
    try {
      await page.mouse.move(target.x, target.y, {steps: 12});
      await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
      await expect.poll(async () => {
        const center = (await canvas.getAttribute('data-first-center')).split(',').map(Number);
        return Math.abs(center[0] - initial.x - 1);
      }).toBeLessThan(1e-4);
      const held = await capture(`${name}-held`);
      near(+held.dataset.firstCenter.split(',')[1], initial.y, 'Held preview retains Y');
    } finally { await page.mouse.up(); }
    await expect(page.locator('.dxfer-command-bar')).toContainText('Moved geometry with PlaneGCS');
    await canvas.press('Escape');
    const saved = await download(`${name}-after`);
    samePoint(saved.doc.entities[0].center, {x: initial.x + 1, y: initial.y}, 'Horizontal permits X movement');
    near(saved.doc.entities[0].radius, circle.radius, 'Center drag retains radius');
    assert.equal(saved.doc.dimensions.length, sized ? 1 : 0);
    if (sized) {
      assert.equal(saved.doc.dimensions[0].isDriving, true);
      assert.equal(saved.doc.dimensions[0].value, before.dimensions[0].value);
    }
    assert.equal(saved.doc.constraints[0].state, 'Satisfied');
    await historyReopen(before, saved, name);
    return {start, center: saved.doc.entities[0].center};
  });
  await run('midpoint-origin-horizontal', async () => {
    const before = await rectangleFixture('midpoint-origin-horizontal'); await autoSnap(false);
    const edge = before.entities[0]; await tool('Horizontal');
    await pick(middle(edge), key => key?.startsWith(`${edge.id}|point|mid|`));
    await pick({x: 0, y: 0}, key => key === '$origin:center');
    const evidence = await capture('midpoint-origin-horizontal-applied');
    await canvas.press('Escape'); const saved = await download('midpoint-origin-horizontal-after');
    const added = saved.doc.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
    assert.equal(added.length, 1); assert.equal(added[0].kind, 'Horizontal'); assert.equal(added[0].state, 'Satisfied');
    assert.deepEqual([...added[0].referenceKeys].sort(), [`${edge.id}:mid`, '$origin:center'].sort());
    near(middle(saved.doc.entities[0]).y, 0, 'Midpoint aligns with virtual origin');
    assertRectangle(before, {...saved.doc, constraints: saved.doc.constraints.filter(c => c.id !== added[0].id)});
    await historyReopen(before, saved, 'midpoint-origin-horizontal'); return {status: evidence.status, refs: added[0].referenceKeys};
  });
  await run('point-origin-horizontal', async () => {
    const pointDxf = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
      '0', 'SECTION', '2', 'ENTITIES', '0', 'POINT', '5', '100', '8', '0', '10', '3', '20', '2',
      '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
    await load(pointDxf, 1); await autoSnap(false);
    const bounds = await canvas.boundingBox();
    await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
    await expect.poll(async () => {
      const origin = await screen({x: 0, y: 0});
      if (origin.x > bounds.x + 200 && origin.x < bounds.x + bounds.width - 200
        && origin.y > bounds.y + 100 && origin.y < bounds.y + bounds.height - 100) return true;
      await page.mouse.wheel(0, 700);
      return false;
    }, {message: 'Zoom out through the UI until the virtual origin is clear of floating toolbars'}).toBe(true);
    const before = (await download('point-origin-before')).doc, point = before.entities[0];
    await tool('Horizontal');
    await pick(point.location, key => key === point.id || key?.startsWith(`${point.id}|point|point|`));
    await pick({x: 0, y: 0}, key => key === '$origin:center');
    const evidence = await capture('point-origin-horizontal-applied');
    await canvas.press('Escape'); const saved = await download('point-origin-horizontal-after');
    assert.deepEqual(saved.doc.entities.map(e => e.id), [point.id], 'Origin remains virtual');
    samePoint(saved.doc.entities[0].location, {x: 3, y: 0}, 'Free point aligns with origin');
    assert.equal(saved.doc.constraints.length, 1);
    const relation = saved.doc.constraints[0];
    assert.equal(relation.kind, 'Horizontal'); assert.equal(relation.state, 'Satisfied');
    assert.deepEqual([...relation.referenceKeys].sort(), [point.id, '$origin:center'].sort());
    await historyReopen(before, saved, 'point-origin-horizontal'); return {status: evidence.status, refs: relation.referenceKeys};
  });
  await run('new-horizontal-and-length-dimension', async () => {
    await load(fixture([[2, 2, 8, 4]]), 1); await autoSnap(false);
    const before = (await download('length-before')).doc, line = before.entities[0];
    await pick({x: 3.8, y: 2.6}, key => key === line.id); await tool('Horizontal');
    await canvas.press('Escape'); const aligned = await download('length-horizontal');
    assert.equal(aligned.doc.constraints.length, 1); assert.equal(aligned.doc.constraints[0].kind, 'Horizontal');
    assert.equal(aligned.doc.constraints[0].state, 'Satisfied');
    near(aligned.doc.entities[0].start.y, aligned.doc.entities[0].end.y, 'New H constraint solves');
    await historyReopen(before, aligned, 'length-horizontal');
    const horizontal = aligned.doc.entities[0], body = {x: horizontal.start.x * .7 + horizontal.end.x * .3,
      y: horizontal.start.y * .7 + horizontal.end.y * .3};
    await tool('Dimension'); await pick(body, key => key === line.id);
    await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
    const anchor = await screen({x: middle(horizontal).x, y: middle(horizontal).y - 2});
    await page.mouse.move(anchor.x, anchor.y); await page.mouse.click(anchor.x, anchor.y);
    await expect(page.locator('input[data-dimension-id]')).toHaveCount(1);
    await page.keyboard.press('Escape'); await canvas.press('Escape');
    const placed = await download('length-placed'), dimension = placed.doc.dimensions[0];
    assert.equal(dimension.kind, 'LinearDistance'); assert.equal(dimension.isDriving, true);
    near(dimension.value, length(placed.doc.entities[0]), 'Placed dimension measures the actual line');
    await historyReopen(aligned.doc, placed, 'length-placement');
    const input = page.locator(`input[data-dimension-id="${dimension.id}"]`);
    await input.dblclick(); await input.fill('8'); await input.press('Enter');
    await expect(input).toHaveAttribute('data-dimension-value', '8');
    const editEvidence = await capture('length-edited-before-download');
    await canvas.press('Escape'); const edited = await download('length-edited');
    assert.equal(edited.doc.dimensions[0].value, 8);
    if (Math.abs(length(edited.doc.entities[0]) - 8) > 1e-6) {
      assert.deepEqual(edited.doc.entities, placed.doc.entities, 'Refused edit preserves accepted geometry exactly');
      await expect(input).toBeVisible();
      await expect(input).toHaveClass(/unsatisfied/i);
      assert.match(editEvidence.status, /geometry unchanged/i, 'Failed requested value has an explicit visible refusal');
      assert.fail(`Unexpected refusal of supported length edit; attempted 8 retained as Unsatisfied, geometry unchanged. ${editEvidence.status}`);
    }
    await expect(input).not.toHaveClass(/unsatisfied/i);
    near(length(edited.doc.entities[0]), 8, 'Edited driving length solves');
    assert.deepEqual(edited.doc.constraints, placed.doc.constraints);
    assert.deepEqual(edited.doc.entities.map(e => e.id), before.entities.map(e => e.id));
    await historyReopen(placed.doc, edited, 'length-edit');
    return {constraint: aligned.doc.constraints[0], dimension: edited.doc.dimensions[0]};
  });
  for (const shape of ['circle', 'rectangle']) await run(`offset-${shape}-gap`, async () => {
    const name = `offset-${shape}-gap`, count = shape === 'circle' ? 1 : 4;
    await load(shape === 'circle' ? circleFixture(3) : rectangle, count); await autoSnap(false);
    const before = (await download(`${name}-before`)).doc;
    if (shape === 'circle') {
      await pick({x: 10 + 3 * Math.cos(.6), y: 10 + 3 * Math.sin(.6)}, key => key === before.entities[0].id);
    } else {
      const a = await screen({x: -4.5, y: 8.5}), b = await screen({x: 18.5, y: -2.5});
      await page.mouse.move(a.x, a.y); await page.mouse.down();
      await page.mouse.move(b.x, b.y, {steps: 12}); await page.mouse.up();
    }
    await expect(canvas).toHaveAttribute('data-selected-count', String(count));
    await tool('Offset');
    const side = await screen(shape === 'circle' ? {x: 14, y: 10} : {x: 7, y: -3});
    await page.mouse.move(side.x, side.y);
    const draft = page.locator('input[data-dimension-key="offset"]');
    await expect(draft).toBeVisible(); await draft.fill('1'); await draft.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', String(count + 1));
    await canvas.press('Escape'); await tool('Fit extents');
    const linked = await download(`${name}-linked`);
    const relations = linked.doc.constraints.filter(c => c.kind === 'Offset');
    assert.equal(relations.length, 1); assert.equal(relations[0].state, 'Satisfied');
    const dimensions = linked.doc.dimensions.filter(d => d.kind === 'Offset');
    assert.equal(dimensions.length, 1);
    const dimension = dimensions[0], derived = linked.doc.entities.find(e => !before.entities.some(old => old.id === e.id));
    assert.ok(derived);
    assert.deepEqual(dimension.referenceKeys, relations[0].referenceKeys);
    const assertGap = (doc, gap) => {
      assert.deepEqual(doc.entities.slice(0, count), before.entities, 'Offset gap edit must preserve source geometry exactly');
      assert.deepEqual(doc.entities.map(e => e.id), linked.doc.entities.map(e => e.id));
      assert.deepEqual(doc.constraints, linked.doc.constraints);
      assert.equal(doc.dimensions.length, 1);
      assert.deepEqual(doc.dimensions[0], {...dimension, value: gap});
      assert.equal(doc.dimensions[0].isDriving, true);
      const copy = doc.entities.find(e => e.id === derived.id);
      if (shape === 'circle') {
        assert.equal(copy.kind, 'circle'); assert.deepEqual(copy.center, before.entities[0].center);
        near(copy.radius, 3 + gap, 'Circle offset radius');
      } else {
        assert.equal(copy.kind, 'polyline'); assert.equal(copy.vertices.length, 5);
        assert.deepEqual(copy.vertices[0], copy.vertices.at(-1));
        near(Math.min(...copy.vertices.map(p => p.x)), -4 - gap, 'Rectangle offset left');
        near(Math.max(...copy.vertices.map(p => p.x)), 18 + gap, 'Rectangle offset right');
        near(Math.min(...copy.vertices.map(p => p.y)), -2 - gap, 'Rectangle offset bottom');
        near(Math.max(...copy.vertices.map(p => p.y)), 8 + gap, 'Rectangle offset top');
      }
    };
    assertGap(linked.doc, 1);
    const editGap = async (previous, value, suffix) => {
      const input = page.locator(`input[data-dimension-id="${dimension.id}"]`);
      await input.dblclick(); await input.fill(String(value)); await input.press('Enter');
      await expect(input).toHaveAttribute('data-dimension-value', String(value));
      const evidence = await capture(`${name}-${suffix}-intent`);
      await canvas.press('Escape');
      const saved = await download(`${name}-${suffix}`);
      if ((await input.getAttribute('class'))?.includes('unsatisfied')) {
        await expect(input).toBeVisible();
        assert.deepEqual(saved.doc.entities, previous.entities, 'Refused gap edit preserves accepted geometry');
        assert.equal(saved.doc.dimensions[0].value, value, 'Failed requested gap remains visible');
        assert.match(evidence.status, /geometry unchanged/i);
        assert.fail(`Unexpected offset refusal with explicit Unsatisfied intent: ${evidence.status}`);
      }
      assertGap(saved.doc, value);
      return saved;
    };
    const edited = await editGap(linked.doc, 2, 'edited');
    await historyReopen(linked.doc, edited, name);
    await editGap(edited.doc, 1.5, 'reopened-edited');
    return {sourceIds: before.entities.map(e => e.id), derivedId: derived.id, relation: relations[0], dimensionId: dimension.id};
  });
  for (const kind of ['Rectangular', 'Radial']) await run(`pattern-${kind.toLowerCase()}-source-follow`, async () => {
    const name = `pattern-${kind.toLowerCase()}-source-follow`;
    await load(circleFixture(1), 1); await autoSnap(false);
    const seed = (await download(`${name}-seed`)).doc.entities[0];
    const body = {x: 10 + Math.cos(.6), y: 10 + Math.sin(.6)};
    await pick(body, key => key === seed.id);
    const at = await screen(body); await page.mouse.click(at.x, at.y, {button: 'right'});
    await page.getByRole('dialog', {name: 'Drawing context commands'}).getByRole('button', {name: 'Pattern', exact: true}).click();
    const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
    await expect(editor).toBeVisible();
    await editor.getByLabel('Pattern layout', {exact: true}).selectOption(kind);
    const fill = async (label, value) => {
      await editor.getByLabel(label, {exact: true}).fill(String(value)); await page.waitForTimeout(150);
    };
    if (kind === 'Radial') {
      await fill('Pattern instances', 4); await fill('Pattern center X', 10); await fill('Pattern center Y', 6);
      await editor.getByLabel('Full circle', {exact: true}).check();
    } else {
      await fill('Pattern columns', 2); await fill('Pattern rows', 2);
      await fill('Pattern X pitch', 5); await fill('Pattern Y pitch', 4);
    }
    await editor.getByRole('button', {name: 'Confirm', exact: true}).click();
    await expect(editor).toBeHidden(); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await canvas.press('Escape'); await tool('Fit extents');
    const created = await download(`${name}-created`), patterns = created.doc.metadata.patterns;
    assert.equal(patterns.length, 1); assert.equal(patterns[0].kind, kind);
    const pattern = patterns[0];
    assert.deepEqual(pattern.sourceEntityIds, [seed.id]); assert.equal(pattern.members.length, 3);
    const assertFamily = doc => {
      assert.deepEqual(doc.metadata.patterns, patterns);
      assert.deepEqual(doc.entities.map(e => e.id), created.doc.entities.map(e => e.id));
      assert.deepEqual(doc.constraints, created.doc.constraints); assert.deepEqual(doc.dimensions, created.doc.dimensions);
      const source = doc.entities.find(e => e.id === seed.id);
      assert.equal(source.radius, seed.radius);
      for (const member of pattern.members) {
        assert.equal(member.sourceEntityId, source.id);
        const copy = doc.entities.find(e => e.id === member.entityId);
        assert.equal(copy.kind, 'circle'); assert.equal(copy.radius, source.radius);
        let expected;
        if (kind === 'Radial') {
          const angle = member.column * 2 * Math.PI / pattern.count;
          const dx = source.center.x - pattern.center.x, dy = source.center.y - pattern.center.y;
          expected = {x: pattern.center.x + dx * Math.cos(angle) - dy * Math.sin(angle),
            y: pattern.center.y + dx * Math.sin(angle) + dy * Math.cos(angle)};
        } else {
          const angle = pattern.angleDegrees * Math.PI / 180, x = member.column * pattern.xPitch, y = member.row * pattern.yPitch;
          expected = {x: source.center.x + x * Math.cos(angle) - y * Math.sin(angle),
            y: source.center.y + x * Math.sin(angle) + y * Math.cos(angle)};
        }
        samePoint(copy.center, expected, 'Generated center follows pattern source');
      }
      return source;
    };
    assertFamily(created.doc);
    const moveSource = async (previous, delta, suffix) => {
      const source = assertFamily(previous), target = {x: source.center.x + delta.x, y: source.center.y + delta.y};
      const a = await hover(source.center, key => key?.startsWith(`${seed.id}|point|center|`)), b = await screen(target);
      await page.mouse.down();
      try {
        await page.mouse.move(b.x, b.y, {steps: 12});
        await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
        await expect.poll(async () => {
          const center = (await canvas.getAttribute('data-first-center')).split(',').map(Number);
          return Math.hypot(center[0] - target.x, center[1] - target.y);
        }, {message: 'PlaneGCS moves pattern source during held preview'}).toBeLessThan(1e-4);
        await capture(`${name}-${suffix}-held`);
      } finally { await page.mouse.up(); }
      await expect(page.locator('.dxfer-command-bar')).toContainText(/Moved geometry with PlaneGCS/);
      await canvas.press('Escape'); const saved = await download(`${name}-${suffix}`);
      samePoint(assertFamily(saved.doc).center, target, 'Released pattern source follows pointer', 1e-4);
      return saved;
    };
    const moved = await moveSource(created.doc, {x: .8, y: .5}, 'moved');
    await historyReopen(created.doc, moved, name);
    await moveSource(moved.doc, {x: -.3, y: .4}, 'reopened-moved');
    return {kind, patternId: pattern.id, sourceId: seed.id, members: pattern.members};
  });
  for (const kind of ['ellipse', 'spline']) await run(`native-${kind}-create-edit`, async () => {
    const name = `native-${kind}-create-edit`;
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await autoSnap(false);
    const empty = (await download(`${name}-empty`)).doc;
    await tool(kind === 'ellipse' ? 'Ellipse' : 'Spline');
    const bounds = await canvas.boundingBox();
    const offsets = kind === 'ellipse' ? [[0, 0], [120, 0], [0, -60]] : [[-130, 20], [-50, -60], [40, 30], [130, -30]];
    const clicks = offsets.map(([x, y]) => ({x: Math.round(bounds.x + bounds.width * .55 + x),
      y: Math.round(bounds.y + bounds.height * .5 + y)}));
    const expectedPoints = [];
    for (let i = 0; i < clicks.length; i++) {
      const p = clicks[i];
      expectedPoints.push(await canvas.evaluate((c, p) => ({
        x: (p.x - c.getBoundingClientRect().left - +c.dataset.offsetX) / +c.dataset.scale,
        y: (+c.dataset.offsetY - p.y + c.getBoundingClientRect().top) / +c.dataset.scale
      }), p));
      await page.mouse.move(p.x, p.y);
      if (kind === 'spline' && i === clicks.length - 1) await page.mouse.dblclick(p.x, p.y);
      else await page.mouse.click(p.x, p.y);
      if (i < clicks.length - 1) await expect(canvas).toHaveAttribute('data-tool-draft-point-count', String(i + 1));
    }
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
    await canvas.press('Escape'); await tool('Fit extents');
    const created = await download(`${name}-created`), entity = created.doc.entities[0];
    assert.equal(entity.kind, kind, 'Creation preserves the native entity type');
    assert.deepEqual(created.doc.constraints, []); assert.deepEqual(created.doc.dimensions, []);
    if (kind === 'ellipse') {
      samePoint(entity.center, expectedPoints[0], 'Ellipse center from actual click', 1e-4);
      samePoint(entity.majorAxisEndPoint, {x: expectedPoints[1].x - expectedPoints[0].x, y: 0}, 'Native major axis', 1e-4);
      near(entity.minorRadiusRatio, .5, 'Native ellipse ratio', 1e-4);
    } else {
      assert.equal(entity.fitPoints.length, 4);
      entity.fitPoints.forEach((p, i) => samePoint(p, expectedPoints[i], 'Native spline fit point', 1e-4));
      assert.ok(entity.degree > 0 && entity.controlPoints.length > 1 && entity.knots.length > 0);
    }
    await historyReopen(empty, created, `${name}-creation`);
    const dragNative = async (before, delta, suffix) => {
      const source = before.entities[0], start = kind === 'ellipse' ? source.center : source.fitPoints[0];
      const target = {x: start.x + delta.x, y: start.y + delta.y};
      await hover(start, key => key?.startsWith(`${source.id}|point|${kind === 'ellipse' ? 'center' : 'start'}|`)
        || kind === 'spline' && key?.startsWith(`${source.id}|point|fit-0|`));
      const destination = await screen(target);
      await page.mouse.down();
      try {
        await page.mouse.move(destination.x, destination.y, {steps: 12});
        await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
        await expect.poll(async () => {
          const point = kind === 'ellipse'
            ? await canvas.evaluate(c => { const [x, y] = c.dataset.firstCenter.split(',').map(Number); return {x, y}; })
            : await firstStart();
          return Math.hypot(point.x - target.x, point.y - target.y);
        }, {message: `Native ${kind} moves during real PlaneGCS held preview`}).toBeLessThan(1e-4);
        await capture(`${name}-${suffix}-held`);
      } finally { await page.mouse.up(); }
      await expect(page.locator('.dxfer-command-bar')).toContainText(/Moved geometry with PlaneGCS/);
      await canvas.press('Escape'); const saved = await download(`${name}-${suffix}`), moved = saved.doc.entities[0];
      assert.equal(saved.doc.entities.length, 1); assert.equal(moved.id, source.id); assert.equal(moved.kind, kind);
      assert.deepEqual(saved.doc.constraints, before.constraints); assert.deepEqual(saved.doc.dimensions, before.dimensions);
      if (kind === 'ellipse') {
        samePoint(moved.center, target, 'Released ellipse center', 1e-4);
        assert.deepEqual({...moved, center: source.center}, source, 'Center drag preserves native axes and ellipse metadata');
      } else {
        samePoint(moved.fitPoints[0], target, 'Released spline fit point', 1e-4);
        assert.deepEqual(moved.fitPoints.slice(1), source.fitPoints.slice(1), 'Other fit points remain unchanged');
        assert.equal(moved.degree, source.degree); assert.equal(moved.isClosed, source.isClosed);
        assert.equal(moved.controlPoints.length, source.controlPoints.length);
        assert.ok(moved.controlPoints.every(p => Number.isFinite(p.x) && Number.isFinite(p.y)));
        assert.ok(moved.knots.every(Number.isFinite));
      }
      return saved;
    };
    const moved = await dragNative(created.doc, {x: .7, y: .4}, 'moved');
    await historyReopen(created.doc, moved, `${name}-edit`);
    await dragNative(moved.doc, {x: -.2, y: .3}, 'reopened-moved');
    return {entityId: entity.id, kind, creationPoints: expectedPoints};
  });
  for (const midpoint of [false, true]) for (const automatic of [false, true]) {
    const name = `explicit-drag-snap-${midpoint ? 'midpoint' : 'endpoint'}-auto-${automatic}`;
    await run(name, async () => {
      await load(fixture([[0, 0, 3, 1], [8, 2, 12, 2]]), 2); await autoSnap(automatic);
      const before = (await download(`${name}-before`)).doc;
      const [moving, anchor] = before.entities, destination = midpoint ? middle(anchor) : anchor.start;
      await hover(destination, key => key?.startsWith(`${anchor.id}|point|${midpoint ? 'mid' : 'start'}|`));
      await hover(moving.end, key => key?.startsWith(`${moving.id}|point|end|`));
      const to = await screen(destination);
      let held;
      await page.mouse.down();
      try {
        await page.mouse.move(to.x, to.y, {steps: 12});
        await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
        await expect.poll(() => canvas.getAttribute('data-snap-cue'), {
          message: 'Accepted hard snap has a cue even when the target moves during solving'
        }).toMatch(automatic ? /Pending:.*(?:Coincident|Midpoint)/ : /auto constraints off/i);
        held = await capture(`${name}-held`);
      } finally { await page.mouse.up(); }
      await expect(page.locator('.dxfer-command-bar')).toContainText(/Moved geometry with PlaneGCS/);
      const released = await capture(`${name}-released`);
      await canvas.press('Escape'); const saved = await download(`${name}-after`);
      assert.deepEqual(saved.doc.entities.map(e => e.id), before.entities.map(e => e.id));
      assert.deepEqual(saved.doc.dimensions, before.dimensions);
      const [changed, target] = saved.doc.entities;
      samePoint(changed.end, midpoint ? middle(target) : target.start, 'Hard snap holds against the solved target');
      const [hx, hy] = held.dataset.firstScreenEnd.split(',').map(Number);
      samePoint(changed.end, {x: (hx - +held.dataset.offsetX) / +held.dataset.scale,
        y: (+held.dataset.offsetY - hy) / +held.dataset.scale}, 'Preview/release selected point agrees');
      assert.ok(Math.hypot(changed.end.x - moving.end.x, changed.end.y - moving.end.y) > .1, 'Drag genuinely moves selected point');
      if (automatic) {
        assert.equal(saved.doc.constraints.length, 1);
        const relation = saved.doc.constraints[0];
        assert.equal(relation.kind, midpoint ? 'Midpoint' : 'Coincident'); assert.equal(relation.state, 'Satisfied');
        assert.deepEqual([...relation.referenceKeys].sort(),
          [`${moving.id}:end`, midpoint ? anchor.id : `${anchor.id}:start`].sort());
        assert.match(released.status, /Added (?:Coincident|Midpoint) constraint/i);
      } else {
        assert.deepEqual(saved.doc.constraints, before.constraints, 'AutoSnap OFF strips the temporary relationship only');
        assert.doesNotMatch(released.status, /Added .*constraint/i);
      }
      await historyReopen(before, saved, name);
      return {automatic, kind: midpoint ? 'Midpoint' : 'Coincident', previewEngine: held.dataset.planeGcsPreviewEngine,
        cue: held.dataset.snapCue, constraints: saved.doc.constraints, solvedTarget: target};
    });
  }
  assert.ok(results.length); assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
