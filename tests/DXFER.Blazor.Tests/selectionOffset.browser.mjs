import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/offset-browser';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')hex+=a[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const near=(a,b)=>assert.ok(Math.abs(a-b)<1e-5,`${a} != ${b}`);
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1188,height:761},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    page.setDefaultTimeout(10000);
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    page.on('console',m=>{if(m.type()==='error' && /unhandled|circuit|exception/i.test(m.text()))errors.push(m.text());});
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return {text,doc:native(text)};};
    const load=async(buffer,count)=>{await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');await page.locator('input[type=file]').setInputFiles({name:'offset.dxf',mimeType:'application/dxf',buffer:Buffer.from(buffer)});await expect(canvas).toHaveAttribute('data-entity-count',String(count));await tool('Fit extents');};
    const screen=p=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
    for(const name of ['rectangle','polyline','polygon','open','unsupported']) {
        const count=name==='rectangle'?4:name==='unsupported'?2:1;
        await load(await readFile(new URL(`./fixtures/selection-offset/${name}.dxf`,import.meta.url)),count);
        const before=(await download(`${name}-before`)).doc;
        const start=await screen(name==='polygon'?{x:1.8,y:5.8}:{x:-0.5,y:name==='unsupported'?3.5:6.5}),
            end=await screen(name==='polygon'?{x:8.2,y:0.2}:{x:10.5,y:-0.5});
        await page.mouse.move(start.x,start.y);await page.mouse.down();await page.mouse.move(end.x,end.y,{steps:12});await page.mouse.up();
        await expect(canvas).toHaveAttribute('data-selected-count',String(count));await tool('Offset');
        const through=await screen({x:5,y:name==='polygon'?0:-1});await page.mouse.move(through.x,through.y);
        await page.waitForTimeout(700);
        await page.screenshot({path:`${output}/${name}-preview.png`});
        await page.mouse.click(through.x,through.y);await page.waitForTimeout(300);
        await expect(canvas).toHaveAttribute('data-entity-count',String(count+(name==='unsupported'?0:1)));
        if(name==='unsupported') {
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
            await expect(page.locator('.dxfer-command-prompt').filter({hasText:'does not support'}).first()).toBeVisible();
            await canvas.press('Escape');
            assert.deepEqual((await download('unsupported-after')).doc.entities,before.entities);
            continue;
        }
        const after=await download(`${name}-after`);
        assert.deepEqual(after.doc.entities.slice(0,count),before.entities);
        assert.deepEqual(after.doc.dimensions,before.dimensions);
        assert.deepEqual(after.doc.constraints,before.constraints);
        const contour=after.doc.entities.at(-1);
        assert.equal(contour.kind,'polyline');
        if(name!=='open')assert.deepEqual(contour.vertices[0],contour.vertices.at(-1));
        if(name==='rectangle'||name==='polyline') {
            near(Math.min(...contour.vertices.map(p=>p.x)),-1);near(Math.max(...contour.vertices.map(p=>p.x)),11);
            near(Math.min(...contour.vertices.map(p=>p.y)),-1);near(Math.max(...contour.vertices.map(p=>p.y)),7);
        }
        await tool('Undo');assert.deepEqual((await download()).doc.entities,before.entities);
        await tool('Redo');assert.deepEqual((await download()).doc.entities,after.doc.entities);
        await load(after.text,count+1);assert.deepEqual((await download(`${name}-reopen`)).doc.entities,after.doc.entities);
        console.log(`PASS ${name}: complete selection, preview, commit, originals, Undo/Redo and native reopen`);
    }
    await load(await readFile(new URL('./fixtures/selection-offset/rectangle.dxf',import.meta.url)),4);
    const start=await screen({x:-0.5,y:6.5}),end=await screen({x:10.5,y:-0.5});
    await page.mouse.move(start.x,start.y);await page.mouse.down();await page.mouse.move(end.x,end.y,{steps:12});await page.mouse.up();
    await tool('Offset');const inside=await screen({x:5,y:1});await page.mouse.move(inside.x,inside.y);
    const field=page.locator('input[data-dimension-key="offset"]');await expect(field).toBeVisible();
    await field.fill('4');await field.press('Enter');await page.waitForTimeout(400);
    await expect(canvas).toHaveAttribute('data-entity-count','4');await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
    await expect(page.locator('.dxfer-command-prompt').filter({hasText:'collapses'}).first()).toBeVisible();
    await expect(field).toBeVisible();await field.fill('2');await field.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count','5');
    const inset=(await download('keyed-inset')).doc.entities.at(-1);
    near(Math.min(...inset.vertices.map(p=>p.x)),2);near(Math.max(...inset.vertices.map(p=>p.x)),8);
    near(Math.min(...inset.vertices.map(p=>p.y)),2);near(Math.max(...inset.vertices.map(p=>p.y)),4);
    await page.screenshot({path:`${output}/keyed-inset.png`});
    console.log('PASS exact keyed inset, collapsed inset refusal without history, recovery');
    assert.deepEqual(errors,[]);
    const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));console.log(build);
} finally {await browser.close();}
