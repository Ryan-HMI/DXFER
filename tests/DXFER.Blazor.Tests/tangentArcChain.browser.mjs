import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url, 'Supply the coordinated candidate URL explicitly');
assert.ok(['127.0.0.1', 'localhost', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-tangent-arc-chain';
await mkdir(output, {recursive: true});
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'native document retained'); return JSON.parse(Buffer.from(hex, 'hex'));
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  layers: doc.metadata.layers, entityStyles: doc.metadata.entityStyles, patterns: doc.metadata.patterns});
const close = (a, b, label) => assert.ok(Math.abs(a - b) < 1e-6, `${label}: ${a} != ${b}`);
const pointClose = (a, b, label) => { close(a.x, b.x, `${label} X`); close(a.y, b.y, `${label} Y`); };
const sweep = arc => ((arc.endAngleDegrees - arc.startAngleDegrees) % 360 + 360) % 360;
const pointOnArc = (arc, degrees) => ({x: arc.center.x + arc.radius * Math.cos(degrees * Math.PI / 180),
  y: arc.center.y + arc.radius * Math.sin(degrees * Math.PI / 180)});
const owner = key => key.split(/[:|]/)[0];
const related = (doc, kind, first, second) => doc.constraints.some(c => c.kind.toLowerCase() === kind.toLowerCase()
  && c.referenceKeys.some(k => owner(k) === first) && c.referenceKeys.some(k => owner(k) === second));

function assertChain(doc, source, degrees, automatic, keyedRadius, hasFollowingLine) {
  const arc = doc.entities.find(e => e.kind === 'arc'); assert.ok(arc);
  assert.equal(doc.entities.length, hasFollowingLine ? 3 : 2);
  const incoming = doc.entities.find(e => e.id === source.id); assert.ok(incoming);
  pointClose(incoming.start, source.start, 'incoming line start preserved');
  pointClose(incoming.end, source.end, 'incoming line end preserved');
  assert.deepEqual({...incoming, start: source.start, end: source.end}, source, 'incoming line identity and metadata preserved');
  close(sweep(arc), degrees, 'cardinal sweep');
  if (keyedRadius !== null) close(arc.radius, keyedRadius, 'keyed radius');
  else assert.ok(Math.abs(arc.radius - 2) < .15, `pointer radius remains approximately 2: ${arc.radius}`);
  const start = pointOnArc(arc, arc.startAngleDegrees), end = pointOnArc(arc, arc.endAngleDegrees);
  pointClose(start, source.end, 'incoming line and arc coincide');
  const tangent = (line, contact) => {
    const dx = line.end.x - line.start.x, dy = line.end.y - line.start.y, length = Math.hypot(dx, dy);
    assert.ok(length > 1e-6);
    close((dx * (contact.x - arc.center.x) + dy * (contact.y - arc.center.y)) / (length * arc.radius), 0, 'line tangent to arc');
  };
  tangent(source, start);
  const radialKind = degrees === 90 ? 'Horizontal' : degrees === 180 ? 'Vertical' : null;
  const quadrant = doc.constraints.filter(c => ['Horizontal', 'Vertical'].includes(c.kind)
    && c.referenceKeys.includes(`${arc.id}:center`) && c.referenceKeys.includes(`${arc.id}:end`));
  assert.equal(quadrant.length, automatic && radialKind ? 1 : 0, 'only an accepted quadrant persists its endpoint-to-center relation');
  if (automatic && radialKind) assert.equal(quadrant[0].kind, radialKind);
  if (automatic) {
    assert.ok(related(doc, 'Coincident', source.id, arc.id), 'incoming permanent coincidence');
    assert.ok(related(doc, 'Tangent', source.id, arc.id), 'incoming permanent tangency');
  } else assert.deepEqual(doc.constraints, [], 'Auto OFF creates no inferred constraints');
  if (hasFollowingLine) {
    const next = doc.entities.find(e => e.kind === 'line' && e.id !== source.id); assert.ok(next);
    pointClose(next.start, end, 'outgoing line begins at arc endpoint'); tangent(next, end);
    if (automatic) {
      assert.ok(related(doc, 'Coincident', next.id, arc.id), 'outgoing permanent coincidence');
      assert.ok(related(doc, 'Tangent', next.id, arc.id), 'outgoing permanent tangency');
    }
  }
  const dimensions = doc.dimensions.filter(d => d.isDriving && d.referenceKeys.some(k => owner(k) === arc.id));
  assert.equal(dimensions.length, keyedRadius === null ? 0 : 1);
  if (keyedRadius !== null) { assert.equal(dimensions[0].kind, 'Radius'); close(dimensions[0].value, keyedRadius, 'saved radius dimension'); }
  return arc;
}

