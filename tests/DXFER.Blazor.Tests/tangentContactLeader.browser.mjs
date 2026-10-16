import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output='/tmp/dxfer-tangent-contact-evidence'; await mkdir(output,{recursive:true});
const native=text=>{
    const lines=text.trim().split(/\r?\n/); let hex='';
    for(let i=0;i<lines.length;i+=2) if(lines[i].trim()==='310') hex+=lines[i+1].trim();
    return JSON.parse(Buffer.from(hex,'hex').toString('utf8'));
};
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const build=await page.locator('.app-brand').getAttribute('title'); console.log(build);
    if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(180);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{
        const pending=page.waitForEvent('download');await menu('Download DXF');
        const text=await readFile(await(await pending).path(),'utf8');
        if(name)await writeFile(`${output}/${name}.dxf`,text);
        return text;
    };
    const load=async text=>{
        await menu('New drawing');
        await expect(canvas).toHaveAttribute('data-entity-count','0');
        const buffer=Buffer.from(text??await readFile(new URL('./fixtures/drag-matrix/tangent-slide-rectangle.dxf',import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({name:'tangent-contact.dxf',mimeType:'application/dxf',buffer});
        await expect(canvas).not.toHaveAttribute('data-entity-count','0');await tool('Fit extents');
    };
    const screen=p=>canvas.evaluate((c,p)=>{const d=c.dataset,r=c.getBoundingClientRect();return{x:r.left+p[0]*+d.scale+ +d.offsetX,y:r.top-p[1]*+d.scale+ +d.offsetY};},p);
    const pixels=()=>canvas.evaluate(c=>Array.from(c.getContext('2d').getImageData(0,0,c.width,c.height).data));
    await load();const before=native(await download());
    await page.mouse.move(10,10);await page.waitForTimeout(100);
    const hidden=await pixels();await tool('Show constraints');await page.mouse.move(10,10);await page.waitForTimeout(100);
    const visible=await pixels(),contact=await screen([10,10]),bounds=await canvas.boundingBox();
    // The on/off pixel difference immediately above contact isolates the new leader
    // from the unchanged circle/edge strokes and the marker 26 pixels away.
    const width=await canvas.evaluate(c=>c.width);let changed=0;
    for(let dy=-12;dy<=-3;dy++)for(let dx=-2;dx<=2;dx++){
        const i=(Math.round(contact.y-bounds.y+dy)*width+Math.round(contact.x-bounds.x+dx))*4;
        if(visible.slice(i,i+4).some((v,j)=>v!==hidden[i+j]))changed++;
    }
    assert.ok(changed>5,`Visible default contact leader pixels: ${changed}`);
    await page.screenshot({path:`${output}/default-contact.png`});
    await page.mouse.move(contact.x,contact.y-26);await page.mouse.down();
    await page.mouse.move(contact.x+90,contact.y-60,{steps:12});await page.mouse.up();await page.waitForTimeout(180);
    await page.screenshot({path:`${output}/moved-marker.png`});
    const markerOnly=native(await download());assert.deepEqual(markerOnly.entities,before.entities);
    assert.deepEqual(markerOnly.constraints,before.constraints);assert.deepEqual(markerOnly.dimensions,before.dimensions);
    await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
    const center=await screen([10,17.5]),destination=await screen([14,19]);
    await page.mouse.move(center.x,center.y);await page.mouse.down();await page.mouse.move(destination.x,destination.y,{steps:12});
    await page.screenshot({path:`${output}/held-slide.png`});
    await page.mouse.up();await page.waitForTimeout(180);
    const saved=await download('slid'),after=native(saved),circle=after.entities.find(e=>e.id==='circle');
    assert.ok(Math.abs(circle.center.x-14)<1e-5);assert.ok(Math.abs(circle.center.y-17.5)<1e-5);assert.equal(circle.radius,7.5);
    await page.screenshot({path:`${output}/released-slide.png`});
    await tool('Undo');assert.deepEqual(native(await download()).entities,before.entities);
    await tool('Redo');assert.deepEqual(native(await download()).entities,after.entities);
    await load(saved);assert.deepEqual(native(await download('reopened')).entities,after.entities);
    await page.screenshot({path:`${output}/reopened.png`});
    assert.deepEqual(errors,[]);
    console.log(`Default contact leader pixels=${changed}; marker drag leaves document/history unchanged; tangent slide, Undo/Redo and native reopen pass.`);
} finally {await browser.close();}
