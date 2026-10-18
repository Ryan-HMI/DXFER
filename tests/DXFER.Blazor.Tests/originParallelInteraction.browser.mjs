import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-origin-interaction';
await mkdir(output, {recursive: true});
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex'));
};
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
  const fresh = async () => {await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');};
  const load = async buffer => {
    await fresh();
    await page.locator('input[type=file]').setInputFiles({name: 'origin-parallel.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', '2'); await tool('Fit extents');
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return {x: r.left + p.x * +d.scale + +d.offsetX, y: r.top - p.y * +d.scale + +d.offsetY};
  }, point);
  const click = async point => {const p = await screen(point); await page.mouse.click(p.x, p.y);};
  const drag = async (start, end, key, name) => {
    const a = await screen(start), b = await screen(end);
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(100);
    assert.match(await canvas.getAttribute('data-hovered-id'), key);
    await page.mouse.down(); await page.mouse.move(b.x, b.y, {steps: 10});
    if (name) await page.screenshot({path: `${output}/${name}.png`});
    await page.mouse.up(); await page.waitForTimeout(250);
  };
  const closePoint = (a, b) => assert.ok(Math.hypot(a.x - b.x, a.y - b.y) < 0.05, JSON.stringify({a, b}));
  for (const reverse of [false, true]) for (const id of ['a', 'b']) for (const endpoint of ['start', 'end']) {
    await load(await readFile(new URL(`./fixtures/drag-matrix/origin-parallel-${reverse ? 'reverse' : 'forward'}.dxf`, import.meta.url)));
    const original = native(await download()), entity = original.entities.find(e => e.id === id);
    const start = entity[endpoint], end = {x: start.x + 2, y: start.y + 1};
    const name = `${reverse}-${id}-${endpoint}`;
    await drag(start, end, new RegExp(`^${id}\\|point\\|${endpoint}`), name);
    const text = await download(name), changed = native(text);
    assert.equal(await canvas.getAttribute('data-selected-count'), '0');
    if (id === 'a' && endpoint === 'end') {
      assert.deepEqual(changed.entities, original.entities);
      await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      console.log(`PASS ${name}: anchored endpoint refuses without history`); continue;
    }
    closePoint(changed.entities.find(e => e.id === 'a').end, {x: 0, y: 0});
    closePoint(changed.entities.find(e => e.id === id)[endpoint], end);
    assert.ok(changed.constraints.every(c => c.state === 'Satisfied'));
    await tool('Undo'); assert.deepEqual(native(await download()).entities, original.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).entities, changed.entities);
    await load(text); assert.deepEqual(native(await download()).entities, changed.entities);
    const againStart = changed.entities.find(e => e.id === id)[endpoint];
    await drag(againStart, {x: againStart.x + 0.5, y: againStart.y + 0.5}, new RegExp(`^${id}\\|point\\|${endpoint}`));
    const again = native(await download());
    assert.ok(again.constraints.every(c => c.state === 'Satisfied'));
    assert.notDeepEqual(again.entities, changed.entities);
    console.log(`PASS ${name}: held/release, no selection, Undo/Redo, reopen/edit again`);
  }
  for (const [command, toolKey, count] of [['Line', 'line', 1], ['Center circle', 'centercircle', 1], ['Point', 'point', 1]]) {
    await fresh(); await tool(command);
    await expect(canvas).toHaveAttribute('data-active-tool', toolKey);
    await click({x: 0.01, y: 0.01});
    if (command !== 'Point') await click({x: -3, y: 4});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await page.keyboard.press('Escape'); await expect(canvas).toHaveAttribute('data-active-tool', 'select');
    await expect(page.getByRole('button', {name: command, exact: true})).not.toHaveAttribute('aria-pressed', 'true');
    const text = await download(`origin-${command.replaceAll(' ', '-')}`), doc = native(text);
    assert.ok(doc.constraints.some(c => c.kind === 'Coincident' && c.referenceKeys.includes('$origin:center') && c.state === 'Satisfied'));
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await tool('Redo'); assert.deepEqual(native(await download()).constraints, doc.constraints);
    await fresh(); await page.locator('input[type=file]').setInputFiles({name: 'origin.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', '1');
    assert.deepEqual(native(await download()).constraints, doc.constraints);
    console.log(`PASS ${command}: implicit origin constraint, Escape, history/native reopen`);
  }
  await load(await readFile(new URL('./fixtures/drag-matrix/origin-parallel-forward.dxf', import.meta.url)));
  await click({x: -5, y: 10}); await expect(canvas).toHaveAttribute('data-selected-count', '1');
  await page.keyboard.press('Escape'); await expect(canvas).toHaveAttribute('data-selected-count', '0');
  await tool('Parallel'); await expect(canvas).toHaveAttribute('data-active-tool', 'parallel'); await page.keyboard.press('Escape');
  await expect(page.getByRole('button', {name: 'Parallel', exact: true})).not.toHaveAttribute('aria-pressed', 'true');
  await expect(canvas).toHaveAttribute('data-active-tool', 'select');
  await page.screenshot({path: `${output}/escape-cleared.png`});
  assert.deepEqual(errors, []);
  console.log('PASS Escape clears idle selection and active tool highlight');
} finally {await browser.close();}