const specs = [
  {name: 'quarter-auto-on', degrees: 90, automatic: true, keyedRadius: null},
  {name: 'half-keyed-radius-history', degrees: 180, automatic: true, keyedRadius: 2.125},
  {name: 'quarter-auto-off', degrees: 90, automatic: false, keyedRadius: null},
  ...[90, 180].flatMap(degrees => [true, false].map(automatic => ({
    name: `${degrees === 90 ? 'quarter' : 'half'}-keyed-enter-auto-${automatic ? 'on' : 'off'}`,
    degrees, automatic, keyedRadius: 3, enterWithoutMove: true
  }))),
  {name: 'quarter-keyed-radius-reaim-off-quadrant', degrees: 90, finalDegrees: 60,
    automatic: true, keyedRadius: 3, enterWithoutMove: true, reaimAfterKeying: true}
];
const selectedSpecs = process.env.DXFER_TANGENT_CASE
  ? specs.filter(spec => spec.name === process.env.DXFER_TANGENT_CASE) : specs;
assert.ok(selectedSpecs.length, 'DXFER_TANGENT_CASE must name an existing case');
const evidence = {requestedCases: selectedSpecs.map(spec => spec.name), cases: [], previews: [],
  keyboardCommits: [], exports: [], errors: [], passed: false};
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
  const command = name => page.getByRole('button', {name, exact: true});
  const menu = async name => {
    await command('Open application menu').click(); await command('File').hover(); await command(name).click();
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); evidence.exports.push(name); return {text, doc: decode(text)};
  };
  const automatic = async enabled => {
    await menu('Settings');
    const setting = page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true});
    await setting.setChecked(enabled); await expect(setting).toBeChecked({checked: enabled});
    await command('Close settings').click();
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const move = async point => {
    const p = await screen(point);
    assert.ok(await canvas.evaluate((c, p) => document.elementFromPoint(p.x, p.y) === c, p), 'canvas target unobscured');
    await page.mouse.move(p.x, p.y); return p;
  };
  const click = async point => { const p = await move(point); await page.mouse.click(p.x, p.y); };
  const lineMode = async () => {
    await expect(canvas).toHaveAttribute('data-active-tool', 'line');
    await expect(command('Line')).toHaveAttribute('aria-pressed', 'true');
    await expect(canvas).toHaveAttribute('data-tool-draft-point-count', '1');
  };
  const decimal = async (input, value) => {
    await input.focus(); await input.press('ControlOrMeta+A'); let typed = '';
    for (const char of value) {
      await page.keyboard.press(char); typed += char;
      if (!typed.endsWith('.')) await expect(input).toHaveValue(typed);
      await page.waitForTimeout(60);
    }
    await expect(input).toHaveValue(value);
  };
  // Observe real canvas curve strokes, forwarding every call unchanged. This proves
  // the pre-click geometry, rather than inferring a snap from committed output.
  await canvas.evaluate(c => {
    const ctx = c.getContext('2d'), originalClear = ctx.clearRect;
    const originalBegin = ctx.beginPath, originalMove = ctx.moveTo, originalLine = ctx.lineTo, originalStroke = ctx.stroke;
    let points = [];
    window.__tangentArcPointerEvents = {moves: 0, downs: 0, ups: 0};
    for (const [type, key] of [['pointermove', 'moves'], ['pointerdown', 'downs'], ['pointerup', 'ups']])
      document.addEventListener(type, () => window.__tangentArcPointerEvents[key]++, true);
    window.__tangentArcPaint = {frame: 0, paths: []};
    ctx.clearRect = function (...args) {
      window.__tangentArcPaint = {frame: window.__tangentArcPaint.frame + 1, paths: []};
      return originalClear.apply(this, args);
    };
    ctx.beginPath = function (...args) { points = []; return originalBegin.apply(this, args); };
    ctx.moveTo = function (x, y) { points.push({x, y}); return originalMove.call(this, x, y); };
    ctx.lineTo = function (x, y) { points.push({x, y}); return originalLine.call(this, x, y); };
    ctx.stroke = function (...args) {
      if (points.length >= 5) window.__tangentArcPaint.paths.push(points.slice());
      return originalStroke.apply(this, args);
    };
  });
  const preview = async (degrees, name, toleranceDegrees = .0000005) => {
    const painted = () => page.evaluate(() => {
      const c = document.querySelector('canvas.drawing-canvas'), scale = +c.dataset.scale;
      for (const points of window.__tangentArcPaint.paths) {
        const a = points[0], b = points[Math.floor(points.length / 2)], z = points.at(-1);
        const determinant = 2 * (a.x * (b.y - z.y) + b.x * (z.y - a.y) + z.x * (a.y - b.y));
        if (Math.abs(determinant) < 1e-8) continue;
        const aa = a.x * a.x + a.y * a.y, bb = b.x * b.x + b.y * b.y, zz = z.x * z.x + z.y * z.y;
        const center = {x: (aa * (b.y - z.y) + bb * (z.y - a.y) + zz * (a.y - b.y)) / determinant,
          y: (aa * (z.x - b.x) + bb * (a.x - z.x) + zz * (b.x - a.x)) / determinant};
        const radius = Math.hypot(a.x - center.x, a.y - center.y);
        if (radius / scale < 1 || radius / scale > 4) continue;
        if (!points.every(p => Math.abs(Math.hypot(p.x - center.x, p.y - center.y) - radius) < 1e-5)) continue;
        let radians = 0;
        for (let i = 1; i < points.length; i++) {
          let delta = Math.atan2(points[i].y - center.y, points[i].x - center.x)
            - Math.atan2(points[i - 1].y - center.y, points[i - 1].x - center.x);
          if (delta > Math.PI) delta -= 2 * Math.PI;
          if (delta < -Math.PI) delta += 2 * Math.PI;
          radians += delta;
        }
        return {center, radius, worldRadius: radius / scale, sweepDegrees: Math.abs(radians) * 180 / Math.PI,
          pointCount: points.length, first: a, last: z};
      }
      return null;
    });
    await expect.poll(async () => {
      const arc = await painted(); return arc ? Math.abs(arc.sweepDegrees - degrees) : Infinity;
    }).toBeLessThan(toleranceDegrees);
    const arc = await painted(); assert.ok(arc);
    evidence.previews.push({name, degrees, arc, at: await page.evaluate(() => performance.now())});
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
    await page.screenshot({path: `${output}/${name}-preview.png`}); return arc;
  };

  // Record the installed toolbar artwork separately so geometry failures are not
  // hidden behind a missing icon integration.
  const iconButton = await command('Tangent arc').count() ? command('Tangent arc') : command('Line');
  const icon = iconButton.locator('svg'); await expect(icon).toBeVisible();
  evidence.icon = await icon.evaluate(svg => ({viewBox: svg.getAttribute('viewBox'), markup: svg.innerHTML,
    paths: [...svg.querySelectorAll('path')].map(p => p.getAttribute('d'))}));
  evidence.icon.quarterArcRendered = evidence.icon.paths.some(d => /[AaCcQq]/.test(d));
  await iconButton.screenshot({path: `${output}/line-quarter-arc-icon.png`});

  for (const spec of selectedSpecs) {
    evidence.current = spec.name; await automatic(spec.automatic); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await expect(command('Undo')).toBeDisabled();
    const empty = await download(`${spec.name}-empty`);
    await canvas.press('Escape');
    await expect(canvas).toHaveAttribute('data-active-tool', 'select');
    await expect(command('Line')).not.toHaveAttribute('aria-pressed', 'true');
    await command('Line').click();
    await expect(canvas).toHaveAttribute('data-active-tool', 'line');
    await expect(command('Line')).toHaveAttribute('aria-pressed', 'true');
    await page.waitForTimeout(150);
    await page.mouse.click(400, 620); await expect(canvas).toHaveAttribute('data-tool-draft-point-count', '1');
    await page.mouse.move(650, 620, {steps: 12}); await page.waitForTimeout(100);
    await expect(canvas).toHaveAttribute('data-tool-draft-point-count', '1');
    await page.mouse.click(650, 620);
    await expect(canvas).toHaveAttribute('data-entity-count', '1'); await lineMode();
    const incoming = await download(`${spec.name}-incoming`), source = incoming.doc.entities[0];
    assert.equal(source.kind, 'line'); close(source.start.y, source.end.y, 'horizontal incoming line');
    assert.ok(source.end.x > source.start.x);
    // Exit/re-enter the last vertex to use the real chained arc hover transition.
    await move({x: source.end.x + 1, y: source.end.y - .5}); await move(source.end);
    await expect(canvas).toHaveAttribute('data-active-tool', 'tangentarc');
    await expect(canvas).toHaveAttribute('data-tool-draft-point-count', '2');
    const radius = spec.enterWithoutMove ? 2 : spec.keyedRadius ?? 2;
    let target = {x: source.end.x + (spec.degrees === 90 ? radius : 0), y: source.end.y + (spec.degrees === 90 ? radius : 2 * radius)};
    // Approach just off a cardinal locus: exact input alone would not prove snapping.
    const scale = Number(await canvas.getAttribute('data-scale'));
    target = {...target, x: target.x + 2 / scale};
    await move(target);
    let pointerBefore, beforeKeying;
    if (spec.enterWithoutMove) {
      const expectedCue = spec.automatic
        ? new RegExp(`Arc quadrant.*Pending:.*${spec.degrees === 90 ? 'Horizontal' : 'Vertical'}`, 's')
        : /Arc quadrant.*auto constraints off/is;
      await expect(canvas).toHaveAttribute('data-snap-cue', expectedCue);
      beforeKeying = await preview(spec.degrees, `${spec.name}-before-keying`);
      assert.ok(Math.abs(beforeKeying.worldRadius - spec.keyedRadius) > .75, 'keyed radius substantially changes the acquired preview');
      pointerBefore = await page.evaluate(() => ({...window.__tangentArcPointerEvents}));
    }
    if (spec.keyedRadius !== null) {
      const input = page.locator('input[data-dimension-key="radius"]'); await expect(input).toBeVisible();
      // Tab cycles this sole field back to itself and intentionally clears its lock.
      await decimal(input, String(spec.keyedRadius));
      await expect(canvas).toHaveAttribute('data-entity-count', '1');
      if (!spec.enterWithoutMove) await move(target);
    }
    let painted = await preview(spec.degrees, spec.name);
    if (spec.enterWithoutMove) {
      close(painted.worldRadius, spec.keyedRadius, 'painted keyed radius before Enter');
      assert.deepEqual(await page.evaluate(() => ({...window.__tangentArcPointerEvents})), pointerBefore,
        'typing radius alone performs no pointer movement or quadrant reacquisition');
      if (spec.reaimAfterKeying) {
        const radians = (spec.finalDegrees - 90) * Math.PI / 180;
        target = {x: source.end.x + spec.keyedRadius * Math.cos(radians),
          y: source.end.y + spec.keyedRadius + spec.keyedRadius * Math.sin(radians)};
        await move(target);
        await expect(canvas).not.toHaveAttribute('data-snap-cue', /Arc quadrant/);
        // A non-snapped pixel gesture is near 60 degrees; native geometry must
        // match the measured paint exactly, not an idealized world target.
        painted = await preview(spec.finalDegrees, `${spec.name}-reaimed`, 1);
        const afterReaim = await page.evaluate(() => ({...window.__tangentArcPointerEvents}));
        assert.ok(afterReaim.moves > pointerBefore.moves, 'negative case actually re-aims the mouse');
        evidence.keyboardCommits.push({name: `${spec.name}-reaim`, pointerBefore, pointerAfter: afterReaim});
        pointerBefore = afterReaim;
      }
      const cueAfterKeying = await canvas.getAttribute('data-snap-cue');
      await page.locator('input[data-dimension-key="radius"]').press('Enter');
      await expect(canvas).toHaveAttribute('data-entity-count', '2');
      const pointerAfter = await page.evaluate(() => ({...window.__tangentArcPointerEvents}));
      evidence.keyboardCommits.push({name: spec.name, beforeKeying, painted, cueAfterKeying, pointerBefore, pointerAfter});
      assert.deepEqual(pointerAfter, pointerBefore, 'typing/Enter performs no mouse movement, click, or quadrant reacquisition');
    } else {
      await click(target); await expect(canvas).toHaveAttribute('data-entity-count', '2');
    }
    await lineMode();
    const curved = await download(`${spec.name}-arc`);
    const finalDegrees = spec.reaimAfterKeying ? painted.sweepDegrees : spec.degrees;
    const arc = assertChain(curved.doc, source, finalDegrees, spec.automatic, spec.keyedRadius, false);
    const end = pointOnArc(arc, arc.endAngleDegrees);
    const angle = arc.endAngleDegrees * Math.PI / 180;
    // Use the tangent direction of the committed arc without re-selecting a tool.
    await click({x: end.x - Math.sin(angle) * 3, y: end.y + Math.cos(angle) * 3});
    await expect(canvas).toHaveAttribute('data-entity-count', '3'); await lineMode();
    await canvas.press('Escape');
    const complete = await download(`${spec.name}-chain`);
    assertChain(complete.doc, source, finalDegrees, spec.automatic, spec.keyedRadius, true);
    await page.screenshot({path: `${output}/${spec.name}-chain.png`});
    if (spec.keyedRadius !== null) {
      for (const [label, before] of [['arc', curved], ['incoming', incoming], ['empty', empty]]) {
        await command('Undo').click();
        assert.deepEqual(content((await download(`${spec.name}-undo-${label}`)).doc), content(before.doc));
      }
      await expect(command('Undo')).toBeDisabled();
      for (const [label, after] of [['incoming', incoming], ['arc', curved], ['chain', complete]]) {
        await command('Redo').click();
        assert.deepEqual(content((await download(`${spec.name}-redo-${label}`)).doc), content(after.doc));
      }
      await expect(command('Redo')).toBeDisabled();
      await menu('New drawing');
      await page.locator('input[type=file]').setInputFiles({name: 'tangent-arc-chain.dxf', mimeType: 'application/dxf', buffer: Buffer.from(complete.text)});
      await expect(canvas).toHaveAttribute('data-entity-count', '3');
      const reopened = await download(`${spec.name}-reopened`);
      assert.deepEqual(content(reopened.doc), content(complete.doc));
      assertChain(reopened.doc, source, finalDegrees, spec.automatic, spec.keyedRadius, true);
      await page.screenshot({path: `${output}/${spec.name}-reopened.png`});
    }
    evidence.cases.push(spec.name); console.log(`PASS ${spec.name}`);
  }
  assert.deepEqual(evidence.errors, []);
  assert.ok(evidence.icon.quarterArcRendered, 'visible toolbar icon includes the new arc segment');
  evidence.passed = true;
} catch (error) {
  failure = error; evidence.failure = error.stack;
  if (page) await page.screenshot({path: `${output}/failure.png`}).catch(() => {});
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(evidence, null, 2)); await browser.close();
}
if (failure) throw failure;
console.log(JSON.stringify({passed: true, cases: evidence.cases.length, build: evidence.build, output}));
