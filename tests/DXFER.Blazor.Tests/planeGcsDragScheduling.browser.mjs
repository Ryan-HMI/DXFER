import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {resolve, sep} from 'node:path';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-plane-gcs-drag-scheduling';
await mkdir(output, {recursive: true});
const browser = await chromium.launch({headless: true});
const results = [], errors = [];
try {
  const page = await browser.newPage({viewport: {width: 1100, height: 900}});
  page.on('pageerror', error => errors.push(error.message));
  // Serve only local source through interception. No web host, auth, or real solver is used.
  await page.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:5037') return route.abort();
    if (url.pathname === '/__plane_gcs_qa') return route.fulfill({contentType: 'text/html',
      body: '<!doctype html><body style="margin:0"><canvas tabindex="0" style="width:1000px;height:800px"></canvas></body>'});
    const file = resolve(root, `.${decodeURIComponent(url.pathname)}`);
    if (!file.startsWith(resolve(root, 'src/DXFER.Blazor/wwwroot') + sep) || !file.endsWith('.js')) return route.abort();
    await route.fulfill({contentType: 'text/javascript', body: await readFile(file, 'utf8')});
  });
  const setup = async () => {
    await page.goto('http://127.0.0.1:5037/__plane_gcs_qa');
    await page.evaluate(async () => {
      const {createDrawingCanvas} = await import('/src/DXFER.Blazor/wwwroot/drawingCanvas.js');
      const documentOf = (x = 0, y = 0) => ({entities: [{id: 'circle', kind: 'circle', center: {x, y}, radius: 2}],
        bounds: {minX: -10, minY: -10, maxX: 10, maxY: 10}, constraints: [], dimensions: []});
      const qa = window.qa = {previews: [], commits: [], calls: [], documentOf};
      qa.canvas = document.querySelector('canvas');
      qa.instance = createDrawingCanvas(qa.canvas, {invokeMethodAsync(method, ...args) {
        qa.calls.push({method, args});
        if (method === 'GetPlaneGcsDragPreview') return new Promise(resolve => qa.previews.push({args, resolve}));
        if (method === 'OnGeometryDragRequested') return new Promise(resolve => qa.commits.push({args, resolve}));
        return Promise.resolve();
      }}, null, {}, {solverEngine: 'PlaneGCS'});
      qa.instance.setDocument(documentOf(), true);
    });
  };
  const canvas = page.locator('canvas');
  const move = async (x, y) => {
    const point = await canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
      y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), {x, y});
    const pixel = {x: Math.round(point.x), y: Math.round(point.y)};
    await page.mouse.move(pixel.x, pixel.y, {steps: 3});
    return canvas.evaluate((c, p) => ({x: (p.x - c.getBoundingClientRect().left - +c.dataset.offsetX) / +c.dataset.scale,
      y: (c.getBoundingClientRect().top + +c.dataset.offsetY - p.y) / +c.dataset.scale}), pixel);
  };
  const start = async () => { await move(0, 0); await page.mouse.down(); await move(1, 1); };
  const count = (kind, n) => expect.poll(() => page.evaluate(kind => window.qa[kind].length, kind)).toBe(n);
  const center = (x, y) => expect.poll(async () => {
    const value = (await canvas.getAttribute('data-first-center')).split(',').map(Number);
    return Math.hypot(value[0] - x, value[1] - y);
  }).toBeLessThan(1e-8);
  const resolvePreview = (index, x, y, engine = 'PlaneGCS') => page.evaluate(({index, x, y, engine}) => {
    window.qa.previews[index].resolve({document: window.qa.documentOf(x, y), reason: '', engine});
  }, {index, x, y, engine});
  const run = async (name, action) => {
    await setup();
    try {
      await action();
      await page.screenshot({path: `${output}/${name}.png`});
      results.push({name, passed: true}); console.log(`PASS ${name}`);
    } catch (error) {
      results.push({name, passed: false, error: error.stack});
      await page.screenshot({path: `${output}/${name}-failure.png`}); console.error(`FAIL ${name}: ${error.message}`);
    } finally {
      await page.mouse.up();
      await page.keyboard.up('Shift');
      await page.evaluate(() => window.qa.instance.dispose());
      await writeFile(`${output}/results.json`, JSON.stringify({results, errors}, null, 2));
    }
  };
  await run('coalesced-preview-and-exact-release', async () => {
    await start(); await count('previews', 1); await center(0, 0);
    await move(2, 2); const latest = await move(3, 4);
    await count('previews', 1);
    await resolvePreview(0, 1, 1); await count('previews', 2); await center(1, 1);
    const queued = await page.evaluate(() => window.qa.previews[1].args);
    assert.deepEqual(queued[2], latest, 'Queued request retains the exact actual pointer coordinates');
    await resolvePreview(1, 3, 4); await center(3, 4);
    await page.screenshot({path: `${output}/coalesced-held.png`});
    const released = await move(5, 6); await page.keyboard.down('Shift'); await page.mouse.up();
    await count('commits', 1);
    const commit = await page.evaluate(() => window.qa.commits[0].args);
    assert.deepEqual({x: commit[3], y: commit[4]}, released, 'Release commits the final pointer, not the displayed preview');
    assert.equal(commit[5], true);
    const total = await page.evaluate(() => window.qa.previews.length);
    await resolvePreview(2, 99, 99); await page.waitForTimeout(50); await center(3, 4);
    await count('previews', total);
    await page.evaluate(() => { window.qa.instance.setDocument(window.qa.documentOf(5, 0)); window.qa.commits[0].resolve(true); });
    await center(5, 0);
  });
  for (const automatic of [false, true]) await run(`snap-intent-preview-release-auto-${automatic}`, async () => {
    await page.evaluate(automatic => {
      const qa = window.qa;
      qa.snapDocument = qa.documentOf();
      qa.snapDocument.entities.push({id: 'peer', kind: 'point', points: [{x: 5, y: 5}]});
      qa.instance.setDocument(qa.snapDocument, true);
      qa.instance.setAutomaticSnapConstraints(automatic);
    }, automatic);
    await start(); await count('previews', 1); await move(5, 5);
    await resolvePreview(0, 1, 1); await count('previews', 2);
    const expected = {id: 'drag-snap', kind: 'Coincident', referenceKeys: ['circle:center', 'peer'], state: 'Satisfied'};
    assert.deepEqual(await page.evaluate(() => window.qa.previews[1].args[4]), expected);
    await page.evaluate(() => {
      const qa = window.qa, document = structuredClone(qa.snapDocument);
      document.entities[0].center = {x: 6, y: 6};
      document.entities[1].points = [{x: 6, y: 6}];
      qa.previews[1].resolve({engine: 'PlaneGCS', document, reason: ''});
    });
    await center(6, 6);
    await page.mouse.up(); await count('commits', 1);
    const commit = await page.evaluate(() => window.qa.commits[0].args);
    assert.deepEqual(commit[6], expected, 'Release carries the same persistent identities even when the target moved in preview');
    assert.deepEqual(commit.slice(3, 5), [5, 5], 'Pointer snap coordinates are not replaced by the last preview result');
    await page.evaluate(() => window.qa.commits[0].resolve(false));
    await center(0, 0);
  });
  await run('escape-invalidates-old-session', async () => {
    await start(); await count('previews', 1); await move(2, 2);
    await canvas.press('Escape'); await page.mouse.up();
    await start(); await count('previews', 1);
    await resolvePreview(0, 8, 8); await count('previews', 2); await center(0, 0);
    await resolvePreview(1, 1, 1); await center(1, 1);
    await canvas.press('Escape'); await page.mouse.up(); await count('commits', 0); await center(0, 0);
  });
  await run('document-replacement-discards-inflight-result', async () => {
    await start(); await count('previews', 1);
    await page.evaluate(() => window.qa.instance.setDocument(window.qa.documentOf(10, 10)));
    await resolvePreview(0, 1, 1); await page.waitForTimeout(50); await center(10, 10);
    await page.mouse.up(); await count('commits', 0);
  });
  await run('engine-mismatch-never-uses-legacy-preview', async () => {
    await start(); await count('previews', 1);
    await resolvePreview(0, 1, 1, 'Legacy');
    await count('previews', 2);
    await resolvePreview(1, 1, 1, 'Legacy');
    await expect(canvas).toHaveAttribute('data-plane-gcs-preview-engine', 'Legacy');
    await center(0, 0);
    assert.equal(await page.evaluate(() => window.qa.calls.some(c => c.method === 'GetOffsetDragPreview')), false);
    await canvas.press('Escape'); await page.mouse.up(); await count('commits', 0);
  });
  assert.deepEqual(errors, []);
  assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
