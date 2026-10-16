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
        if(name.startsWith('arc-')) {
            const rect=await canvas.boundingBox();
            await page.mouse.move(rect.x+rect.width/2,rect.y+rect.height/2);
            await page.mouse.wheel(0,500);await page.waitForTimeout(180);
        }
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
    const drag=async(from,to,label,previewCircle=null)=>{
        const a=await screen(from),b=await screen(to);
        await page.mouse.move(a.x,a.y);await page.waitForTimeout(80);
        await expect(canvas).toHaveAttribute('data-hovered-kind','point');
        await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:12});await page.waitForTimeout(100);
        await page.screenshot({path:`${output}/${label}-preview.png`});
        if(previewCircle) {
            const painted=await canvas.evaluate((c,circle)=>{
                const d=c.dataset,ctx=c.getContext('2d'),ratio=c.width/c.clientWidth;
                const samples=circle.points ?? [0.61,2.11].map(angle=>[circle.x+Math.cos(angle)*circle.radius,circle.y+Math.sin(angle)*circle.radius]);
                return samples.map(([worldX,worldY])=>{
                    const x=Math.round((worldX*Number(d.scale)+Number(d.offsetX))*ratio);
                    const y=Math.round((-worldY*Number(d.scale)+Number(d.offsetY))*ratio);
                    const pixels=ctx.getImageData(x-4,y-4,9,9).data;
                    for(let i=0;i<pixels.length;i+=4)if(pixels[i+1]>100&&pixels[i+3]>80)return true;
                    return false;
                });
            },previewCircle);
            assert.ok(painted.every(Boolean),`${label}: expected geometry must be painted while pointer is held (${painted})`);
        }
        const preview=await visiblePoints();await page.screenshot({path:`${output}/${label}-preview.png`});
        await page.mouse.up();await page.waitForTimeout(150);
        await page.screenshot({path:`${output}/${label}-released.png`});
        return preview;
    };
    if(process.env.DXFER_MATRIX_ONLY_SPLINE || process.env.DXFER_MATRIX_ONLY_DIAGNOSTICS || process.env.DXFER_MATRIX_ONLY_ARC) process.env.DXFER_MATRIX_ONLY_CLICKED='1';
    if(!process.env.DXFER_MATRIX_ONLY_CLICKED || process.env.DXFER_MATRIX_ONLY_ARC) {
        const arcPaint=(x,y,end=90)=>({points:[end/3,end*2/3].map(a=>[x+3*Math.cos(a*Math.PI/180),y+3*Math.sin(a*Math.PI/180)])});
        for(const locked of [false,true]) {
            const name=locked?'arc-weld-fixed':'arc-weld-free';
            await load(name);const original=snapshot(await download());
            await drag([0,3],[1,4],name,arcPaint(locked?0:1,locked?0:1));
            const file=await download(),next=snapshot(file);
            assert.deepEqual(next.dimensions.map(({anchor,...intent})=>intent),original.dimensions.map(({anchor,...intent})=>intent));
            next.dimensions.forEach((d,i)=>{
                close(d.anchor.x,original.dimensions[i].anchor.x+(locked?0:1));
                close(d.anchor.y,original.dimensions[i].anchor.y+(locked?0:1));
            });
            if(locked) {
                assert.deepEqual(next.entities,original.entities);
                await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
            } else {
                close(next.entities[0].center.x,1);close(next.entities[0].center.y,1);
                close(next.entities[0].radius,3);close(next.entities[0].endAngleDegrees,90);
                close(next.entities[1].start.x,1);close(next.entities[1].start.y,4);
                assert.ok(next.constraints.every(c=>c.state==='Satisfied'));
                await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
                await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,next.entities);
                await load(`${name}-reopen`,file);assert.deepEqual(snapshot(await download()).entities,next.entities);
                await drag([1,4],[2,5],`${name}-reedit`,arcPaint(2,2));
                const edited=snapshot(await download());close(edited.entities[0].center.x,2);close(edited.entities[1].start.y,5);
            }
            console.log('PASS arc radius/sweep weld preview/release/history/reopen:',name);
        }
        await load('arc-radius-anchor');const original=snapshot(await download());
        await page.keyboard.down('Shift');
        await drag([0,3],[0,0],'arc-zero-direction',arcPaint(0,0));
        await page.keyboard.up('Shift');
        await expect(page.getByRole('status')).toContainText('direction away from its center');
        assert.deepEqual(snapshot(await download()).entities,original.entities);
        await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        await page.keyboard.down('Shift');await drag([0,3],[3,3],'arc-sweep-recovery',arcPaint(0,0,45));await page.keyboard.up('Shift');
        const file=await download(),next=snapshot(file);close(next.entities[0].endAngleDegrees,45);close(next.entities[0].radius,3);
        assert.deepEqual(next.entities[0].center,original.entities[0].center);assert.deepEqual(next.dimensions,original.dimensions);
        await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
        await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,next.entities);
        await load('arc-sweep-reopened',file);assert.deepEqual(snapshot(await download()).entities,next.entities);
        await page.keyboard.down('Shift');await drag([Math.sqrt(4.5),Math.sqrt(4.5)],[0,3],'arc-sweep-reedit',arcPaint(0,0));await page.keyboard.up('Shift');
        close(snapshot(await download()).entities[0].endAngleDegrees,90);
        console.log('PASS arc undefined-direction refusal, Shift sweep recovery/history/reopen/reedit');
    }
    for(const name of (process.env.DXFER_MATRIX_ONLY_CLICKED ? [] : ['line-fixed','weld-fixed','line-free','line-anchor','poly-anchor','weld-free'])) {
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
    if(!process.env.DXFER_MATRIX_ONLY_CLICKED || process.env.DXFER_MATRIX_ONLY_SPLINE) for(const kind of ['control','fit','tangent']) for(const locked of [false,true]) {
        const name=`spline-${kind}-${locked?'fixed':'free'}`;
        await load(name);
        const original=snapshot(await download());
        const start=await screen([0,0]);await page.mouse.click(start.x,start.y);await page.waitForTimeout(120);
        const from=kind==='tangent'?[.25,0]:[1,0],dy=kind==='tangent'?.5:1;
        const expectedDy=locked?0:dy;
        const samples=kind==='fit'?[[0,expectedDy],[1,expectedDy]]:[[2,.5+expectedDy],[3.09375,.84375+expectedDy]];
        await drag(from,[from[0],dy],name,{points:samples});
        const file=await download(),next=snapshot(file);
        if(locked) {
            assert.deepEqual(next.entities,original.entities);assert.deepEqual(next.dimensions,original.dimensions);
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        } else {
            const points=kind==='fit'?next.entities[0].fitPoints:next.entities[0].controlPoints;
            close(points[0].x,0);close(points[0].y,dy);close(points[1].x,1);close(points[1].y,dy);
            assert.equal(next.dimensions[0].value,kind==='tangent'?.25:1);
            assert.deepEqual(next.entities[0].knots,original.entities[0].knots);assert.equal(next.entities[0].degree,original.entities[0].degree);
            await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
            await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,next.entities);
            await load(`${name}-reopen`,file);assert.deepEqual(snapshot(await download()).entities,next.entities);
            const endpoint=await screen([0,dy]);await page.mouse.click(endpoint.x,endpoint.y);await page.waitForTimeout(120);
            await drag([3,1+dy],[3,.5+dy],`${name}-reedit`);
            const reedited=snapshot(await download()),updated=kind==='fit'?reedited.entities[0].fitPoints:reedited.entities[0].controlPoints;
            close(updated[2].y,.5+dy);assert.deepEqual(updated.slice(0,2),points.slice(0,2));
            assert.equal(reedited.dimensions[0].value,next.dimensions[0].value);
        }
        console.log('PASS dimensioned spline preview/release/history/native reopen/reedit or fixed refusal:',name);
    }
    if(!process.env.DXFER_MATRIX_ONLY_CLICKED) for(const name of ['mid-locked','mid-anchor']) {
        await load(name);
        const original=snapshot(await download());
        const preview=await drag([10,0],[8,2],name);
        const file=await download(),next=snapshot(file);
        if(name==='mid-locked') {
            assert.deepEqual(next.entities,original.entities);assert.deepEqual(next.constraints,original.constraints);
            close(preview[0].x,0);close(preview[0].y,0);close(preview[1].x,10);close(preview[1].y,0);
            await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        } else {
            const line=next.entities[0];close(line.start.x,2);close(line.start.y,-2);close(line.end.x,8);close(line.end.y,2);
            close(preview[0].x,line.start.x);close(preview[0].y,line.start.y);close(preview[1].x,line.end.x);close(preview[1].y,line.end.y);
            assert.deepEqual(next.entities[1],original.entities[1]);assert.ok(next.constraints.every(c=>c.state==='Satisfied'));
            await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
            await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,next.entities);
            await load('mid-anchor-reopen',file);
            assert.deepEqual(snapshot(await download()).entities,next.entities);
            await drag([8,2],[7,1],'mid-anchor-reedit');
            const reedited=snapshot(await download());close(reedited.entities[0].start.x,3);close(reedited.entities[0].start.y,-1);
            assert.deepEqual(reedited.entities[1],original.entities[1]);
        }
        console.log('PASS fixed midpoint preview/release, refusal or history/reopen/reedit:',name);
    }
    if(!process.env.DXFER_MATRIX_ONLY_CLICKED) for(const name of ['equal-tangent-fixed','tangent-radius-free','tangent-diameter-fixed']) {
        await load(name);
        const original=snapshot(await download());
        const equal=name.startsWith('equal'),diameter=name.includes('diameter');
        const center=equal?[2,1]:diameter?[1,0]:[0,1],radius=diameter||equal?3:2;
        const expectedPreview={x:center[0],y:center[1],radius};
        await drag([0,0],center,`${name}-positive`,expectedPreview);
        const file=await download(),next=snapshot(file),circle=next.entities[0];
        close(circle.center.x,center[0]);close(circle.center.y,center[1]);close(circle.radius,radius);
        assert.deepEqual(next.entities[1],original.entities[1]);assert.ok(next.constraints.every(c=>c.state==='Satisfied'));
        await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,original.entities);
        await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,next.entities);
        await load(`${name}-reopen`,file);
        assert.deepEqual(snapshot(await download()).entities,next.entities);
        const invalidFrom=equal?[center[0]+radius,center[1]]:center;
        const invalidTo=equal?[center[0]+radius+2,center[1]]:diameter?[1,1]:[0,3];
        await drag(invalidFrom,invalidTo,`${name}-refusal`,expectedPreview);
        const rejected=snapshot(await download());assert.deepEqual(rejected.entities,next.entities);assert.deepEqual(rejected.dimensions,next.dimensions);
        await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
        const recovery=equal?[1,2]:diameter?[2,0]:[0,2];
        await drag(center,recovery,`${name}-recovery`,{x:recovery[0],y:recovery[1],radius:equal||diameter?3:1});
        const recovered=snapshot(await download());close(recovered.entities[0].center.x,recovery[0]);close(recovered.entities[0].center.y,recovery[1]);
        assert.deepEqual(recovered.entities[1],original.entities[1]);assert.ok(recovered.constraints.every(c=>c.state==='Satisfied'));
        console.log('PASS combination valid/refusal/recovery/history/native reopen:',name);
    }
    if(!process.env.DXFER_MATRIX_ONLY_CLICKED || process.env.DXFER_MATRIX_ONLY_DIAGNOSTICS) {
        await load('equal-rectangle');
        const original=snapshot(await download());
        await page.getByRole('button',{name:'Dimension',exact:true}).click();await page.waitForTimeout(200);
        await page.getByRole('button',{name:'Fit extents',exact:true}).click();await page.waitForTimeout(150);
        for(const position of [[0,12.5],[11,12.5],[5.5,10.5]]){const p=await screen(position);await page.mouse.click(p.x,p.y);await page.waitForTimeout(150);}
        await expect(page.getByRole('status')).toContainText('Redundant driving dimension');
        await page.screenshot({path:`${output}/rectangle-redundant.png`});
        const redundantFile=await download(),redundant=snapshot(redundantFile);
        assert.deepEqual(redundant.entities,original.entities);assert.equal(redundant.dimensions.length,2);
        const added=redundant.dimensions.find(d=>d.id!=='original');close(added.value,11);
        const input=page.locator(`[data-dimension-id="${added.id}"]`);
        await input.fill('12');await input.press('Enter');await page.waitForTimeout(150);
        await expect(page.getByRole('status')).toContainText('conflicts with existing constraints');
        await page.screenshot({path:`${output}/rectangle-conflicting.png`});
        const conflictFile=await download(),conflict=snapshot(conflictFile);assert.deepEqual(conflict.entities,original.entities);
        await page.getByRole('button',{name:'Undo',exact:true}).click();
        await expect(page.getByRole('status')).toContainText('Redundant driving dimension');
        await page.getByRole('button',{name:'Redo',exact:true}).click();
        await expect(page.getByRole('status')).toContainText('conflicts with existing constraints');
        await load('rectangle-conflict-reopen',conflictFile);
        await expect(page.getByRole('status')).toContainText('conflicts with existing constraints');
        assert.deepEqual(snapshot(await download()).entities,original.entities);
        await load('rectangle-redundant-reopen',redundantFile);
        await expect(page.getByRole('status')).toContainText('Redundant driving dimension');
        assert.deepEqual(snapshot(await download()).entities,original.entities);
        console.log('PASS actual Equal-rectangle redundant/conflicting reasons, rollback, history and native reopen');
    }
    const rawPolyline=[0,'SECTION',2,'HEADER',9,'$ACADVER',1,'AC1015',9,'$INSUNITS',70,1,0,'ENDSEC',
        0,'SECTION',2,'ENTITIES',0,'LWPOLYLINE',8,0,90,3,70,0,10,0,20,0,10,10,20,0,10,10,20,6,0,'ENDSEC',0,'EOF'].join('\n');
    await load('poly-clicks',rawPolyline);
    await page.getByRole('button',{name:'Dimension',exact:true}).click();
    await expect(canvas).toHaveAttribute('data-active-tool','dimension');
    await page.waitForTimeout(200);
    await page.getByRole('button',{name:'Fit extents',exact:true}).click();await page.waitForTimeout(150);
    for(const position of [[0,0],[10,0],[5,2]]){const p=await screen(position);await page.mouse.click(p.x,p.y);await page.waitForTimeout(100);}
    await page.screenshot({path:`${output}/poly-clicks-dimension-create.png`});
    await page.keyboard.press('Escape');
    const clicked=snapshot(await download());
    assert.equal(clicked.dimensions.length,1);
    const dimension=clicked.dimensions[0];assert.equal(dimension.kind,'LinearDistance');assert.equal(dimension.isDriving,true);close(dimension.value,10);
    assert.ok(dimension.referenceKeys.every(k=>k.includes('|point|vertex-')),'must test UI-created canvas references');
    const preview=await drag([10,0],[10,2],'poly-clicks');
    const movedFile=await download(),moved=snapshot(movedFile),vertices=moved.entities[0].vertices;
    close(Math.hypot(vertices[1].x-vertices[0].x,vertices[1].y-vertices[0].y),10);
    for(let i=0;i<2;i++){close(vertices[i].x,preview[i].x);close(vertices[i].y,preview[i].y);}
    assert.equal(moved.dimensions[0].value,10);
    await page.getByRole('button',{name:'Undo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,clicked.entities);
    await page.getByRole('button',{name:'Redo',exact:true}).click();assert.deepEqual(snapshot(await download()).entities,moved.entities);
    await load('poly-clicks-reopened',movedFile);
    for(const value of [12,9]) {
        const input=page.locator(`[data-dimension-id="${dimension.id}"]`);
        await input.fill(String(value));await input.press('Enter');await page.waitForTimeout(150);
        const edited=snapshot(await download()),p=edited.entities[0].vertices;
        close(Math.hypot(p[1].x-p[0].x,p[1].y-p[0].y),value);
        assert.equal(edited.dimensions[0].id,dimension.id);assert.deepEqual(edited.dimensions[0].referenceKeys,dimension.referenceKeys);
        await expect(input).not.toHaveClass(/unsatisfied/);
    }
    console.log('PASS actual toolbar-created polyline LinearDistance: preview/release, history, native reopen, same-ID edits 10->12->9');
    await load('poly-clicks-corrupt');
    await expect(page.locator('[data-dimension-id="length"]')).toHaveClass(/unsatisfied/);
    const corrupt=snapshot(await download());
    assert.deepEqual(corrupt.entities[0].vertices,[{x:0,y:0},{x:10,y:2},{x:10,y:6}]);
    assert.equal(corrupt.dimensions[0].value,10);
    await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
    console.log('PASS previously corrupted native drawing: visible unsatisfied dimension, no automatic geometry repair');
    assert.deepEqual(errors,[]);
    console.log('Build:',await page.locator('.app-brand').getAttribute('title'));
} finally {await browser.close();}
