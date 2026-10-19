import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/editable-patterns';
await mkdir(output, {recursive: true});
// Disposable geometry only. Pattern metadata must be authored by the actual application.
const fixture = '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n30\n0\n40\n1\n0\nENDSEC\n0\nEOF\n';
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex, 'Download must include native document data');
  return JSON.parse(Buffer.from(hex, 'hex').toString('utf8'));
};
const close = (actual, expected, message) => assert.ok(Math.abs(actual - expected) < 1e-6, `${message}: ${actual} != ${expected}`);
// Pointer coordinates cross CSS/browser floating-point transforms; persisted copy geometry stays stricter above.
const gestureClose = (actual, expected, message) => assert.ok(Math.abs(actual - expected) < 1e-4, `${message}: ${actual} != ${expected}`);
const patterns = doc => doc.metadata.patterns ?? [];
const xy = point => Array.isArray(point) ? {x: point[0], y: point[1]} : point;
function assertPattern(doc, kind) {
  assert.equal(patterns(doc).length, 1);
  const p = patterns(doc)[0];
  assert.equal(p.kind, kind);
  assert.equal(p.sourceEntityIds.length, 1);
  const source = doc.entities.find(e => e.id === p.sourceEntityIds[0]);
  assert.ok(source && source.kind === 'circle');
  const center = xy(source.center);
  const count = kind === 'Radial' ? p.count : p.columns * p.rows;
  assert.equal(doc.entities.length, count);
  assert.equal(p.members.length, count - 1);
  assert.equal(new Set(doc.entities.map(e => e.id)).size, count);
  for (const member of p.members) {
    assert.equal(member.sourceEntityId, source.id);
    const copy = doc.entities.find(e => e.id === member.entityId);
    assert.ok(copy && copy.kind === 'circle');
    close(copy.radius, source.radius, 'dependent radius');
    let expected;
    if (kind === 'Radial') {
      const angle = member.column * (p.fullCircle ? 360 / p.count : p.stepAngleDegrees) * Math.PI / 180;
      const origin = xy(p.center), dx = center.x - origin.x, dy = center.y - origin.y;
      expected = {x: origin.x + dx * Math.cos(angle) - dy * Math.sin(angle), y: origin.y + dx * Math.sin(angle) + dy * Math.cos(angle)};
    } else {
      const x = (member.column + (kind === 'Staggered' && member.row % 2 ? .5 : 0)) * p.xPitch;
      const y = member.row * p.yPitch, angle = p.angleDegrees * Math.PI / 180;
      expected = {x: center.x + x * Math.cos(angle) - y * Math.sin(angle), y: center.y + x * Math.sin(angle) + y * Math.cos(angle)};
    }
    close(xy(copy.center).x, expected.x, 'dependent X');
    close(xy(copy.center).y, expected.y, 'dependent Y');
  }
  return {pattern: p, source};
}

