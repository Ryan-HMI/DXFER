// Synthetic corner-welded rectangles; no live manufacturing drawings.
import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/joined-rectangle-browser';
await mkdir(output, {recursive: true});
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (a, b) => assert.ok(Math.hypot(a.x - b.x, a.y - b.y) < 1e-5, JSON.stringify({a, b}));
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = name => page.getByRole('button', {name, exact: true}).click();
  const menu = async name => {await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);};
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async buffer => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'joined-rectangles.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', '8'); await tool('Fit extents'); await page.waitForTimeout(250);
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return {x: r.left + p.x * +d.scale + +d.offsetX, y: r.top - p.y * +d.scale + +d.offsetY};
  }, point);
  const drag = async (x, nextX, name, fixed) => {
    const a = await screen({x, y: 3}), b = await screen({x: nextX, y: 4});
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(100);
    assert.match(await canvas.getAttribute('data-hovered-id'), /^a1(?:\||$)/);
    await page.evaluate(() => {
      window.dragHeartbeat = {count: 0, maxGap: 0, last: performance.now()};
      window.dragHeartbeatTimer = setInterval(() => {
        const h = window.dragHeartbeat, now = performance.now();
        h.maxGap = Math.max(h.maxGap, now - h.last); h.last = now; h.count++;
      }, 20);
    });
    await page.mouse.down();
    for (let i = 1; i <= 100; i++) {
      const t = i / 100;
      await page.mouse.move(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t + Math.sin(t * Math.PI * 8) * 15);
      await page.waitForTimeout(20);
    }
    const held = await canvas.evaluate(c => {
      const [x, y] = c.dataset.firstScreenEnd.split(',').map(Number), d = c.dataset;
      return {x: (x - +d.offsetX) / +d.scale, y: (+d.offsetY - y) / +d.scale};
    });
    assert.ok(Math.abs(held.x - (fixed ? x : nextX)) < 1e-6, JSON.stringify(held));
    assert.ok(Math.abs(held.y) < 1e-6);
    const heartbeat = await page.evaluate(() => {clearInterval(window.dragHeartbeatTimer); return window.dragHeartbeat;});
    assert.ok(heartbeat.count >= 80 && heartbeat.maxGap < 500, JSON.stringify(heartbeat));
    await page.screenshot({path: `${output}/${name}-held.png`});
    await page.mouse.up(); await page.waitForTimeout(300);
    console.log(`${name}: held ${heartbeat.count} heartbeats; max gap ${heartbeat.maxGap.toFixed(1)}ms`);
  };
  for (const reverse of [false, true]) for (const fixed of [false, true]) {
    const name = `joined-rectangles${reverse ? '-reverse' : ''}${fixed ? '-fixed' : ''}`;
    await load(await readFile(new URL(`./fixtures/drag-matrix/${name}.dxf`, import.meta.url)));
    const original = native(await download());
    await drag(6, 8, name, fixed);
    const text = await download(name), changed = native(text);
    if (fixed) {
      assert.deepEqual(changed.entities, original.entities);
      await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    } else {
      near(changed.entities[1].start, {x: 8, y: 0});
      near(changed.entities[1].end, {x: 8, y: 9});
      assert.deepEqual(changed.entities.slice(4), original.entities.slice(4));
      assert.ok(changed.constraints.every(c => c.state === 'Satisfied'));
      await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
      await tool('Redo'); assert.deepEqual(native(await download()).entities, changed.entities);
      await load(text); assert.deepEqual(native(await download()).entities, changed.entities);
      await drag(8, 7, `${name}-reopened`, false);
      const again = native(await download(`${name}-reedited`));
      near(again.entities[1].start, {x: 7, y: 0});
      assert.ok(again.constraints.every(c => c.state === 'Satisfied'));
    }
    console.log(`PASS ${name}: sustained held preview, release, peer preservation${fixed ? ', refusal without history' : ', Undo/Redo/native reopen/reedit'}`);
  }
  assert.deepEqual(errors, []);
} finally {await browser.close();}
