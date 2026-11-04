import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {randomBytes} from 'node:crypto';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5058';
const target = new URL(url);
assert.ok(['127.0.0.1', 'localhost'].includes(target.hostname) ||
  target.origin === 'https://dxferdev.sync.harrisonmetals.com' && process.env.DXFER_ALLOW_PUBLIC_DEV === '1');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-collapsed-browser';
const fixtures = process.env.DXFER_COLLAPSED_SYMMETRY_FIXTURES ?? '/tmp/dxfer-collapsed-fixtures';
await mkdir(output, {recursive: true});
let key = process.env.DXFER_QA_API_KEY, server, browser, page;
const results = {cases: [], errors: []};
if (process.env.DXFER_START_LOCAL === '1') {
  assert.equal(target.hostname, '127.0.0.1');
  key = randomBytes(32).toString('hex');
  server = spawn('dotnet', ['run', '--project', 'src/DXFER.Web', '--no-build', '--no-launch-profile'], {
    env: {...process.env, ASPNETCORE_URLS: url, ASPNETCORE_ENVIRONMENT: 'Development', DXFER_API_KEY: key,
      Dxfer__SketchSolverEngine: 'PlaneGCS', Dxfer__EnableFullEditing: 'true', Dxfer__EnableSyncIntegration: 'false'},
    stdio: ['ignore', 'pipe', 'pipe']
  });
  server.stdout.on('data', d => process.stdout.write(d)); server.stderr.on('data', d => process.stderr.write(d));
}
try {
  await expect.poll(async () => { try { return (await fetch(`${url}/auth/status`)).status; } catch { return 0; } },
    {timeout: 30000}).toBe(200);
  browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
  page = await browser.newPage({viewport: {width: Number(process.env.DXFER_QA_WIDTH ?? 1440), height: 1000},
    extraHTTPHeaders: key ? {'X-DXFER-API-Key': key} : {}});
  page.on('dialog', d => d.accept()); page.on('pageerror', e => results.errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(100); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const content = d => ({entities: d.entities, dimensions: d.dimensions, constraints: d.constraints});
  const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-5, `${a} != ${b}`);
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8'); await writeFile(`${output}/${name}.dxf`, text);
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex); return {text, doc: JSON.parse(Buffer.from(hex, 'hex'))};
  };
  const load = async (name, text, count = 3) => {
    await canvas.press('Escape'); await menu('New drawing');
    await page.locator('input[type=file]').setInputFiles({name: `${name}.dxf`, mimeType: 'application/dxf',
      buffer: Buffer.from(text ?? await readFile(`${fixtures}/${name}.dxf`, 'utf8'))});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(250);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const allSatisfied = d => assert.ok(d.constraints.every(c => c.state === 'Satisfied'));
  await menu('Settings'); await page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true}).setChecked(false);
  await tool('Close settings');
  for (const [name, dimensionId, next] of [['collapsed-circles', 'diameter', 8], ['collapsed-lines', 'length', 8]]) {
    results.currentCase = name; await load(name); const before = await download(`${name}-before`);
    const input = page.locator(`input[data-dimension-id="${dimensionId}"]`);
    await input.dblclick(); await input.fill(String(next)); await input.press('Enter');
    await expect(input).toHaveAttribute('data-dimension-value', String(next)); await canvas.press('Escape');
    const edited = await download(`${name}-edited`); allSatisfied(edited.doc);
    assert.deepEqual(edited.doc.entities.find(e => e.id === 'axis'), before.doc.entities.find(e => e.id === 'axis'));
    for (const e of edited.doc.entities.filter(e => e.id !== 'axis')) {
      if (name === 'collapsed-circles') near(e.radius, 4);
      else near(Math.hypot(e.end.x - e.start.x, e.end.y - e.start.y), 8);
    }
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before.doc));
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(edited.doc));
    await load(`${name}-reopened`, edited.text); allSatisfied((await download(`${name}-reopened`)).doc);
    await page.screenshot({path: `${output}/${name}.png`}); results.cases.push(name);
  }
  results.currentCase = 'collapsed-points'; await load('collapsed-points');
  const from = await screen({x: 0, y: 2}), to = await screen({x: -3, y: 4});
  await page.mouse.move(from.x, from.y); await page.mouse.down();
  await page.mouse.move(to.x, to.y, {steps: 12}); await page.mouse.up(); await page.waitForTimeout(300);
  const moved = await download('collapsed-points-moved'); allSatisfied(moved.doc);
  const points = moved.doc.entities.filter(e => e.kind === 'point');
  for (const point of points) { near(Math.abs(point.location.x), 3); near(point.location.y, 4); }
  near(points[0].location.x, -points[1].location.x);
  await page.screenshot({path: `${output}/collapsed-points.png`}); results.cases.push('collapsed-points');
  for (const preselect of [false, true]) {
    const name = `same-direction-ellipses-${preselect}`; results.currentCase = name;
    await load('same-direction-ellipses'); const before = await download(`${name}-before`);
    if (!preselect) await tool('Symmetric');
    const picks = [{x: -5 + 3 * Math.cos(.6), y: 1 + 1.5 * Math.sin(.6)},
      {x: 5 + 3 * Math.cos(.6), y: 1 + 1.5 * Math.sin(.6)}, {x: 0, y: 8}];
    for (let i = 0; i < picks.length; i++) {
      const q = await screen(picks[i]);
      if (preselect && i > 0) await page.keyboard.down('Shift');
      try { await page.mouse.click(q.x, q.y); } finally { if (preselect && i > 0) await page.keyboard.up('Shift'); }
      await page.waitForTimeout(150);
    }
    if (preselect) await tool('Symmetric'); await canvas.press('Escape');
    const after = await download(`${name}-after`); allSatisfied(after.doc);
    assert.equal(after.doc.constraints.filter(c => c.kind === 'Symmetric').length, 1);
    for (const [i, e] of after.doc.entities.entries()) if (e.kind === 'ellipse') {
      near(e.center.x, before.doc.entities[i].center.x); near(e.center.y, before.doc.entities[i].center.y);
      near(e.majorAxisEndPoint.x, 3); near(e.majorAxisEndPoint.y, 0); near(e.minorRadiusRatio, .5);
    }
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before.doc));
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after.doc));
    await load(`${name}-reopen`, after.text); allSatisfied((await download(`${name}-reopen`)).doc);
    await page.screenshot({path: `${output}/${name}.png`}); results.cases.push(name);
  }
  await menu('Settings'); await page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true}).setChecked(true);
  await tool('Close settings');
  const creationFixtures = process.env.DXFER_CREATION_REFERENCE_FIXTURES ?? '/tmp/dxfer-creation-reference-fixtures-20261104';
  for (const source of ['inscribed-pentagon', 'circumscribed-pentagon', 'ellipticalarc']) {
    const recipe = JSON.parse(await readFile(`${creationFixtures}/${source}-recipe.json`, 'utf8'));
    const baseline = await readFile(`${creationFixtures}/${recipe.file}`, 'utf8');
    const picks = recipe.endpoints ?? recipe.vertices.filter(v => [1, 4].includes(v.index));
    for (const pick of picks) for (const keyed of [false, true]) {
      const name = `${source}-${pick.label ?? pick.index}-${keyed}`; results.currentCase = name;
      await load(name, baseline, 1); const before = await download(`${name}-before`);
      const bounds = await canvas.boundingBox();
      await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
      await page.mouse.wheel(0, 500); await page.waitForTimeout(200);
      await tool('Line'); const start = await screen(pick.point), end = await screen(pick.pointerEnd);
      await page.mouse.move(start.x, start.y); await page.waitForTimeout(200); await page.mouse.click(start.x, start.y);
      await page.mouse.move(end.x, end.y); await page.waitForTimeout(200);
      if (keyed) {
        const field = page.locator('input[data-dimension-key="length"]');
        await field.focus(); await page.keyboard.press('Control+a'); await page.keyboard.type('12', {delay: 60});
        await field.press('Enter');
      } else await page.mouse.click(end.x, end.y);
      await expect(canvas).toHaveAttribute('data-entity-count', '2'); await canvas.press('Escape');
      const created = await download(`${name}-created`); allSatisfied(created.doc);
      assert.deepEqual(created.doc.entities.find(e => e.id === 'source'), before.doc.entities[0]);
      const line = created.doc.entities.find(e => e.kind === 'line');
      near(line.start.x, pick.point.x); near(line.start.y, pick.point.y);
      const sourceReference = key => key === pick.canonicalReferenceKey ||
        key.startsWith(`source|point|${pick.label ?? `vertex-${pick.index}`}|`) ||
        pick.index !== undefined && key === `source|segment|${pick.index}:start`;
      assert.ok(created.doc.constraints.some(c => c.kind === 'Coincident' && c.referenceKeys.some(sourceReference)));
      if (keyed) {
        near(Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y), 12);
        assert.ok(created.doc.dimensions.some(d => d.isDriving && d.value === 12));
      }
      await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before.doc));
      await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(created.doc));
      await load(`${name}-reopened`, created.text, 2); allSatisfied((await download(`${name}-reopened`)).doc);
      await page.screenshot({path: `${output}/${name}.png`}); results.cases.push(name);
    }
  }
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  results.failure = error.stack; await page?.screenshot({path: `${output}/failure.png`}).catch(() => {}); throw error;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2)); await browser?.close();
  if (server) { const exited = new Promise(resolve => server.once('exit', resolve)); server.kill('SIGTERM'); await exited; }
}
console.log(JSON.stringify(results));
