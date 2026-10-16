import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = '/tmp/dxfer-centers-tangent-evidence'; await mkdir(output, { recursive: true });
const native = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    return JSON.parse(Buffer.from(hex, 'hex').toString('utf8'));
};
const close = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 },
        extraHTTPHeaders: { 'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const build = await page.locator('.app-brand').getAttribute('title'); console.log(build);
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    const canvas = page.locator('canvas.drawing-canvas');
    const tool = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.waitForTimeout(250); };
    const menu = async name => { await tool('Open application menu'); await page.getByRole('button', { name: 'File', exact: true }).hover(); await tool(name); };
    const download = async name => {
        const pending = page.waitForEvent('download'); await menu('Download DXF');
        const text = await readFile(await (await pending).path(), 'utf8');
        if (name) await writeFile(`${output}/${name}.dxf`, text);
        return text;
    };
    const load = async (kind, text) => {
        await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
        const buffer = Buffer.from(text ?? await readFile(new URL(`./fixtures/drag-matrix/tangent-${kind}.dxf`, import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({ name: `${kind}.dxf`, mimeType: 'application/dxf', buffer });
        await expect(canvas).not.toHaveAttribute('data-entity-count', '0'); await tool('Fit extents');
    };
    const screen = p => canvas.evaluate((c, p) => {
        const d = c.dataset, r = c.getBoundingClientRect(); return { x: r.left + p[0] * +d.scale + +d.offsetX, y: r.top - p[1] * +d.scale + +d.offsetY };
    }, p);
    const move = async p => { const s = await screen(p); await page.mouse.move(s.x, s.y, { steps: 8 }); await page.waitForTimeout(150); };
    const click = async p => { await move(p); await page.mouse.down(); await page.mouse.up(); await page.waitForTimeout(180); };
    const centerPainted = (p) => canvas.evaluate((c, p) => {
        const d = c.dataset, ratio = c.width / c.clientWidth;
        const x = Math.round((p[0] * +d.scale + +d.offsetX) * ratio), y = Math.round((-p[1] * +d.scale + +d.offsetY) * ratio);
        const pixels = c.getContext('2d').getImageData(x - 3, y - 3, 7, 7).data;
        for (let i = 0; i < pixels.length; i += 4) if (pixels[i+1] > 150 && pixels[i+3] > 150) return true;
        return false;
    }, p);
    for (const kind of ['circle', 'arc']) {
        await load(kind); const before = native(await download());
        await click([13, 14]); await move([19, 1]);
        assert.ok(await centerPainted([10, 10]), `${kind} selected center visible without hover`);
        await page.screenshot({ path: `${output}/${kind}-selected-center.png` });
        await move([10, 10]); await page.mouse.down(); await move([11, 11]);
        assert.ok(await centerPainted([11, 11]), `${kind} center follows held preview`);
        await page.mouse.up(); await page.waitForTimeout(180);
        const moved = native(await download(`${kind}-moved`));
        close(moved.entities[0].center.x, 11); close(moved.entities[0].center.y, 11);
        await tool('Undo'); assert.deepEqual(native(await download()).entities, before.entities);
        await tool('Redo'); assert.deepEqual(native(await download()).entities, moved.entities);
        await tool('Undo'); await download(); await load(kind);
        await tool('Line'); await click([13, 14]); await move([9.02, 17.03]);
        assert.ok((await canvas.getAttribute('data-hovered-id')).includes('tangent-curve'), 'Outgoing tangent snap selected');
        await page.screenshot({ path: `${output}/${kind}-outgoing-tangent.png` });
        await page.mouse.down(); await page.mouse.up(); await page.waitForTimeout(200); await page.keyboard.press('Escape'); await page.waitForTimeout(150);
        const saved = await download(`${kind}-tangent`), after = native(saved);
        const line = after.entities.find(e => e.kind === 'line'); assert.ok(line);
        close(line.start.x, 13); close(line.start.y, 14);
        close(((line.end.x-line.start.x)*3 + (line.end.y-line.start.y)*4)
            / (5 * Math.hypot(line.end.x-line.start.x, line.end.y-line.start.y)), 0);
        assert.equal(after.constraints.filter(c => c.kind === 'Tangent').length, 1);
        assert.equal(after.constraints.find(c => c.kind === 'Tangent').state, 'Satisfied');
        await tool('Undo'); assert.equal(native(await download()).entities.length, 3);
        await tool('Redo'); assert.deepEqual(native(await download()).entities, after.entities);
        await load(kind, saved); const reopened = native(await download(`${kind}-reopened`));
        assert.deepEqual(reopened.entities, after.entities); assert.deepEqual(reopened.constraints, after.constraints);
        console.log(`${kind}: visible center, held drag, outgoing snap, automatic Tangent, history and native reopen pass`);
    }
    assert.deepEqual(errors, []);
} finally { await browser.close(); }
