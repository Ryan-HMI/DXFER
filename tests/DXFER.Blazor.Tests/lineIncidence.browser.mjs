// Synthetic fixtures only; no live manufacturing drawings or save-back.
import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/line-incidence-browser';
await mkdir(output, {recursive: true});
const native = text => {
  const a = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < a.length; i += 2) if (a[i].trim() === '310') hex += a[i + 1].trim();
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-5, `${a} != ${b}`);
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  const errors = []; page.on('pageerror', e => {errors.push(e.message); console.error(e.stack);});
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => {await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150);};
  const menu = async name => {await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);};
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    if (name) await writeFile(`${output}/${name}.dxf`, text);
    return text;
  };
  const load = async buffer => {
    await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'line-incidence.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
    await expect(canvas).toHaveAttribute('data-entity-count', '2'); await tool('Fit extents'); await page.waitForTimeout(250);
  };
  const screen = point => canvas.evaluate((c, p) => {
    const d = c.dataset, r = c.getBoundingClientRect();
    return {x: r.left + p.x * +d.scale + +d.offsetX, y: r.top - p.y * +d.scale + +d.offsetY};
  }, point);
  const pick = async p => {const s = await screen(p); await page.mouse.move(s.x, s.y); await page.waitForTimeout(100); await page.mouse.click(s.x, s.y); await page.waitForTimeout(250);};
  for (const lines of [false, true]) for (const reverse of [false, true]) for (const conflict of [false, true]) {
    const name = `${lines ? 'lines' : 'point'}${reverse ? '-reverse' : ''}${conflict ? '-conflict' : ''}`;
    if (process.env.DXFER_CASE && process.env.DXFER_CASE !== name) continue;
    await load(await readFile(new URL(`./fixtures/line-incidence/${lines ? 'lines' : 'point'}${conflict ? '-conflict' : ''}.dxf`, import.meta.url)));
    const before = native(await download());
    await tool('Coincident'); await tool('Fit extents'); await page.waitForTimeout(250);
    const edge = {x: 3, y: 0}, moving = lines ? {x: 13.2, y: 3.6} : {x: 15, y: 3};
    await pick(reverse ? moving : edge);
    await pick(reverse ? edge : moving);
    await page.screenshot({path: `${output}/${name}-before-escape.png`});
    await canvas.focus(); await page.keyboard.press('Escape');
    const text = await download(name), applied = native(text);
    const relation = applied.constraints.find(c => c.kind === (lines ? 'Collinear' : 'PointOnLine'));
    await page.screenshot({path: `${output}/${name}-picked.png`});
    assert.ok(relation, JSON.stringify(applied.constraints));
    assert.equal(relation.state, conflict ? 'Unsatisfied' : 'Satisfied');
    assert.deepEqual(applied.entities[0], before.entities[0]);
    if (conflict) assert.deepEqual(applied.entities, before.entities);
    else if (lines) {
      near(applied.entities[1].start.y, 0); near(applied.entities[1].end.y, 0);
      near(applied.entities[1].end.x - applied.entities[1].start.x, 5);
      assert.ok(applied.entities[1].start.x > 10);
    } else {near(applied.entities[1].location.x, 15); near(applied.entities[1].location.y, 0);}
    await tool('Undo'); assert.deepEqual(native(await download()).entities, before.entities);
    await tool('Redo'); assert.deepEqual(native(await download()).constraints, applied.constraints);
    await load(text); assert.deepEqual(native(await download()).constraints, applied.constraints);
    if (!conflict) {
      const p = lines ? {x: 12.7, y: 0} : {x: 15, y: 0};
      const a = await screen(p), b = await screen({x: p.x + 1, y: 1});
      await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, {steps: 15});
      await page.screenshot({path: `${output}/${name}-held.png`});
      await page.mouse.up(); await page.waitForTimeout(250);
      const dragged = native(await download(`${name}-dragged`));
      assert.deepEqual(dragged.entities[0], before.entities[0]);
      assert.ok(dragged.constraints.every(c => c.state === 'Satisfied'));
      if (lines) {near(dragged.entities[1].start.x, applied.entities[1].start.x + 1); near(dragged.entities[1].start.y, 0);}
      else {near(dragged.entities[1].location.x, 16); near(dragged.entities[1].location.y, 0);}
      await tool('Undo'); assert.deepEqual(native(await download()).entities, applied.entities);
      await tool('Redo'); assert.deepEqual(native(await download()).entities, dragged.entities);
    }
    await page.screenshot({path: `${output}/${name}.png`});
    console.log(`PASS ${name}: canvas picks, persistent relation, geometry, history/reopen${conflict ? ', unchanged conflicting geometry' : ', projected drag after reopen'}`);
  }
  assert.deepEqual(errors, []);
} finally {await browser.close();}
