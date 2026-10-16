// Disposable polygon transactions against isolated local/Dev instances only.
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-polygon-evidence';
await mkdir(output, { recursive: true });
const snapshot = text => {
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
        const rect = await canvas.boundingBox();
        await page.mouse.move(rect.x + rect.width / 2, rect.y + rect.height / 2);
        await page.mouse.wheel(0, 300); await page.waitForTimeout(150);
    };
    const screen = point => canvas.evaluate((c, p) => {
        const d = c.dataset, r = c.getBoundingClientRect();
        return { x: r.left + p[0] * Number(d.scale) + Number(d.offsetX), y: r.top - p[1] * Number(d.scale) + Number(d.offsetY) };
    }, point);
    const drag = async (from, delta, name, locked, paintCenter = locked ? [0, 0] : delta) => {
        const a = await screen(from), b = await screen([from[0] + delta[0], from[1] + delta[1]]);
        await page.mouse.move(a.x, a.y); await page.waitForTimeout(80);
        const hovered = await canvas.getAttribute('data-hovered-id');
        assert.ok(hovered?.startsWith('polygon'), `Wrong grip: ${hovered}`);
        await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 12 }); await page.waitForTimeout(120);
        await page.screenshot({ path: `${output}/${name}-preview.png` });
        // Sample the opposite edges away from glyphs and the drag target.
        const painted = await canvas.evaluate((c, p) => {
            const d = c.dataset, ctx = c.getContext('2d'), ratio = c.width / c.clientWidth;
            return p.map(([wx, wy]) => {
                const x = Math.round((wx * Number(d.scale) + Number(d.offsetX)) * ratio);
                const y = Math.round((-wy * Number(d.scale) + Number(d.offsetY)) * ratio);
                const pixels = ctx.getImageData(x - 4, y - 4, 9, 9).data;
                for (let i = 0; i < pixels.length; i += 4)
                    if (pixels[i + 1] > 100 && pixels[i + 3] > 80) return true;
                return false;
            });
        }, [[-4.3301270189, -1], [-2.1650635095, -3.75]].map(([x, y]) => [x + paintCenter[0], y + paintCenter[1]]));
        assert.ok(painted.every(Boolean), `Held-pointer geometry absent: ${name} ${painted}`);
        await page.mouse.up(); await page.waitForTimeout(180);
        await page.screenshot({ path: `${output}/${name}-released.png` });
    };
    for (const fixture of ['polygon-free', 'polygon-weld-free', 'polygon-fixed', 'polygon-weld-fixed']) {
        for (const [grip, from] of [['center', [0, 0]], ['midpoint', [2.1650635095, 3.75]], ['edge', [3.0310889132, 3.25]]]) {
            const name = `${fixture}-${grip}`, locked = fixture.endsWith('fixed');
            await load(fixture); const original = snapshot(await download());
            await drag(from, [1, 1], name, locked);
            const file = await download(name), next = snapshot(file);
            if (locked) {
                assert.deepEqual(next.entities, original.entities);
                assert.deepEqual(next.dimensions, original.dimensions);
                assert.deepEqual(next.constraints, original.constraints);
                await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
                await load(`${name}-reopened`, file);
                const reopened = snapshot(await download());
                for (const key of ['entities', 'dimensions', 'constraints'])
                    assert.deepEqual(reopened[key], original[key]);
            } else {
                const polygon = next.entities[0];
                close(polygon.center.x, 1); close(polygon.center.y, 1);
                assert.equal(polygon.radius, 5); assert.equal(polygon.sideCount, 6); assert.equal(polygon.rotationAngleDegrees, 30);
                for (let i = 0; i < next.dimensions.length; i++) {
                    const before = original.dimensions[i], after = next.dimensions[i];
                    assert.equal(after.value, before.value); assert.equal(after.isDriving, before.isDriving);
                    close(after.anchor.x, before.anchor.x + 1); close(after.anchor.y, before.anchor.y + 1);
                }
                assert.deepEqual(next.constraints, original.constraints.map(c => ({ ...c, state: 'Satisfied' })));
                if (fixture.includes('weld')) {
                    close(next.entities[1].start.x, 1); close(next.entities[1].start.y, 1);
                    assert.deepEqual(next.entities[1].end, original.entities[1].end);
                }
                await page.getByRole('button', { name: 'Undo', exact: true }).click();
                const undone = snapshot(await download());
                for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(undone[key], original[key]);
                await page.getByRole('button', { name: 'Redo', exact: true }).click();
                const redone = snapshot(await download());
                for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(redone[key], next[key]);
                await load(`${name}-reopened`, file);
                const reopened = snapshot(await download());
                for (const key of ['entities', 'dimensions', 'constraints']) assert.deepEqual(reopened[key], next[key]);
                await drag([from[0] + 1, from[1] + 1], [-1, -1], `${name}-reedit`, false, [0, 0]);
                const edited = snapshot(await download(`${name}-reedit`));
                close(edited.entities[0].center.x, 0); close(edited.entities[0].center.y, 0);
                assert.deepEqual(edited.constraints, next.constraints);
                for (let i = 0; i < edited.dimensions.length; i++) {
                    const { anchor, ...intent } = edited.dimensions[i];
                    const { anchor: beforeAnchor, ...beforeIntent } = original.dimensions[i];
                    assert.deepEqual(intent, beforeIntent);
                    close(anchor.x, beforeAnchor.x); close(anchor.y, beforeAnchor.y);
                }
            }
            console.log(`PASS ${name}: preview/release, driving intent, history/native reopen${locked ? '/refusal' : '/edit again'}`);
        }
    }
    assert.deepEqual(errors, []);
} finally { await browser.close(); }
