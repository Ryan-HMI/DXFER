// Isolated desktop preference checks; never target Production/Test.
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = '/tmp/dxfer-scrollbar-evidence';
await mkdir(output, {recursive:true});
const browser = await chromium.launch({headless:true});
try {
    const page = await browser.newPage({viewport:{width:1188,height:761},
        extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    const ready = async () => {
        await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
        await page.waitForTimeout(350);
    };
    const names = ['View','Sketch','Modify','Transform','Pattern','Constraints','Cleanup','Grain'];
    const group = name => page.locator(`[data-palette-index="${names.indexOf(name)}"]`);
    const key = 'dxfer.paletteLayout.v1';
    await page.goto(url); await ready();
    const build = await page.locator('.app-brand').getAttribute('title');
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
    for (const name of names) assert.match(await group(name).getAttribute('class'),
        name === 'Cleanup' || name === 'Grain' ? /group-right/ : /group-left/);
    const inventory = await page.locator('.dxfer-command-strip button').count();
    for (const [width,height] of [[1188,761],[1100,705]]) {
        await page.setViewportSize({width,height});
        const metrics = await page.locator('.dxfer-tool-dock-zone-left').evaluate(e => {
            const s=getComputedStyle(e), r=e.getBoundingClientRect();
            return {color:s.scrollbarColor, width:s.scrollbarWidth, gutter:s.scrollbarGutter,
                scroll:e.scrollHeight, client:e.clientHeight,
                contentRight:r.left+e.clientWidth-parseFloat(s.paddingRight),
                groupRight:e.querySelector('.dxfer-tool-group').getBoundingClientRect().right};
        });
        assert.equal(metrics.color,'rgb(104, 123, 144) rgb(16, 20, 27)');
        assert.equal(metrics.width,'thin');
        assert.equal(metrics.gutter,'stable');
        assert.ok(metrics.scroll > metrics.client, JSON.stringify(metrics));
        assert.ok(metrics.groupRight <= metrics.contentRight + 1, JSON.stringify(metrics));
        await page.locator('.dxfer-tool-dock-zone-left').evaluate(e => e.scrollTop=e.scrollHeight);
        await page.screenshot({path:`${output}/default-${width}.png`});
        assert.ok(await group('Cleanup').isVisible());
        assert.ok(await group('Grain').isVisible());
    }
    const old=names.map((groupName,index)=>({groupName, side:1, compaction:2,
        dockOrder:60+index*100,floatingX:24+(index%4)*24,floatingY:24+index*18,showLabels:false}));
    old[1]={...old[1],side:0,floatingX:420,floatingY:120};
    old[2].compaction=1;
    old[6].showLabels=true;
    await page.evaluate(({key,old})=>localStorage.setItem(key,JSON.stringify({version:1,densityVersion:2,groups:old})),{key,old});
    await page.reload(); await ready();
    for(const name of ['Cleanup','Grain']) assert.match(await group(name).getAttribute('class'),/group-right/);
    assert.match(await group('Sketch').getAttribute('class'),/group-floating/);
    assert.ok(await page.getByRole('button',{name:'Expand Modify tools',exact:true}).isVisible());
    const floating=await group('Sketch').boundingBox();
    assert.equal(await page.getByRole('button',{name:'Show Cleanup tool labels',exact:true}).getAttribute('aria-pressed'),'true');
    await page.screenshot({path:`${output}/migrated-custom.png`});
    await page.getByRole('button',{name:'Show Cleanup tool labels',exact:true}).click();
    await page.waitForFunction(key=>JSON.parse(localStorage.getItem(key))?.dockingVersion===1,key);
    // Real drag back to the left must remain an explicit preference after reload.
    const header=group('Grain').locator('.dxfer-tool-group-grip');
    const grip=await header.boundingBox();
    await page.mouse.move(grip.x+3,grip.y+8);
    await page.mouse.down();
    await page.mouse.move(60,200,{steps:20});
    await page.mouse.up();
    await page.waitForFunction(key=>JSON.parse(localStorage.getItem(key)).groups.find(g=>g.groupName==='Grain').side===1,key);
    await page.reload(); await ready();
    assert.match(await group('Grain').getAttribute('class'),/group-left/);
    assert.match(await group('Cleanup').getAttribute('class'),/group-right/);
    assert.deepEqual(await group('Sketch').boundingBox(),floating);
    await page.screenshot({path:`${output}/explicit-drag-reload.png`});
    assert.equal(await page.locator('.dxfer-command-strip button').count(),inventory);
    assert.equal(await page.locator('canvas').getAttribute('data-entity-count'),'0');
    assert.ok(await page.getByRole('button',{name:'Undo',exact:true,includeHidden:true}).isDisabled());
    assert.deepEqual(errors,[]);
    console.log('PASS: fresh right defaults, themed scrollable unclipped docks at desktop sizes, legacy migration, custom layout preservation, explicit drag/reload, unchanged inventory/history. Build:',build);
} finally {await browser.close();}
