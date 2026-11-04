import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {createServer} from 'node:net';
import {fileURLToPath} from 'node:url';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-large-step-browser';
const fixtures = process.env.DXFER_LARGE_STEP_FIXTURES ?? '/tmp/dxfer-large-step-fixtures';
await mkdir(output, {recursive: true});
const reservation = createServer();
await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
const port = reservation.address().port;
await new Promise(resolve => reservation.close(resolve));
const url = `http://127.0.0.1:${port}`;
const key = randomBytes(32).toString('hex');
const results = {url, cases: [], errors: [], exports: [], passed: false};
let browser, page, serverLog = '';
// Local ephemeral host and credentials only. No public URL or stored login is accepted.
const server = spawn('dotnet', ['run', '--project', 'src/DXFER.Web', '--no-build', '--no-launch-profile'], {
  cwd: root,
  env: {...process.env, DOTNET_PROCESSOR_COUNT: '2', ASPNETCORE_URLS: url,
    ASPNETCORE_ENVIRONMENT: 'Development', DXFER_API_KEY: key,
    Dxfer__SketchSolverEngine: 'PlaneGCS', Dxfer__EnableFullEditing: 'true', Dxfer__EnableSyncIntegration: 'false'},
  stdio: ['ignore', 'pipe', 'pipe']
});
const serverExited = new Promise(resolve => server.once('exit', resolve));
server.stdout.on('data', data => { serverLog += data; });
server.stderr.on('data', data => { serverLog += data; });
const content = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions});
const near = (a, b, label) => assert.ok(Math.abs(a - b) < 1e-6, `${label}: ${a} != ${b}`);
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const pointNear = (a, b, label) => { near(a.x, b.x, `${label} X`); near(a.y, b.y, `${label} Y`); };
const decode = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex, 'export includes native payload'); return JSON.parse(Buffer.from(hex, 'hex'));
};
const satisfied = doc => {
  assert.ok(doc.constraints.every(c => c.state === 'Satisfied'), JSON.stringify(doc.constraints));
  assert.equal(doc.solveEvidence?.engine, 'PlaneGCS');
  assert.ok(!doc.solveEvidence?.intents.some(i => i.state === 'Unsatisfied'), JSON.stringify(doc.solveEvidence));
};
const slot = (doc, width, constraintCount) => {
  satisfied(doc); assert.equal(doc.entities.length, 5); assert.equal(doc.constraints.length, constraintCount);
  const source = doc.entities.find(e => e.id === 'source');
  pointNear(source.start, {x: 0, y: 0}, 'origin'); near(distance(source.start, source.end), 1, 'center separation');
  assert.equal(doc.constraints.filter(c => c.kind === 'Tangent').length, 4);
  for (const arc of doc.entities.filter(e => e.kind === 'arc')) {
    near(arc.radius, width / 2, 'cap radius');
    near(Math.min(distance(arc.center, source.start), distance(arc.center, source.end)), 0, 'cap follows source');
  }
  near(doc.dimensions.find(d => d.id === 'width').value, width, 'driving full width');
};
const fourBar = (doc, ground, target) => {
  satisfied(doc); assert.deepEqual(doc.entities.find(e => e.id === 'ground'), ground);
  for (const [id, length] of [['left', 8], ['top', 12], ['right', 10]]) {
    const line = doc.entities.find(e => e.id === id); near(distance(line.start, line.end), length, `${id} length`);
  }
  const byId = Object.fromEntries(doc.entities.map(e => [e.id, e]));
  for (const [a, ae, b, be] of [['ground', 'start', 'left', 'start'], ['left', 'end', 'top', 'start'],
    ['top', 'end', 'right', 'end'], ['right', 'start', 'ground', 'end']]) pointNear(byId[a][ae], byId[b][be], 'weld');
  if (target === 180) {
    near(byId.right.end.x - byId.right.start.x, -10, 'inward branch');
    near(byId.right.end.y - byId.right.start.y, 0, 'flat right bar');
  }
  near(doc.dimensions.find(d => d.kind === 'Angle').value, target, 'angle intent');
};

