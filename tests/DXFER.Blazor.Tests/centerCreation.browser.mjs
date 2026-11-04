import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';

const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5057';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-center-creation';
await mkdir(output, { recursive: true });
let key = process.env.DXFER_QA_API_KEY;
let server;
const results = { cases: [], errors: [] };
if (process.env.DXFER_START_LOCAL === '1') {
  assert.equal(new URL(url).hostname, '127.0.0.1');
  key = randomBytes(32).toString('hex');
  server = spawn('dotnet', ['run', '--project', 'src/DXFER.Web', '--no-build', '--no-launch-profile'], {
    env: { ...process.env, ASPNETCORE_URLS: url, ASPNETCORE_ENVIRONMENT: 'Development',
      DXFER_API_KEY: key, Dxfer__SketchSolverEngine: 'PlaneGCS', Dxfer__EnableFullEditing: 'true',
      Dxfer__EnableSyncIntegration: 'false' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  server.stdout.on('data', data => process.stdout.write(data));
  server.stderr.on('data', data => process.stderr.write(data));
}
let browser;
let page;
try {
  browser = await chromium.launch({ headless: true, executablePath: process.env.DXFER_CHROMIUM });
  await expect.poll(async () => {
    try { return (await fetch(`${url}/auth/status`)).status; } catch { return 0; }
  }, { timeout: 30000 }).toBe(200);
  page = await browser.newPage({ viewport: { width: Number(process.env.DXFER_QA_WIDTH ?? 1440), height: Number(process.env.DXFER_QA_HEIGHT ?? 1000) },
    extraHTTPHeaders: key ? { 'X-DXFER-API-Key': key } : {} });
  page.on('dialog', d => d.accept());
  page.on('pageerror', e => results.errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) results.errors.push(m.text()); });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(results.build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  const tool = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.waitForTimeout(100); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', { name: 'File', exact: true }).hover(); await tool(name); };
  const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-5, `${a} != ${b}`);
  const screen = p => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), p);
  const move = async p => { const s = await screen(p); await page.mouse.move(s.x, s.y); await page.waitForTimeout(150); return s; };
  const click = async p => { const s = await move(p); await page.mouse.click(s.x, s.y); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex); return { text, doc: JSON.parse(Buffer.from(hex, 'hex')) };
  };
  const content = d => ({ entities: d.entities, dimensions: d.dimensions, constraints: d.constraints });
  const rect = (doc, center, width, height) => {
    const lines = doc.entities.filter(e => e.kind === 'line'); assert.equal(lines.length, 4);
    const points = lines.flatMap(e => [e.start, e.end]);
    const minX = Math.min(...points.map(p => p.x)), maxX = Math.max(...points.map(p => p.x));
    const minY = Math.min(...points.map(p => p.y)), maxY = Math.max(...points.map(p => p.y));
    near((minX + maxX) / 2, center.x); near((minY + maxY) / 2, center.y);
    near(maxX - minX, width); near(maxY - minY, height);
    assert.ok(doc.constraints.every(c => c.state === 'Satisfied'));
  };
  const type = async (key, value, next) => {
    const field = page.locator(`input[data-dimension-key="${key}"]`);
    await field.focus(); await page.keyboard.press('Control+a');
    await page.keyboard.type(String(value), { delay: 60 });
    await expect(field).toHaveValue(String(value)); await field.press(next);
  };
  const fresh = async automatic => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await menu('Settings'); await page.getByRole('checkbox', { name: 'Automatic snap constraints', exact: true }).setChecked(automatic);
    await tool('Close settings');
  };
  for (const automatic of [true, false]) {
    const name = `center-rectangle-auto-${automatic}`; results.currentCase = name;
    await fresh(automatic); await tool('Center rectangle');
    await click({ x: 0, y: 0 }); await move({ x: 5, y: -4 });
    await type('width', 15, 'Tab'); await expect(page.locator('input[data-dimension-key="height"]')).toBeFocused();
    await type('height', 10, 'Enter'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await canvas.press('Escape');
    const saved = await download(`${name}-created`);
    rect(saved.doc, { x: 0, y: 0 }, 15, 10);
    assert.deepEqual(saved.doc.dimensions.map(d => d.value).sort((a, b) => a - b), [10, 15]);
    assert.ok(saved.doc.dimensions.every(d => d.isDriving));
    assert.equal(saved.doc.constraints.some(c => c.referenceKeys.includes('$origin:center')), automatic);
    await page.screenshot({ path: `${output}/${name}.png` });
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await tool('Redo'); await expect(canvas).toHaveAttribute('data-entity-count', '4');
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(saved.doc));
    await page.locator('input[type=file]').setInputFiles({ name: `${name}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(saved.text) });
    await expect(page.locator('.document-filename')).toHaveText(`${name}.dxf`);
    assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
    if (automatic) {
      const width = saved.doc.dimensions.find(d => d.value === 15);
      const input = page.locator(`input[data-dimension-id="${width.id}"]`);
      await input.dblclick(); await input.fill('18'); await input.press('Enter');
      await expect(input).toHaveAttribute('data-dimension-value', '18'); await canvas.press('Escape');
      rect((await download(`${name}-edited`)).doc, { x: 0, y: 0 }, 18, 10);
    }
    results.cases.push(name); console.log(`PASS ${name}`);
  }
  results.currentCase = 'center-rectangle-free-corner';
  await fresh(true); await tool('Center rectangle');
  await click({ x: 0, y: 0 }); await click({ x: 5, y: -4 });
  await expect(canvas).toHaveAttribute('data-entity-count', '4'); await canvas.press('Escape');
  const before = await download('pointer-created'); rect(before.doc, { x: 0, y: 0 }, 10, 8);
  assert.equal(before.doc.dimensions.length, 0);
  await move({ x: 5, y: -4 });
  const end = await screen({ x: 7, y: -5 });
  await page.mouse.down(); await page.mouse.move(end.x, end.y, { steps: 12 }); await page.mouse.up();
  await page.waitForTimeout(600);
  rect((await download('pointer-resized')).doc, { x: 0, y: 0 }, 14, 10);
  await page.screenshot({ path: `${output}/pointer-resized.png` });
  results.cases.push(results.currentCase);

  results.currentCase = 'midpoint-line-keyed';
  await fresh(true); await tool('Midpoint line');
  await click({ x: 0, y: 0 }); await move({ x: 3, y: 4 }); await type('length', 10, 'Enter');
  await expect(canvas).toHaveAttribute('data-entity-count', '1'); await canvas.press('Escape');
  const midpoint = await download('midpoint-line-keyed');
  const line = midpoint.doc.entities[0];
  near((line.start.x + line.end.x) / 2, 0); near((line.start.y + line.end.y) / 2, 0);
  near(Math.hypot(line.end.x - line.start.x, line.end.y - line.start.y), 10);
  assert.equal(midpoint.doc.dimensions.length, 1); near(midpoint.doc.dimensions[0].value, 10);
  assert.ok(midpoint.doc.constraints.some(c => c.referenceKeys.includes('$origin:center')));
  assert.ok(midpoint.doc.constraints.every(c => c.state === 'Satisfied'));
  results.cases.push(results.currentCase);

  results.currentCase = 'accepted-length-conflict-recovery';
  await tool('Fix'); await click(line.end); await canvas.press('Escape');
  const accepted = await download('fixed-midpoint-line');
  assert.equal(accepted.doc.constraints.filter(c => c.kind === 'Fix').length, 1);
  const lengthInput = page.locator(`input[data-dimension-id="${midpoint.doc.dimensions[0].id}"]`);
  await lengthInput.dblclick(); await lengthInput.fill('12'); await lengthInput.press('Enter');
  await expect(lengthInput).toHaveAttribute('data-dimension-value', '12'); await canvas.press('Escape');
  const failed = await download('conflicted-midpoint-line');
  assert.deepEqual(failed.doc.entities, accepted.doc.entities);
  assert.equal(failed.doc.solveEvidence.status, 'Failed');
  await lengthInput.dblclick(); await lengthInput.fill('10'); await lengthInput.press('Enter');
  await expect(lengthInput).toHaveAttribute('data-dimension-value', '10'); await canvas.press('Escape');
  const restored = await download('restored-midpoint-line');
  assert.deepEqual(restored.doc.entities, accepted.doc.entities);
  assert.equal(restored.doc.solveEvidence.status, 'Solved');
  assert.ok(restored.doc.solveEvidence.intents.every(i => i.state !== 'Unsatisfied'));
  await page.screenshot({ path: `${output}/restored-midpoint-line.png` });
  results.cases.push(results.currentCase);

  for (const center of [{ x: 0, y: 0 }, { x: 2, y: -2 }]) {
    const name = `ellipse-keyed-${center.x}-${center.y}`; results.currentCase = name;
    await fresh(true); await tool('Ellipse');
    await click(center); await move({ x: center.x + 5, y: center.y }); await type('major', 12, 'Enter');
    await move({ x: center.x, y: center.y + 4 }); await type('minor', 6, 'Enter');
    await expect(canvas).toHaveAttribute('data-entity-count', '1'); await canvas.press('Escape');
    const saved = await download(name), ellipse = saved.doc.entities[0];
    assert.equal(ellipse.kind, 'ellipse');
    near(ellipse.center.x, center.x); near(ellipse.center.y, center.y);
    near(Math.hypot(ellipse.majorAxisEndPoint.x, ellipse.majorAxisEndPoint.y) * 2, 12);
    near(ellipse.minorRadiusRatio, 0.5);
    assert.deepEqual(saved.doc.dimensions.map(d => d.value).sort((a, b) => a - b), [6, 12]);
    assert.ok(saved.doc.dimensions.every(d => d.isDriving));
    assert.ok(saved.doc.constraints.every(c => c.state === 'Satisfied'));
    await page.screenshot({ path: `${output}/${name}.png` });
    await page.locator('input[type=file]').setInputFiles({ name: `${name}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(saved.text) });
    await expect(page.locator('.document-filename')).toHaveText(`${name}.dxf`);
    assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(saved.doc));
    const major = saved.doc.dimensions.find(d => d.value === 12);
    const input = page.locator(`input[data-dimension-id="${major.id}"]`);
    await input.dblclick(); await input.fill('14'); await input.press('Enter');
    await expect(input).toHaveAttribute('data-dimension-value', '14'); await canvas.press('Escape');
    const edited = (await download(`${name}-edited`)).doc.entities[0];
    near(Math.hypot(edited.majorAxisEndPoint.x, edited.majorAxisEndPoint.y) * 2, 14);
    near(Math.hypot(edited.majorAxisEndPoint.x, edited.majorAxisEndPoint.y) * edited.minorRadiusRatio * 2, 6);
    results.cases.push(name);
  }
  assert.deepEqual(results.errors, []); delete results.currentCase; results.passed = true;
} catch (error) {
  results.failure = error.stack;
  if (page) {
    await page.screenshot({ path: `${output}/failure.png` });
    await writeFile(`${output}/failure.txt`, await page.locator('body').innerText());
  }
  throw error;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  await browser?.close();
  if (server && server.exitCode === null) {
    const stopped = new Promise(resolve => server.once('exit', resolve));
    server.kill('SIGTERM'); await stopped;
  }
}
console.log(JSON.stringify(results));
