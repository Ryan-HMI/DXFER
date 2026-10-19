import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-offset-native-typing';
await mkdir(output, {recursive: true});
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
  '0','SECTION','2','ENTITIES','0','LINE','8','0','10','0','20','0','11','10','21','0','0','ENDSEC','0','EOF',''].join('\n');
const browser = await chromium.launch({headless: true});
const results = [], errors = [], sourceReads = [];
let build, canvasSha256, canvasUrl;
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => {
    if (/\/drawingCanvas\.js(?:\?|$)/.test(response.url()) && response.status() === 200)
      sourceReads.push(response.body().then(body => { canvasUrl = response.url(); canvasSha256 = createHash('sha256').update(body).digest('hex'); }));
  });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  build = await page.locator('.app-brand').getAttribute('title');
  assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION ?? '10c5177'), `verified current build: ${build}`);
  console.log(`VERIFIED ${build}`);
  const canvas = page.locator('canvas.drawing-canvas');
  const input = page.locator('input[data-dimension-key="offset"]');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(100); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const point = (x, y) => canvas.evaluate((c, p) => {
    const b = c.getBoundingClientRect();
    return {x: b.x + +c.dataset.offsetX + p.x * +c.dataset.scale, y: b.y + +c.dataset.offsetY - p.y * +c.dataset.scale};
  }, {x, y});
  const move = async (x, y) => { const p = await point(x, y); await page.mouse.move(p.x, p.y); };
  const load = async name => {
    await page.keyboard.press('Escape'); await menu('New drawing');
    await page.locator('input[type=file]').setInputFiles({name: `${name}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(fixture)});
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
    await tool('Fit extents'); await page.waitForTimeout(250);
    const p = await point(3, 0); await page.mouse.click(p.x, p.y);
    await expect(canvas).toHaveAttribute('data-selected-count', '1');
    await tool('Offset'); await move(5, -0.75); await expect(input).toBeVisible();
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    const pairs = text.trim().split(/\r?\n/), checksums = []; let hex = '';
    for (let i = 0; i < pairs.length; i += 2) {
      if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
      if (pairs[i].trim() === '1') checksums.push(pairs[i + 1].trim());
    }
    const bytes = Buffer.from(hex, 'hex');
    assert.ok(hex && checksums.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()));
    return JSON.parse(bytes.toString('utf8'));
  };
  // Observation only: no injected key/input events, focus, selection, or runtime replacements.
  await page.evaluate(() => {
    window.offsetTypingEvents = [];
    for (const type of ['keydown', 'keyup', 'beforeinput', 'input', 'focusin', 'focusout', 'select'])
      document.addEventListener(type, e => window.offsetTypingEvents.push({type, key: e.key, code: e.code,
        data: e.data, inputType: e.inputType, trusted: e.isTrusted, value: e.target.value,
        target: e.target.dataset?.dimensionKey ?? e.target.tagName, time: performance.now()}), true);
  });
  for (const [mode, delay] of [['automatic', 0], ['automatic', 350], ['mousemove', 0], ['mousemove', 350], ['field-click', 35], ['ctrl-a', 35]]) {
    const name = `${mode}-${delay}ms`, trace = [];
    try {
      await load(name);
      const node = await input.elementHandle();
      await page.evaluate(() => { window.offsetTypingEvents.length = 0; });
      const read = () => node.evaluate(el => {
        const current = document.querySelector('input[data-dimension-key="offset"]');
        const c = document.querySelector('canvas.drawing-canvas'), ratio = c.width / c.getBoundingClientRect().width;
        // Sample the offset line away from the cursor, its label, and the source at Y=0.
        const x = Math.round((+c.dataset.offsetX + 2 * +c.dataset.scale) * ratio);
        const top = Math.max(0, Math.round((+c.dataset.offsetY + .1 * +c.dataset.scale) * ratio));
        const bottom = Math.min(c.height, Math.round((+c.dataset.offsetY + 2 * +c.dataset.scale) * ratio));
        const pixels = c.getContext('2d').getImageData(x - 12, top, 24, Math.max(1, bottom - top)).data;
        let ink = 0, rows = 0;
        for (let i = 0; i < pixels.length; i += 4) if (Math.max(pixels[i], pixels[i + 1], pixels[i + 2]) > 160) { ink++; rows += Math.floor(i / 4 / 24); }
        return {value: current?.value, type: current?.type, connected: el.isConnected, sameNode: current === el,
          focused: document.activeElement === current, activeElement: document.activeElement?.tagName,
          selectionStart: current?.selectionStart, selectionEnd: current?.selectionEnd, badInput: current?.validity.badInput,
          preview: {ink, worldY: ink ? (+c.dataset.offsetY - (top + rows / ink) / ratio) / +c.dataset.scale : null}, time: performance.now()};
      });
      trace.push({phase: 'automatically-exposed', ...await read()});
      if (mode === 'field-click') await input.click();
      if (mode === 'ctrl-a') await page.keyboard.press('ControlOrMeta+A');
      trace.push({phase: 'before-keys', ...await read()});
      const failures = [];
      let prefix = '';
      for (const key of ['1', '.', '2', '5']) {
        await page.keyboard.press(key);
        prefix += key;
        if (mode === 'mousemove') await move(5 + prefix.length * .15, -.75 - prefix.length * .1);
        if (delay) await page.waitForTimeout(delay);
        const sample = await read();
        const expectedValue = sample.type === 'number' && prefix.endsWith('.') ? prefix.slice(0, -1) : prefix;
        trace.push({phase: 'keypress', key, prefix, expectedValue, ...sample});
        if (sample.value !== expectedValue || !sample.sameNode || !sample.focused)
          failures.push(`${prefix}: value=${sample.value}, sameNode=${sample.sameNode}, focused=${sample.focused}`);
      }
      await page.waitForTimeout(500);
      const settled = await read(); trace.push({phase: 'settled', ...settled});
      await page.screenshot({path: `${output}/${name}.png`});
      await writeFile(`${output}/${name}-trace.json`, JSON.stringify({trace, events: await page.evaluate(() => window.offsetTypingEvents)}, null, 2));
      if (settled.value !== '1.25') failures.push(`settled value=${settled.value}`);
      await page.keyboard.press('Enter');
      await expect(canvas).toHaveAttribute('data-entity-count', '2'); await page.keyboard.press('Escape');
      const doc = await download(`${name}-committed`);
      const dimension = doc.dimensions.find(d => d.kind === 'Offset');
      if (dimension?.value !== 1.25 || !dimension.isDriving) failures.push(`committed ${JSON.stringify(dimension)}`);
      const offset = doc.entities.find(e => e.kind === 'line' && Math.abs(e.start.y) > 1e-8);
      assert.ok(offset && Math.abs(Math.abs(offset.start.y) - 1.25) < 1e-8 && Math.abs(offset.end.y - offset.start.y) < 1e-8, 'committed line offset is exactly 1.25');
      assert.deepEqual(failures, []);
      results.push({name, passed: true, checksumValid: true, preview: settled.preview}); console.log(`PASS ${name}`);
    } catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await writeFile(`${output}/${name}-trace.json`, JSON.stringify({trace, events: await page.evaluate(() => window.offsetTypingEvents)}, null, 2));
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await Promise.all(sourceReads);
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, canvasUrl, canvasSha256, results, errors}, null, 2));
  }
  assert.deepEqual(errors, []);
  assert.ok(results.every(result => result.passed), 'native keyboard cases must pass');
} finally { await browser.close(); }
