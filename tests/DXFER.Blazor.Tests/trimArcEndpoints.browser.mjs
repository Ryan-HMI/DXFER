import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-trim-arc-endpoints';
const only = process.env.DXFER_CASES?.split(',');
const verify = process.env.DXFER_TRIM_MODE === 'verify';
await mkdir(output, {recursive: true});
// Synthetic dimensions, not inferred from Ryan's screenshot. Left edge runs upward.
const lines = [[0, 0, 0, 10], [0, 10, 22, 10], [22, 10, 22, 0], [22, 0, 0, 0]];
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', ...lines.flatMap(([x, y, ex, ey], i) =>
    ['0', 'LINE', '5', String(100 + i), '8', '0', '10', x, '20', y, '11', ex, '21', ey]),
  '0', 'ARC', '5', '200', '8', '0', '10', '0', '20', '5', '40', '2', '50', '270', '51', '90',
  '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex);
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const withNative = (text, doc) => {
  const bytes = Buffer.from(JSON.stringify(doc));
  const header = /(DXFER_NATIVE_V1\r?\n1\r?\n[^\r\n]+\r?\n1\r?\n)[^\r\n]+/;
  assert.match(text, header);
  return text.replace(header, '$1' + createHash('sha256').update(bytes).digest('hex').toUpperCase())
    .replace(/(?:310\r?\n[^\r\n]+\r?\n)+/,
      bytes.toString('hex').match(/.{1,240}/g).map(chunk => `310\n${chunk}\n`).join(''));
};
const content = doc => ({entities: doc.entities, dimensions: doc.dimensions, constraints: doc.constraints});
const results = [], errors = [];
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(8000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const tool = async name => {
    await page.getByRole('button', {name, exact: true}).click();
    await page.waitForTimeout(150);
  };
  const menu = async name => {
    await tool('Open application menu');
    await page.getByRole('button', {name: 'File', exact: true}).hover();
    await tool(name);
  };
  const download = async name => {
    const pending = page.waitForEvent('download');
    await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (text, count = 5) => {
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'synthetic-trim-arc.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents');
    await canvas.press('Escape');
    await page.waitForTimeout(250);
  };
  await load(fixture);
  const seed = await download('seed');
  const edges = seed.doc.entities.filter(e => e.kind === 'line');
  const target = edges[0], arc = seed.doc.entities.find(e => e.kind === 'arc');
  assert.deepEqual(target.start, {x: 0, y: 0});
  assert.deepEqual(target.end, {x: 0, y: 10});
  assert.deepEqual(arc.center, {x: 0, y: 5});
  assert.equal(arc.radius, 2);
  for (const name of ['unconstrained', 'corner-welds-axis', 'corner-welds-axis-midpoint']) {
    if (only && !only.includes(name)) continue;
    let evidence;
    try {
      const doc = structuredClone(seed.doc);
      // Explicit native equivalent of rectangle auto corner welds and axis constraints.
      if (name !== 'unconstrained') for (const [i, edge] of edges.entries()) {
        doc.constraints.push({id: `corner-${i}`, kind: 'Coincident', state: 'Satisfied',
          referenceKeys: [`${edge.id}:end`, `${edges[(i + 1) % 4].id}:start`]},
        {id: `axis-${i}`, kind: i % 2 ? 'Horizontal' : 'Vertical', state: 'Satisfied', referenceKeys: [edge.id]});
      }
      if (name.endsWith('-midpoint')) doc.constraints.push({id: 'arc-center-midpoint', kind: 'Midpoint',
        state: 'Satisfied', referenceKeys: [target.id, `${arc.id}:center`]});
      await load(withNative(seed.text, doc));
      const before = (await download(`${name}-before`)).doc;
      assert.deepEqual(content(before), content(doc), 'Native fixture loads without dropping intent');
      await tool('Power trim/extend');
      // Inside the removed span but off the center glyph to avoid selecting a constraint badge.
      const p = await canvas.evaluate(c => ({x: c.getBoundingClientRect().left + +c.dataset.offsetX,
        y: c.getBoundingClientRect().top - 4.5 * +c.dataset.scale + +c.dataset.offsetY}));
      await page.mouse.move(p.x + 25, p.y + 25);
      await page.mouse.move(p.x, p.y, {steps: 2});
      await expect.poll(() => canvas.getAttribute('data-hovered-id')).toBe(target.id);
      await page.screenshot({path: `${output}/${name}-hover.png`});
      await page.mouse.click(p.x, p.y);
      await page.waitForTimeout(350);
      const status = await page.locator('.dxfer-command-bar').innerText();
      await page.screenshot({path: `${output}/${name}-after.png`});
      await canvas.press('Escape');
      const saved = await download(`${name}-after`);
      const after = saved.doc;
      const changed = JSON.stringify(content(before)) !== JSON.stringify(content(after));
      evidence = {name, build, status, changed, beforeCount: before.entities.length, afterCount: after.entities.length,
        constraintsBefore: before.constraints, constraintsAfter: after.constraints};
      if (changed) {
        assert.ok(!name.endsWith('-midpoint'), 'Real midpoint conflict must not silently lose or reinterpret intent');
        assert.equal(after.entities.length, 6);
        for (const original of before.entities.filter(e => e.id !== target.id))
          assert.deepEqual(after.entities.find(e => e.id === original.id), original, 'Arc and other rectangle edges stay exact');
        const remnants = after.entities.filter(e => e.kind === 'line' && e.start.x === 0 && e.end.x === 0);
        assert.equal(remnants.length, 2);
        const spans = remnants.map(e => [Math.min(e.start.y, e.end.y), Math.max(e.start.y, e.end.y)]).sort((a, b) => a[0] - b[0]);
        assert.deepEqual(spans, [[0, 3], [7, 10]], 'Only the span between the arc endpoints is removed');
        if (verify && name === 'corner-welds-axis') {
          const upper = remnants.find(e => Math.max(e.start.y, e.end.y) === 10);
          const lower = remnants.find(e => Math.min(e.start.y, e.end.y) === 0);
          const upperEndpoint = `${upper.id}:${upper.start.y === 10 ? 'start' : 'end'}`;
          const lowerEndpoint = `${lower.id}:${lower.start.y === 0 ? 'start' : 'end'}`;
          assert.deepEqual(after.constraints.find(c => c.id === 'corner-0'), {
            ...before.constraints.find(c => c.id === 'corner-0'),
            referenceKeys: [upperEndpoint, `${edges[1].id}:start`]
          }, 'Original upper corner follows its surviving endpoint, not the shortened original line');
          assert.deepEqual(after.constraints.find(c => c.id === 'corner-3'), {
            ...before.constraints.find(c => c.id === 'corner-3'),
            referenceKeys: [`${edges[3].id}:end`, lowerEndpoint]
          }, 'Original lower corner stays attached to the lower endpoint');
          for (const constraint of before.constraints.filter(c => !['corner-0', 'corner-3'].includes(c.id)))
            assert.deepEqual(after.constraints.find(c => c.id === constraint.id), constraint);
          assert.ok(after.constraints.every(c => c.state === 'Satisfied'));
          evidence.mappedUpperEndpoint = upperEndpoint;
        }
        await tool('Undo');
        assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
        await tool('Redo');
        assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
        if (verify && name === 'corner-welds-axis') {
          await load(saved.text, 6);
          assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(after),
            'Native reopen retains exact remapped constraints and geometry');
          // A second trim cannot delete the remnant carrying the surviving upper-corner weld.
          await tool('Power trim/extend');
          const second = await canvas.evaluate(c => ({x: c.getBoundingClientRect().left + +c.dataset.offsetX,
            y: c.getBoundingClientRect().top - 8.5 * +c.dataset.scale + +c.dataset.offsetY}));
          await page.mouse.move(second.x + 25, second.y + 25);
          await page.mouse.move(second.x, second.y, {steps: 2});
          const upperId = evidence.mappedUpperEndpoint.slice(0, evidence.mappedUpperEndpoint.lastIndexOf(':'));
          await expect.poll(() => canvas.getAttribute('data-hovered-id')).toBe(upperId);
          await page.mouse.click(second.x, second.y);
          await page.waitForTimeout(350);
          evidence.secondTrimStatus = await page.locator('.dxfer-command-bar').innerText();
          assert.match(evidence.secondTrimStatus, /unchanged/i);
          await page.screenshot({path: `${output}/${name}-reopened-second-trim.png`});
          await canvas.press('Escape');
          assert.deepEqual(content((await download(`${name}-reopened-second-trim`)).doc), content(after));
          await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
          evidence.nativeReopenAndSecondTrimPassed = true;
        }
      } else {
        assert.deepEqual(content(after), content(before));
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        assert.match(status, /unchanged|cannot|could not|would/i);
      }
      assert.equal(name === 'unconstrained' ? changed : name.endsWith('-midpoint') ? !changed : true, true);
      if (verify && name === 'corner-welds-axis') assert.ok(changed, 'Surviving original corner endpoints must remap to their remnants');
      if (verify && name.endsWith('-midpoint')) assert.match(status, /\bMidpoint\b/i,
        'Refusal must explicitly identify the real Midpoint conflict');
      results.push({...evidence, passed: true});
      console.log(`${verify ? 'VERIFY' : 'BASELINE'} ${name}: ${JSON.stringify(evidence)}`);
    } catch (error) {
      results.push({...evidence, name, passed: false, error: error.stack});
      await page.screenshot({path: `${output}/${name}-failure.png`});
      console.error(`FAIL ${name}: ${error.message}`);
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, verify, results, errors}, null, 2));
  }
  assert.ok(results.length);
  assert.deepEqual(errors, []);
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
} finally {
  await browser.close();
}
