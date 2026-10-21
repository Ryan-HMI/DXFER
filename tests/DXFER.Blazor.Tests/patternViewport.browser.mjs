import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5038';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-pattern-viewport';
await mkdir(output, {recursive: true});
const fixture = '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
const native = text => { const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex')); };
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
const errors = [];
const layouts = [], heldMoves = [];
let page, failure = null, passed = false;
const close = (actual, expected, message, tolerance = 1e-5) =>
  assert.ok(Math.abs(actual - expected) < tolerance, `${message}: ${actual} != ${expected}`);
const geometry = doc => ({entities: doc.entities, constraints: doc.constraints, dimensions: doc.dimensions,
  patterns: doc.metadata.patterns});
const radialGeometry = (doc, expectedCenter, originalSource) => {
  const pattern = doc.metadata.patterns[0];
  close(pattern.center.x, expectedCenter.x, 'native radial center X');
  close(pattern.center.y, expectedCenter.y, 'native radial center Y');
  const source = doc.entities.find(entity => entity.id === pattern.sourceEntityIds[0]);
  assert.deepEqual(source, originalSource, 'moving pattern center never moves or rewrites its source');
  assert.equal(doc.entities.length, pattern.count);
  assert.equal(pattern.members.length, pattern.count - 1);
  for (const member of pattern.members) {
    const entity = doc.entities.find(item => item.id === member.entityId);
    const angle = member.column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) * Math.PI / 180;
    const dx = source.center.x - expectedCenter.x, dy = source.center.y - expectedCenter.y;
    close(entity.center.x, expectedCenter.x + dx * Math.cos(angle) - dy * Math.sin(angle), 'native member X');
    close(entity.center.y, expectedCenter.y + dx * Math.sin(angle) + dy * Math.cos(angle), 'native member Y');
    close(entity.radius, source.radius, 'native member radius');
  }
};
try {
  page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.setDefaultTimeout(10000);
  page.on('dialog', d => d.accept()); page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text()); });
  await page.goto(url);
  await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const build = await page.locator('.app-brand').getAttribute('title'); console.log(build);
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
  const canvas = page.locator('canvas.drawing-canvas'), widget = page.getByRole('region', {name: 'Viewport pattern parameters'});
  const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
  const field = name => widget.getByRole('spinbutton', {name: `Viewport pattern ${name}`, exact: true});
  const cancel = () => widget.getByRole('button', {name: 'Cancel pattern changes', exact: true}).click();
  const tool = async name => { await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150); };
  const menu = async name => { await tool('Open application menu'); await page.getByRole('button', {name:'File',exact:true}).hover(); await tool(name); };
  const download = async name => { const pending = page.waitForEvent('download'); await menu('Download DXF');
    const text = await readFile(await (await pending).path(),'utf8'); await writeFile(`${output}/${name}.dxf`,text); return {text,doc:native(text)}; };
  const load = async (text, count) => { await canvas.press('Escape'); await menu('New drawing');
    await expect(canvas).toHaveAttribute('data-entity-count','0');
    await page.locator('input[type=file]').setInputFiles({name:'viewport-pattern.dxf',mimeType:'application/dxf',buffer:Buffer.from(text)});
    await expect(canvas).toHaveAttribute('data-entity-count',String(count)); await tool('Fit extents'); await canvas.press('Escape'); await page.waitForTimeout(300); };
  const click = async (x,y,button='left') => { const p = await canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,
    y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),{x,y}); await page.mouse.click(p.x,p.y,{button}); await page.waitForTimeout(200); };
  const rightClickMember = async doc => {
    const point = await canvas.evaluate((c,entities) => {
      const r=c.getBoundingClientRect();
      for (const e of entities.slice(1)) {
        const x=r.left+(e.center.x+e.radius*.6)*+c.dataset.scale+ +c.dataset.offsetX;
        const y=r.top-(e.center.y+e.radius*.8)*+c.dataset.scale+ +c.dataset.offsetY;
        if (document.elementFromPoint(x,y)===c) return {x,y};
      }
      throw new Error('No unobscured pattern member');
    }, doc.entities);
    await page.mouse.click(point.x,point.y,{button:'right'}); await page.waitForTimeout(200);
  };
  const set = async (name,value) => { const input=field(name); await input.fill(value); await input.press('Enter'); await expect(input).toHaveValue(value); };
  const layout = async name => {
    await expect(widget).toBeVisible();
    const c = await canvas.boundingBox(), root = await widget.boundingBox();
    close(root.x, c.x, 'overlay origin X', 1); close(root.y, c.y, 'overlay origin Y', 1);
    close(root.width, c.width, 'overlay fills canvas width', 1); close(root.height, c.height, 'overlay fills canvas height', 1);
    const style = await widget.evaluate(node => ({pointerEvents: getComputedStyle(node).pointerEvents,
      background: getComputedStyle(node).backgroundColor}));
    assert.equal(style.pointerEvents, 'none', 'transparent root does not intercept empty canvas');
    assert.equal(style.background, 'rgba(0, 0, 0, 0)');
    const overlaps = (a, b) => a.x < b.x + b.width - .5 && a.x + a.width > b.x + .5
      && a.y < b.y + b.height - .5 && a.y + a.height > b.y + .5;
    const palettes = [];
    for (const palette of await page.locator('.dxfer-tool-group, .dxfer-sync-control-bar').all()) {
      if (await palette.isVisible()) palettes.push(await palette.boundingBox());
    }
    const groups = [];
    for (const group of await widget.locator('[data-pattern-anchor]').all()) {
      const anchor = await group.getAttribute('data-pattern-anchor');
      await expect(group, `${name}: ${anchor} remains visible`).toBeVisible();
      const box = await group.boundingBox();
      assert.ok(box.x >= c.x - 1 && box.y >= c.y - 1 && box.x + box.width <= c.x + c.width + 1
        && box.y + box.height <= c.y + c.height + 1, `${name}: ${anchor} stays within canvas`);
      for (const palette of palettes) assert.ok(!overlaps(box, palette), `${name}: ${anchor} clears palette`);
      for (const previous of groups) assert.ok(!overlaps(box, previous.box), `${name}: ${anchor} clears ${previous.anchor}`);
      groups.push({anchor, box});
    }
    assert.ok(groups.length >= 3, 'independent groups, not one card');
    layouts.push({name, viewport: page.viewportSize(), canvas: c, groups});
    await page.screenshot({path:`${output}/${name}.png`});
  };
  const visibleControls = async radial => {
    await expect(widget).toBeVisible();
    for (const anchor of radial ? ['source', 'angle', 'actions', 'center-handle'] : ['x', 'y', 'actions'])
      await expect(widget.locator(`[data-pattern-anchor="${anchor}"]`)).toBeVisible();
    await expect(widget.locator('[data-pattern-center-handle]')).toHaveCount(radial ? 1 : 0);
  };
  const hiddenControls = async () => {
    await expect(widget).toHaveCount(0);
    await expect(page.locator('[data-pattern-anchor], [data-pattern-center-handle]')).toHaveCount(0);
  };
  const centerDrag = async (doc, delta, name) => {
    await visibleControls(true);
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    const pattern = doc.metadata.patterns[0], source = doc.entities.find(e => e.id === pattern.sourceEntityIds[0]);
    const handle = widget.locator('[data-pattern-center-handle]');
    await expect(handle).toHaveAttribute('title', 'Move pattern center');
    await expect(handle).toHaveAccessibleName('Move pattern center');
    const initialBox = await handle.boundingBox();
    const screenCenter = await canvas.evaluate((c, point) => ({x: c.getBoundingClientRect().left + point.x * +c.dataset.scale + +c.dataset.offsetX,
      y: c.getBoundingClientRect().top - point.y * +c.dataset.scale + +c.dataset.offsetY}), pattern.center);
    close(initialBox.x + initialBox.width / 2, screenCenter.x, 'handle is at exact world center X', 1);
    close(initialBox.y + initialBox.height / 2, screenCenter.y, 'handle is at exact world center Y', 1);
    // A slightly off-center grab must preserve its offset throughout the gesture.
    const start = {x: screenCenter.x + 3, y: screenCenter.y - 2};
    await page.mouse.move(start.x, start.y); await page.mouse.down();
    try {
      await expect(widget).toHaveAttribute('data-center-dragging', 'true');
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
      await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
      const scale = +(await canvas.getAttribute('data-scale'));
      let pixels = await canvas.screenshot();
      for (const fraction of [.25, .6, 1]) {
        const target = {x: pattern.center.x + delta.x * fraction, y: pattern.center.y + delta.y * fraction};
        await page.mouse.move(start.x + delta.x * scale * fraction, start.y - delta.y * scale * fraction, {steps: 8});
        await expect.poll(async () => Math.hypot(+(await field('center X').inputValue()) - target.x,
          +(await field('center Y').inputValue()) - target.y), {message: 'held motion updates server preview before release'}).toBeLessThan(1e-5);
        await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
        await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
        close(+(await widget.getAttribute('data-source-x')), source.center.x, 'held source X unchanged');
        close(+(await widget.getAttribute('data-source-y')), source.center.y, 'held source Y unchanged');
        const box = await handle.boundingBox();
        close(box.x + box.width / 2, screenCenter.x + delta.x * scale * fraction, 'held handle X', 1);
        close(box.y + box.height / 2, screenCenter.y - delta.y * scale * fraction, 'held handle Y', 1);
        const member = pattern.members[0];
        const angle = member.column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) * Math.PI / 180;
        const dx = source.center.x - target.x, dy = source.center.y - target.y;
        const expectedCircle = {x: target.x + dx * Math.cos(angle) - dy * Math.sin(angle),
          y: target.y + dx * Math.sin(angle) + dy * Math.cos(angle), radius: source.radius};
        const ink = await canvas.evaluate((c, circle) => {
          const ratio = c.width / c.clientWidth, ctx = c.getContext('2d');
          return Array.from({length: 8}, (_, i) => {
            const angle = .23 + i * Math.PI / 4;
            const x = Math.round(((circle.x + circle.radius * Math.cos(angle)) * +c.dataset.scale + +c.dataset.offsetX) * ratio);
            const y = Math.round((-(circle.y + circle.radius * Math.sin(angle)) * +c.dataset.scale + +c.dataset.offsetY) * ratio);
            if (x < 4 || y < 4 || x + 4 >= c.width || y + 4 >= c.height) return false;
            const data = ctx.getImageData(x - 4, y - 4, 9, 9).data;
            const min = [255, 255, 255], max = [0, 0, 0];
            for (let pixel = 0; pixel < data.length; pixel += 4)
              for (let channel = 0; channel < 3; channel++) {
                min[channel] = Math.min(min[channel], data[pixel + channel]);
                max[channel] = Math.max(max[channel], data[pixel + channel]);
              }
            return max.some((value, channel) => value - min[channel] > 35);
          });
        }, expectedCircle);
        assert.ok(ink.filter(Boolean).length >= 5, `held preview paints the moved member circumference, not just an overlay guide: ${ink}`);
        const nextPixels = await canvas.screenshot();
        assert.notDeepEqual(nextPixels, pixels, 'canvas preview changes during continuous held movement');
        pixels = nextPixels;
        heldMoves.push({name, fraction, target, expectedCircle, ink, viewport: page.viewportSize()});
      }
      await layout(`${name}-held`);
    } finally { await page.mouse.up(); }
    await expect(widget).not.toHaveAttribute('data-center-dragging', 'true');
    await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeEnabled();
    const target = {x: pattern.center.x + delta.x, y: pattern.center.y + delta.y};
    close(+(await field('center X').inputValue()), target.x, 'released staged X');
    close(+(await field('center Y').inputValue()), target.y, 'released staged Y');
    assert.deepEqual(geometry((await download(`${name}-uncommitted`)).doc), geometry(doc),
      'releasing center handle stages preview only; native geometry and center remain committed values');
    return target;
  };
  await load(fixture,1); await click(10.6,10.8); await tool('Rectangular / staggered pattern');
  await expect(widget).toBeVisible();
  await set('columns','4'); await set('rows','3');
  const pitch=field('X pitch'); await pitch.focus(); await page.keyboard.press('ControlOrMeta+A');
  for(const key of ['1','.','2','5']) { await page.keyboard.press(key); await page.waitForTimeout(80); }
  await expect(pitch).toHaveValue('1.25'); await pitch.press('Enter');
  await expect(editor.getByRole('spinbutton',{name:'Pattern X pitch',exact:true})).toHaveValue('1.25');
  await editor.getByRole('spinbutton',{name:'Pattern Y pitch',exact:true}).fill('2.5');
  await editor.getByRole('spinbutton',{name:'Pattern Y pitch',exact:true}).press('Tab');
  await expect(field('Y pitch')).toHaveValue('2.5');
  await set('columns','0'); await expect(widget.getByRole('button',{name:'Confirm',exact:true})).toBeDisabled();
  await expect(widget.getByRole('alert')).toBeVisible(); await set('columns','4');
  await tool('Fit pattern preview'); await layout('rectangular');
  const viewBefore = await canvas.getAttribute('data-scale');
  const clearPoint = await canvas.evaluate(c => {
    const r = c.getBoundingClientRect();
    for (let y = r.top + 80; y < r.bottom - 80; y += 40)
      for (let x = r.left + 200; x < r.right - 80; x += 40)
        if (document.elementFromPoint(x,y) === c && document.elementFromPoint(x+40,y+30) === c) return {x,y};
    throw new Error('No clear canvas point for wheel/pan');
  });
  await page.mouse.move(clearPoint.x,clearPoint.y);
  await page.mouse.wheel(0,160); await page.waitForTimeout(300);
  assert.notEqual(await canvas.getAttribute('data-scale'), viewBefore);
  const panBefore = await canvas.getAttribute('data-offset-x');
  await page.mouse.down({button:'middle'}); await page.mouse.move(clearPoint.x + 40,clearPoint.y + 30,{steps:8});
  await page.mouse.up({button:'middle'}); await page.waitForTimeout(200);
  assert.notEqual(await canvas.getAttribute('data-offset-x'), panBefore); await layout('rectangular-pan-zoom');
  await page.setViewportSize({width:1100,height:705}); await page.waitForTimeout(300); await layout('rectangular-1100');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click(); await expect(canvas).toHaveAttribute('data-entity-count','12');
  const saved=await download('rectangular-saved'), pattern=saved.doc.metadata.patterns[0];
  assert.equal(pattern.columns,4); assert.equal(pattern.rows,3); assert.equal(pattern.xPitch,1.25); assert.equal(pattern.yPitch,2.5);
  await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count','1'); await tool('Redo'); await expect(canvas).toHaveAttribute('data-entity-count','12');
  await load(saved.text,12);
  const context=page.getByRole('dialog',{name:'Drawing context commands'});
  await click(14.35,15.8,'right'); await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await visibleControls(false); await layout('context-shown');
  await click(14.35,15.8,'right'); await page.screenshot({path:`${output}/context-hide-menu.png`});
  await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click(); await hiddenControls();
  await click(10.6,10.8); await expect(widget).toHaveCount(0);
  await click(14.35,15.8,'right'); await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await visibleControls(false);
  await widget.getByRole('button',{name:'Edit pattern',exact:true}).click(); await set('columns','5');
  await cancel(); assert.equal((await download('cancelled')).doc.entities.length,12);
  await canvas.press('Escape'); await click(10.6,10.8); await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
  await editor.getByRole('combobox',{name:'Pattern layout'}).selectOption('Staggered');
  await set('X pitch','3.25'); await set('Y pitch','4.5'); await tool('Fit pattern preview'); await layout('staggered');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  const staggered=await download('staggered-saved'); assert.equal(staggered.doc.metadata.patterns[0].kind,'Staggered');
  assert.equal(staggered.doc.metadata.patterns[0].xPitch,3.25); assert.equal(staggered.doc.metadata.patterns[0].yPitch,4.5);
  await page.setViewportSize({width:1440,height:1000}); await load(fixture,1);
  const radialSeed = (await download('radial-source')).doc.entities[0];
  await click(10.6,10.8); await tool('Radial pattern');
  await set('instances','8'); await expect(field('step angle')).toHaveValue('45');
  const fullCircle = widget.getByRole('button', {name: 'Full circle', exact: true});
  const stepMode = widget.getByRole('button', {name: 'Angle between instances', exact: true});
  await expect(fullCircle).toHaveAttribute('aria-pressed', 'true');
  await expect(field('step angle')).toHaveAttribute('readonly', '');
  await stepMode.click();
  await expect(stepMode).toHaveAttribute('aria-pressed', 'true');
  await expect(fullCircle).toHaveAttribute('aria-pressed', 'false');
  await set('step angle','30.25');
  await expect(editor.getByRole('spinbutton',{name:'Pattern step angle'})).toHaveValue('30.25');
  await set('center X', '1.25'); await set('center Y', '-2.5');
  await tool('Fit pattern preview'); await layout('radial');
  await page.setViewportSize({width:1100,height:705}); await page.waitForTimeout(300); await layout('radial-1100');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(canvas).toHaveAttribute('data-entity-count','8'); const radial=await download('radial-saved');
  assert.equal(radial.doc.metadata.patterns[0].count,8); assert.equal(radial.doc.metadata.patterns[0].stepAngleDegrees,30.25);
  assert.equal(radial.doc.metadata.patterns[0].fullCircle,false);
  const radialSource = radial.doc.entities.find(e => e.id === radial.doc.metadata.patterns[0].sourceEntityIds[0]);
  assert.deepEqual(radialSource, radialSeed, 'center coordinate entry preserves original source');
  radialGeometry(radial.doc, {x: 1.25, y: -2.5}, radialSource);
  await load(radial.text,8); await rightClickMember(radial.doc);
  await context.getByRole('button',{name:'Show pattern controls',exact:true}).click(); await visibleControls(true);
  await rightClickMember(radial.doc); await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click();
  await hiddenControls(); await rightClickMember(radial.doc);
  await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await visibleControls(true);
  await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
  await fullCircle.click(); await expect(field('step angle')).toHaveValue('45');
  await expect(fullCircle).toHaveAttribute('aria-pressed', 'true');
  await expect(stepMode).toHaveAttribute('aria-pressed', 'false');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  const full = await download('radial-reopened-edit'); assert.equal(full.doc.metadata.patterns[0].fullCircle,true);
  for (const viewport of [{width:1440,height:1000}, {width:1100,height:705}]) {
    await page.setViewportSize(viewport); await load(full.text,8);
    await rightClickMember(full.doc);
    const show = context.getByRole('button', {name:'Show pattern controls',exact:true});
    if (await show.count()) await show.click(); else await context.press('Escape');
    await visibleControls(true); await layout(`center-saved-${viewport.width}`);
    const source = full.doc.entities.find(e => e.id === full.doc.metadata.patterns[0].sourceEntityIds[0]);
    await centerDrag(full.doc, {x: 1.75, y: -1.25}, `center-cancel-${viewport.width}`);
    await cancel();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    assert.deepEqual(geometry((await download(`center-cancelled-${viewport.width}`)).doc), geometry(full.doc));
    const target = await centerDrag(full.doc, {x: 2.5, y: 1.25}, `center-commit-${viewport.width}`);
    await widget.getByRole('button', {name:'Confirm',exact:true}).click();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    const committed = await download(`center-committed-${viewport.width}`);
    radialGeometry(committed.doc, target, source);
    await tool('Undo');
    assert.deepEqual(geometry((await download(`center-undo-${viewport.width}`)).doc), geometry(full.doc), 'one undo restores pre-center-drag native state');
    await tool('Redo');
    assert.deepEqual(geometry((await download(`center-redo-${viewport.width}`)).doc), geometry(committed.doc));
    await load(committed.text,8); await rightClickMember(committed.doc);
    const reopen = context.getByRole('button', {name:'Show pattern controls',exact:true});
    if (await reopen.count()) await reopen.click(); else await context.press('Escape');
    await visibleControls(true);
    const next = await centerDrag(committed.doc, {x: -.75, y: .5}, `center-reopened-${viewport.width}`);
    await widget.getByRole('button', {name:'Confirm',exact:true}).click();
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
    radialGeometry((await download(`center-reopened-edited-${viewport.width}`)).doc, next, source);
  }
  assert.deepEqual(errors,[]); passed = true;
  console.log('PASS rectangular/staggered/radial groups, segmented modes, typing, context show/hide, continuous staged center drag, cancel, native source preservation, history and native reopen/edit at both viewports');
} catch (error) {
  failure = error.stack;
  await page?.screenshot({path: `${output}/pattern-viewport-failure.png`}).catch(() => {});
  throw error;
} finally {
  await writeFile(`${output}/pattern-viewport-results.json`, JSON.stringify({passed, failure, layouts, heldMoves, errors}, null, 2));
  await browser.close();
}
