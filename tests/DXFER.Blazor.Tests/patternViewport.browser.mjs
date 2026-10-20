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
try {
  const page = await browser.newPage({viewport: {width: 1440, height: 1000}, extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
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
    const box=await widget.boundingBox(), c=await canvas.boundingBox();
    assert.ok(box.x>=c.x && box.y>=c.y && box.x+box.width<=c.x+c.width+1 && box.y+box.height<=c.y+c.height+1);
    for(const palette of await page.locator('.dxfer-tool-group').all()) {
      const p=await palette.boundingBox(); if(p) assert.ok(box.x>=p.x+p.width || box.x+box.width<=p.x || box.y>=p.y+p.height || box.y+box.height<=p.y,'widget clears palette');
    }
    await page.screenshot({path:`${output}/${name}.png`});
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
  await expect(widget).toBeVisible(); await page.screenshot({path:`${output}/context-shown.png`});
  await click(14.35,15.8,'right'); await page.screenshot({path:`${output}/context-hide-menu.png`});
  await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click(); await expect(widget).toHaveCount(0);
  await click(10.6,10.8); await expect(widget).toHaveCount(0);
  await click(14.35,15.8,'right'); await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await expect(widget).toBeVisible();
  await widget.getByRole('button',{name:'Edit pattern',exact:true}).click(); await set('columns','5');
  await widget.getByRole('button',{name:'Cancel',exact:true}).click(); assert.equal((await download('cancelled')).doc.entities.length,12);
  await canvas.press('Escape'); await click(10.6,10.8); await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
  await editor.getByRole('combobox',{name:'Pattern layout'}).selectOption('Staggered');
  await set('X pitch','3.25'); await set('Y pitch','4.5'); await tool('Fit pattern preview'); await layout('staggered');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  const staggered=await download('staggered-saved'); assert.equal(staggered.doc.metadata.patterns[0].kind,'Staggered');
  assert.equal(staggered.doc.metadata.patterns[0].xPitch,3.25); assert.equal(staggered.doc.metadata.patterns[0].yPitch,4.5);
  await page.setViewportSize({width:1440,height:1000}); await load(fixture,1); await click(10.6,10.8); await tool('Radial pattern');
  await set('instances','8'); await expect(field('step angle')).toHaveValue('45');
  await set('step angle','30'); await expect(widget.getByRole('checkbox',{name:'Viewport full circle'})).not.toBeChecked();
  await expect(editor.getByRole('spinbutton',{name:'Pattern step angle'})).toHaveValue('30');
  await tool('Fit pattern preview'); await layout('radial'); await widget.getByRole('button',{name:'Confirm',exact:true}).click();
  await expect(canvas).toHaveAttribute('data-entity-count','8'); const radial=await download('radial-saved');
  assert.equal(radial.doc.metadata.patterns[0].count,8); assert.equal(radial.doc.metadata.patterns[0].stepAngleDegrees,30);
  assert.equal(radial.doc.metadata.patterns[0].fullCircle,false);
  await load(radial.text,8); await rightClickMember(radial.doc);
  await context.getByRole('button',{name:'Show pattern controls',exact:true}).click(); await expect(widget).toBeVisible();
  await rightClickMember(radial.doc); await context.getByRole('button',{name:'Hide pattern controls',exact:true}).click();
  await expect(widget).toHaveCount(0); await rightClickMember(radial.doc);
  await context.getByRole('button',{name:'Show pattern controls',exact:true}).click();
  await widget.getByRole('button',{name:'Edit pattern',exact:true}).click();
  await widget.getByRole('checkbox',{name:'Viewport full circle'}).check(); await expect(field('step angle')).toHaveValue('45');
  await widget.getByRole('button',{name:'Confirm',exact:true}).click(); assert.equal((await download('radial-reopened-edit')).doc.metadata.patterns[0].fullCircle,true);
  assert.deepEqual(errors,[]); console.log('PASS rectangular/staggered/radial viewport counts/pitches, instance context show/hide, native typing, sidebar sync, invalid refusal, desktop layout/pan/zoom, history, cancel and native reopen/edit');
} finally {await browser.close();}
