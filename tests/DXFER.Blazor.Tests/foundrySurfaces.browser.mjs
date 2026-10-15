// Explicit isolated desktop QA; never target Production/Test.
import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-foundry-evidence';
await mkdir(output, {recursive:true});
const browser = await chromium.launch({headless:true});
try {
    const page = await browser.newPage({viewport:{width:1440,height:900},
        extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const canvas=page.locator('canvas.drawing-canvas');
    const background=selector=>page.locator(selector).first().evaluate(e=>getComputedStyle(e).backgroundColor);
    assert.equal(await background('.app-header'),'rgb(10, 13, 18)');
    assert.equal(await background('.dxfer-tool-group'),'rgb(16, 20, 27)');
    assert.equal(await background('.dxfer-tool-section-summary'),'rgb(21, 27, 36)');
    const split=page.getByRole('button',{name:'Split at point',exact:true});
    assert.equal(await split.locator('.dxfer-split-point-icon').count(),1);
    assert.match(await split.getAttribute('title'),/Split at point/i);
    await split.screenshot({path:`${output}/split-at-point.png`});
    assert.match(await canvas.evaluate(e=>getComputedStyle(e).backgroundImage),/radial-gradient.*23, 40, 50.*11, 16, 22/);
    await page.mouse.move(800,20);
    const screenshot=await canvas.screenshot();
    const pixels=await page.evaluate(async base64=>{
        const img=new Image(); img.src='data:image/png;base64,'+base64; await img.decode();
        const c=document.createElement('canvas'); c.width=img.width; c.height=img.height;
        const ctx=c.getContext('2d'); ctx.drawImage(img,0,0);
        const read=(x,y)=>[...ctx.getImageData(Math.floor(c.width*x),Math.floor(c.height*y),1,1).data];
        return {center:read(.5,.5),edge:read(.99,.99)};
    },screenshot.toString('base64'));
    assert.ok(pixels.center[1]>pixels.edge[1]+15,JSON.stringify(pixels));
    assert.deepEqual(pixels.edge,[11,16,22,255]);
    const fixture=[0,'SECTION',2,'HEADER',9,'$ACADVER',1,'AC1015',9,'$INSUNITS',70,1,0,'ENDSEC',
        0,'SECTION',2,'ENTITIES',0,'LINE',8,0,10,0,20,0,11,10,21,0,
        0,'LINE',8,0,10,10,20,0,11,10,21,6,0,'ENDSEC',0,'EOF'].join('\n');
    await page.locator('input[type=file]').setInputFiles({name:'Foundry-contrast.dxf',mimeType:'application/dxf',buffer:Buffer.from(fixture)});
    await page.waitForFunction(()=>document.querySelector('canvas').dataset.entityCount==='2');
    const menu=async name=>{
        await page.getByRole('button',{name:'Open application menu'}).click();
        await page.getByRole('button',{name:'File',exact:true}).hover();
        await page.getByRole('button',{name,exact:true}).click();
    };
    const download=async()=>{
        const pending=page.waitForEvent('download'); await menu('Download DXF');
        return readFile(await (await pending).path(),'utf8');
    };
    const snapshot=text=>{
        const lines=text.trim().split(/\r?\n/); let hex='';
        for(let i=0;i<lines.length;i+=2)if(lines[i].trim()==='310')hex+=lines[i+1].trim();
        return JSON.parse(Buffer.from(hex,'hex').toString('utf8'));
    };
    const before=snapshot(await download());
    const fillet=page.getByRole('button',{name:'Fillet',exact:true});
    await fillet.click(); await page.mouse.move(800,20);
    await page.waitForFunction(()=>document.querySelector('[aria-label="Fillet"]')?.getAttribute('aria-pressed')==='true');
    assert.equal(await fillet.evaluate(e=>getComputedStyle(e).color),'rgb(102, 217, 255)');
    assert.equal(await background('.dxfer-operation-inspector'),'rgb(16, 20, 27)');
    for(const [width,height] of [[1440,900],[1188,761],[1100,705],[1920,1080]]) {
        await page.setViewportSize({width,height});
        await page.getByRole('button',{name:'Fit extents',exact:true}).click();
        await page.mouse.move(width-20,20); await page.waitForTimeout(180);
        const header=await page.locator('.app-header').boundingBox(); assert.equal(header.height,48);
        const button=await fillet.boundingBox(); assert.equal(button.width,32); assert.equal(button.height,32);
        const exit=await page.getByRole('button',{name:'Exit tool',exact:true}).boundingBox();
        assert.ok(exit.x+exit.width<=width && exit.y+exit.height<=height);
        const raw=await canvas.evaluate(c=>{
            const ctx=c.getContext('2d'), p=ctx.getImageData(c.width-1,c.height-1,1,1).data;
            const all=ctx.getImageData(0,0,c.width,c.height).data;
            let marks=0; for(let i=3;i<all.length;i+=4)if(all[i])marks++;
            return {edgeAlpha:p[3],marks};
        });
        assert.equal(raw.edgeAlpha,0,'background must remain transparent after redraw');
        assert.ok(raw.marks>100,'geometry must still be painted');
        await page.screenshot({path:`${output}/foundry-${width}.png`});
    }
    await page.getByRole('button',{name:'Exit tool',exact:true}).click();
    assert.deepEqual(snapshot(await download()).entities,before.entities);
    assert.ok(await page.getByRole('button',{name:'Undo',exact:true,includeHidden:true}).isDisabled());
    assert.deepEqual(errors,[]);
    console.log('PASS: reference surface colors, screenshot gradient pixels, compact sizing, inspector visibility, transparent redraw and unchanged exported geometry.',pixels,
        'Build:',await page.locator('.app-brand').getAttribute('title'));
} finally {await browser.close();}
