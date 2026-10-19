import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/offset-tangent-browser';
await mkdir(output, {recursive: true});
const native = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixture = `0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n`
    + [[0, 0, 30, 0], [30, 0, 30, -6], [30, -6, 0, -6], [0, -6, 0, 0]]
        .map(([x, y, a, b]) => `0\nLINE\n8\n0\n10\n${x}\n20\n${y}\n11\n${a}\n21\n${b}\n`).join('')
    + `0\nCIRCLE\n8\n0\n10\n15\n20\n10\n40\n2\n0\nENDSEC\n0\nEOF\n`;
const browser = await chromium.launch({headless: true});
try {
    const page = await browser.newPage({viewport: {width: 1440, height: 1000},
        extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => {if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text());});
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const canvas = page.locator('canvas.drawing-canvas');
    const tool = async name => {await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(180);};
    const menu = async name => {await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);};
    const download = async name => {
        const pending = page.waitForEvent('download'); await menu('Download DXF');
        const text = await readFile(await (await pending).path(), 'utf8');
        await writeFile(`${output}/${name}.dxf`, text); return {text, doc: native(text)};
    };
    const load = async (text, count) => {
        await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
        await page.locator('input[type=file]').setInputFiles({name: 'offset-tangent.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text)});
        await expect(canvas).toHaveAttribute('data-entity-count', String(count)); await tool('Fit extents');
    };
    const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
        y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
    const click = async (x, y) => {
        const p = await screen({x, y}); await page.mouse.move(p.x, p.y); await page.waitForTimeout(150);
        await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
    };
    const offset = async (x, y, count) => {
        await tool('Offset'); const p = await screen({x, y}); await page.mouse.move(p.x, p.y);
        const field = page.locator('input[data-dimension-key="offset"]'); await expect(field).toBeVisible();
        await field.fill('1'); await field.press('Enter'); await expect(canvas).toHaveAttribute('data-entity-count', String(count));
        await canvas.press('Escape');
    };
    for (const derivedEdge of [false, true]) for (const reverse of [false, true]) {
        if (process.env.DXFER_QA_DERIVED_ONLY && !derivedEdge) continue;
        const tag = `${derivedEdge ? 'offset-edge' : 'source-edge'}-${reverse ? 'reverse' : 'forward'}`;
        await load(fixture, 5);
        if (await canvas.getAttribute('data-origin-axes') === 'true') await tool('Origin axes');
        await click(16.6, 11.2); await offset(18, 10, 6);
        if (derivedEdge) {
            for (const [x, y] of [[10, 0], [30, -2], [10, -6], [0, -2]]) await click(x, y);
            await page.screenshot({path: `${output}/${tag}-selection.png`});
            console.log('Rectangle selection', await canvas.getAttribute('data-selected-keys'));
            await expect(canvas).toHaveAttribute('data-selected-count', '4'); await offset(10, 1, 7);
        }
        await tool('Fit extents');
        const before = await download(`${tag}-before`);
        await tool('Tangent');
        const picks = [[17.4, 11.8], [24, derivedEdge ? 1 : 0]];
        for (const [x, y] of reverse ? picks.reverse() : picks) {
            await click(x, y);
            console.log('Tangent pick', tag, x, y, await canvas.getAttribute('data-hovered-id'), await canvas.getAttribute('data-hovered-kind'), await canvas.getAttribute('data-selected-keys'));
        }
        await page.screenshot({path: `${output}/${tag}-applied.png`});
        await canvas.press('Escape');
        const after = await download(`${tag}-after`);
        assert.equal(after.doc.constraints.filter(c => c.kind === 'Tangent').length, 1);
        assert.ok(after.doc.constraints.every(c => c.state === 'Satisfied'));
        const circles = after.doc.entities.filter(e => e.kind === 'circle');
        assert.deepEqual(circles.map(c => c.radius).sort(), [2, 3]);
        assert.ok(circles.every(c => Math.abs(c.center.x - 15) < 1e-6 && Math.abs(c.center.y - (derivedEdge ? 4 : 3)) < 1e-6));
        assert.deepEqual(after.doc.entities.filter(e => e.kind !== 'circle'), before.doc.entities.filter(e => e.kind !== 'circle'));
        assert.deepEqual(after.doc.dimensions, before.doc.dimensions);
        await page.screenshot({path: `${output}/${tag}.png`});
        await tool('Undo'); assert.deepEqual((await download(`${tag}-undo`)).doc.entities, before.doc.entities);
        await tool('Redo'); assert.deepEqual((await download(`${tag}-redo`)).doc.entities, after.doc.entities);
        await load(after.text, derivedEdge ? 7 : 6);
        const reopened = await download(`${tag}-reopened`);
        assert.deepEqual(reopened.doc.entities, after.doc.entities);
        assert.deepEqual(reopened.doc.constraints, after.doc.constraints);
        console.log(`PASS ${tag}: derived circle Tangent, radii/gaps, unchanged target, Undo/Redo, native reopen`);
    }
    assert.deepEqual(errors, []);
    const build = await page.locator('.app-brand').getAttribute('title');
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    console.log(build);
} finally {await browser.close();}
