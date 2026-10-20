import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com']
  .includes(new URL(url).hostname), 'Isolated local/dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-pattern-center-dimensions';
const only = process.env.DXFER_CASES?.split(',').filter(Boolean);
await mkdir(output, {recursive: true});
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', '0', 'CIRCLE', '5', '100', '8', '0',
  '10', '10', '20', '10', '40', '1', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex, 'Export contains native document');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const content = doc => ({entities: doc.entities, constraints: doc.constraints,
  dimensions: doc.dimensions, patterns: doc.metadata.patterns ?? []});
const centerRef = key => {
  const match = /^(.+)\|point\|center\|[^|]+\|[^|]+$/.exec(key);
  return match ? `${match[1]}:center` : key;
};
const near = (a, b, message) => assert.ok(Math.abs(a - b) < 1e-7, `${message}: ${a} != ${b}`);
const results = [], errors = [];
let seed;
const browser = await chromium.launch({headless: true});
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000},
    extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text());
  });
  const ready = async () => {
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  };
  await ready();
  const build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
  console.log(build);
  const canvas = page.locator('canvas.drawing-canvas');
  const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const persistent = id => page.locator(`input[data-dimension-id="${id}"]`);
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
  const fit = async () => {
    await tool('Fit extents');
    let previous, stable = 0;
    await expect.poll(async () => {
      const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale, c.dataset.offsetX,
        c.dataset.offsetY, c.getBoundingClientRect().toJSON()]));
      stable = view === previous ? stable + 1 : 0;
      previous = view;
      return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const load = async (text, count) => {
    if (await context.isVisible()) await context.press('Escape');
    if (await editor.isVisible()) await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await page.keyboard.press('Escape');
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'pattern-center-dimensions.dxf',
      mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await fit();
    await canvas.press('Escape');
  };
  const screen = p => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), p);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify({
      dataset: await canvas.evaluate(c => ({...c.dataset})),
      status: await page.locator('.dxfer-command-bar').innerText()
    }, null, 2));
  };
  const pick = async (circle, body, name) => {
    const point = body ? {x: circle.center.x + circle.radius * Math.cos(0.4),
      y: circle.center.y + circle.radius * Math.sin(0.4)} : circle.center;
    const p = await screen(point);
    let key;
    await expect.poll(async () => {
      await page.mouse.move(p.x + 24, p.y + 24);
      await page.mouse.move(p.x, p.y, {steps: 2});
      key = await canvas.getAttribute('data-hovered-id');
      return body ? key === circle.id : Boolean(key?.startsWith(`${circle.id}|point|center|`));
    }, {timeout: 3000, intervals: [100, 150, 250], message: `Actual ${body ? 'body' : 'center'} pick for ${circle.id}`}).toBe(true);
    await capture(`${name}-hover`);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(180);
    return key;
  };
  const createPattern = async () => {
    if (seed) return seed;
    await load(fixture, 1);
    const original = (await download('pattern-seed')).doc.entities[0];
    await pick(original, true, 'pattern-source');
    const p = await screen({x: original.center.x + Math.cos(0.4), y: original.center.y + Math.sin(0.4)});
    await page.mouse.click(p.x, p.y, {button: 'right'});
    await context.getByRole('button', {name: 'Pattern', exact: true}).click();
    await expect(editor).toBeVisible();
    await editor.getByLabel('Pattern layout', {exact: true}).selectOption('Radial');
    for (const [label, value] of [['Pattern instances', '4'], ['Pattern center X', '5'], ['Pattern center Y', '10']]) {
      await editor.getByLabel(label, {exact: true}).fill(value);
      await page.waitForTimeout(150);
    }
    await editor.getByLabel('Full circle', {exact: true}).check();
    await editor.getByRole('button', {name: 'Confirm', exact: true}).click();
    await expect(editor).toBeHidden();
    await expect(canvas).toHaveAttribute('data-entity-count', '4');
    await canvas.press('Escape');
    await fit();
    seed = await download('actual-radial-pattern');
    assert.equal(seed.doc.metadata.patterns.length, 1);
    assert.equal(seed.doc.metadata.patterns[0].kind, 'Radial');
    assert.equal(seed.doc.metadata.patterns[0].members.length, 3);
    assert.deepEqual(seed.doc.entities.find(e => e.id === original.id), original);
    return seed;
  };
  const history = async (before, after, name) => {
    await tool('Undo');
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo');
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
  };
  const cases = [];
  for (const pair of ['members', 'source-member']) for (const workflow of ['tool-first', 'preselect-entry'])
    for (const reverse of [false, true]) cases.push({pair, workflow, reverse,
      name: `${pair}-${workflow}-${reverse ? 'reverse' : 'forward'}`});
  for (const pair of ['members', 'source-member']) cases.push({pair, workflow: 'tool-first', body: true,
    name: `${pair}-body-gap-cancel`});

  for (const {name, pair, workflow, reverse = false, body = false} of cases) {
    if (only && !only.includes(name)) continue;
    let phase = 'create-pattern', preselectionRepicked = false, changedValue;
    try {
      const original = await createPattern();
      await load(original.text, 4);
      const before = (await download(`${name}-before`)).doc;
      const pattern = before.metadata.patterns[0];
      const source = before.entities.find(e => e.id === pattern.sourceEntityIds[0]);
      const member = column => before.entities.find(e => e.id === pattern.members.find(m => m.column === column).entityId);
      const picks = pair === 'members' ? [member(1), member(2)] : [source, member(1)];
      if (reverse) picks.reverse();
      phase = 'pick-references';
      if (workflow === 'preselect-entry') {
        const selected = [];
        for (const [index, circle] of picks.entries()) {
          selected.push(await pick(circle, false, `${name}-preselect-${index}`));
          await expect(canvas).toHaveAttribute('data-selected-count', String(index + 1));
        }
        assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), selected.sort());
      }
      await tool('Dimension');
      if (!(await canvas.getAttribute('data-dimension-draft-keys'))) {
        preselectionRepicked = workflow === 'preselect-entry';
        for (const [index, circle] of picks.entries()) await pick(circle, body, `${name}-dimension-${index}`);
      }
      await capture(`${name}-after-picks`);
      if (body) {
        assert.equal((await canvas.getAttribute('data-dimension-draft-keys')).split(',').length, 2);
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(0);
        await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
        await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
        await canvas.press('Escape');
        assert.deepEqual(content((await download(`${name}-refused`)).doc), content(before));
      } else {
        const refs = (await canvas.getAttribute('data-dimension-draft-keys')).split(',').filter(Boolean);
        assert.deepEqual(refs.map(centerRef).sort(), picks.map(e => `${e.id}:center`).sort(), 'Draft must reference centers, never circle bodies');
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(0);
        phase = 'place-current-distance';
        const p = await screen({x: (picks[0].center.x + picks[1].center.x) / 2 + 1,
          y: (picks[0].center.y + picks[1].center.y) / 2 + 1});
        await page.mouse.move(p.x, p.y);
        await page.mouse.click(p.x, p.y);
        await page.waitForTimeout(300);
        await capture(`${name}-after-placement`);
        // Cancel the auto-open value editor without focusing away or accepting rounded display text.
        await page.keyboard.press('Escape');
        await canvas.press('Escape');
        const placed = await download(`${name}-placed`);
        assert.equal(placed.doc.dimensions.length, 1, 'Current center distance must place');
        const dimension = placed.doc.dimensions[0];
        assert.equal(dimension.kind, 'LinearDistance');
        assert.equal(dimension.isDriving, true);
        assert.deepEqual(dimension.referenceKeys.map(centerRef).sort(), picks.map(e => `${e.id}:center`).sort());
        near(dimension.value, Math.hypot(picks[0].center.x - picks[1].center.x, picks[0].center.y - picks[1].center.y), 'Measured current distance');
        assert.deepEqual(placed.doc.entities, before.entities, 'Placement cannot move a source or generated circle');
        assert.deepEqual(placed.doc.metadata.patterns, before.metadata.patterns);
        assert.deepEqual(placed.doc.constraints, before.constraints);
        await history(before, placed.doc, `${name}-placement`);
        phase = 'attempt-changed-distance';
        await persistent(dimension.id).dblclick();
        await persistent(dimension.id).fill('10');
        await persistent(dimension.id).press('Enter');
        await page.waitForTimeout(400);
        await capture(`${name}-changed-value`);
        changedValue = {requested: 10, status: await page.locator('.dxfer-command-bar').innerText(),
          displayedValue: await persistent(dimension.id).inputValue()};
        const attempted = await download(`${name}-changed-value`);
        changedValue.persistedDimension = attempted.doc.dimensions.find(d => d.id === dimension.id);
        assert.deepEqual(attempted.doc.entities, placed.doc.entities, 'Refused edit preserves every source/member coordinate and radius');
        assert.deepEqual(attempted.doc.metadata.patterns, placed.doc.metadata.patterns);
        assert.deepEqual(attempted.doc.constraints, placed.doc.constraints);
        assert.deepEqual(attempted.doc.dimensions, placed.doc.dimensions, 'Refused edit retains original dimension intent');
        console.log(`CHANGED VALUE ${name}: ${JSON.stringify(changedValue)}`);
        await load(placed.text, 4);
        phase = 'convert-driven';
        await persistent(dimension.id).click({button: 'right'});
        await expect(context).toBeVisible();
        await expect(context.getByRole('radio', {name: 'Driving', exact: true})).toBeChecked();
        await context.getByRole('radio', {name: 'Driven', exact: true}).check();
        await expect(context).toBeHidden();
        await expect(persistent(dimension.id)).toHaveAttribute('readonly', '');
        const driven = await download(`${name}-driven`);
        assert.ok(Math.abs(driven.doc.dimensions[0].value - dimension.value) <= 1e-12,
          'Driven measurement agrees within floating-point recomputation precision');
        assert.deepEqual(content(driven.doc), {...content(placed.doc), dimensions: [{...dimension,
          value: driven.doc.dimensions[0].value, isDriving: false}]},
          'Driven conversion changes only the mode');
        await history(placed.doc, driven.doc, `${name}-driven-conversion`);
        phase = 'native-reopen';
        await load(driven.text, 4);
        assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(driven.doc));
        await expect(persistent(dimension.id)).toHaveAttribute('readonly', '');
        await capture(`${name}-final`);
      }
      results.push({name, passed: true, preselectionRepicked, changedValue});
      console.log(`PASS ${name}${preselectionRepicked ? ': preselection clears on tool entry; actual centers repicked' : ''}`);
    } catch (error) {
      results.push({name, passed: false, phase, preselectionRepicked, changedValue, error: error.stack});
      console.error(`FAIL ${name} [${phase}]: ${error.message}`);
      await capture(`${name}-failure`);
      await ready();
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors,
      scope: 'Baseline characterization: current-distance placement, refused changed value, driven measurement and body rejection. No inverse pattern driving claim. Preselection-entry cases explicitly repick after tool entry clears selection.'}, null, 2));
  }
  assert.ok(results.length, 'At least one matching case must run');
  assert.deepEqual(errors, [], 'No browser/circuit errors');
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
} finally {
  await browser.close();
}
