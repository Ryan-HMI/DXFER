import assert from 'node:assert/strict';
import {chmod, copyFile, mkdir, readFile, writeFile, rm, symlink} from 'node:fs/promises';
import {spawn, execFileSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {resolve} from 'node:path';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5063';
assert.equal(new URL(url).hostname, '127.0.0.1', 'This replay is isolated and local only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-session-ui-browser';
const fixtures = process.env.DXFER_SESSION_FIXTURES ?? '/tmp/dxfer-session-fixtures';
await mkdir(output, {recursive: true});
const wrapper = resolve(output, 'trace-node.cjs');
await copyFile(new URL('./sessionTraceNode.cjs', import.meta.url), wrapper);
await chmod(wrapper, 0o700);
await writeFile(resolve(output, 'native-frames.jsonl'), '');
await rm(resolve(output, 'trace-config.json'), {force: true});
if (process.env.DXFER_SESSION_MAPPER_REVISION) {
  const source = resolve('src/DXFER.Blazor/wwwroot');
  const assets = resolve(output, 'historical-assets');
  await mkdir(assets, {recursive: true});
  for (const file of ['planeGcsSolver.js', 'planeGcsOffsets.js', 'planeGcsPatterns.js', 'perimeterDistance.js']) {
    const text = execFileSync('git', ['show', `${process.env.DXFER_SESSION_MAPPER_REVISION}:src/DXFER.Blazor/wwwroot/${file}`]);
    await writeFile(resolve(assets, file), text);
  }
  await rm(resolve(assets, 'vendor'), {force: true});
  await symlink(resolve(source, 'vendor'), resolve(assets, 'vendor'));
  await writeFile(resolve(output, 'trace-config.json'), JSON.stringify({modulePath: resolve(assets, 'planeGcsSolver.js')}));
}
const key = randomBytes(32).toString('hex');
const server = spawn('dotnet', ['run', '--project', 'src/DXFER.Web', '--no-build', '--no-launch-profile'], {
  env: {...process.env, ASPNETCORE_URLS: url, ASPNETCORE_ENVIRONMENT: 'Development', DXFER_API_KEY: key,
    Dxfer__SketchSolverEngine: 'PlaneGCS', Dxfer__EnableFullEditing: 'true', Dxfer__EnableSyncIntegration: 'false',
    Dxfer__PlaneGcsNodeExecutable: wrapper}, stdio: ['ignore', 'pipe', 'pipe']
});
let serverLog = '';
server.stdout.on('data', d => { serverLog += d; }); server.stderr.on('data', d => { serverLog += d; });
let browser, page;
const results = {cases: [], errors: [], mapperRevision: process.env.DXFER_SESSION_MAPPER_REVISION ?? 'worktree'};
try {
  await expect.poll(async () => { try { return (await fetch(`${url}/auth/status`)).status; } catch { return 0; } },
    {timeout: 30000}).toBe(200);
  browser = await chromium.launch({headless: true});
  page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': key}});
  page.on('dialog', d => d.accept()); page.on('pageerror', e => results.errors.push(e.message));
  const canvas = page.locator('canvas.drawing-canvas');
  const ready = async () => {
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    await expect(canvas).toHaveAttribute('data-solver-engine', 'PlaneGCS');
  };
  await page.goto(url); await ready();
  results.build = await page.locator('.app-brand').getAttribute('title');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(100); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const decode = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex'));
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text); return decode(text);
  };
  const model = d => ({entities: d.entities, dimensions: d.dimensions, constraints: d.constraints});
  let loaded;
  const load = async (name, count = 3) => {
    await canvas.press('Escape');
    // Deliberately replace the document without New drawing or browser reload.
    const text = await readFile(`${fixtures}/${name}.dxf`, 'utf8');
    await page.locator('input[type=file]').setInputFiles({name: name + '.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await page.waitForTimeout(350); await tool('Fit extents'); await canvas.press('Escape');
    loaded = decode(text);
    assert.deepEqual(model(await download(`${results.currentCase}-loaded`)), model(loaded), 'Open must restore the exact baseline');
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const trace = async () => (await readFile(`${output}/native-frames.jsonl`, 'utf8')).trim().split('\n').filter(Boolean).map(JSON.parse);
  const pick = async p => { const q = await screen(p); await page.mouse.click(q.x, q.y); await page.waitForTimeout(150); };
  const requests = new Map();
  const addMirror = async () => {
    const offset = (await trace()).length;
    await tool('Symmetric');
    for (const p of loaded.entities.filter(e => e.kind === 'polygon')) {
      const phase = p.rotationAngleDegrees * Math.PI / 180 + (p.circumscribed ? Math.PI / p.sideCount : 0);
      const radius = p.circumscribed ? p.radius / Math.cos(Math.PI / p.sideCount) : p.radius;
      await pick({x: p.center.x + radius * (Math.cos(phase) + Math.cos(phase + 2 * Math.PI / p.sideCount)) / 2,
        y: p.center.y + radius * (Math.sin(phase) + Math.sin(phase + 2 * Math.PI / p.sideCount)) / 2});
    }
    const axis = loaded.entities.find(e => e.id === 'axis');
    await pick({x: axis.start.x, y: 7});
    await page.screenshot({path: `${output}/${results.currentCase}-status.png`});
    const visibleText = await page.locator('body').innerText();
    await canvas.press('Escape');
    const doc = await download(results.currentCase);
    const frames = (await trace()).slice(offset);
    results.cases.push({name: results.currentCase, frames, visibleText});
    const request = structuredClone(frames.find(f => f.direction === 'request')?.frame.request);
    assert.ok(request, 'The actual UI must reach the native solver');
    for (const c of [...request.document.constraints, ...request.constraints]) if (c.kind === 'Symmetric') c.id = 'mirror';
    delete request.document.metadata;
    const baseline = JSON.stringify(model(loaded));
    if (requests.has(baseline)) assert.deepEqual(request, requests.get(baseline), 'Same baseline/picks must produce the same solve request');
    else requests.set(baseline, request);
    await page.screenshot({path: `${output}/${results.currentCase}.png`});
    assert.equal(doc.constraints.filter(c => c.kind === 'Symmetric').length, 1, 'UI must add one symmetry');
    assert.ok(doc.constraints.every(c => c.state === 'Satisfied'), JSON.stringify(frames));
    assert.ok(frames.some(f => f.direction === 'response' && f.frame.result.status === 'solved'));
    const polys = doc.entities.filter(e => e.kind === 'polygon');
    for (const e of polys) assert.ok(Math.abs(e.radius - loaded.dimensions[0].value) < 1e-6);
    assert.ok(Math.abs(polys[0].center.x + polys[1].center.x - 2 * axis.start.x) < 1e-6);
    assert.ok(Math.abs(polys[0].center.y - polys[1].center.y) < 1e-6);
  };
  await menu('Settings'); await page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true}).setChecked(false);
  await tool('Close settings');
  const editRectangle = async () => {
    await load('rectangle', 4);
    const width = page.locator('input[data-dimension-id="width"]');
    await width.dblclick(); await width.fill('16'); await width.press('Enter');
    await expect(width).toHaveAttribute('data-dimension-value', '16'); await canvas.press('Escape');
    const rectangle = await download(results.currentCase);
    assert.ok(rectangle.constraints.every(c => c.state === 'Satisfied'));
  };
  if (process.env.DXFER_SESSION_WITNESS === 'rectangle') {
    results.currentCase = 'witness-rectangle'; await editRectangle();
    results.currentCase = 'witness-after-rectangle'; await load('session-witness'); await addMirror();
  }
  if (process.env.DXFER_SESSION_WITNESS === 'reopen') {
    let failure;
    for (let i = 0; i < 3; i++) {
      results.currentCase = `session-witness-${i}`;
      await load('session-witness');
      try { await addMirror(); }
      catch (error) { failure ??= error; results.cases.at(-1).failure = error.message; }
    }
    await page.reload(); await ready();
    results.currentCase = 'session-witness-reload'; await load('session-witness'); await addMirror();
    results.reloadRecovered = true;
    if (failure) throw failure;
  }
  for (const circumscribed of [false, true]) {
    await page.reload(); await ready();
    for (let i = 0; i < 4; i++) {
      results.currentCase = `polygon-${circumscribed}-open-${i}`;
      await load(`polygon-${circumscribed ? 'True' : 'False'}`); await addMirror(circumscribed);
    }
    await page.reload(); await ready(); results.currentCase = `rectangle-before-${circumscribed}`;
    await editRectangle();
    results.currentCase = `polygon-${circumscribed}-after-rectangle`;
    await load(`polygon-${circumscribed ? 'True' : 'False'}`); await addMirror(circumscribed);
  }
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  results.failure = error.stack; await page?.screenshot({path: `${output}/failure.png`}).catch(() => {}); throw error;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  await writeFile(`${output}/server.log`, serverLog); await browser?.close();
  if (server.exitCode === null && server.signalCode === null) {
    const exited = new Promise(resolve => server.once('exit', resolve)); server.kill('SIGTERM'); await exited;
  }
}
console.log(JSON.stringify({build: results.build, cases: results.cases.map(c => c.name), passed: results.passed}));
