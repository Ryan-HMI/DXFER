import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com']
  .includes(new URL(url).hostname), 'Isolated local/dev QA only');
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-pattern-center-constraints';
const only = process.env.DXFER_CASES?.split(',').filter(Boolean);
await mkdir(output, {recursive: true});
const fixture = peerKind => ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES',
  '0', 'CIRCLE', '5', '100', '8', '0', '10', '10', '20', '10', '40', '1',
  '0', peerKind === 'circle' ? 'CIRCLE' : 'POINT', '5', '200', '8', '0', '10', '8', '20', '15',
  ...(peerKind === 'circle' ? ['40', '0.6'] : []), '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const native = text => {
  const pairs = text.trim().split(/\r?\n/);
  let hex = '';
  for (let i = 0; i < pairs.length; i += 2) if (pairs[i].trim() === '310') hex += pairs[i + 1].trim();
  assert.ok(hex, 'Native document must be exported');
  return JSON.parse(Buffer.from(hex, 'hex'));
};
const near = (a, b, message, tolerance = 1e-7) => assert.ok(Math.abs(a - b) < tolerance, `${message}: ${a} != ${b}`);
const location = entity => entity.kind === 'circle' ? entity.center : entity.location;
const content = doc => ({entities: doc.entities, constraints: doc.constraints,
  dimensions: doc.dimensions, patterns: doc.metadata.patterns ?? []});
