// Disposable native circle fixtures; no live Sync documents or machine actions.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-circle-evidence';
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
        await expect(canvas).not.toHaveAttribute('data-entity-count', '0');
        await page.getByRole('button', { name: 'Fit extents', exact: true }).click();
        await page.waitForTimeout(150);
        const box = await canvas.boundingBox(); await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.wheel(0, 300); await page.waitForTimeout(150);
    };
    const drag = async (from, to, name, center, radius) => {
        const screen = p => canvas.evaluate((c, p) => {
            const d = c.dataset, r = c.getBoundingClientRect();
            return { x: r.left + p[0] * Number(d.scale) + Number(d.offsetX), y: r.top - p[1] * Number(d.scale) + Number(d.offsetY) };
        }, p);
        const a = await screen(from), b = await screen(to);
        await page.mouse.move(a.x, a.y); await page.waitForTimeout(80);
        assert.ok((await canvas.getAttribute('data-hovered-id'))?.startsWith('circle'));
        await page.mouse.down();
        await page.mouse.move(b.x, b.y, { steps: 12 }); await page.waitForTimeout(120);
        const painted = await canvas.evaluate((c, points) => {
            const d = c.dataset, ctx = c.getContext('2d'), ratio = c.width / c.clientWidth;
            return points.every(([wx, wy]) => {
                const x = Math.round((wx * Number(d.scale) + Number(d.offsetX)) * ratio);
                const y = Math.round((-wy * Number(d.scale) + Number(d.offsetY)) * ratio);
                const pixels = ctx.getImageData(x - 4, y - 4, 9, 9).data;
                for (let i = 0; i < pixels.length; i += 4) if (pixels[i + 1] > 100 && pixels[i + 3] > 80) return true;
                return false;
            });
        }, [Math.PI * 0.75, Math.PI * 1.25].map(t => [center[0] + radius * Math.cos(t), center[1] + radius * Math.sin(t)]));
        assert.ok(painted, `Held circle not painted: ${name}`);
        const label = page.locator('input[data-dimension-id="radius"]');
        close(Number(await label.getAttribute('data-dimension-value')), radius);
        close(Number((await label.inputValue()).replace(/^R\s*/, '')), radius);
        await page.screenshot({ path: `${output}/${name}-preview.png` });
        await page.mouse.up(); await page.waitForTimeout(180);
        await page.screenshot({ path: `${output}/${name}-released.png` });
    };
    const cases = ['free', 'weld-free', 'fixed', 'weld-fixed'].flatMap(mode => ['center', 'quadrant'].map(grip => ({ mode, grip })))
        .concat(['radius-free', 'radius-center-fixed'].map(mode => ({ mode, grip: 'quadrant' })));
    for (const { mode, grip } of cases.filter(c => !process.env.DXFER_CASE_FILTER || c.mode.includes(process.env.DXFER_CASE_FILTER))) {
        const name = `circle-${mode}-${grip}`, resizing = mode.startsWith('radius-'), locked = !resizing && mode.endsWith('fixed');
        const from = grip === 'center' ? [0, 0] : [3, 0], to = resizing ? [4, 0] : [from[0] + 1, from[1] + 1];
        const center = locked || resizing ? [0, 0] : [1, 1], radius = resizing ? 4 : 3;
        await load(`circle-${mode}`); const original = native(await download());
        await drag(from, to, name, center, radius);
        const file = await download(name), next = native(file);
        close(next.entities[0].center.x, center[0]); close(next.entities[0].center.y, center[1]); close(next.entities[0].radius, radius);
        assert.equal(next.dimensions[0].isDriving, !resizing); close(next.dimensions[0].value, radius);
        if (!resizing) {
            close(next.dimensions[0].anchor.x, 4 + center[0]); close(next.dimensions[0].anchor.y, -1 + center[1]);
        }
        if (mode.startsWith('weld')) {
            close(next.entities[1].start.x, center[0]); close(next.entities[1].start.y, center[1]);
            assert.deepEqual(next.entities[1].end, original.entities[1].end);
        }
        if (locked) {
            for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(next[key], original[key]);
            await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
        } else {
            assert.deepEqual(next.constraints, original.constraints.map(c => ({ ...c, state: 'Satisfied' })));
            await page.getByRole('button', { name: 'Undo', exact: true }).click();
            const undone = native(await download());
            for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(undone[key], original[key]);
            await page.getByRole('button', { name: 'Redo', exact: true }).click();
            const redone = native(await download());
            for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(redone[key], next[key]);
        }
        await load(`${name}-reopened`, file); const reopened = native(await download());
        for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(reopened[key], next[key]);
        if (!locked) {
            await drag(to, from, `${name}-reedit`, [0, 0], 3);
            const edited = native(await download(`${name}-reedit`));
            close(edited.entities[0].center.x, 0); close(edited.entities[0].center.y, 0); close(edited.entities[0].radius, 3);
            close(edited.dimensions[0].value, 3); assert.equal(edited.dimensions[0].isDriving, !resizing);
        }
        console.log(`PASS ${name}: held preview/release, intent, history/native reopen${locked ? '/refusal' : '/reedit'}`);
    }
    assert.deepEqual(errors, []);
} finally { await browser.close(); }
