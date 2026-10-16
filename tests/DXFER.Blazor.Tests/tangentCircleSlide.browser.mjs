// Disposable native fixtures; actual held canvas preview, release and persistence.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output='/tmp/dxfer-tangent-slide-evidence'; await mkdir(output,{recursive:true});
const native=text=>{
    const lines=text.trim().split(/\r?\n/); let hex='';
    for(let i=0;i<lines.length;i+=2) if(lines[i].trim()==='310') hex+=lines[i+1].trim();
    return JSON.parse(Buffer.from(hex,'hex').toString('utf8'));
};
const close=(a,b)=>assert.ok(Math.abs(a-b)<1e-6,`${a} != ${b}`);
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const build=await page.locator('.app-brand').getAttribute('title'); console.log(build);
    if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{
        const pending=page.waitForEvent('download');await menu('Download DXF');
        const text=await readFile(await(await pending).path(),'utf8');
        if(name)await writeFile(`${output}/${name}.dxf`,text);
        return text;
    };
    const load=async(kind,text)=>{
        await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');
        const buffer=Buffer.from(text??await readFile(new URL(`./fixtures/drag-matrix/tangent-slide-${kind}.dxf`,import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({name:`${kind}.dxf`,mimeType:'application/dxf',buffer});
        await expect(canvas).not.toHaveAttribute('data-entity-count','0');await tool('Fit extents');
    };
    const screen=p=>canvas.evaluate((c,p)=>{const d=c.dataset,r=c.getBoundingClientRect();return{x:r.left+p[0]*+d.scale+ +d.offsetX,y:r.top-p[1]*+d.scale+ +d.offsetY};},p);
    const move=async p=>{const s=await screen(p);await page.mouse.move(s.x,s.y,{steps:12});await page.waitForTimeout(150);};
    const assertDocument=(before,after,x,y)=>{
        const circle=after.entities.find(e=>e.id==='circle');close(circle.center.x,x);close(circle.center.y,y);close(circle.radius,7.5);
        assert.deepEqual(after.entities.filter(e=>e.id!=='circle'),before.entities.filter(e=>e.id!=='circle'));
        assert.deepEqual(after.dimensions.map(d=>[d.id,d.value,d.isDriving,d.referenceKeys]),before.dimensions.map(d=>[d.id,d.value,d.isDriving,d.referenceKeys]));
        assert.ok(after.constraints.every(c=>c.state==='Satisfied'));
        assert.deepEqual(after.constraints.map(c=>[c.id,c.referenceKeys]),before.constraints.map(c=>[c.id,c.referenceKeys]));
    };
    for(const [kind,grip] of [['fixed-line','center'],['rectangle','center'],['rectangle','perimeter'],['fixed-center','center']]) {
        const name=`${kind}-${grip}`,y=kind==='rectangle'?17.5:7.5,offset=grip==='perimeter'?7.5:0;
        await load(kind);const before=native(await download());
        const label=page.locator('input[data-dimension-id="diameter"]'),labelBefore=await label.boundingBox();
        await move([10+offset,y]);await page.mouse.down();await move([14+offset,y+1.5]);
        const held=await label.boundingBox(),scale=Number(await canvas.getAttribute('data-scale'));
        const dx=kind==='fixed-center'?0:4;
        assert.ok(Math.abs(held.x-labelBefore.x-dx*scale)<2,'Held dimension follows legal tangent slide');
        assert.ok(Math.abs(held.y-labelBefore.y)<2,'Held diameter label does not follow rejected normal movement');
        await page.screenshot({path:`${output}/${name}-held.png`});
        await page.mouse.up();await page.waitForTimeout(200);
        const saved=await download(name),after=native(saved);
        assertDocument(before,after,10+dx,y);
        if(kind==='fixed-center') {
            assert.deepEqual(after.entities,before.entities);
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
            console.log(`${name}: refusal without geometry/history change`);continue;
        }
        await tool('Undo');assert.deepEqual(native(await download()).entities,before.entities);
        await tool('Redo');assert.deepEqual(native(await download()).entities,after.entities);
        await load(kind,saved);assertDocument(before,native(await download(`${name}-reopened`)),14,y);
        await move([14,y]);await page.mouse.down();await move([16,y+1.5]);await page.mouse.up();await page.waitForTimeout(180);
        assertDocument(before,native(await download(`${name}-reedit`)),16,y);
        console.log(`${name}: slide preserves diameter/tangency/peers, held preview, history, native reopen and reedit pass`);
    }
    assert.deepEqual(errors,[]);
} finally {await browser.close();}
