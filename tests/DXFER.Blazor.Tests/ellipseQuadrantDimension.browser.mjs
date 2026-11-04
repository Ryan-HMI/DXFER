import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {randomBytes} from 'node:crypto';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5068';
assert.ok(['127.0.0.1', 'localhost'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-ellipse-quadrant';
await mkdir(output, {recursive: true});
let key = process.env.DXFER_QA_API_KEY, server, browser, page;
const results = {cases: [], errors: []};
if (process.env.DXFER_START_LOCAL === '1') {
  assert.equal(new URL(url).hostname, '127.0.0.1');
  const occupied = await fetch(`${url}/auth/status`).then(() => true, () => false);
  assert.equal(occupied, false, 'Choose a free localhost port; do not reuse another QA server.');
  key = randomBytes(32).toString('hex');
  server = spawn('dotnet', ['run', '--project', 'src/DXFER.Web', '--no-build', '--no-launch-profile'], {
    env: {...process.env, DOTNET_PROCESSOR_COUNT: '2', ASPNETCORE_URLS: url, ASPNETCORE_ENVIRONMENT: 'Development',
      DXFER_API_KEY: key, Dxfer__SketchSolverEngine: 'PlaneGCS', Dxfer__EnableFullEditing: 'true',
      Dxfer__EnableSyncIntegration: 'false', Dxfer__DataProtectionDirectory: `${output}/keys`,
      Dxfer__ApplicationName: 'DXFER-ellipse-quadrant-local-QA'}, stdio: ['ignore', 'pipe', 'pipe']
  });
  server.stdout.on('data', d => process.stdout.write(d)); server.stderr.on('data', d => process.stderr.write(d));
}
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC','0','SECTION','2','ENTITIES',
  '0','ELLIPSE','5','100','8','0','10','7','20','-2','11','6','21','0','40','0.5','41','0','42',String(2 * Math.PI),
  '0','ENDSEC','0','EOF',''].join('\n');
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
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8'); await writeFile(`${output}/${name}.dxf`, text);
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex); return {text, doc: JSON.parse(Buffer.from(hex, 'hex'))};
  };
  const load = async text => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'ellipse-quadrant.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '1'); await tool('Fit extents'); await canvas.press('Escape');
    const bounds = await canvas.boundingBox(); await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
    await page.mouse.wheel(0, 400); await page.waitForTimeout(250);
  };
  const click = async (x, y) => {
    const p = await canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
      y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), {x, y});
    await page.mouse.move(p.x, p.y); await page.waitForTimeout(100); await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
  };
  const verify = (doc, value, keys) => {
    assert.equal(doc.entities.length, 1); assert.equal(doc.dimensions.length, 1); assert.equal(doc.constraints.length, 0);
    const d = doc.dimensions[0], e = doc.entities[0];
    assert.equal(d.isDriving, true); assert.equal(d.value, value); assert.deepEqual(d.referenceKeys, keys);
    const points = keys.map(key => {
      const angle = Number(key.split('|')[2].slice('quadrant-'.length)) * Math.PI / 180;
      return {x: e.center.x + e.majorAxisEndPoint.x * Math.cos(angle) - e.majorAxisEndPoint.y * e.minorRadiusRatio * Math.sin(angle),
        y: e.center.y + e.majorAxisEndPoint.y * Math.cos(angle) + e.majorAxisEndPoint.x * e.minorRadiusRatio * Math.sin(angle)};
    });
    const dx = points[1].x - points[0].x, dy = points[1].y - points[0].y;
    assert.ok(['LinearDistance', 'HorizontalDistance', 'VerticalDistance'].includes(d.kind));
    const measured = d.kind === 'HorizontalDistance' ? Math.abs(dx) : d.kind === 'VerticalDistance' ? Math.abs(dy) : Math.hypot(dx, dy);
    assert.ok(Math.abs(measured - value) < 1e-6, `Live measurement ${measured}, intent ${value}`);
    assert.ok(doc.solveEvidence?.intents?.every(i => i.state !== 'Unsatisfied'));
  };
  for (const reverse of [false, true]) {
    const name = `minor-${reverse ? 'bottom-first' : 'top-first'}`; results.currentCase = name;
    await load(fixture); const before = await download(`${name}-before`);
    assert.equal(before.doc.constraints.length, 0); assert.equal(before.doc.dimensions.length, 0);
    await tool('Dimension');
    for (const y of reverse ? [-5, 1] : [1, -5]) await click(7, y);
    await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
    const keys = (await canvas.getAttribute('data-dimension-draft-keys')).split(',');
    assert.deepEqual(keys.map(k => k.split('|')[2]), reverse ? ['quadrant-270', 'quadrant-90'] : ['quadrant-90', 'quadrant-270']);
    await click(16, -2);
    const input = page.locator('input[data-dimension-id]'); await expect(input).toHaveCount(1);
    await input.press('Escape'); await canvas.press('Escape'); await expect(input).not.toHaveClass(/unsatisfied/);
    const placed = await download(`${name}-placed`); verify(placed.doc, 6, keys);
    assert.deepEqual(placed.doc.entities, before.doc.entities);
    const edit = async value => {
      await input.dblclick(); await input.fill(String(value)); await input.press('Enter'); await canvas.press('Escape');
      await expect(input).toHaveAttribute('data-dimension-value', String(value)); await expect(input).not.toHaveClass(/unsatisfied/);
    };
    await edit(8); const edited = await download(`${name}-edited`); verify(edited.doc, 8, keys);
    await tool('Undo'); verify((await download(`${name}-undo`)).doc, 6, keys);
    await tool('Redo'); verify((await download(`${name}-redo`)).doc, 8, keys);
    await load(edited.text); verify((await download(`${name}-reopened`)).doc, 8, keys);
    await edit(10); verify((await download(`${name}-reopened-edited`)).doc, 10, keys);
    await page.screenshot({path: `${output}/${name}.png`}); results.cases.push(name);
  }
  assert.deepEqual(results.errors, []); results.passed = true;
} catch (error) {
  results.failure = error.stack; await page?.screenshot({path: `${output}/failure.png`}).catch(() => {}); throw error;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2)); await browser?.close();
  if (server && server.exitCode === null) {
    const exited = new Promise(resolve => server.once('exit', resolve)); server.kill('SIGTERM'); await exited;
  }
}
console.log(JSON.stringify(results));