const browser = await chromium.launch({headless: true});
const results = [], errors = [];
let page;
try {
  page = await browser.newPage({viewport: {width: 1188, height: 761}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', dialog => dialog.accept());
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text()); });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const canvas = page.locator('canvas.drawing-canvas');
  const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
  const context = page.getByRole('dialog', {name: 'Drawing context commands'});
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(120); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name); };
  const download = async name => {
    const pending = page.waitForEvent('download');
    await menu('Download DXF');
    const text = await readFile(await (await pending).path(), 'utf8');
    await writeFile(`${output}/${name}.dxf`, text);
    return {text, doc: native(text)};
  };
  const load = async (text = fixture, count = 1) => {
    if (await context.isVisible()) await context.press('Escape');
    if (await editor.isVisible()) await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'editable-pattern-circle.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await tool('Fit extents');
    await canvas.press('Escape');
  };
  const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
  const click = async (p, button = 'left') => { const s = await screen(p); await page.mouse.click(s.x, s.y, {button}); };
  const circlePick = circle => ({x: xy(circle.center).x + circle.radius, y: xy(circle.center).y});
  const openNew = async () => {
    await click({x: 11, y: 10});
    await expect(canvas).toHaveAttribute('data-selected-count', '1');
    await click({x: 11, y: 10}, 'right');
    await context.getByRole('button', {name: 'Pattern', exact: true}).click();
    await expect(editor).toBeVisible();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
    await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
  };
  const fill = async (name, value) => { await editor.getByLabel(name, {exact: true}).fill(String(value)); await page.waitForTimeout(150); };
  const confirm = async count => {
    await expect(editor.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
    await editor.getByRole('button', {name: 'Confirm', exact: true}).click();
    await expect(editor).toBeHidden();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await canvas.press('Escape');
    await tool('Fit extents');
  };
  const reopenList = async () => {
    await page.getByRole('region', {name: 'Patterns', exact: true}).getByRole('button').click();
    await expect(editor).toBeVisible();
  };
  const reopenContext = async circle => {
    await canvas.press('Escape');
    await click(circlePick(circle), 'right');
    await context.getByRole('button', {name: /^Edit .* pattern$/}).click();
    await expect(editor).toBeVisible();
  };
  const drag = async (circle, delta, perimeter = false, heldName = null) => {
    await canvas.press('Escape');
    await click(circlePick(circle));
    await expect(canvas).toHaveAttribute('data-selected-count', '1');
    // Server selection readouts can reflow the inspector before the next gesture.
    await page.waitForTimeout(200);
    const start = perimeter ? circlePick(circle) : xy(circle.center);
    const a = await screen(start), b = await screen({x: start.x + delta.x, y: start.y + delta.y});
    await page.mouse.move(a.x, a.y); await page.waitForTimeout(120);
    const hovered = await canvas.getAttribute('data-hovered-id');
    if (!hovered.startsWith(`${circle.id}|point|${perimeter ? 'quadrant' : 'center'}`)) {
      await writeFile(`${output}/handle-miss-${circle.id}.json`, JSON.stringify({circle, start, a, b, hovered,
        dataset: await canvas.evaluate(c => ({...c.dataset})),
        hit: await page.evaluate(p => document.elementFromPoint(p.x, p.y)?.outerHTML, a)}, null, 2));
    }
    assert.ok(hovered.startsWith(`${circle.id}|point|${perimeter ? 'quadrant' : 'center'}`), `drag targets ${circle.id} ${perimeter ? 'quadrant' : 'center'}, actual ${hovered}`);
    const before = heldName ? await canvas.screenshot() : null;
    await page.mouse.down();
    try {
      await page.mouse.move(b.x, b.y, {steps: 12});
      if (heldName) {
        await page.waitForTimeout(500);
        await page.screenshot({path: `${output}/${heldName}.png`});
        await expect.poll(async () => {
          const center = (await canvas.getAttribute('data-first-center') ?? '').split(',').map(Number);
          return Math.hypot(center[0] - xy(circle.center).x - delta.x, center[1] - xy(circle.center).y - delta.y);
        }, {message: 'source center moves before pointer release'}).toBeLessThan(1e-4);
        assert.notDeepEqual(await canvas.screenshot(), before, 'held-pointer preview visibly changes');
        await writeFile(`${output}/${heldName}.json`, JSON.stringify(await canvas.evaluate(c => ({...c.dataset})), null, 2));
      }
    } finally { await page.mouse.up(); }
    await page.waitForTimeout(400);
    await canvas.press('Escape');
  };
  const screenshot = async name => {
    const viewport = page.viewportSize();
    const bounds = await canvas.boundingBox();
    assert.ok(bounds.width > 250 && bounds.height > 250, 'usable canvas remains visible');
    if (await editor.isVisible()) {
      for (const locator of [editor, editor.getByRole('button', {name: 'Confirm', exact: true}), editor.getByRole('button', {name: 'Cancel', exact: true})]) {
        const box = await locator.boundingBox();
        assert.ok(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, 'pattern editor/actions stay in viewport');
        if (locator !== editor) assert.ok(await locator.evaluate(button => {
          const b = button.getBoundingClientRect();
          return [.5, .9].every(y => button.contains(document.elementFromPoint(b.x + b.width / 2, b.y + b.height * y)));
        }), 'pattern actions are not occluded at their center or lower edge');
      }
    }
    await page.screenshot({path: `${output}/${name}.png`});
  };
  const run = async (name, action) => {
    try { await action(); results.push({name, passed: true}); console.log(`PASS ${name}`); }
    catch (error) {
      results.push({name, passed: false, error: error.stack}); console.error(`FAIL ${name}: ${error.message}`);
      await page.screenshot({path: `${output}/${name}-failure.png`});
      await page.reload();
      await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    }
    await writeFile(`${output}/results.json`, JSON.stringify({url, results, errors}, null, 2));
  };

  await run('defaults-cancel', async () => {
    await load(); const before = await download('defaults-before');
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    await openNew();
    await expect(editor.getByLabel('Pattern columns', {exact: true})).toHaveValue('3');
    await expect(editor.getByLabel('Pattern rows', {exact: true})).toHaveValue('2');
    await expect(editor.getByRole('alert')).toHaveCount(0);
    await screenshot('defaults-1188x761');
    const selected = await canvas.getAttribute('data-selected-keys');
    const bounds = await canvas.boundingBox();
    await page.mouse.click(bounds.x + bounds.width * .55, bounds.y + bounds.height * .55);
    await expect(editor).toBeVisible();
    await expect(canvas).toHaveAttribute('data-selected-keys', selected);
    assert.deepEqual((await download('defaults-staged-click')).doc, before.doc);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await expect(editor).toBeHidden();
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    assert.deepEqual((await download('defaults-cancel')).doc, before.doc);
  });

  for (const kind of ['Rectangular', 'Staggered', 'Radial']) await run(kind.toLowerCase(), async () => {
    const prefix = kind.toLowerCase();
    await page.setViewportSize({width: 1188, height: 761});
    await load(); await openNew();
    await editor.getByLabel('Pattern layout', {exact: true}).selectOption(kind);
    if (kind === 'Radial') {
      await fill('Pattern instances', 4); await fill('Pattern center X', 0); await fill('Pattern center Y', 0);
      await editor.getByLabel('Full circle', {exact: true}).check();
    } else {
      await fill('Pattern X pitch', 5); await fill('Pattern Y pitch', 4);
    }
    await tool('Fit extents');
    await screenshot(`${prefix}-preview-1188x761`);
    await confirm(kind === 'Radial' ? 4 : 6);
    let current = (await download(`${prefix}-created`)).doc;
    let {source} = assertPattern(current, kind);
    await screenshot(`${prefix}-created-1188x761`);

    await drag(source, {x: .8, y: .5}, false, `${prefix}-source-held-preview`);
    let moved = (await download(`${prefix}-source-moved`)).doc;
    let movedSource = assertPattern(moved, kind).source;
    gestureClose(xy(movedSource.center).x, xy(source.center).x + .8, 'source center drag X');
    gestureClose(xy(movedSource.center).y, xy(source.center).y + .5, 'source center drag Y');
    await drag(movedSource, {x: .35, y: 0}, true);
    current = (await download(`${prefix}-source-resized`)).doc;
    source = assertPattern(current, kind).source;
    gestureClose(source.radius, movedSource.radius + .35, 'source perimeter edit');
    const dependent = current.entities.find(e => e.id === patterns(current)[0].members[0].entityId);
    await drag(dependent, {x: .6, y: .4});
    assert.deepEqual((await download(`${prefix}-dependent-refused`)).doc, current, 'dependent drag is rejected atomically');
    // The rejected drag must not consume a history entry.
    await tool('Undo'); assert.deepEqual((await download(`${prefix}-undo-radius`)).doc, moved);
    await tool('Redo'); assert.deepEqual((await download(`${prefix}-redo-radius`)).doc, current);

    await reopenContext(source);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await reopenContext(dependent);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await reopenList();
    if (kind === 'Radial') {
      await editor.getByLabel('Full circle', {exact: true}).uncheck();
      await fill('Pattern instances', 6); await fill('Pattern step angle', 45);
    } else {
      await fill('Pattern columns', 4); await fill('Pattern rows', 2);
      await fill('Pattern X pitch', 6); await fill('Pattern Y pitch', 5); await fill('Pattern rotation', 30);
    }
    const invalid = kind === 'Radial'
      ? [['Pattern instances', '0', '6'], ['Pattern instances', '', '6'], ['Pattern step angle', '0', '45'], ['Pattern step angle', '', '45'], ['Pattern center X', '', '0']]
      : [['Pattern columns', '0', '4'], ['Pattern rows', '', '2'], ['Pattern X pitch', '0', '6'], ['Pattern X pitch', '', '6'], ['Pattern Y pitch', '0', '5']];
    for (const [label, bad, restore] of invalid) {
      await fill(label, bad);
      await expect(editor.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
      await expect(canvas).toHaveAttribute('data-corner-preview', 'false');
      await expect(editor.getByRole('alert')).toBeVisible();
      await editor.getByLabel(label, {exact: true}).press('Enter');
      assert.deepEqual((await download(`${prefix}-invalid-${label.replaceAll(' ', '-')}-${bad || 'blank'}`)).doc, current, 'invalid preview cannot persist stale settings');
      await fill(label, restore);
      await expect(editor.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
    }
    await page.setViewportSize({width: 1100, height: 705});
    await tool('Fit extents');
    await screenshot(`${prefix}-edit-1100x705`);
    await confirm(kind === 'Radial' ? 6 : 8);
    const edited = await download(`${prefix}-edited`);
    assertPattern(edited.doc, kind);
    await reopenList(); await fill(kind === 'Radial' ? 'Pattern instances' : 'Pattern columns', 5);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    assert.deepEqual((await download(`${prefix}-edit-canceled`)).doc, edited.doc);
    await tool('Undo'); assert.deepEqual((await download(`${prefix}-undo-settings`)).doc, current);
    await tool('Redo'); assert.deepEqual((await download(`${prefix}-redo-settings`)).doc, edited.doc);

    await load(edited.text, edited.doc.entities.length);
    const reopened = (await download(`${prefix}-reopened`)).doc;
    assert.equal(reopened.metadata.sourceSha256, createHash('sha256').update(edited.text).digest('hex'));
    assert.deepEqual(reopened, {...edited.doc, metadata: {...edited.doc.metadata, sourceSha256: reopened.metadata.sourceSha256}});
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    const linkedMember = edited.doc.entities.find(e => e.id === patterns(edited.doc)[0].members[0].entityId);
    await drag(linkedMember, {x: .6, y: .4});
    await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
    assert.deepEqual((await download(`${prefix}-reopened-dependent-refused`)).doc, reopened);
    await reopenList();
    await expect(editor.getByLabel('Pattern layout', {exact: true})).toHaveValue(kind);
    await fill(kind === 'Radial' ? 'Pattern step angle' : 'Pattern X pitch', kind === 'Radial' ? 40 : 7);
    await confirm(edited.doc.entities.length);
    const again = (await download(`${prefix}-reopened-edited`)).doc;
    assertPattern(again, kind);
    await reopenList(); await tool('Unlink pattern');
    const unlink = page.getByRole('group', {name: 'Unlink pattern confirmation'});
    await expect(unlink).toBeVisible(); await unlink.getByRole('button', {name: 'Keep linked', exact: true}).click();
    await tool('Unlink pattern'); await unlink.getByRole('button', {name: 'Unlink', exact: true}).click();
    await expect(editor).toBeHidden();
    const unlinked = (await download(`${prefix}-unlinked`)).doc;
    assert.equal(patterns(unlinked).length, 0); assert.deepEqual(unlinked.entities, again.entities);
    await tool('Undo'); assert.deepEqual((await download(`${prefix}-undo-unlink`)).doc, again);
    await tool('Redo'); assert.deepEqual((await download(`${prefix}-redo-unlink`)).doc, unlinked);
    const formerMember = unlinked.entities.find(e => e.id === patterns(again)[0].members[0].entityId);
    await drag(formerMember, {x: .5, y: .25});
    const independent = (await download(`${prefix}-independent`)).doc;
    const changed = independent.entities.find(e => e.id === formerMember.id);
    gestureClose(xy(changed.center).x, xy(formerMember.center).x + .5, 'unlinked independent X');
    gestureClose(xy(changed.center).y, xy(formerMember.center).y + .25, 'unlinked independent Y');
    assert.deepEqual(independent.entities.filter(e => e.id !== formerMember.id), unlinked.entities.filter(e => e.id !== formerMember.id));
    await screenshot(`${prefix}-independent-1100x705`);
  });
  await run('perforation-grid', async () => {
    await page.setViewportSize({width:1188,height:761});
    await load(); await openNew();
    await editor.getByLabel('Pattern layout', {exact:true}).selectOption('Staggered');
    await fill('Pattern X pitch',3); await fill('Pattern Y pitch',3);
    await fill('Pattern columns',20); await fill('Pattern rows',20);
    await tool('Fit pattern preview');
    await screenshot('perforation-preview');
    await confirm(400);
    const grid = await download('perforation-grid');
    assertPattern(grid.doc,'Staggered');
    assert.equal(grid.doc.entities.length,400);
    await reopenList();
    await fill('Pattern rows',100000);
    await expect(editor.getByRole('button',{name:'Confirm',exact:true})).toBeDisabled();
    await expect(editor.getByRole('alert')).toBeVisible();
    await editor.getByRole('button',{name:'Cancel',exact:true}).click();
    assert.deepEqual((await download('perforation-limit-refused')).doc,grid.doc);
    await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count','1');
    await tool('Redo'); await expect(canvas).toHaveAttribute('data-entity-count','400');
    await load(grid.text,400);
    assertPattern((await download('perforation-reopened')).doc,'Staggered');
  });
  assert.deepEqual(errors, [], 'no browser/circuit errors');
  const build = await page.locator('.app-brand').getAttribute('title');
  await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
  assert.ok(results.every(r => r.passed), `${results.filter(r => !r.passed).length} editable-pattern scenarios failed; see ${output}/results.json`);
} finally {
  await browser.close();
}
