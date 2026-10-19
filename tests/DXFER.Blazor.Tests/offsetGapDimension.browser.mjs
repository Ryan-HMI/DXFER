import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/offset-gap-dimension';
await mkdir(output, {recursive: true});
const native = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    return JSON.parse(Buffer.from(hex, 'hex'));
};
const browser = await chromium.launch({headless: true});
try {
    const page = await browser.newPage({viewport: {width: 1188, height: 761},
        extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => {if (m.type() === 'error' && /unhandled|circuit|exception/i.test(m.text())) errors.push(m.text());});
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const canvas = page.locator('canvas.drawing-canvas');
    const tool = async name => {await page.getByRole('button', {name, exact: true}).click(); await page.waitForTimeout(150);};
    const menu = async name => {await tool('Open application menu'); await page.getByRole('button', {name: 'File', exact: true}).hover(); await tool(name);};
    const download = async name => {
        const pending = page.waitForEvent('download'); await menu('Download DXF');
        const text = await readFile(await (await pending).path(), 'utf8');
        await writeFile(`${output}/${name}.dxf`, text);
        return {text, doc: native(text)};
    };
    const load = async (buffer, count) => {
        await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
        await page.locator('input[type=file]').setInputFiles({name: 'offset-gap.dxf', mimeType: 'application/dxf', buffer: Buffer.from(buffer)});
        await expect(canvas).toHaveAttribute('data-entity-count', String(count)); await tool('Fit extents');
    };
    const screen = p => canvas.evaluate((c, p) => ({x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
        y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY}), p);
    const click = async (x, y) => {const p = await screen({x, y}); await page.mouse.click(p.x, p.y); await page.waitForTimeout(180);};
    for (const keyed of [false, true]) {
        const tag = keyed ? 'keyed' : 'pointer';
        await load(await readFile(new URL('./fixtures/selection-offset/rectangle.dxf', import.meta.url)), 4);
        const a = await screen({x: -0.5, y: 6.5}), b = await screen({x: 10.5, y: -0.5});
        await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, {steps: 12}); await page.mouse.up();
        await expect(canvas).toHaveAttribute('data-selected-count', '4'); await tool('Offset');
        const side = await screen({x: 5, y: -1}); await page.mouse.move(side.x, side.y);
        const field = page.locator('input[data-dimension-key="offset"]'); await expect(field).toBeVisible();
        if (keyed) {await field.fill('1'); await field.press('Enter');}
        else await page.mouse.click(side.x, side.y);
        await expect(canvas).toHaveAttribute('data-entity-count', '5'); await canvas.press('Escape'); await tool('Fit extents');
        const original = await download(`${tag}-before`);
        await tool('Dimension'); await click(3, 0); await click(3, -1); await click(11.5, -0.5);
        await canvas.press('Escape');
        const placed = await download(`${tag}-placed`);
        const gap = placed.doc.dimensions.filter(d => d.kind === 'Offset');
        assert.equal(gap.length, 1);
        assert.equal(placed.doc.dimensions.length, original.doc.dimensions.length + (keyed ? 0 : 1));
        assert.deepEqual(placed.doc.entities, original.doc.entities);
        if (keyed) assert.equal(gap[0].id, original.doc.dimensions.find(d => d.kind === 'Offset').id);
        const edit = async value => {
            const input = page.locator(`input[data-dimension-id="${gap[0].id}"]`);
            await input.dblclick(); await input.fill(String(value)); await input.press('Enter'); await page.waitForTimeout(300);
        };
        await edit(2);
        const changed = await download(`${tag}-changed`);
        assert.equal(changed.doc.dimensions.find(d => d.id === gap[0].id).value, 2);
        assert.ok(changed.doc.constraints.every(c => c.state === 'Satisfied'));
        assert.ok(Math.abs(Math.min(...changed.doc.entities.at(-1).vertices.map(p => p.y)) + 2) < 1e-6);
        assert.deepEqual(changed.doc.entities.slice(0, 4), original.doc.entities.slice(0, 4));
        await page.screenshot({path: `${output}/${tag}-edited.png`});
        await tool('Undo'); assert.deepEqual((await download(`${tag}-undo`)).doc.entities, placed.doc.entities);
        await tool('Redo'); assert.deepEqual((await download(`${tag}-redo`)).doc.entities, changed.doc.entities);
        await load(changed.text, 5); await edit(1.5);
        const reopened = await download(`${tag}-reopen-edit`);
        assert.equal(reopened.doc.dimensions.find(d => d.id === gap[0].id).value, 1.5);
        assert.ok(reopened.doc.constraints.every(c => c.state === 'Satisfied'));
        console.log(`PASS ${tag}: Dimension source/offset edges, unique driving gap, edit, history, native reopen/edit`);
    }
    assert.deepEqual(errors, []);
    const build = await page.locator('.app-brand').getAttribute('title');
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    console.log(build);
} finally {await browser.close();}
