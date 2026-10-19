import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/settings-browser';await mkdir(output,{recursive:true});
const native=text=>{const a=text.trim().split(/\r?\n/);let hex='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')hex+=a[i+1].trim();return JSON.parse(Buffer.from(hex,'hex'));};
const baseline=await readFile(new URL('./fixtures/drag-matrix/implicit-snap-line.dxf',import.meta.url));
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1188,height:761},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    page.on('console',m=>{if(m.type()==='error' && /unhandled|circuit|exception/i.test(m.text()))errors.push(m.text());});
    await page.goto(url);
    const ready=()=>page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    await ready();
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(150);};
    const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
    const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');if(name)await writeFile(`${output}/${name}.dxf`,text);return {text,doc:native(text)};};
    const load=async(buffer,count=2)=>{await menu('New drawing');await expect(canvas).toHaveAttribute('data-entity-count','0');await page.locator('input[type=file]').setInputFiles({name:'snap-source.dxf',mimeType:'application/dxf',buffer:Buffer.from(buffer)});await expect(canvas).toHaveAttribute('data-entity-count',String(count));await tool('Fit extents');};
    const screen=p=>canvas.evaluate((c,p)=>({x:c.getBoundingClientRect().left+p.x*+c.dataset.scale+ +c.dataset.offsetX,y:c.getBoundingClientRect().top-p.y*+c.dataset.scale+ +c.dataset.offsetY}),p);
    const click=async p=>{const s=await screen(p);await page.mouse.move(s.x,s.y);await page.waitForTimeout(150);await page.mouse.click(s.x,s.y);};
    const drag=async(a,b)=>{const s=await screen(a),e=await screen(b);await page.mouse.move(s.x,s.y);await page.mouse.down();await page.mouse.move(e.x,e.y,{steps:10});await page.mouse.up();await page.waitForTimeout(250);};
    const checkbox=page.getByRole('checkbox',{name:'Automatic snap constraints'});
    const setting=async value=>{await menu('Settings');await expect(checkbox).toBeEnabled();await checkbox.setChecked(value);await tool('Close settings');};
    await menu('Settings');await expect(checkbox).toBeChecked();
    await page.screenshot({path:`${output}/settings-1188.png`});
    await checkbox.uncheck();await tool('Close settings');
    await page.reload();await ready();await menu('Settings');await expect(checkbox).not.toBeChecked();
    await page.keyboard.press('Escape');await expect(page.getByRole('dialog',{name:'Settings'})).toHaveCount(0);await expect(canvas).toBeFocused();
    for(const enabled of [false,true]) {
        await load(baseline);await setting(enabled);
        const before=(await download()).doc;
        await tool('Point');await click({x:4,y:3});await expect(canvas).toHaveAttribute('data-entity-count','3');
        await canvas.focus();await page.keyboard.press('Escape');
        const saved=await download(enabled?'point-on':'point-off');
        const added=saved.doc.entities.find(e=>!before.entities.some(b=>b.id===e.id));
        assert.ok(Math.abs(added.location.x-4)<1e-8 && Math.abs(added.location.y-3)<1e-8,'snap coordinates unchanged');
        assert.deepEqual(saved.doc.entities.filter(e=>before.entities.some(b=>b.id===e.id)),before.entities);
        assert.equal(saved.doc.constraints.some(c=>c.kind==='Midpoint'),enabled);
        await tool('Undo');await expect(canvas).toHaveAttribute('data-entity-count','2');await tool('Redo');
        assert.deepEqual((await download()).doc,saved.doc);
        await load(saved.text,3);
        const reopened=(await download()).doc;
        for(const key of ['entities','dimensions','constraints'])assert.deepEqual(reopened[key],saved.doc[key]);
        if(enabled) {
            await setting(false);await drag({x:6,y:3},{x:6,y:5});
            const moved=(await download('existing-midpoint-still-enforced')).doc;
            assert.ok(moved.constraints.some(c=>c.kind==='Midpoint'&&c.state==='Satisfied'));
            const point=moved.entities.find(e=>e.id===added.id),source=moved.entities.find(e=>e.id==='source');
            assert.ok(Math.abs(point.location.x-(source.start.x+source.end.x)/2)<1e-8);
            assert.ok(Math.abs(point.location.y-(source.start.y+source.end.y)/2)<1e-8);
        }
        console.log(`PASS creation snaps ${enabled?'on':'off'}, history, native reopen and existing intent protection`);
    }
    for(const enabled of [false,true]) {
        await download();await load(baseline);await setting(enabled);
        await drag({x:-2,y:1},{x:4,y:3});
        const saved=(await download(enabled?'drag-on':'drag-off')).doc;
        assert.equal(saved.constraints.some(c=>c.kind==='Midpoint'),enabled);
        const line=saved.entities.find(e=>e.id==='line');
        assert.ok(Math.abs(line.start.x-4)<1e-8 && Math.abs(line.start.y-3)<1e-8);
        console.log(`PASS drag snap ${enabled?'on':'off'}`);
    }
    for(let i=0;i<6;i++){await setting(i%2===0);}
    await setting(true);await page.reload();await ready();await menu('Settings');await expect(checkbox).toBeChecked();
    await page.setViewportSize({width:1100,height:705});await page.screenshot({path:`${output}/settings-1100.png`});
    const box=await page.getByRole('dialog',{name:'Settings'}).boundingBox();assert.ok(box.width<=360 && box.x>=0 && box.x+box.width<=1100);
    await tool('Close settings');assert.deepEqual(errors,[]);
    const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));console.log(build);
} finally {await browser.close();}
