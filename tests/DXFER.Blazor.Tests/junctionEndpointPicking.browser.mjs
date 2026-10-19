import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/junction-browser';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')hex+=a[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const near=(a,b)=>assert.ok(Math.abs(a-b)<1e-5,`${a} != ${b}`);
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1188,height:761},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    page.on('console',m=>{if(m.type()==='error' && /unhandled|circuit|exception/i.test(m.text()))errors.push(m.text());});
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return {text,doc:native(text)};};
    const load=async buffer=>{await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');await page.locator('input[type=file]').setInputFiles({name:'junction.dxf',mimeType:'application/dxf',buffer:Buffer.from(buffer)});await expect(canvas).toHaveAttribute('data-entity-count','2');await tool('Fit extents');};
    const screen=p=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
    const drag=async(a,b,name)=>{
        const s=await screen(a),e=await screen(b);
        await page.mouse.move(s.x+2,s.y+2);await page.mouse.down();await page.mouse.move(e.x,e.y,{steps:15});
        await page.waitForTimeout(300);await page.screenshot({path:`${output}/${name}-held.png`});
        await page.mouse.up();await page.waitForTimeout(250);
    };
    const check=(doc,x,y)=>{
        const branch=doc.entities.find(e=>e.id==='branch'),carrier=doc.entities.find(e=>e.id==='carrier');
        near(branch.end.x,x);near(branch.end.y,y);
        assert.deepEqual(branch.start,{x:0,y:1});
        near((x-carrier.start.x)*(carrier.end.y-carrier.start.y)-(y-carrier.start.y)*(carrier.end.x-carrier.start.x),0);
        assert.ok(doc.constraints.every(c=>c.state==='Satisfied'));
    };
    for(const name of ['forward','reverse','forward-fixed','reverse-fixed']) {
        await load(await readFile(new URL(`./fixtures/junction-picking/${name}.dxf`,import.meta.url)));
        const original=(await download(`${name}-before`)).doc;
        await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        await drag({x:3,y:2},{x:4,y:3},name);
        const changed=await download(`${name}-after`);
        assert.deepEqual(changed.doc.constraints,original.constraints,'existing intent must not change');
        if(name.includes('fixed')) {
            assert.deepEqual(changed.doc.entities,original.entities);
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        } else {
            check(changed.doc,4,3);
            await tool('Undo');assert.deepEqual((await download()).doc.entities,original.entities);
            await tool('Redo');assert.deepEqual((await download()).doc.entities,changed.doc.entities);
            await load(changed.text);check((await download()).doc,4,3);
            await drag({x:4,y:3},{x:5,y:4},`${name}-reopened`);
            check((await download(`${name}-edited-again`)).doc,5,4);
        }
        console.log(`PASS ${name}: real endpoint pick, held preview/release, intent${name.includes('fixed')?', fixed refusal without history':', Undo/Redo/native reopen/edit again'}`);
    }
    assert.deepEqual(errors,[]);
    const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));console.log(build);
} finally {await browser.close();}
