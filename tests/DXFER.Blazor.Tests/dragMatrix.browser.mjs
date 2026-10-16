// Real canvas transactions against disposable native fixtures, isolated local/Dev only.
import assert from 'node:assert/strict';
import { readFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1','localhost','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-drag-matrix-evidence';
await mkdir(output,{recursive:true});
const snapshot=text=>{
    const lines=text.trim().split(/\r?\n/);let hex='';
    for(let i=0;i<lines.length;i+=2)if(lines[i].trim()==='310')hex+=lines[i+1].trim();
    return JSON.parse(Buffer.from(hex,'hex').toString('utf8'));
};
const close=(a,b)=>assert.ok(Math.abs(a-b)<1e-6,`${a} != ${b}`);
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const canvas=page.locator('canvas.drawing-canvas');
    const menu=async name=>{
        await page.getByRole('button',{name:'Open application menu'}).click();
        await page.getByRole('button',{name:'File',exact:true}).hover();
        await page.getByRole('button',{name,exact:true}).click();
    };
    const download=async()=>{
        const pending=page.waitForEvent('download');await menu('Download DXF');
        return readFile(await (await pending).path(),'utf8');
    };
    const load=async(name,text)=>{
        await menu('New drawing');
        await expect(canvas).toHaveAttribute('data-entity-count','0');
        const buffer=Buffer.from(text ?? await readFile(new URL(`./fixtures/drag-matrix/${name}.dxf`,import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({name:`${name}.dxf`,mimeType:'application/dxf',buffer});
        await expect(canvas).not.toHaveAttribute('data-entity-count','0');
        await page.getByRole('button',{name:'Fit extents',exact:true}).click();
        await page.waitForTimeout(120);
    };
    const screen=([x,y])=>canvas.evaluate((c,p)=>{
        const d=c.dataset,r=c.getBoundingClientRect();
        return {x:r.left+p[0]*Number(d.scale)+Number(d.offsetX),y:r.top-p[1]*Number(d.scale)+Number(d.offsetY)};
    },[x,y]);
    const visiblePoints=()=>canvas.evaluate(c=>{
        const d=c.dataset;return [d.firstScreenStart,d.firstScreenEnd].map(s=>{
            const [x,y]=s.split(',').map(Number);return {x:(x-Number(d.offsetX))/Number(d.scale),y:(Number(d.offsetY)-y)/Number(d.scale)};
        });
    });
    const drag=async(from,to,label)=>{
        const a=await screen(from),b=await screen(to);
        await page.mouse.move(a.x,a.y);await page.waitForTimeout(80);
        await expect(canvas).toHaveAttribute('data-hovered-kind','point');
        await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:12});await page.waitForTimeout(100);
        const preview=await visiblePoints();await page.screenshot({path:`${output}/${label}-preview.png`});
        await page.mouse.up();await page.waitForTimeout(150);
        await page.screenshot({path:`${output}/${label}-released.png`});
        return preview;
    };
    for(const name of ['line-fixed','weld-fixed','line-free','line-anchor','poly-anchor','weld-free']) {
        await load(name);
        const original=snapshot(await download());
        const blocked=name.endsWith('fixed');
        const preview=await drag([10,0],[13,3],name);
        const status=await page.getByRole('status').innerText();
        const text=await download(),after=snapshot(text);
        const points=after.entities[0].kind==='polyline'?after.entities[0].vertices:[after.entities[0].start,after.entities[0].end];
        if(blocked) {
            assert.deepEqual(after.entities,original.entities);assert.deepEqual(after.dimensions,original.dimensions);
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
            close(preview[0].x,0);close(preview[0].y,0);close(preview[1].x,10);close(preview[1].y,0);
            console.log('PASS refusal:',name,status);
            continue;
        }
        assert.notDeepEqual(after.entities,original.entities);
        for(let i=0;i<2;i++){close(preview[i].x,points[i].x);close(preview[i].y,points[i].y);}
        assert.equal(after.dimensions[0].value,10);assert.ok(!after.constraints.some(c=>c.state==='Unsatisfied'));
        if(name==='poly-anchor'){close(points[0].x,0);close(points[0].y,0);close(points[1].x,10);close(points[1].y,3);}
        else close(Math.hypot(points[1].x-points[0].x,points[1].y-points[0].y),10);
        if(name==='line-anchor'){close(points[0].x,0);close(points[0].y,0);}
        if(name==='weld-free') {
            assert.deepEqual(after.entities[1].vertices[0],points[1]);assert.deepEqual(after.entities[2].location,points[1]);
        }
        await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
        await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,after.entities);
        await load(`${name}-reopened`,text);
        assert.deepEqual(snapshot(await download()).entities,after.entities);
        await drag([points[1].x,points[1].y],[points[1].x+2,points[1].y+2],`${name}-reedit`);
        const edited=snapshot(await download());assert.notDeepEqual(edited.entities,after.entities);assert.equal(edited.dimensions[0].value,10);
        console.log('PASS preview/release, history, native reopen and reedit:',name);
    }
    assert.deepEqual(errors,[]);
    console.log('Build:',await page.locator('.app-brand').getAttribute('title'));
} finally {await browser.close();}
