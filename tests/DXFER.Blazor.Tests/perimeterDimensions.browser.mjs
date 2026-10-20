import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { measurePerimeterDistance } from '../../src/DXFER.Blazor/wwwroot/perimeterDistance.js';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5038';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-perimeter-browser';
await mkdir(output, { recursive: true });
const native = text => {
    const lines = text.trim().split(/\r?\n/);
    let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex);
    return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixture = arc => ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC','0','SECTION','2','ENTITIES',
    '0',arc ? 'ARC' : 'CIRCLE','5','100','8','0','10','10','20','10','40','2',
    ...(arc ? ['50','90','51','270','0','POINT','5','200','8','0','10','4','20','10']
        : ['0','LINE','5','200','8','0','10','16','20','6','11','16','21','14']),
    '0','ENDSEC','0','EOF',''].join('\n');
const browser = await chromium.launch({ headless: true, executablePath: process.env.DXFER_CHROMIUM });
const errors = [];
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 },
        extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? { 'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY } : {} });
    page.setDefaultTimeout(10000);
    page.on('dialog', dialog => dialog.accept());
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error' && /unhandled|circuit|exception/i.test(message.text())) errors.push(message.text()); });
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const canvas = page.locator('canvas.drawing-canvas');
    const build = await page.locator('.app-brand').getAttribute('title');
    console.log(build);
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    const tool = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.waitForTimeout(150); };
    const menu = async name => { await tool('Open application menu'); await page.getByRole('button', { name: 'File', exact: true }).hover(); await tool(name); };
    const download = async name => {
        const wait = page.waitForEvent('download');
        await menu('Download DXF');
        const text = await readFile(await (await wait).path(), 'utf8');
        await writeFile(`${output}/${name}.dxf`, text);
        return { text, doc: native(text) };
    };
    const load = async text => {
        await canvas.press('Escape'); await menu('New drawing');
        await expect(canvas).toHaveAttribute('data-entity-count', '0');
        await page.locator('input[type=file]').setInputFiles({ name: 'perimeter.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text) });
        await expect(canvas).toHaveAttribute('data-entity-count', '2');
        await tool('Fit extents'); await page.waitForTimeout(500); await canvas.press('Escape');
    };
    const click = async (x, y) => {
        const p = await canvas.evaluate((c, p) => ({ x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
            y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY }), { x, y });
        await page.mouse.move(p.x, p.y); await page.waitForTimeout(100); await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
    };
    const verify = (doc, value) => {
        assert.equal(doc.dimensions.length, 1);
        assert.equal(doc.dimensions[0].kind, 'PerimeterDistance');
        assert.equal(doc.dimensions[0].isDriving, true);
        assert.equal(doc.dimensions[0].value, value);
        const shapes = doc.entities.map(e => e.kind === 'point' ? { kind: 'point', point: e.location }
            : e.kind === 'line' ? { kind: 'line', start: e.start, end: e.end } : e);
        const measured = measurePerimeterDistance(...shapes);
        assert.ok(measured && Math.abs(measured.value - value) < 1e-6, JSON.stringify({ measured, value }));
    };
    for (const [arc, quadrant, reverse] of [[false, false, false], [true, false, false], [false, true, false], [false, true, true]]) {
        const name = arc ? 'arc-point' : quadrant ? `circle-quadrant-line-${reverse ? 'edge-first' : 'circle-first'}` : 'circle-line';
        await load(fixture(arc));
        await tool('Dimension');
        await expect(canvas).toHaveAttribute('data-active-tool', 'dimension');
        const angle = arc ? Math.PI - 0.3 : quadrant ? Math.PI / 2 : 0.3;
        const picks = [[10 + 2 * Math.cos(angle), 10 + 2 * Math.sin(angle)], [arc ? 4 : 16, arc ? 10 : 11]];
        if (reverse) picks.reverse();
        for (const pick of picks) await click(...pick);
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        assert.equal((await canvas.getAttribute('data-dimension-draft-keys')).split(',').length, 2);
        await click(arc ? 6 : 14, 8);
        const input = page.locator('input[data-dimension-id]');
        await expect(input).toHaveCount(1);
        await page.keyboard.press('Escape'); await canvas.press('Escape');
        verify((await download(`${name}-placed`)).doc, 4);
        await input.dblclick(); await input.fill('5'); await input.press('Enter'); await canvas.press('Escape');
        await expect(input).toHaveAttribute('data-dimension-value', '5');
        const edited = await download(`${name}-edited`); verify(edited.doc, 5);
        await tool('Undo'); verify((await download(`${name}-undo`)).doc, 4);
        await tool('Redo'); verify((await download(`${name}-redo`)).doc, 5);
        await load(edited.text); verify((await download(`${name}-reopened`)).doc, 5);
        await input.dblclick(); await input.fill('4.5'); await input.press('Enter'); await canvas.press('Escape');
        await expect(input).toHaveAttribute('data-dimension-value', '4.5');
        verify((await download(`${name}-reopened-edited`)).doc, 4.5);
        await page.screenshot({ path: `${output}/${name}.png` });
        console.log(`PASS ${name}: placement, edit, Undo/Redo, native reopen/edit again`);
    }
    assert.deepEqual(errors, []);
} finally { await browser.close(); }
