import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output='/tmp/dxfer-circle-creation-evidence';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')hex+=a[i+1].trim();return JSON.parse(Buffer.from(hex,'hex').toString('utf8'));};
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const build=await page.locator('.app-brand').getAttribute('title');console.log(build);
    if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return text;};
    const load=async(kind,text)=>{
        await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');
        const fixture=kind==='line'?'tangent-slide-fixed-line':`tangent-${kind}`;
        const buffer=Buffer.from(text??await readFile(new URL(`./fixtures/drag-matrix/${fixture}.dxf`,import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({name:`${kind}.dxf`,mimeType:'application/dxf',buffer});
        await expect(canvas).not.toHaveAttribute('data-entity-count','0');await tool('Fit extents');
    };
    const move=async point=>{
        const s=await canvas.evaluate((c,p)=>{const d=c.dataset,r=c.getBoundingClientRect();return{x:r.left+p[0]*+d.scale+ +d.offsetX,y:r.top-p[1]*+d.scale+ +d.offsetY};},point);
        await page.mouse.move(s.x,s.y,{steps:8});await page.waitForTimeout(120);
    };
    const click=async point=>{await move(point);await page.mouse.down();await page.mouse.up();await page.waitForTimeout(180);};
    for(const kind of ['line','circle','arc']) {
        await load(kind);const before=native(await download());
        const center=kind==='line'?[4,4]:[18,10],contact=kind==='line'?[4,0]:[15,10];
        await tool('Center circle');await click(center);await move(contact);
        assert.ok((await canvas.getAttribute('data-hovered-id')).includes('circle-tangent'),'Explicit circle tangent cue');
        await page.screenshot({path:`${output}/${kind}-snap.png`});
        await page.mouse.down();await page.mouse.up();await page.waitForTimeout(200);await page.keyboard.press('Escape');
        const saved=await download(`${kind}-created`),after=native(saved);
        const created=after.entities.find(e=>!before.entities.some(b=>b.id===e.id));assert.equal(created?.kind,'circle');
        assert.ok(Math.abs(created.radius-(kind==='line'?4:3))<1e-6);
        assert.deepEqual(after.entities.filter(e=>e.id!==created.id),before.entities);
        const tangents=after.constraints.filter(c=>c.kind==='Tangent'&&!before.constraints.some(b=>b.id===c.id));
        assert.equal(tangents.length,1);assert.equal(tangents[0].state,'Satisfied');
        assert.ok(tangents[0].referenceKeys.includes(created.id));
        await tool('Undo');const undone=native(await download());assert.deepEqual(undone.entities,before.entities);assert.deepEqual(undone.constraints,before.constraints);
        await tool('Redo');assert.deepEqual(native(await download()).constraints,after.constraints);
        await load(kind,saved);const reopened=native(await download(`${kind}-reopened`));
        assert.deepEqual(reopened.entities,after.entities);assert.deepEqual(reopened.constraints,after.constraints);
        console.log(`${kind}: circle tangent cue, retained Satisfied relation, unchanged peer, Undo/Redo/native reopen pass`);
    }
    for(const typed of [false,true]) {
        await load('line');const before=native(await download());
        await tool('Center circle');await click([4,4]);await move(typed?[4,0]:[6,0]);
        if(typed) {
            const input=page.locator('input[data-dimension-key="radius"]');
            await input.fill('3');await input.press('Enter');await page.waitForTimeout(200);
        } else {await page.mouse.down();await page.mouse.up();await page.waitForTimeout(200);}
        await page.keyboard.press('Escape');const after=native(await download(typed?'radius-override':'ordinary-edge'));
        const created=after.entities.find(e=>!before.entities.some(b=>b.id===e.id));assert.equal(created?.kind,'circle');
        assert.equal(after.constraints.filter(c=>c.kind==='Tangent').length,before.constraints.filter(c=>c.kind==='Tangent').length);
        if(typed)assert.ok(Math.abs(created.radius-3)<1e-6);
        assert.deepEqual(after.entities.filter(e=>e.id!==created.id),before.entities);
        console.log(`${typed?'Typed radius overriding snap':'Ordinary edge click'}: no spurious tangent relation`);
    }
    assert.deepEqual(errors,[]);
} finally {await browser.close();}