try {
  await expect.poll(async () => {
    if (server.exitCode !== null) throw new Error(`Local server exited: ${serverLog}`);
    try { return (await fetch(`${url}/auth/status`)).status; } catch { return 0; }
  }, {timeout: 30000}).toBe(200);
  browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
  page = await browser.newPage({viewport: {width: Number(process.env.DXFER_QA_WIDTH ?? 1440), height: 1000},
    extraHTTPHeaders: {'X-DXFER-API-Key': key}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => results.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) results.errors.push(message.text());
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  results.build = await page.locator('.app-brand').getAttribute('title');
  const canvas = page.locator('canvas.drawing-canvas');
  await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  // Assert the normal served module contains the hook; no loader, route interception or injected solver.
  const served = await page.request.get(`${url}/_content/DXFER.Blazor/planeGcsSolver.js`);
  assert.ok((await served.text()).includes('const solveNative = solveNativeWithFallback;'));
  const command = name => page.getByRole('button', {name, exact: true});
  const tool = async name => { await command(name).click(); };
  const menu = async name => { await tool('Open application menu'); await command('File').hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); results.exports.push(name); return {text, doc: decode(text)};
  };
  const load = async (name, text) => {
    await canvas.press('Escape'); await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    text ??= await readFile(`${fixtures}/${name}.dxf`, 'utf8');
    await page.locator('input[type=file]').setInputFiles({name: `${name}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(decode(text).entities.length));
    await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(300);
    await expect(command('Undo')).toBeDisabled(); await expect(command('Redo')).toBeDisabled();
  };
  const screen = point => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), point);
  const pick = async (entity, fraction = .32) => {
    const point = entity.kind === 'arc'
      ? (() => { const angle = (entity.startAngleDegrees + 67) * Math.PI / 180;
        return {x: entity.center.x + entity.radius * Math.cos(angle), y: entity.center.y + entity.radius * Math.sin(angle)}; })()
      : {x: entity.start.x + fraction * (entity.end.x - entity.start.x),
        y: entity.start.y + fraction * (entity.end.y - entity.start.y)};
    const p = await screen(point); await page.mouse.move(p.x, p.y);
    await expect(canvas).toHaveAttribute('data-hovered-id', entity.id); await page.mouse.click(p.x, p.y);
  };
  const dimension = id => page.locator(`input[data-dimension-id="${id}"]`);
  const edit = async (id, value, refusal = false) => {
    const input = dimension(id); await input.dblclick(); await input.press('ControlOrMeta+A');
    await input.pressSequentially(String(value), {delay: 70}); await expect(input).toHaveValue(String(value));
    await input.press('Enter'); await canvas.press('Escape');
    if (refusal) await expect(input).toHaveClass(/unsatisfied/);
    else { await expect(input).toHaveAttribute('data-dimension-value', String(value)); await expect(input).not.toHaveClass(/unsatisfied/); }
  };
  const history = async (before, after, name) => {
    await tool('Undo'); assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo'); assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
  };
  const addAngle = async (doc, name) => {
    // Native baselines have no label anchors. Move the length label off its bar
    // using the normal drag gesture before selecting the bar underneath it.
    const right = doc.entities.find(e => e.id === 'right');
    const label = await dimension('right-length').boundingBox();
    const destination = await screen({x: (right.start.x + right.end.x) / 2 - 3,
      y: (right.start.y + right.end.y) / 2});
    await page.mouse.move(label.x + label.width / 2, label.y + label.height / 2); await page.mouse.down();
    await page.mouse.move(destination.x, destination.y, {steps: 8}); await page.mouse.up();
    await expect(page.getByRole('status')).toHaveText('Moved dimension.');
    await canvas.press('Escape'); await tool('Dimension');
    await expect(canvas).toHaveAttribute('data-active-tool', 'dimension');
    await pick(doc.entities.find(e => e.id === 'ground')); await pick(right);
    await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
    await expect(canvas).toHaveAttribute('data-dimension-draft-keys', 'ground,right');
    const p = await screen({x: 10, y: 3}); await page.mouse.move(p.x, p.y); await page.mouse.click(p.x, p.y);
    await expect(page.locator('input[data-dimension-id]')).toHaveCount(4); await canvas.press('Escape');
    const saved = await download(`${name}-angle-created`);
    const angle = saved.doc.dimensions.find(d => d.kind === 'Angle'); assert.ok(angle, 'actual angular dimension');
    satisfied(saved.doc); return {saved, id: angle.id};
  };
  const record = name => { results.cases.push(name); console.log(`PASS ${name}`); };

  for (const name of ['slot-sloped-width20', 'slot-horizontal-width20']) {
    results.current = name; await load(name); const before = await download(`${name}-before`);
    await edit('width', 10); const after = await download(`${name}-width10`);
    slot(after.doc, 10, before.doc.constraints.length);
    await page.screenshot({path: `${output}/${name}-width10.png`});
    await history(before.doc, after.doc, name);
    await load(`${name}-reopened`, after.text); const reopened = await download(`${name}-reopened`);
    slot(reopened.doc, 10, before.doc.constraints.length);
    await edit('width', 20); slot((await download(`${name}-reopened-width20`)).doc, 20, before.doc.constraints.length);
    record(name);
  }
  for (const name of ['four-bar-right-leaning', 'four-bar-left-leaning']) {
    results.current = name; await load(name); const seed = await download(`${name}-before`);
    const {saved: before, id} = await addAngle(seed.doc, name);
    await edit(id, 180); const after = await download(`${name}-180`);
    const ground = seed.doc.entities.find(e => e.id === 'ground'); fourBar(after.doc, ground, 180);
    await tool('Fit extents'); await page.waitForTimeout(250);
    await page.screenshot({path: `${output}/${name}-180.png`});
    await history(before.doc, after.doc, name);
    await load(`${name}-reopened`, after.text); fourBar((await download(`${name}-reopened`)).doc, ground, 180);
    await edit(id, 160); fourBar((await download(`${name}-reopened-160`)).doc, ground, 160); record(name);
  }
  for (const name of ['slot-sloped-width20', 'four-bar-right-leaning']) {
    results.current = `${name}-fixed-refusal`; await load(name); let seed = await download(`${name}-fixed-seed`), id = 'width';
    if (name.startsWith('four')) { const angular = await addAngle(seed.doc, `${name}-fixed`); seed = angular.saved; id = angular.id; }
    await load(`${name}-fixed-ready`, seed.text);
    await tool('Fix'); await expect(canvas).toHaveAttribute('data-active-tool', 'fix');
    await pick(seed.doc.entities.find(e => e.id === (id === 'width' ? 'slot2' : 'right')));
    await expect(command('Undo')).toBeEnabled();
    await expect(canvas).toHaveAttribute('data-selected-keys', '');
    await canvas.press('Escape'); const before = await download(`${name}-fixed-before`);
    assert.ok(before.doc.constraints.some(c => c.kind === 'Fix' && c.referenceKeys.includes(id === 'width' ? 'slot2' : 'right')));
    await edit(id, id === 'width' ? 10 : 180, true); const refused = await download(`${name}-fixed-refused`);
    assert.deepEqual(refused.doc.entities, before.doc.entities, 'failed edit preserves every entity exactly');
    assert.ok(refused.doc.solveEvidence?.intents.some(i => i.state === 'Unsatisfied'), 'retained failed intent remains visible');
    await page.screenshot({path: `${output}/${name}-fixed-refused.png`});
    await history(before.doc, refused.doc, `${name}-fixed`);
    await load(`${name}-fixed-reopened`, refused.text);
    const reopened = await download(`${name}-fixed-reopened`);
    assert.deepEqual(reopened.doc.entities, before.doc.entities);
    await expect(dimension(id)).toHaveClass(/unsatisfied/); record(`${name}-fixed-refusal`);
  }
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  results.failure = error.stack; await page?.screenshot({path: `${output}/failure.png`}).catch(() => {}); throw error;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  await browser?.close();
  if (server.exitCode === null) server.kill('SIGTERM');
  await serverExited;
  await writeFile(`${output}/server.log`, serverLog);
}
console.log(JSON.stringify(results));
