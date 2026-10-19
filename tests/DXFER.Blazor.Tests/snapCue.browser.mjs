import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-snap-cue';
await mkdir(output, {recursive: true});
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC','0','SECTION','2','ENTITIES',
  '0','LINE','8','0','10','2','20','2','11','10','21','2',
  '0','CIRCLE','8','0','10','8','20','7','40','1.5',
  '0','POINT','8','0','10','-4','20','-4','0','POINT','8','0','10','14','20','12',
  '0','ENDSEC','0','EOF',''].join('\n');
const native = text => {
  const lines = text.trim().split(/\r?\n/); let hex = ''; const strings = [];
  for (let i = 0; i < lines.length; i += 2) {
    if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    if (lines[i].trim() === '1') strings.push(lines[i + 1].trim());
  }
  assert.ok(hex); const bytes = Buffer.from(hex, 'hex');
  assert.ok(strings.includes(createHash('sha256').update(bytes).digest('hex').toUpperCase()), 'native checksum');
  return JSON.parse(bytes);
};
const results = [], errors = [], exports = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000); page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    if (process.env.DXFER_EXPECTED_REVISION) await expect(page.locator('.app-brand')).toHaveAttribute('title', new RegExp(process.env.DXFER_EXPECTED_REVISION));
  };
  await ready();
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const setting = async enabled => {
    await menu('Settings'); const checkbox = page.getByRole('checkbox', {name: 'Automatic snap constraints', exact: true});
    await expect(checkbox).toBeEnabled(); await checkbox.setChecked(enabled); await tool('Close settings');
  };
  const download = async name => {
    const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8'); await writeFile(`${output}/${name}.dxf`, text);
    const doc = native(text); exports.push({name, checksumValid: true}); return doc;
  };
  const load = async () => {
    await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'snap-cue-disposable.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture)});
    await expect(canvas).toHaveAttribute('data-entity-count', '4'); await tool('Fit extents'); await canvas.press('Escape');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX, c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0; previous = view; return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const move = async p => { const point = await screen(p); await page.mouse.move(point.x, point.y); };
  const click = async p => { const point = await screen(p); await page.mouse.click(point.x, point.y); };
  const hoverEnd = async target => { await tool('Line'); await click({x: -2, y: 6}); await move(target); };
  const captureCue = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}-cue.json`, JSON.stringify(await canvas.evaluate(c => ({cue: c.dataset.snapCue,
      hovered: c.dataset.hoveredId, snapPoint: c.dataset.hoveredSnapPoint, activeTool: c.dataset.activeTool})), null, 2));
  };
  const commit = async (before, target, name) => {
    await click(target); await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape');
    await expect(canvas).toHaveAttribute('data-snap-cue', '');
    const doc = await download(name);
    assert.deepEqual(doc.entities.filter(e => before.entities.some(old => old.id === e.id)), before.entities, 'existing geometry stays exact');
    const created = doc.entities.find(e => !before.entities.some(old => old.id === e.id));
    assert.equal(created.kind, 'line');
    assert.ok(Math.hypot(created.end.x - target.x, created.end.y - target.y) < 1e-6, 'committed endpoint is the advertised snap');
    return {doc, created};
  };
  const hasRelation = (doc, created, kind, reference) => doc.constraints.some(c => c.kind === kind && c.state === 'Satisfied'
    && c.referenceKeys.includes(reference) && c.referenceKeys.some(key => key === created.id || key.startsWith(`${created.id}:`)));
  const run = async (name, action) => {
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, results, errors, exports}, null, 2)); await ready();
  };
  const cases = [
    {name: 'origin', target: {x: 0, y: 0}, cue: /Origin.*Coincident/i, kind: 'Coincident', ref: () => '$origin:center'},
    {name: 'midpoint', target: {x: 6, y: 2}, cue: /Midpoint.*pending.*Midpoint/i, kind: 'Midpoint', ref: d => d.entities.find(e => e.kind === 'line').id},
    {name: 'endpoint', target: {x: 2, y: 2}, cue: /Endpoint.*Coincident/i, kind: 'Coincident', ref: d => `${d.entities.find(e => e.kind === 'line').id}:start`},
    {name: 'line-body', target: {x: 4, y: 2}, cue: /On line.*Point\s*on\s*line/i, kind: 'PointOnLine', ref: d => d.entities.find(e => e.kind === 'line').id},
    {name: 'circle-center', target: {x: 8, y: 7}, cue: /Center.*Coincident/i, kind: 'Coincident', ref: d => `${d.entities.find(e => e.kind === 'circle').id}:center`}
  ];
  for (const test of cases) await run(test.name, async () => {
    await load(); await setting(true); const before = await download(`${test.name}-before`);
    await hoverEnd(test.target); await expect(canvas).toHaveAttribute('data-snap-cue', test.cue); await captureCue(test.name);
    const {doc, created} = await commit(before, test.target, `${test.name}-committed`);
    assert.ok(hasRelation(doc, created, test.kind, test.ref(before)), 'advertised relation persists and is satisfied');
  });
  await run('settings-off-on', async () => {
    for (const enabled of [false, true]) {
      await load(); await setting(enabled); const before = await download(`setting-${enabled}-before`);
      await hoverEnd({x: 0, y: 0});
      await expect(canvas).toHaveAttribute('data-snap-cue', enabled ? /Origin.*Coincident/i : /Origin.*Snap only/i);
      await captureCue(`setting-${enabled}`);
      const {doc, created} = await commit(before, {x: 0, y: 0}, `setting-${enabled}-committed`);
      if (enabled) assert.ok(hasRelation(doc, created, 'Coincident', '$origin:center'));
      else assert.deepEqual(doc.constraints, before.constraints, 'OFF preserves geometric snap but creates no constraints');
    }
  });
  await run('circle-perimeter-no-false-coincident', async () => {
    await load(); await setting(true); const before = await download('perimeter-before');
    await hoverEnd({x: 9.5, y: 7}); await expect(canvas).toHaveAttribute('data-snap-cue', /Quadrant.*Snap only/i);
    await expect(canvas).not.toHaveAttribute('data-snap-cue', /Coincident/i); await captureCue('circle-perimeter');
    const {doc} = await commit(before, {x: 9.5, y: 7}, 'perimeter-committed');
    const id = before.entities.find(e => e.kind === 'circle').id;
    assert.ok(!doc.constraints.some(c => c.kind === 'Coincident' && c.referenceKeys.some(key => key === id || key.startsWith(`${id}:`))));
  });
  await run('escape-leave-keyed-override', async () => {
    await load(); await setting(true); const before = await download('cleanup-before');
    await tool('Line'); await move({x: 0, y: 0}); await expect(canvas).toHaveAttribute('data-snap-cue', /Origin.*Coincident/i);
    await page.mouse.move(20, 20); await expect(canvas).toHaveAttribute('data-snap-cue', '');
    await move({x: 0, y: 0}); await expect(canvas).toHaveAttribute('data-snap-cue', /Origin.*Coincident/i);
    await canvas.press('Escape'); await expect(canvas).toHaveAttribute('data-snap-cue', '');
    await hoverEnd({x: 6, y: 2}); await expect(canvas).toHaveAttribute('data-snap-cue', /Midpoint/);
    const length = page.locator('input[data-dimension-key="length"]'); await length.focus();
    await page.keyboard.press('ControlOrMeta+A'); await page.keyboard.press('1');
    await expect(canvas).toHaveAttribute('data-snap-cue', ''); await captureCue('keyed-override-cleared');
    await page.keyboard.press('Enter'); await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape');
    const doc = await download('keyed-override-committed'), id = before.entities.find(e => e.kind === 'line').id;
    assert.ok(!doc.constraints.some(c => c.kind === 'Midpoint' && c.referenceKeys.includes(id)), 'overridden snap must not create stale advertised intent');
    assert.ok(doc.dimensions.some(d => d.isDriving && d.value === 1));
  });
  const build = await page.locator('.app-brand').getAttribute('title');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors, exports}, null, 2));
  assert.deepEqual(errors, []); assert.ok(results.every(r => r.passed), `See ${output}/results.json`);
} finally { await browser.close(); }
