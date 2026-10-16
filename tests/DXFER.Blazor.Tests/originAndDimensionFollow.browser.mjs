// Disposable native documents only; no Sync callback or machine access.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-origin-evidence';
await mkdir(output, { recursive: true });
const close = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const native = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    return JSON.parse(Buffer.from(hex, 'hex').toString('utf8'));
};
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 },
        extraHTTPHeaders: { 'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const build = await page.locator('.app-brand').getAttribute('title');
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION), build);
    console.log(`Browser build: ${build}`);
    const canvas = page.locator('canvas.drawing-canvas');
    const menu = async name => {
        await page.getByRole('button', { name: 'Open application menu' }).click();
        await page.getByRole('button', { name: 'File', exact: true }).hover();
        await page.getByRole('button', { name, exact: true }).click();
    };
    const download = async name => {
        const pending = page.waitForEvent('download'); await menu('Download DXF');
        const text = await readFile(await (await pending).path(), 'utf8');
        if (name) await writeFile(`${output}/${name}.dxf`, text);
        return text;
    };
    const load = async (name, text) => {
        await menu('New drawing'); await expect(canvas).toHaveAttribute('data-entity-count', '0');
        const buffer = Buffer.from(text ?? await readFile(new URL(`./fixtures/drag-matrix/${name}.dxf`, import.meta.url)));
        await page.locator('input[type=file]').setInputFiles({ name: `${name}.dxf`, mimeType: 'application/dxf', buffer });
        await expect(canvas).toHaveAttribute('data-entity-count', '3');
        await page.getByRole('button', { name: 'Fit extents', exact: true }).click();
        await page.waitForTimeout(180);
    };
    const screen = p => canvas.evaluate((c, p) => {
        const d = c.dataset, r = c.getBoundingClientRect();
        return { x: r.left + p[0] * Number(d.scale) + Number(d.offsetX), y: r.top - p[1] * Number(d.scale) + Number(d.offsetY) };
    }, p);
    const click = async p => { const s = await screen(p); await page.mouse.click(s.x, s.y); await page.waitForTimeout(180); };
    const tool = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.waitForTimeout(250); };
    const escape = async () => { await page.keyboard.press('Escape'); await page.waitForTimeout(100); };
    const history = async (before, after) => {
        await tool('Undo'); const undone = native(await download());
        for (const key of ['entities', 'dimensions']) assert.deepEqual(undone[key], before[key]);
        await tool('Redo'); const redone = native(await download());
        for (const key of ['entities', 'dimensions']) assert.deepEqual(redone[key], after[key]);
    };
    await load('anchor-follow'); const before = native(await download());
    const label = page.locator('input[data-dimension-id="width"]'), labelBefore = await label.boundingBox();
    const a = await screen([6, 2]), b = await screen([6, 4]);
    await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 12 });
    await page.waitForTimeout(150);
    const held = await label.boundingBox(), scale = Number(await canvas.getAttribute('data-scale'));
    assert.ok(Math.abs((held.y - labelBefore.y) + scale) < 2, 'Label must follow span during held preview');
    await page.screenshot({ path: `${output}/anchor-held.png` });
    await page.mouse.up(); await page.waitForTimeout(180);
    const text = await download('anchor-released'), after = native(text);
    close(after.dimensions[0].anchor.x, 4); close(after.dimensions[0].anchor.y, 1);
    close(after.dimensions[0].value, 4); close(after.entities[0].end.y, 4);
    await history(before, after);
    await load('anchor-follow', text); const reopened = native(await download());
    assert.deepEqual(reopened.dimensions, after.dimensions);
    console.log('Anchor preview, release, Undo/Redo and native reopen passed');

    for (const reverse of [false, true]) {
        await load('origin-pick'); const original = native(await download());
        await tool('Coincident');
        await click(reverse ? [2, 2] : [0, 0]);
        await expect(canvas).toHaveAttribute('data-selected-keys', reverse ? 'line|point|start|2|2' : '$origin:center');
        await click(reverse ? [0, 0] : [2, 2]);
        await escape();
        const text = await download(`origin-coincident-${reverse}`), next = native(text);
        await page.screenshot({ path: `${output}/origin-coincident-${reverse}.png` });
        if (next.entities[0].start.x !== 0) console.log(JSON.stringify({ constraints: next.constraints,
            status: await page.locator('.dxfer-command-bar').innerText(), canvas: await canvas.evaluate(c => ({ ...c.dataset })) }));
        close(next.entities[0].start.x, 0); close(next.entities[0].start.y, 0);
        assert.ok(next.constraints.some(c => c.referenceKeys.includes('$origin:center')));
        assert.equal(next.entities.length, 3, 'Origin is not an exported entity');
        await history(original, next);
        await load('origin-pick', text); const reopened = native(await download());
        assert.deepEqual(reopened.constraints, next.constraints);
        const a = await screen([0, 0]), b = await screen([1, 1]);
        await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 10 }); await page.mouse.up();
        await page.waitForTimeout(180);
        const refused = native(await download(`origin-fixed-${reverse}`));
        assert.deepEqual(refused.entities, next.entities);
        await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
        console.log(`Coincident origin order=${reverse}, native reopen and protected drag passed`);
    }
    await load('origin-pick'); await tool('Dimension');
    await click([0, 0]); await click([2, 2]); await click([1, -1]); await escape();
    const withDimension = native(await download('origin-dimension'));
    assert.equal(withDimension.dimensions.length, 1);
    assert.ok(withDimension.dimensions[0].referenceKeys.includes('$origin:center'));
    const dim = page.locator(`input[data-dimension-id="${withDimension.dimensions[0].id}"]`);
    await dim.fill('4'); await dim.press('Enter'); await escape();
    const changedText = await download('origin-dimension-edited'), changed = native(changedText);
    close(Math.hypot(changed.entities[0].start.x, changed.entities[0].start.y), 4);
    close(changed.dimensions[0].value, 4);
    await load('origin-pick', changedText);
    assert.deepEqual(native(await download()).dimensions, changed.dimensions);
    await page.screenshot({ path: `${output}/origin-dimension.png` });
    console.log('Origin dimension create/edit/native reopen passed');
    assert.deepEqual(errors, []);
} finally { await browser.close(); }
