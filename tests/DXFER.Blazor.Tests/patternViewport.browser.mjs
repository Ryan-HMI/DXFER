import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5038';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-pattern-viewport';
const draftFixtures = process.env.DXFER_DRAFT_CONTEXT_FIXTURES;
await mkdir(output, {recursive: true});
const fixture = '0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n5\n100\n8\n0\n10\n10\n20\n10\n40\n1\n0\nENDSEC\n0\nEOF\n';
const native = text => { const lines = text.trim().split(/\r?\n/); let hex = '';
  for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
  assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex')); };
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
const errors = [];
const layouts = [], heldMoves = [], draftContextCases = [];
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
  const context=page.getByRole('dialog',{name:'Drawing context commands'});
  const field = name => widget.getByRole('spinbutton', {name: `Viewport pattern ${name}`, exact: true});
  const centerField = axis => editor.getByRole('spinbutton', {name:`Pattern center ${axis}`,exact:true});
  const setCenter = async (axis,value) => {
    const input=centerField(axis); await input.fill(value); await input.press('Tab');
    await expect(input).toHaveValue(value);
    await expect(widget).toHaveAttribute(`data-center-${axis.toLowerCase()}`,value);
  };
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
    const radial = await widget.getAttribute('data-pattern-kind') === 'Radial';
    let sourceBounds = null;
    if (radial) {
      assert.deepEqual(groups.map(g=>g.anchor).sort(), ['center-handle','source'], 'one compact source card plus independent center handle');
      await expect(widget.getByLabel('Viewport pattern center X',{exact:true})).toHaveCount(0);
      await expect(widget.getByLabel('Viewport pattern center Y',{exact:true})).toHaveCount(0);
      const card=widget.locator('[data-pattern-anchor="source"]'), box=await card.boundingBox();
      assert.ok(box.width<=240 && box.height<=160, `${name}: compact radial card bounds ${box.width}x${box.height}`);
      // The imported source is independently known: center (10,10), radius 1.
      // Check the exported bounds too, so an empty/undersized obstacle cannot pass.
      for (const [attribute, value] of Object.entries({'min-x':9,'min-y':9,'max-x':11,'max-y':11})) {
        const actual=await widget.getAttribute(`data-source-${attribute}`);
        assert.notEqual(actual,null,`${name}: source ${attribute} is present`);
        close(Number(actual),value,`${name}: source ${attribute} matches native circle`);
      }
      sourceBounds=await canvas.evaluate(c=>{
        const r=c.getBoundingClientRect(), scale=+c.dataset.scale;
        return {x:r.left+9*scale+ +c.dataset.offsetX,y:r.top-11*scale+ +c.dataset.offsetY,
          width:2*scale,height:2*scale};
      });
      assert.ok(!overlaps(box,sourceBounds),`${name}: radial card clears projected source bounds ${JSON.stringify({card:box,sourceBounds})}`);
      const count=card.getByLabel('Viewport pattern instances',{exact:true});
      await expect(count).toBeVisible();
      const fullCircle=card.getByRole('checkbox',{name:/^Full circle$/i});
      await expect(fullCircle).toBeVisible();
      if (await fullCircle.isChecked()) await expect(field('step angle')).toHaveCount(0);
      else await expect(card.getByLabel('Viewport pattern step angle',{exact:true})).toBeVisible();
      if (await card.getByRole('button',{name:'Confirm',exact:true}).count()) {
        const countBox=await count.boundingBox();
        for (const name of ['Confirm','Cancel pattern changes']) {
          const button=card.getByRole('button',{name,exact:true}), buttonBox=await button.boundingBox();
          close(buttonBox.y+buttonBox.height/2,countBox.y+countBox.height/2,'count and action buttons share row',2);
          assert.ok(buttonBox.width<=40 && buttonBox.height<=40,'radial action is compact icon control');
          assert.equal((await button.innerText()).trim(),'','radial action uses icon, retaining accessible name');
        }
      }
      for (const control of await card.locator('input,button,label').all()) {
        if (!await control.isVisible()) continue;
        const b=await control.boundingBox();
        assert.ok(b.x>=box.x-1 && b.y>=box.y-1 && b.x+b.width<=box.x+box.width+1 && b.y+b.height<=box.y+box.height+1,
          'radial card controls fit without overflow');
      }
    } else assert.ok(groups.length >= 3, 'linear controls remain independent groups');
    layouts.push({name, viewport: page.viewportSize(), canvas: c, groups, sourceBounds});
    await page.screenshot({path:`${output}/${name}.png`});
  };
  const visibleControls = async radial => {
    await expect(widget).toBeVisible();
    for (const anchor of radial ? ['source', 'center-handle'] : ['x', 'y', 'actions'])
      await expect(widget.locator(`[data-pattern-anchor="${anchor}"]`)).toBeVisible();
    await expect(widget.locator('[data-pattern-center-handle]')).toHaveCount(radial ? 1 : 0);
    if (radial) {
      await expect(widget.getByLabel('Viewport pattern center X',{exact:true})).toHaveCount(0);
      await expect(widget.getByLabel('Viewport pattern center Y',{exact:true})).toHaveCount(0);
    }
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
    await expect(handle).toHaveAttribute('title', 'Select or drag pattern center');
    await expect(handle).toHaveAccessibleName('Move pattern center');
    const initialBox = await handle.boundingBox();
    const screenCenter = await canvas.evaluate((c, point) => ({x: c.getBoundingClientRect().left + point.x * +c.dataset.scale + +c.dataset.offsetX,
      y: c.getBoundingClientRect().top - point.y * +c.dataset.scale + +c.dataset.offsetY}), pattern.center);
    close(initialBox.x + initialBox.width / 2, screenCenter.x, 'handle is at exact world center X', 1);
    close(initialBox.y + initialBox.height / 2, screenCenter.y, 'handle is at exact world center Y', 1);
    // A slightly off-center grab must preserve its offset throughout the gesture.
    const start = {x: screenCenter.x + 3, y: screenCenter.y - 2};
    const initialScale = +(await canvas.getAttribute('data-scale'));
    const grabOffset = {x:3/initialScale,y:2/initialScale};
    await page.mouse.move(start.x, start.y); await page.mouse.down();
    try {
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'false');
      await page.mouse.move(start.x + 4, start.y);
      await expect(widget).toHaveAttribute('data-center-dragging', 'true');
      await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
      await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
      // Opening the draft may fit its preview once. Project subsequent targets
      // through the settled current view, retaining the original world grab offset.
      let previous, stable = 0;
      await expect.poll(async () => {
        const view = await canvas.evaluate(c => JSON.stringify([c.dataset.scale,c.dataset.offsetX,c.dataset.offsetY]));
        stable = view === previous ? stable + 1 : 0; previous = view; return stable;
      }, {intervals:[100,150,150]}).toBeGreaterThanOrEqual(2);
      let pixels = await canvas.screenshot();
      for (const fraction of [.25, .6, 1]) {
        const target = {x: pattern.center.x + delta.x * fraction, y: pattern.center.y + delta.y * fraction};
        const pointer = await canvas.evaluate((c,p) => ({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,
          y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),
          {x:target.x+grabOffset.x,y:target.y+grabOffset.y});
        await page.mouse.move(pointer.x,pointer.y,{steps:8});
        await expect.poll(async () => Math.hypot(+(await centerField('X').inputValue()) - target.x,
          +(await centerField('Y').inputValue()) - target.y), {message: 'held motion updates sidebar coordinates before release'}).toBeLessThan(1e-5);
        await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
        await expect(widget.getByRole('button', {name: 'Confirm', exact: true})).toBeDisabled();
        close(+(await widget.getAttribute('data-source-x')), source.center.x, 'held source X unchanged');
        close(+(await widget.getAttribute('data-source-y')), source.center.y, 'held source Y unchanged');
        const box = await handle.boundingBox();
        const targetScreen = await canvas.evaluate((c,p) => ({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,
          y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),target);
        close(box.x + box.width / 2, targetScreen.x, 'held handle X', 1);
        close(box.y + box.height / 2, targetScreen.y, 'held handle Y', 1);
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
    close(+(await centerField('X').inputValue()), target.x, 'released staged X');
    close(+(await centerField('Y').inputValue()), target.y, 'released staged Y');
    assert.deepEqual(geometry((await download(`${name}-uncommitted`)).doc), geometry(doc),
      'releasing center handle stages preview only; native geometry and center remain committed values');
    return target;
  };
  let saved, full;
  if (draftFixtures) {
    const linearText = await readFile(`${draftFixtures}/rectangular-saved.dxf`, 'utf8');
    const radialText = await readFile(`${draftFixtures}/radial-reopened-edit.dxf`, 'utf8');
    saved = {text: linearText, doc: native(linearText)};
    full = {text: radialText, doc: native(radialText)};
  } else {
  await load(fixture,1); await click(10.6,10.8); await tool('Rectangular / staggered pattern');
  await expect(widget).toBeVisible();
  await set('U count','4'); await set('V count','3');
  const pitch=field('U pitch'); await pitch.focus(); await page.keyboard.press('ControlOrMeta+A');
  for(const key of ['1','.','2','5']) { await page.keyboard.press(key); await page.waitForTimeout(80); }
  await expect(pitch).toHaveValue('1.25'); await pitch.press('Enter');
  await expect(editor.getByRole('spinbutton',{name:'Pattern U pitch',exact:true})).toHaveValue('1.25');
  await editor.getByRole('spinbutton',{name:'Pattern V pitch',exact:true}).fill('2.5');
  await editor.getByRole('spinbutton',{name:'Pattern V pitch',exact:true}).press('Tab');
  await expect(field('V pitch')).toHaveValue('2.5');
  await set('U count','0'); await expect(widget.getByRole('button',{name:'Confirm',exact:true})).toBeDisabled();
  await expect(widget.getByRole('alert')).toBeVisible(); await set('U count','4');
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
  saved=await download('rectangular-saved'); const pattern=saved.doc.metadata.patterns[0];
  assert.equal(pattern.columns,4); assert.equal(pattern.rows,3); assert.equal(pattern.xPitch,1.25); assert.equal(pattern.yPitch,2.5);
  await tool('Undo'); await expect(canvas).toHaveAttribute('data-entity-count','1'); await tool('Redo'); await expect(canvas).toHaveAttribute('data-entity-count','12');
  await load(saved.text,12);
  await click(14.35,15.8,'right'); await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await visibleControls(false); await layout('context-shown');
  await click(14.35,15.8,'right'); await page.screenshot({path:`${output}/context-hide-menu.png`});
  await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click(); await hiddenControls();
  await click(10.6,10.8); await expect(widget).toHaveCount(0);
  await click(14.35,15.8,'right'); await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await visibleControls(false);
  await widget.getByRole('button',{name:'Edit pattern',exact:true}).click(); await set('U count','5');
  await cancel(); assert.equal((await download('cancelled')).doc.entities.length,12);
  await canvas.press('Escape'); await click(10.6,10.8); await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
  await widget.getByRole('checkbox',{name:'Viewport stagger rows',exact:true}).check();
  await set('U pitch','3.25'); await set('V pitch','4.5'); await tool('Fit pattern preview'); await layout('staggered');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  const staggered=await download('staggered-saved'); assert.equal(staggered.doc.metadata.patterns[0].kind,'Staggered');
  assert.equal(staggered.doc.metadata.patterns[0].xPitch,3.25); assert.equal(staggered.doc.metadata.patterns[0].yPitch,4.5);
  await page.setViewportSize({width:1440,height:1000}); await load(fixture,1);
  const radialSeed = (await download('radial-source')).doc.entities[0];
  await click(10.6,10.8); await tool('Radial pattern');
  const fullCircle = widget.getByRole('checkbox', {name:/^Full circle$/i});
  await expect(fullCircle).toBeChecked();
  await set('instances','8'); await expect(field('step angle')).toHaveCount(0);
  await layout('radial-full-circle-default');
  for (const viewport of [{width:1440,height:1000},{width:1180,height:757},{width:1100,height:705}]) {
    await page.setViewportSize(viewport); await tool('Fit pattern preview'); await page.waitForTimeout(300);
    await layout(`radial-source-fit-${viewport.width}`);
    for (let step=1;step<=8;step++) {
      const point=await canvas.evaluate(c=>{
        const r=c.getBoundingClientRect();
        return {x:r.left+10*+c.dataset.scale+ +c.dataset.offsetX,y:r.top-10*+c.dataset.scale+ +c.dataset.offsetY};
      });
      assert.equal(await page.evaluate(p=>document.elementFromPoint(p.x,p.y)?.matches('canvas.drawing-canvas'),point),true,
        'source center remains unobscured for zoom');
      const scale=await canvas.getAttribute('data-scale');
      await page.mouse.move(point.x,point.y); await page.mouse.wheel(0,-120); await page.waitForTimeout(300);
      assert.notEqual(await canvas.getAttribute('data-scale'),scale,'radial wheel zoom changes camera');
      await layout(`radial-source-zoom-${viewport.width}-${step}`);
    }
    const panPoint=await canvas.evaluate(c=>{
      const r=c.getBoundingClientRect();
      for(let y=r.top+80;y<r.bottom-80;y+=35)
        for(let x=r.left+180;x<r.right-100;x+=35)
          if(document.elementFromPoint(x,y)===c && document.elementFromPoint(x+40,y+30)===c) return {x,y};
      throw new Error('No clear radial pan target');
    });
    const before=await canvas.getAttribute('data-offset-x');
    await page.mouse.move(panPoint.x,panPoint.y); await page.mouse.down({button:'middle'});
    try { await page.mouse.move(panPoint.x+40,panPoint.y+30,{steps:8}); }
    finally { await page.mouse.up({button:'middle'}); }
    await page.waitForTimeout(300); assert.notEqual(await canvas.getAttribute('data-offset-x'),before);
    await layout(`radial-source-pan-${viewport.width}`);
  }
  await page.setViewportSize({width:1440,height:1000}); await tool('Fit pattern preview');
  await fullCircle.uncheck(); await expect(fullCircle).not.toBeChecked();
  await expect(field('step angle')).toHaveValue('45');
  await set('step angle','30.25');
  await expect(editor.getByRole('spinbutton',{name:'Pattern step angle'})).toHaveValue('30.25');
  await setCenter('X', '1.25'); await setCenter('Y', '-2.5');
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
  await fullCircle.check(); await expect(field('step angle')).toHaveCount(0);
  await expect(fullCircle).toBeChecked();
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  full = await download('radial-reopened-edit'); assert.equal(full.doc.metadata.patterns[0].fullCircle,true);
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
  }
  const setDraftParameter = async (radial, value) => {
    const parameter = radial ? 'center X' : 'U pitch';
    if (radial) await setCenter('X',String(value)); else await set(parameter, String(value));
    await expect(editor.getByLabel(`Pattern ${parameter}`, {exact:true})).toHaveValue(String(value));
    await expect(widget).toHaveAttribute(radial ? 'data-center-x' : 'data-pitch-x', String(value));
  };
  const previewCircles = (doc, radial, value) => {
    const p = {...doc.metadata.patterns[0]}, source = doc.entities.find(e => e.id === p.sourceEntityIds[0]);
    if (radial) p.center = {...p.center, x: value}; else p.xPitch = value;
    return [source, ...p.members.map(member => {
      if (radial) {
        const angle = member.column * (p.fullCircle ? 360 / p.count : p.stepAngleDegrees) * Math.PI / 180;
        const x = source.center.x - p.center.x, y = source.center.y - p.center.y;
        return {...source, id: member.entityId, center: {x: p.center.x + x * Math.cos(angle) - y * Math.sin(angle),
          y: p.center.y + x * Math.sin(angle) + y * Math.cos(angle)}};
      }
      const x = (member.column + (p.kind === 'Staggered' && member.row % 2 ? p.staggerFraction : 0)) * p.xPitch;
      const y = member.row * p.yPitch, angle = p.angleDegrees * Math.PI / 180;
      return {...source, id: member.entityId, center: {x: source.center.x + x * Math.cos(angle) - y * Math.sin(angle),
        y: source.center.y + x * Math.sin(angle) + y * Math.cos(angle)}};
    })];
  };
  const draftState = async () => {
    await expect(canvas).toHaveAttribute('data-pattern-editing', 'true');
    await expect(canvas).toHaveAttribute('data-corner-preview', 'true');
    return {selected: await canvas.getAttribute('data-selected-keys'),
    editing: await canvas.getAttribute('data-pattern-editing'), preview: await canvas.getAttribute('data-corner-preview'),
    fields: await editor.locator('input,select').evaluateAll(nodes => nodes.map(node => ({label: node.getAttribute('aria-label'),
      value: node.value, checked: node.type === 'checkbox' ? node.checked : undefined}))),
    undoDisabled: await page.getByRole('button', {name: 'Undo', exact: true}).isDisabled(),
    redoDisabled: await page.getByRole('button', {name: 'Redo', exact: true}).isDisabled()};
  };
  const draftRightClick = async (kind, circles) => {
    await expect(context).toHaveCount(0);
    await expect(page.locator('.dxfer-context-dismiss')).toHaveCount(0);
    const target = await canvas.evaluate((c, {kind, circles}) => {
      const box = c.getBoundingClientRect(), scale = +c.dataset.scale;
      const screen = p => ({x: box.left + p.x * scale + +c.dataset.offsetX, y: box.top - p.y * scale + +c.dataset.offsetY});
      const clear = p => p.x > box.left + 8 && p.x < box.right - 8 && p.y > box.top + 8 && p.y < box.bottom - 8
        && document.elementFromPoint(p.x, p.y) === c;
      if (kind === 'blank') {
        for (let y = box.top + 80; y < box.bottom - 40; y += 35)
          for (let x = box.left + 170; x < box.right - 40; x += 35)
            if (clear({x,y}) && circles.every(circle => {
              const center = screen(circle.center); return Math.hypot(x - center.x, y - center.y) > circle.radius * scale + 18;
            })) return {x,y};
      } else {
        for (const circle of kind === 'source' ? circles.slice(0, 1) : circles.slice(1))
          for (let i = 0; i < 32; i++) {
            const angle = .19 + i * Math.PI / 16;
            const point = screen({x: circle.center.x + circle.radius * Math.cos(angle), y: circle.center.y + circle.radius * Math.sin(angle)});
            if (clear(point)) return point;
          }
      }
      throw new Error(`No unobscured draft ${kind} right-click target`);
    }, {kind, circles});
    await page.mouse.move(target.x,target.y); await page.mouse.click(target.x,target.y,{button:'right'});
    await expect(context).toBeVisible();
    const toggle = await widget.count() ? 'Hide pattern controls' : 'Show pattern controls';
    assert.deepEqual((await context.getByRole('button').allTextContents()).map(text => text.trim()).sort(),
      [toggle, 'Close'].sort(), 'draft menu contains no geometry/edit commands');
    await expect(context.locator('input,select,fieldset')).toHaveCount(0);
    return toggle;
  };
  for (const viewport of [{width:1440,height:1000}, {width:1100,height:705}]) {
    await page.setViewportSize(viewport);
    for (const [kind, baseline] of [['linear', saved], ['radial', full]]) {
      const radial = kind === 'radial', name = `draft-context-${kind}-${viewport.width}`;
      await load(baseline.text, baseline.doc.entities.length); await rightClickMember(baseline.doc);
      const show = context.getByRole('button',{name:'Show pattern controls',exact:true});
      if (await show.count()) await show.click(); else await context.getByRole('button',{name:'Close',exact:true}).click();
      await visibleControls(radial); await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
      const originalValue = radial ? baseline.doc.metadata.patterns[0].center.x : baseline.doc.metadata.patterns[0].xPitch;
      const value = originalValue + .625, parameter = radial ? 'center X' : 'U pitch';
      await setDraftParameter(radial, value); await tool('Fit pattern preview');
      const draft = await draftState(), circles = previewCircles(baseline.doc, radial, value);
      assert.equal(draft.editing, 'true'); assert.equal(draft.preview, 'true');
      for (const target of ['source','member','blank']) {
        assert.equal(await draftRightClick(target, circles), 'Hide pattern controls');
        await page.screenshot({path:`${output}/${name}-${target}-menu.png`});
        await context.getByRole('button',{name:'Close',exact:true}).click();
        assert.deepEqual(await draftState(), draft, `${target} right-click/Close preserves selection, draft, preview and history buttons`);
      }
      await draftRightClick('source', circles); await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click();
      await hiddenControls(); assert.deepEqual(await draftState(), draft, 'hide preserves staged edit');
      assert.deepEqual(geometry((await download(`${name}-hidden-uncommitted`)).doc), geometry(baseline.doc));
      assert.equal(await draftRightClick('blank', circles), 'Show pattern controls');
      await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
      await visibleControls(radial); await expect(radial ? centerField('X') : field(parameter)).toHaveValue(String(value));
      assert.deepEqual(await draftState(), draft, 'show preserves staged edit');
      await layout(`${name}-restored`);
      await widget.getByRole('button',{name:'Confirm',exact:true}).click();
      const committed = await download(`${name}-committed`);
      if (radial) radialGeometry(committed.doc, {...baseline.doc.metadata.patterns[0].center, x:value}, circles[0]);
      else {
        close(committed.doc.metadata.patterns[0].xPitch, value, 'draft pitch committed');
        for (const expected of circles) {
          const actual = committed.doc.entities.find(e => e.id === expected.id);
          close(actual.center.x, expected.center.x, 'committed member X'); close(actual.center.y, expected.center.y, 'committed member Y');
          close(actual.radius, expected.radius, 'committed member radius');
        }
      }
      await tool('Undo'); assert.deepEqual(geometry((await download(`${name}-undo`)).doc), geometry(baseline.doc), 'one undo crosses all draft menu actions');
      await tool('Redo'); assert.deepEqual(geometry((await download(`${name}-redo`)).doc), geometry(committed.doc));

      await widget.getByRole('button',{name:'Edit pattern',exact:true}).click(); await setDraftParameter(radial, value + .375);
      const cancelDraft = await draftState(), cancelCircles = previewCircles(committed.doc, radial, value + .375);
      await draftRightClick('member', cancelCircles); await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click();
      await hiddenControls(); assert.deepEqual(await draftState(), cancelDraft);
      assert.deepEqual(geometry((await download(`${name}-cancel-uncommitted`)).doc), geometry(committed.doc));
      await editor.getByRole('button',{name:'Cancel',exact:true}).click();
      await expect(canvas).toHaveAttribute('data-pattern-editing','false');
      assert.deepEqual(geometry((await download(`${name}-cancelled`)).doc), geometry(committed.doc));
      await tool('Undo'); assert.deepEqual(geometry((await download(`${name}-cancel-history`)).doc), geometry(baseline.doc), 'hide/cancel adds no history entry');
      await tool('Redo'); assert.deepEqual(geometry((await download(`${name}-cancel-redo`)).doc), geometry(committed.doc));
      draftContextCases.push({kind, viewport, targets:['source','member','blank'], confirmed:true, cancelled:true, historyPreserved:true});
      console.log(`PASS ${name}: restricted menu, draft/selection preservation, hide/show/confirm, hide/cancel, atomic history`);
    }
  }
  assert.deepEqual(errors,[]); passed = true;
  console.log(draftFixtures ? 'PASS targeted linear/radial draft context menus at both viewports'
    : 'PASS full viewport replay and linear/radial draft context menus at both viewports');
} catch (error) {
  failure = error.stack;
  await page?.screenshot({path: `${output}/pattern-viewport-failure.png`}).catch(() => {});
  throw error;
} finally {
  await writeFile(`${output}/pattern-viewport-results.json`, JSON.stringify({mode:draftFixtures ? 'draft-context-only' : 'full', passed, failure, layouts, heldMoves, draftContextCases, errors}, null, 2));
  await browser.close();
}
