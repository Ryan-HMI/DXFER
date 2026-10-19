import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.equal(new URL(url).hostname, 'dxferdev.sync.harrisonmetals.com');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-offset-unpaused-dev';
await mkdir(output, {recursive: true});
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  const errors = [], hashes = [], reads = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (/\/drawingCanvas\.js(?:\?|$)/.test(response.url()) && response.status() === 200) reads.push(response.body().then(body => {
      hashes.push(createHash('sha256').update(body).digest('hex'));
    }).catch(error => errors.push(error.message)));
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  await page.locator('input[type=file]').setInputFiles({name: 'offset-motion-disposable.dxf', mimeType: 'application/dxf',
    buffer: await readFile(new URL('./fixtures/selection-offset/rectangle.dxf', import.meta.url))});
  await expect(canvas).toHaveAttribute('data-entity-count', '4'); await tool('Fit extents'); await canvas.press('Escape');
  let previous, stable = 0;
  await expect.poll(async () => {
    const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
    stable = view === previous ? stable + 1 : 0; previous = view; return stable;
  }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const a = await screen({x: -.5, y: 6.5}), b = await screen({x: 10.5, y: -.5});
  await page.mouse.move(a.x, a.y); await page.mouse.down();
  try { await page.mouse.move(b.x, b.y, {steps: 12}); } finally { await page.mouse.up(); }
  await expect(canvas).toHaveAttribute('data-selected-count', '4'); await tool('Offset');
  const from = await screen({x: 5, y: -.8}), to = await screen({x: 5.8, y: -2});
  await page.mouse.move(from.x, from.y);
  await expect(page.locator('input[data-dimension-key="offset"]')).toBeVisible();
  const view = await canvas.evaluate(c => ({rect: c.getBoundingClientRect().toJSON(), scale: +c.dataset.scale,
    offsetX: +c.dataset.offsetX, offsetY: +c.dataset.offsetY}));
  await canvas.evaluate(c => {
    window.__offsetMotion = [];
    c.addEventListener('pointermove', event => window.__offsetMotion.push({epoch: performance.timeOrigin + performance.now(),
      x: event.clientX, y: event.clientY, buttons: event.buttons, trusted: event.isTrusted}));
  });
  const cdp = await page.context().newCDPSession(page), frames = [];
  cdp.on('Page.screencastFrame', event => {
    frames.push({data: event.data, epoch: event.metadata.timestamp * 1000, metadata: event.metadata});
    cdp.send('Page.screencastFrameAck', {sessionId: event.sessionId}).catch(error => errors.push(error.message));
  });
  await cdp.send('Page.startScreencast', {format: 'png', maxWidth: 1188, maxHeight: 761, everyNthFrame: 1});
  const startEpoch = await page.evaluate(() => performance.timeOrigin + performance.now());
  const started = performance.now(); let sent = 0;
  // No sleeps, screenshots, DOM reads or pauses between these real mouse events.
  while (performance.now() - started < 3000) {
    const t = Math.min(1, (performance.now() - started) / 3000);
    await cdp.send('Input.dispatchMouseEvent', {type: 'mouseMoved', x: from.x + (to.x - from.x) * t,
      y: from.y + (to.y - from.y) * t, button: 'none', buttons: 0});
    sent++;
  }
  const endEpoch = await page.evaluate(() => performance.timeOrigin + performance.now());
  await page.waitForTimeout(700);
  await cdp.send('Page.stopScreencast');
  const pointers = await page.evaluate(() => window.__offsetMotion);
  const samples = [];
  for (let i = 0; i < frames.length; i++) {
    const frame = frames[i];
    const pixels = await page.evaluate(async ({data, view}) => {
      const image = new Image(); image.src = 'data:image/png;base64,' + data; await image.decode();
      const c = document.createElement('canvas'); c.width = image.width; c.height = image.height;
      const ctx = c.getContext('2d'); ctx.drawImage(image, 0, 0);
      const ratio = image.width / innerWidth;
      const x = Math.round((view.rect.x + view.offsetX + 1.5 * view.scale) * ratio);
      const y1 = Math.round((view.rect.y + view.offsetY + .6 * view.scale) * ratio);
      const y2 = Math.round((view.rect.y + view.offsetY + 2.3 * view.scale) * ratio);
      const dataPixels = ctx.getImageData(x - 16, y1, 32, y2 - y1).data;
      let ink = 0, sum = 0;
      for (let p = 0; p < dataPixels.length; p += 4) if (dataPixels[p] > 150 && dataPixels[p + 1] > 100 && dataPixels[p + 2] < 140) {
        ink++; sum += Math.floor(p / 4 / 32);
      }
      return {ink, worldY: ink ? (view.rect.y + view.offsetY - (y1 + sum / ink) / ratio) / view.scale : null};
    }, {data: frame.data, view});
    const lastPointer = pointers.findLast(p => p.epoch <= frame.epoch);
    const moving = frame.epoch >= startEpoch && frame.epoch <= endEpoch && lastPointer && frame.epoch - lastPointer.epoch < 80;
    samples.push({index: i, epoch: frame.epoch, moving: Boolean(moving), pointerAgeMs: lastPointer ? frame.epoch - lastPointer.epoch : null, ...pixels});
    await writeFile(`${output}/frame-${String(i).padStart(3, '0')}.png`, Buffer.from(frame.data, 'base64'));
  }
  const active = samples.filter(s => s.moving), visible = active.filter(s => s.ink > 0);
  const positions = visible.map(s => s.worldY);
  const distinctPositions = new Set(positions.map(y => Math.round(y / .05))).size;
  await expect(canvas).toHaveAttribute('data-entity-count', '4');
  await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
  await Promise.all(reads);
  const result = {url, build, canvasSha256: [...new Set(hashes)], startEpoch, endEpoch, sent,
    pointerEvents: pointers.length, trustedEvents: pointers.every(p => p.trusted),
    capturedFrames: frames.length, movingFrames: active.length, visibleMovingFrames: visible.length, distinctPositions,
    firstY: positions[0], lastY: positions.at(-1), errors, samples, pointers};
  await writeFile(`${output}/results.json`, JSON.stringify(result, null, 2));
  console.log(JSON.stringify({...result, samples: undefined, pointers: undefined}, null, 2));
  assert.deepEqual(errors, []); assert.ok(hashes.length > 0);
  assert.ok(active.length >= 8, 'enough screencast frames during unpaused pointer movement');
  assert.equal(visible.length, active.length, 'preview remains visible in every captured movement-time frame');
  assert.ok(distinctPositions >= 4 && positions[0] - positions.at(-1) > .5, 'preview changes position during movement, before the pause');
} finally { await browser.close(); }
