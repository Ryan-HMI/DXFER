import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-no-information-panel';
await mkdir(output, {recursive: true});
const dxf = entities => ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
  '0','SECTION','2','ENTITIES',...entities,'0','ENDSEC','0','EOF',''].join('\n');
const corner = dxf(['0','LINE','8','0','10','0','20','0','11','10','21','0',
  '0','LINE','8','0','10','0','20','0','11','0','21','10']);
const circle = dxf(['0','CIRCLE','8','0','10','10','20','10','40','1']);
const results = [], errors = [], exports = [];
const browser = await chromium.launch({headless: true});
let build;
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  };
  await ready();
  build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  const canvas = page.locator('canvas.drawing-canvas');
  const controls = page.getByRole('complementary', {name: 'Drawing controls', exact: true});
  const operation = page.getByRole('complementary', {name: 'Active operation', exact: true});
  const layers = page.getByRole('region', {name: 'Layers', exact: true});
  const undo = page.getByRole('button', {name: 'Undo', exact: true});
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download');
    await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    const pairs = text.trim().split(/\r?\n/), checksums = []; let hex = '';
    for (let i = 0; i < pairs.length; i += 2) {
      if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
      if (pairs[i].trim() === '1') checksums.push(pairs[i + 1].trim());
    }
    assert.ok(hex, 'native document payload exists');
    const bytes = Buffer.from(hex, 'hex');
    assert.ok(checksums.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()), 'native checksum valid');
    exports.push({name, checksumValid: true});
    return JSON.parse(bytes.toString('utf8'));
  };
  const fit = async () => {
    await tool('Fit extents');
    let previous;
    await expect.poll(async () => {
      const current = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().width]));
      const stable = current === previous; previous = current; return stable;
    }).toBe(true);
  };
  const load = async (name, text = corner, count = 2) => {
    await canvas.press('Escape'); await menu('New drawing');
    await page.locator('input[type=file]').setInputFiles({name: `${name}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await fit(); await canvas.press('Escape');
    await expect(undo).toBeDisabled();
  };
  const click = async (x, y, button = 'left') => {
    const p = await canvas.evaluate((c, p) => {
      const b = c.getBoundingClientRect();
      return {x: b.x + +c.dataset.offsetX + p.x * +c.dataset.scale, y: b.y + +c.dataset.offsetY - p.y * +c.dataset.scale};
    }, {x, y});
    await page.mouse.click(p.x, p.y, {button}); await page.waitForTimeout(150);
  };
  const noInformation = async (active = false) => {
    await expect(page.getByRole('heading', {name: 'Information', exact: true})).toHaveCount(0);
    await expect(page.getByRole('button', {name: /^(Show|Hide) operation inspector$/})).toHaveCount(0);
    await expect(controls.locator('.dxfer-operation-readouts, .dxfer-live-readout-item, dl')).toHaveCount(0);
    await expect(operation).toHaveCount(active ? 1 : 0);
    await expect(layers).toBeVisible();
    if (!active) assert.equal(await controls.locator('h2').first().innerText(), 'Layers');
  };
  const visibleAction = async locator => {
    await expect(locator).toBeVisible();
    assert.ok(await locator.evaluate(e => {
      const b = e.getBoundingClientRect();
      return b.x >= 0 && b.y >= 0 && b.right <= innerWidth && b.bottom <= innerHeight &&
        e.contains(document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2));
    }), 'action is inside viewport and not covered');
  };
  const snapshot = async name => {
    assert.equal(Math.round((await page.locator('.app-header').boundingBox()).height), 48);
    const a = await canvas.boundingBox(), b = await controls.boundingBox();
    assert.ok(a.width > 250 && a.height > 250 && a.x + a.width <= b.x + 1, 'canvas and right controls do not overlap');
    await page.screenshot({path: `${output}/${name}.png`});
  };
  const run = async (name, action) => {
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`}); await ready();
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors, exports}, null, 2));
  };

  for (const [width, height] of [[1188, 761], [1100, 705]]) {
    await run(`idle-line-measure-${width}`, async () => {
      await page.setViewportSize({width, height}); await load(`idle-${width}`);
      await noInformation(); await snapshot(`idle-${width}`);
      await tool('Line'); await expect(canvas).toHaveAttribute('data-active-tool', 'line');
      await noInformation(); await snapshot(`line-${width}`);
      await canvas.press('Escape'); await noInformation();
      await click(5, 0); await noInformation(); await canvas.press('Escape');
      await tool('Measure'); await click(0, 0); await click(10, 0);
      await noInformation();
      const measurement = page.locator('.dxfer-command-bar .dxfer-live-readout-item').filter({has: page.locator('.dxfer-live-readout-label', {hasText: /^Dist$/})});
      await expect(measurement.locator('.dxfer-live-readout-value')).toHaveText('10');
      await snapshot(`measure-${width}`);
      await canvas.press('Escape'); await noInformation(); await expect(undo).toBeDisabled();
    });
    await run(`chamfer-${width}`, async () => {
      await load(`chamfer-${width}`);
      const before = await download(`chamfer-${width}-before`);
      const input = page.locator('.dxfer-corner-size-control input');
      const confirm = operation.getByRole('button', {name: 'Confirm', exact: true});
      const cancel = operation.getByRole('button', {name: 'Cancel', exact: true});
      const stage = async () => {
        await tool('Chamfer'); await noInformation(true); await input.fill('2'); await click(0, 0);
        await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
        await visibleAction(confirm); await visibleAction(cancel); await expect(undo).toBeDisabled();
      };
      await stage(); await snapshot(`chamfer-preview-${width}`); await cancel.click();
      await noInformation(); await expect(undo).toBeDisabled();
      assert.deepEqual((await download(`chamfer-${width}-cancel`)).entities, before.entities);
      await stage(); await input.press('Escape'); await noInformation(); await expect(undo).toBeDisabled();
      await stage(); await confirm.click(); await noInformation(); await expect(undo).toBeEnabled();
      const changed = await download(`chamfer-${width}-confirmed`);
      assert.equal(changed.entities.length, 3);
      assert.notDeepEqual(changed.entities, before.entities);
      await undo.click(); assert.deepEqual((await download(`chamfer-${width}-undo`)).entities, before.entities);
      await tool('Redo'); assert.deepEqual((await download(`chamfer-${width}-redo`)).entities, changed.entities);
    });
  }
  await run('layers-create-rename', async () => {
    await load('layers'); const before = await download('layers-before');
    await tool('Add layer'); await page.getByLabel('Layer name', {exact: true}).fill('QA details'); await tool('Rename');
    await expect(page.getByRole('button', {name: 'Layer QA details', exact: true})).toBeVisible();
    await noInformation(); await snapshot('layers-renamed-1100');
    const after = await download('layers-renamed');
    assert.ok(after.metadata.layers['QA details']); assert.deepEqual(after.entities, before.entities);
  });
  await run('pattern-controls', async () => {
    await page.setViewportSize({width: 1188, height: 761}); await load('pattern', circle, 1);
    const before = await download('pattern-before');
    const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
    await click(11, 10); await expect(canvas).toHaveAttribute('data-selected-count', '1'); await click(11, 10, 'right');
    await page.getByRole('dialog', {name: 'Drawing context commands'}).getByRole('button', {name: 'Pattern', exact: true}).click();
    await expect(editor).toBeVisible(); await noInformation(true);
    await expect(editor.getByLabel('Pattern columns', {exact: true})).toHaveValue('3');
    await expect(editor.getByLabel('Pattern rows', {exact: true})).toHaveValue('2');
    await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
    for (const [width, height] of [[1188, 761], [1100, 705]]) {
      await page.setViewportSize({width, height}); await fit();
      await visibleAction(editor.getByRole('button', {name: 'Confirm', exact: true}));
      await visibleAction(editor.getByRole('button', {name: 'Cancel', exact: true}));
      await snapshot(`pattern-preview-${width}`);
    }
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await noInformation(); await expect(undo).toBeDisabled();
    assert.deepEqual((await download('pattern-cancel')).entities, before.entities);
  });
  assert.deepEqual(errors, []);
  assert.ok(results.every(result => result.passed), 'all no-Information-panel cases must pass');
} finally { await browser.close(); }
