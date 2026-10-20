// Real HTTP/UI only. Observation never replaces interop, schedules solver replies, or mutates the drawing.
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-planegcs-sustained-drag';
await mkdir(output, {recursive: true});
const evidence = {url, startedAt: new Date().toISOString(), passed: false, browserModules: [], errors: [], consoleErrors: []};
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
const rectangle = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  ...[[-4, -2, -4, 8], [-4, 8, 18, 8], [18, 8, 18, -2], [18, -2, -4, -2]].flatMap(([x, y, ex, ey], i) =>
    ['0', 'LINE', '5', String(100 + i), '8', '0', '10', x, '20', y, '11', ex, '21', ey]),
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions, metadata: doc.metadata});
const samePoint = (a, b, label, tolerance = 1e-6) =>
  assert.ok(Math.hypot(a.x - b.x, a.y - b.y) <= tolerance, `${label}: ${JSON.stringify(a)} != ${JSON.stringify(b)}`);

const browser = await chromium.launch({headless: true});
let page;
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(12000);
  page.on('dialog', d => d.accept());
  page.on('pageerror', e => evidence.errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error') evidence.consoleErrors.push(m.text()); });
  page.on('response', async response => {
    if (!new URL(response.url()).pathname.endsWith('/drawingCanvas.js')) return;
    try {
      evidence.browserModules.push({url: response.url(), sha256: createHash('sha256').update(await response.body()).digest('hex')});
    } catch (error) { evidence.consoleErrors.push(`Module fingerprint: ${error.message}`); }
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  evidence.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(evidence.build?.includes(process.env.DXFER_EXPECTED_REVISION));
  console.log(`${evidence.startedAt}; ${evidence.build}; real PlaneGCS sustained-motion test`);
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150); };
  const menu = async name => {
    await tool('Open application menu');
    await page.getByRole('button', {name: 'File', exact: true}).hover();
    await tool(name);
  };
  const load = async text => {
    await page.keyboard.press('Escape'); await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'sustained-drag.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const screen = point => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), point);
  const firstStart = () => canvas.evaluate(c => {
    const [x, y] = c.dataset.firstScreenStart.split(',').map(Number);
    return {x: (x - +c.dataset.offsetX) / +c.dataset.scale, y: (+c.dataset.offsetY - y) / +c.dataset.scale};
  });
  const hover = async (point, id) => {
    const p = await screen(point);
    await expect.poll(async () => {
      await page.mouse.move(p.x + 24, p.y + 24); await page.mouse.move(p.x, p.y, {steps: 2});
      return await canvas.getAttribute('data-hovered-id');
    }).toMatch(new RegExp(`^${id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\|point\\|start\\|`));
  };
  await load(rectangle);
  const seed = await download('seed');
  const doc = structuredClone(seed.doc);
  doc.entities.forEach((e, i) => doc.constraints.push(
    {id: `corner-${i}`, kind: 'Coincident', referenceKeys: [`${e.id}:end`, `${doc.entities[(i + 1) % 4].id}:start`], state: 'Satisfied'},
    {id: `axis-${i}`, kind: i % 2 ? 'Horizontal' : 'Vertical', referenceKeys: [e.id], state: 'Satisfied'}));
  await load(withNative(seed.text, doc));
  await menu('Settings');
  await page.getByRole('checkbox', {name: 'Automatic snap constraints'}).setChecked(false);
  await tool('Close settings');
  const before = (await download('before')).doc;

  // A read-only RAF trace counts displayed geometry changes, not queued requests or repeated dataset writes.
  await canvas.evaluate(c => {
    const trace = {phase: 'idle', frames: [], moves: [], events: [], held: false, running: true};
    window.__planeGcsSustainedTrace = trace;
    c.addEventListener('pointermove', e => {
      if (trace.held) trace.moves.push({at: performance.now(), x: e.clientX, y: e.clientY, phase: trace.phase});
    }, true);
    c.addEventListener('pointerdown', () => { trace.held = true; }, true);
    window.addEventListener('pointerup', () => { trace.held = false; }, true);
    window.addEventListener('keydown', e => {
      if (e.key === 'Escape' && trace.held) {
        trace.phase = 'escaped';
        trace.events.push({kind: 'escape', at: performance.now(), callback: c.dataset.lastDotnetCallback});
      }
    }, true);
    const sample = () => {
      if (!trace.running) return;
      const [x, y] = (c.dataset.firstScreenStart ?? '').split(',').map(Number);
      trace.frames.push({at: performance.now(), phase: trace.phase, held: trace.held, moves: trace.moves.length,
        point: {x: (x - +c.dataset.offsetX) / +c.dataset.scale, y: (+c.dataset.offsetY - y) / +c.dataset.scale},
        engine: c.dataset.planeGcsPreviewEngine, reason: c.dataset.planeGcsPreviewReason});
      requestAnimationFrame(sample);
    };
    requestAnimationFrame(sample);
  });
  const phase = name => page.evaluate(name => { window.__planeGcsSustainedTrace.phase = name; }, name);
  const trace = () => page.evaluate(() => structuredClone(window.__planeGcsSustainedTrace));
  const motion = async (from, to, duration = 1900, count = 48) => {
    const a = await screen(from), b = await screen(to), started = Date.now();
    for (let i = 1; i <= count; i++) {
      await page.mouse.move(a.x + (b.x - a.x) * i / count, a.y + (b.y - a.y) * i / count);
      const delay = started + duration * i / count - Date.now();
      if (delay > 0) await page.waitForTimeout(delay);
    }
  };
  await hover(before.entities[0].start, before.entities[0].id);
  await page.mouse.down(); await phase('moving');
  await motion(before.entities[0].start, {x: -2, y: -1.2});
  await phase('release');
  const movingTrace = await trace();
  const movingEvents = movingTrace.moves.filter(m => m.phase === 'moving');
  assert.ok(movingEvents.length >= 40, 'Sustained drag must send actual pointer moves');
  const firstMove = movingEvents[0].at, lastMove = movingEvents.at(-1).at;
  assert.ok(lastMove - firstMove >= 1500 && lastMove - firstMove <= 3500, 'Motion must span approximately two seconds');
  const accepted = [];
  for (const frame of movingTrace.frames.filter(f => f.phase === 'moving' && f.held && f.engine === 'PlaneGCS'
    && f.at > firstMove && f.at < lastMove)) {
    const previous = accepted.at(-1)?.point ?? before.entities[0].start;
    if (Math.hypot(frame.point.x - previous.x, frame.point.y - previous.y) > 1e-6) accepted.push(frame);
  }
  evidence.movingPreviewFrames = accepted;
  assert.ok(accepted.length >= 3, `Need multiple accepted previews BEFORE pointer stops; observed ${accepted.length}`);
  assert.ok(new Set(accepted.map(f => f.moves)).size >= 3, 'Accepted frames must span distinct pointer events');
  console.log(`Accepted ${accepted.length} distinct real preview frames during ${Math.round(lastMove - firstMove)}ms of motion`);

  // Release at a new actual pointer location immediately, without awaiting its preview.
  const finalScreen = await screen({x: -1, y: -0.5});
  await page.mouse.move(finalScreen.x, finalScreen.y);
  await page.mouse.up();
  const releasedTrace = await trace();
  const lastPointer = releasedTrace.moves.at(-1);
  assert.equal(lastPointer.phase, 'release');
  const requested = await canvas.evaluate((c, p) => ({
    x: (p.x - c.getBoundingClientRect().left - +c.dataset.offsetX) / +c.dataset.scale,
    y: (+c.dataset.offsetY - (p.y - c.getBoundingClientRect().top)) / +c.dataset.scale
  }), lastPointer);
  evidence.releaseRequested = requested;
  assert.ok(Math.hypot(requested.x - accepted.at(-1).point.x, requested.y - accepted.at(-1).point.y) > 0.5,
    'Newest release intent must differ materially from the sustained-motion preview');
  await expect(page.locator('.dxfer-command-bar')).toContainText(/Moved geometry with PlaneGCS/);
  await page.screenshot({path: `${output}/released.png`});
  await canvas.press('Escape');
  const saved = await download('released');
  samePoint(saved.doc.entities[0].start, requested, 'Release commits newest actual pointer intent', 1e-4);
  assert.deepEqual(saved.doc.constraints, before.constraints);
  assert.deepEqual(saved.doc.dimensions, before.dimensions);
  assert.deepEqual(saved.doc.entities.map(e => e.id), before.entities.map(e => e.id));
  saved.doc.entities.forEach((edge, i) => {
    samePoint(edge.end, saved.doc.entities[(i + 1) % 4].start, 'Corner remains welded');
    assert.ok(Math.abs(edge.start[i % 2 ? 'y' : 'x'] - edge.end[i % 2 ? 'y' : 'x']) < 1e-6, 'H/V remains satisfied');
    assert.ok(Math.hypot(edge.end.x - edge.start.x, edge.end.y - edge.start.y) > 1e-6, 'Rectangle remains noncollapsed');
  });

  await hover(saved.doc.entities[0].start, saved.doc.entities[0].id);
  await page.mouse.down(); await phase('cancel-motion');
  await motion(saved.doc.entities[0].start, {x: -2.7, y: -1.4}, 700, 20);
  await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'PlaneGCS');
  const cancelTarget = await screen({x: -3.2, y: -1.6});
  await page.mouse.move(cancelTarget.x, cancelTarget.y);
  await page.keyboard.press('Escape');
  await page.mouse.up();
  await expect.poll(async () => {
    const p = await firstStart(), expected = saved.doc.entities[0].start;
    return Math.hypot(p.x - expected.x, p.y - expected.y);
  }).toBeLessThan(1e-6);
  await phase('after-escape');
  await page.waitForTimeout(1500);
  evidence.trace = await trace();
  const afterEscape = evidence.trace.frames.filter(f => f.phase === 'after-escape');
  assert.ok(afterEscape.length >= 20, 'Observe multiple frames after Escape for late repaint');
  afterEscape.forEach(frame => samePoint(frame.point, saved.doc.entities[0].start, 'No stale preview after Escape'));
  evidence.escape = evidence.trace.events.find(e => e.kind === 'escape');
  assert.ok(evidence.escape, 'Escape must occur during an actual held drag');
  evidence.escapeCoverage = 'Real requests during motion, followed immediately by Escape; no artificial response delay or guaranteed pending-response ordering.';
  await page.screenshot({path: `${output}/escaped.png`});
  assert.deepEqual(content((await download('escaped')).doc), content(saved.doc), 'Escape preserves exact committed native drawing');
  await tool('Undo');
  assert.deepEqual(content((await download('undo')).doc), content(before), 'Canceled drag adds no history entry');
  await tool('Redo');
  assert.deepEqual(content((await download('redo')).doc), content(saved.doc));
  await load(saved.text);
  const reopened = (await download('reopened')).doc;
  const expectedReopened = structuredClone(saved.doc);
  // Reimport records the saved file's provenance; all other native content remains exact.
  expectedReopened.metadata.sourceSha256 = createHash('sha256').update(saved.text).digest('hex');
  assert.deepEqual(content(reopened), content(expectedReopened));
  assert.deepEqual(evidence.errors, [], 'No browser page errors');
  evidence.passed = true;
  console.log('PASS real-engine-sustained-drag-release-escape');
} catch (error) {
  evidence.error = error.stack;
  console.error(`FAIL real-engine-sustained-drag-release-escape: ${error.message}`);
  if (page) await page.screenshot({path: `${output}/failure.png`}).catch(() => {});
  process.exitCode = 1;
} finally {
  if (page) {
    evidence.trace = await page.evaluate(() => {
      const trace = window.__planeGcsSustainedTrace;
      if (trace) trace.running = false;
      return trace;
    }).catch(() => evidence.trace);
    evidence.dataset = await page.locator('canvas.drawing-canvas').evaluate(c => ({...c.dataset})).catch(() => null);
    evidence.status = await page.locator('.dxfer-command-bar').innerText().catch(() => null);
  }
  evidence.finishedAt = new Date().toISOString();
  await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2));
  await browser.close();
}