const semanticRef = key => {
  const match = /^(.+)\|point\|(center|point)\|[^|]+\|[^|]+$/.exec(key);
  return match ? `${match[1]}${match[2] === 'center' ? ':center' : ''}` : key;
};
const assertPattern = doc => {
  assert.equal(doc.metadata.patterns?.length, 1);
  const pattern = doc.metadata.patterns[0];
  assert.equal(pattern.kind, 'Radial');
  assert.equal(pattern.count, 4);
  assert.equal(pattern.fullCircle, true);
  assert.equal(pattern.sourceEntityIds.length, 1);
  assert.equal(pattern.members.length, 3);
  assert.equal(doc.entities.length, 5);
  const source = doc.entities.find(e => e.id === pattern.sourceEntityIds[0]);
  for (const member of pattern.members) {
    assert.equal(member.sourceEntityId, source.id);
    const copy = doc.entities.find(e => e.id === member.entityId);
    const angle = member.column * Math.PI / 2;
    const dx = source.center.x - pattern.center.x, dy = source.center.y - pattern.center.y;
    near(copy.center.x, pattern.center.x + dx * Math.cos(angle) - dy * Math.sin(angle), 'Pattern member X');
    near(copy.center.y, pattern.center.y + dx * Math.sin(angle) + dy * Math.cos(angle), 'Pattern member Y');
    near(copy.radius, source.radius, 'Pattern member radius');
  }
  return {pattern, source, member: doc.entities.find(e => e.id === pattern.members.find(m => m.column === 1).entityId)};
};
const results = [], errors = [], seeds = new Map();
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
      stable = previous === view ? stable + 1 : 0;
      previous = view;
      return stable;
    }, {intervals: [100, 150, 150]}).toBeGreaterThanOrEqual(2);
  };
  const load = async (text, count) => {
    if (await context.isVisible()) await context.press('Escape');
    if (await editor.isVisible()) await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await canvas.press('Escape');
    await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count', '0');
    await page.locator('input[type=file]').setInputFiles({name: 'pattern-center-constraints.dxf',
      mimeType: 'application/dxf', buffer: Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count', String(count));
    await fit();
    await canvas.press('Escape');
  };
  const screen = point => canvas.evaluate((c, p) => ({
    x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
    y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY
  }), point);
  const capture = async name => {
    await page.screenshot({path: `${output}/${name}.png`});
    await writeFile(`${output}/${name}.json`, JSON.stringify({
      dataset: await canvas.evaluate(c => ({...c.dataset})),
      status: await page.locator('.dxfer-command-bar').innerText()
    }, null, 2));
  };
  const hoverPoint = async (entity, name) => {
    const p = await screen(location(entity));
    let key;
    await expect.poll(async () => {
      await page.mouse.move(p.x + 24, p.y + 24);
      await page.mouse.move(p.x, p.y, {steps: 2});
      key = await canvas.getAttribute('data-hovered-id');
      return entity.kind === 'circle' ? Boolean(key?.startsWith(`${entity.id}|point|center|`))
        : key === entity.id || Boolean(key?.startsWith(`${entity.id}|point|point|`));
    }, {timeout: 3000, intervals: [100, 150, 250], message: `${name}: actual center/point must be pickable`}).toBe(true);
    await capture(`${name}-hover`);
    return {p, key};
  };
  const seed = async peerKind => {
    if (seeds.has(peerKind)) return seeds.get(peerKind);
    await load(fixture(peerKind), 2);
    const p = await screen({x: 10 + Math.cos(0.4), y: 10 + Math.sin(0.4)});
    await page.mouse.move(p.x, p.y);
    await page.mouse.click(p.x, p.y);
    await expect(canvas).toHaveAttribute('data-selected-count', '1');
    await page.mouse.click(p.x, p.y, {button: 'right'});
    await context.getByRole('button', {name: 'Pattern', exact: true}).click();
    await expect(editor).toBeVisible();
    await editor.getByLabel('Pattern layout', {exact: true}).selectOption('Radial');
    for (const [label, value] of [['Pattern instances', '4'], ['Pattern center X', '5'], ['Pattern center Y', '10']]) {
      await editor.getByLabel(label, {exact: true}).fill(value);
      await page.waitForTimeout(150);
    }
    await editor.getByLabel('Full circle', {exact: true}).check();
    await expect(editor.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
    await editor.getByRole('button', {name: 'Confirm', exact: true}).click();
    await expect(editor).toBeHidden();
    await expect(canvas).toHaveAttribute('data-entity-count', '5');
    await canvas.press('Escape');
    await fit();
    const created = await download(`${peerKind}-actual-radial-pattern`);
    assertPattern(created.doc);
    seeds.set(peerKind, created);
    return created;
  };
  const history = async (before, after, name) => {
    await tool('Undo');
    assert.deepEqual(content((await download(`${name}-undo`)).doc), content(before));
    await tool('Redo');
    assert.deepEqual(content((await download(`${name}-redo`)).doc), content(after));
  };

  const cases = [];
  for (const peerKind of ['circle', 'point']) for (const workflow of ['preselect', 'tool-first'])
    for (const memberFirst of [true, false]) cases.push({peerKind, workflow, memberFirst,
      name: `${peerKind}-${workflow}-${memberFirst ? 'member-first' : 'peer-first'}`});
  for (const memberFirst of [true, false]) cases.push({peerKind: 'circle', workflow: 'preselect', memberFirst,
    mode: 'fixed', name: `fixed-external-${memberFirst ? 'member-first' : 'peer-first'}`});
  cases.push({peerKind: 'circle', mode: 'direct', name: 'direct-generated-drag-refused'});
  for (const {name, peerKind, workflow, memberFirst, mode = 'free'} of cases) {
      if (only && !only.includes(name)) continue;
      let phase = 'create-pattern';
      try {
        const original = await seed(peerKind);
        await load(original.text, 5);
        let before = (await download(`${name}-before`)).doc;
        const {pattern, source, member} = assertPattern(before);
        const family = new Set([source.id, ...pattern.members.map(m => m.entityId)]);
        const peer = before.entities.find(e => !family.has(e.id));
        assert.equal(peer.kind, peerKind);
        await canvas.press('Escape');
        if (mode === 'direct') {
          phase = 'direct-generated-drag';
          await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
          await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
          await hoverPoint(member, `${name}-member`);
          const destination = await screen({x: member.center.x + 0.6, y: member.center.y + 0.4});
          await page.mouse.down();
          try { await page.mouse.move(destination.x, destination.y, {steps: 12}); }
          finally { await page.mouse.up(); }
          await page.waitForTimeout(300);
          await capture(`${name}-after-drag`);
          const refused = await download(`${name}-refused`);
          assert.deepEqual(content(refused.doc), content(before), 'No independent member move or inverse pattern solve');
          await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
          await expect(page.getByRole('button', {name: 'Redo', exact: true})).toBeDisabled();
          results.push({name, passed: true});
          console.log(`PASS ${name}: generated center is pickable but direct drag changes neither document nor history`);
          await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
          continue;
        }
        const beforeFix = before;
        if (mode === 'fixed') {
          phase = 'fix-external-peer';
          const {p} = await hoverPoint(peer, `${name}-fix-peer`);
          await page.mouse.click(p.x, p.y);
          await expect(canvas).toHaveAttribute('data-selected-count', '1');
          await tool('Fix');
          await canvas.press('Escape');
          before = (await download(`${name}-fixed-baseline`)).doc;
          assert.deepEqual(before.entities, beforeFix.entities);
          assert.deepEqual(before.metadata.patterns, beforeFix.metadata.patterns);
          const fixes = before.constraints.filter(c => c.kind === 'Fix');
          assert.equal(fixes.length, 1);
          assert.equal(fixes[0].state, 'Satisfied');
          assert.ok(fixes[0].referenceKeys.map(semanticRef).some(key => key === peer.id || key === `${peer.id}:center`));
        }
        if (workflow === 'tool-first') await tool('Coincident');
        const picks = memberFirst ? [member, peer] : [peer, member];
        const selected = [];
        for (const [index, entity] of picks.entries()) {
          phase = entity.id === member.id ? 'pick-generated-center' : 'pick-external-peer';
          const {p, key} = await hoverPoint(entity, `${name}-${entity.id === member.id ? 'member' : 'peer'}`);
          await page.mouse.click(p.x, p.y);
          await page.waitForTimeout(180);
          selected.push(key);
          if (index === 0 || workflow === 'preselect') {
            await expect(canvas).toHaveAttribute('data-selected-count', String(index + 1));
            assert.deepEqual((await canvas.getAttribute('data-selected-keys')).split(',').sort(), [...selected].sort());
          }
        }
        phase = 'apply-constraint';
        if (workflow === 'preselect') await tool('Coincident');
        await page.waitForTimeout(300);
        await capture(`${name}-after-command`);
        await canvas.press('Escape');
        const applied = await download(`${name}-applied`);
        assert.deepEqual(applied.doc.metadata.patterns, before.metadata.patterns, 'Pattern definition/membership preserved');
        assert.deepEqual(applied.doc.entities.filter(e => family.has(e.id)), before.entities.filter(e => family.has(e.id)),
          'Constraint must not move the pattern source or generated members');
        assert.deepEqual(applied.doc.dimensions, before.dimensions);
        const added = applied.doc.constraints.filter(c => !before.constraints.some(old => old.id === c.id));
        if (mode === 'fixed') {
          assert.deepEqual(applied.doc.entities, before.entities, 'Fixed external conflict cannot move peers or solve the pattern backwards');
          assert.deepEqual(applied.doc.constraints.filter(c => before.constraints.some(old => old.id === c.id)), before.constraints);
          assert.ok(added.length <= 1, 'Safe refusal or one retained failed intent');
          for (const constraint of added) {
            assert.equal(constraint.kind, 'Coincident');
            assert.equal(constraint.state, 'Unsatisfied');
            assert.deepEqual(constraint.referenceKeys.map(semanticRef).sort(), [`${member.id}:center`, `${peer.id}:center`].sort());
          }
          if (added.length) await history(before, applied.doc, name);
          else await history(beforeFix, before, `${name}-no-extra-history-entry`);
          await load(applied.text, 5);
          assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(applied.doc));
          results.push({name, passed: true});
          console.log(`PASS ${name}: actual Fix, immutable pattern, guarded refusal, history/reopen`);
          await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
          continue;
        }
        assert.equal(added.length, 1, 'Coincident must persist, not be rejected by pattern reconciliation');
        assert.equal(added[0].kind, 'Coincident');
        assert.equal(added[0].state, 'Satisfied');
        assert.deepEqual(added[0].referenceKeys.map(semanticRef).sort(),
          [`${member.id}:center`, peerKind === 'circle' ? `${peer.id}:center` : peer.id].sort());
        const movedPeer = applied.doc.entities.find(e => e.id === peer.id);
        near(location(movedPeer).x, member.center.x, 'External peer moves to member X');
        near(location(movedPeer).y, member.center.y, 'External peer moves to member Y');
        if (peerKind === 'circle') near(movedPeer.radius, peer.radius, 'External radius preserved');
        assertPattern(applied.doc);
        phase = 'history-reopen';
        await history(before, applied.doc, name);
        await load(applied.text, 5);
        assert.deepEqual(content((await download(`${name}-reopened`)).doc), content(applied.doc));
        phase = 'source-edit';
        await hoverPoint(source, `${name}-source`);
        const destination = {x: source.center.x + 0.8, y: source.center.y + 0.5};
        const to = await screen(destination);
        await page.mouse.down();
        try { await page.mouse.move(to.x, to.y, {steps: 12}); }
        finally { await page.mouse.up(); }
        await page.waitForTimeout(300);
        await capture(`${name}-after-source-drag`);
        const edited = await download(`${name}-source-edited`);
        const updated = assertPattern(edited.doc);
        assert.deepEqual(updated.pattern, pattern, 'Source edit keeps pattern settings and identities');
        near(updated.source.center.x, destination.x, 'Source drag X', 1e-4);
        near(updated.source.center.y, destination.y, 'Source drag Y', 1e-4);
        near(updated.source.radius, source.radius, 'Source radius unchanged');
        const following = edited.doc.entities.find(e => e.id === peer.id);
        near(location(following).x, updated.member.center.x, 'External peer follows regenerated member X');
        near(location(following).y, updated.member.center.y, 'External peer follows regenerated member Y');
        if (peerKind === 'circle') near(following.radius, peer.radius, 'Following preserves radius');
        assert.ok(edited.doc.constraints.every(c => c.state === 'Satisfied'));
        assert.deepEqual(edited.doc.constraints.find(c => c.id === added[0].id).referenceKeys, added[0].referenceKeys);
        if (name === 'circle-preselect-member-first') {
          phase = 'regenerated-history-reopen-reedit';
          await history(applied.doc, edited.doc, `${name}-source-edit`);
          await load(edited.text, 5);
          assert.deepEqual(content((await download(`${name}-regenerated-reopened`)).doc), content(edited.doc));
          await hoverPoint(updated.source, `${name}-source-again`);
          const nextDestination = {x: updated.source.center.x + 0.35, y: updated.source.center.y + 0.2};
          const nextScreen = await screen(nextDestination);
          await page.mouse.down();
          try { await page.mouse.move(nextScreen.x, nextScreen.y, {steps: 12}); }
          finally { await page.mouse.up(); }
          await page.waitForTimeout(300);
          await capture(`${name}-after-source-reedit`);
          const again = (await download(`${name}-source-edited-again`)).doc;
          const regenerated = assertPattern(again);
          assert.deepEqual(regenerated.pattern, pattern, 'Second edit preserves pattern definition and member IDs');
          near(regenerated.source.center.x, nextDestination.x, 'Second source drag X', 1e-4);
          near(regenerated.source.center.y, nextDestination.y, 'Second source drag Y', 1e-4);
          near(regenerated.source.radius, source.radius, 'Second source edit preserves radius');
          const peerAgain = again.entities.find(e => e.id === peer.id);
          near(location(peerAgain).x, regenerated.member.center.x, 'Reopened external peer follows X again');
          near(location(peerAgain).y, regenerated.member.center.y, 'Reopened external peer follows Y again');
          near(peerAgain.radius, peer.radius, 'Second edit preserves external radius');
          assert.ok(again.constraints.every(c => c.state === 'Satisfied'));
          assert.deepEqual(again.constraints.find(c => c.id === added[0].id).referenceKeys, added[0].referenceKeys);
          assert.deepEqual(again.dimensions, before.dimensions);
        }
        await capture(`${name}-final`);
        results.push({name, passed: true});
        console.log(`PASS ${name}: real pattern, generated center pick, external-peer-only move, history/reopen/source follow`);
      } catch (error) {
        results.push({name, passed: false, phase, error: error.stack});
        console.error(`FAIL ${name} [${phase}]: ${error.message}`);
        await capture(`${name}-failure`);
        await ready();
      }
      await writeFile(`${output}/results.json`, JSON.stringify({url, build, results, errors}, null, 2));
    }
  assert.ok(results.length, 'At least one matching case must run');
  assert.deepEqual(errors, [], 'No browser/circuit errors');
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
} finally {
  await browser.close();
}
