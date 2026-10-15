// Explicit isolated desktop QA; never target Production/Test.
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-compact-evidence';
await mkdir(output, {recursive:true});
const browser = await chromium.launch({headless:true});
try {
    const page = await browser.newPage({viewport:{width:1440,height:900},
        extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const ready = async () => {
        await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
        await page.waitForTimeout(250);
    };
    const names = ['View','Sketch','Modify','Transform','Pattern','Constraints','Cleanup','Grain'];
    const group = name => page.locator(`[data-palette-index="${names.indexOf(name)}"]`);
    const labels = name => page.getByRole('button',{name:`Show ${name} tool labels`,exact:true});
    const inventory = () => page.locator('.dxfer-command-strip button').evaluateAll(bs=>bs.map(b=>b.getAttribute('aria-label')).sort());
    const key = 'dxfer.paletteLayout.v1';
    await page.goto(url); await ready();
    const commands = await inventory();
    for (const name of names) assert.equal(await labels(name).getAttribute('aria-pressed'),'false');
    assert.equal(await page.locator('.dxfer-command-strip .dxfer-icon-button-label').count(),0);
    const measurements = await page.locator('.dxfer-command-strip button').evaluateAll(bs=>bs.filter(b=>b.getBoundingClientRect().width).map(b=>{
        const r=b.getBoundingClientRect(),i=b.querySelector('svg').getBoundingClientRect();
        return {name:b.getAttribute('aria-label'),title:b.title,width:r.width,height:r.height,icon:i.width};
    }));
    assert.ok(measurements.length>30);
    for(const m of measurements) {
        assert.ok(m.name && m.title, JSON.stringify(m));
        assert.ok(Math.abs(m.width-32)<1 && Math.abs(m.height-32)<1 && Math.abs(m.icon-20)<1,JSON.stringify(m));
    }
    const sketch=await group('Sketch').boundingBox();
    assert.ok(Math.abs(sketch.width-152)<1 && sketch.height<300,JSON.stringify(sketch));
    await page.screenshot({path:`${output}/default-1440.png`});
    const line=page.getByRole('button',{name:'Line',exact:true});
    const color=b=>b.evaluate(e=>({background:getComputedStyle(e).backgroundColor,color:getComputedStyle(e).color}));
    const base=await color(line);
    await line.hover(); await page.waitForTimeout(900);
    assert.notDeepEqual(await color(line),base);
    assert.match(await line.getAttribute('title'),/Line/);
    await line.focus();
    assert.equal(await line.evaluate(e=>getComputedStyle(e).outlineWidth),'2px');
    await page.keyboard.press('Enter');
    await page.waitForFunction(()=>document.querySelector('canvas').dataset.activeTool==='line');
    assert.equal(await line.getAttribute('aria-pressed'),'true');
    await page.mouse.move(600,400);
    assert.notDeepEqual(await color(line),base);
    const undo=page.getByRole('button',{name:'Undo',exact:true,includeHidden:true});
    assert.ok(await undo.isDisabled());
    assert.notDeepEqual(await color(undo),base);
    await page.screenshot({path:`${output}/active-disabled-focus.png`});
    await page.getByRole('button',{name:'Exit tool',exact:true}).click();

    const old=names.map((groupName,index)=>({groupName,side:groupName==='Sketch'?0:groupName==='View'?2:1,
        compaction:groupName==='Modify'?1:2,dockOrder:60+index*100,floatingX:450+index*8,floatingY:190+index*8,showLabels:true}));
    await page.evaluate(({key,old})=>localStorage.setItem(key,JSON.stringify({version:1,groups:old})),{key,old});
    await page.reload(); await ready();
    for(const name of names) assert.equal(await labels(name).getAttribute('aria-pressed'),'false');
    assert.match(await group('Sketch').getAttribute('class'),/group-floating/);
    assert.match(await group('View').getAttribute('class'),/group-right/);
    assert.ok(await page.getByRole('button',{name:'Expand Modify tools',exact:true}).isVisible());
    const migrated=await group('Sketch').boundingBox();
    assert.equal(Math.round(migrated.x),458);
    assert.equal(Math.round(migrated.y),246);
    assert.equal(Math.round(migrated.width),152);
    await page.screenshot({path:`${output}/migrated-1440.png`});

    await labels('Sketch').click();
    await page.waitForFunction(key=>JSON.parse(localStorage.getItem(key))?.densityVersion===2,key);
    const saved=await page.evaluate(key=>JSON.parse(localStorage.getItem(key)),key);
    for(const entry of old) {
        const actual=saved.groups.find(g=>g.groupName===entry.groupName);
        assert.deepEqual(actual,{...entry,showLabels:entry.groupName==='Sketch'});
    }
    assert.equal(Math.round((await group('Sketch').boundingBox()).width),232);
    assert.ok((await group('Sketch').getByRole('button',{name:'Line',exact:true}).boundingBox()).height<40);
    await page.reload(); await ready();
    assert.equal(await labels('Sketch').getAttribute('aria-pressed'),'true');
    assert.equal(await labels('View').getAttribute('aria-pressed'),'false');
    assert.deepEqual(await page.evaluate(key=>JSON.parse(localStorage.getItem(key)),key),saved);
    await page.screenshot({path:`${output}/optional-labels.png`});
    await labels('Sketch').click();
    await page.waitForFunction(()=>document.querySelectorAll('.dxfer-command-strip .dxfer-icon-button-label').length===0);
    for(const [width,height] of [[1188,761],[1100,705]]) {
        await page.setViewportSize({width,height}); await page.waitForTimeout(180);
        for(const name of ['Sketch','View']) {
            const r=await group(name).boundingBox();
            assert.ok(r.x>=0 && r.x+r.width<=width+1 && r.width<=153);
        }
        await page.screenshot({path:`${output}/compact-${width}.png`});
    }
    assert.deepEqual(await inventory(),commands);
    assert.equal(await page.locator('canvas').getAttribute('data-entity-count'),'0');
    assert.ok(await undo.isDisabled());
    assert.deepEqual(errors,[]);
    console.log('PASS: compact dimensions, glyphs, hover/title/accessibility/focus/active/disabled, old preference migration, explicit labels/reload, unchanged docks/order/positions/collapse and command inventory. Sketch:',JSON.stringify(sketch),'Build:',await page.locator('.app-brand').getAttribute('title'));
} finally {await browser.close();}
