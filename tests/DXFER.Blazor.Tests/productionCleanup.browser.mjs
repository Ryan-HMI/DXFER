import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
import http from 'node:http';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const base = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5274';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(base).hostname));
const output = process.env.DXFER_QA_OUTPUT ?? '/tmp/dxfer-production-cleanup-browser';
const line = (layer, x, y, xx, yy) => `0\nLINE\n8\n${layer}\n10\n${x}\n20\n${y}\n11\n${xx}\n21\n${yy}\n`;
const circle = radius => `0\nCIRCLE\n8\nCUT\n10\n8\n20\n8\n40\n${radius}\n`;
const dxf = entities => `0\nSECTION\n2\nENTITIES\n${entities}0\nENDSEC\n0\nEOF\n`;
const outline = line('SHEETMETAL_CUT_LINES', -5, -5, 10, -5)
    + line('SHEETMETAL_CUT_LINES', 10, -5, 10, 10)
    + line('SHEETMETAL_CUT_LINES', 10, 10, -5, 10)
    + line('SHEETMETAL_CUT_LINES', -5, 10, -5, -5);
const fixture = dxf(outline + line('SHEETMETAL_FORM_TEAR', 0, 0, 3, 0)
    + line('SHEETMETAL_FORM_UP', 0, 0, 3, 1) + line('BEND', 0, 2, 3, 2) + circle(.0249) + circle(.025));
const unsafe = dxf(outline + line('SHEETMETAL_FORM_TEAR', 0, 0, 3, 0) + line('CUT', 1, -1, 1, 1));
let callback;
const source = http.createServer(async (req, res) => {
    if (req.url.startsWith('/api/dxfer/edit-callback')) {
        const chunks = [];
        for await (const chunk of req) chunks.push(chunk);
        callback = Buffer.concat(chunks).toString();
        res.writeHead(200, { 'Content-Type': 'application/json' }).end('{}');
    } else res.writeHead(200, { 'Content-Type': 'application/dxf' }).end(req.url.startsWith('/unsafe') ? unsafe : fixture);
});
await new Promise(resolve => source.listen(0, '127.0.0.1', resolve));
const sourceUrl = `http://127.0.0.1:${source.address().port}`;
const launch = (path, autoNormalize) => `${base}/?${new URLSearchParams({ syncBaseUrl: sourceUrl,
    artifactId: '1', jobId: 'cleanup-qa', editToken: 'synthetic-placeholder', downloadUrl: sourceUrl + path,
    autoNormalize: String(autoNormalize) })}`;
const checkDxf = text => {
    const entities = text.replaceAll('\r', '').split('2\nENTITIES\n')[1]?.split('0\nENDSEC')[0];
    assert.ok(entities, 'complete entity section');
    assert.equal((entities.match(/^ARC$/gm) ?? []).length, 2);
    assert.equal((entities.match(/^LINE$/gm) ?? []).length, 6);
    assert.equal((entities.match(/^CIRCLE$/gm) ?? []).length, 1);
    assert.ok(entities.includes('40\n0.025\n'), 'cutoff circle retained');
    assert.equal((entities.match(/40\n0\.02\n/g) ?? []).length, 2, 'slot cap radius');
    assert.ok(!entities.includes('SHEETMETAL_FORM') && !entities.includes('BEND'));
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_PATH ?? '/usr/bin/google-chrome' });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const canvas = page.locator('canvas.drawing-canvas');
    const waitLoaded = () => page.waitForFunction(() => document.querySelector('canvas.drawing-canvas')?.dataset.entityCount === '9');
    for (const normalize of [false, true]) {
        await page.goto(launch('/fixture', normalize));
        await waitLoaded();
        await page.locator('.dxfer-sync-send-button').click();
        for (let attempt = 0; attempt < 100 && !callback; attempt++) await page.waitForTimeout(50);
        assert.ok(callback, 'synthetic callback received');
        checkDxf(callback);
        callback = undefined;
        await page.screenshot({ path: `${output}/sync-${normalize}.png` });
    }
    const api = await page.request.post(`${base}/api/dxfer/normalize`, {
        multipart: { dxf: { name: 'cleanup.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture) } }
    });
    assert.equal(api.status(), 200);
    const normalized = await api.json();
    assert.equal(normalized.productionCleanup.tearSlotsCreated, 1);
    assert.equal(normalized.productionCleanup.smallCirclesRemoved, 1);
    checkDxf(normalized.normalizedDxf);
    const refused = await page.request.post(`${base}/api/dxfer/normalize`, {
        multipart: { dxf: { name: 'unsafe.dxf', mimeType: 'application/dxf', buffer: Buffer.from(unsafe) } }
    });
    assert.equal(refused.status(), 400);
    assert.ok(!(await refused.text()).includes('normalizedDxf'));

    // Local Open uses the same workbench, without a Sync launch or callback.
    await page.goto(base);
    await page.waitForFunction(() => document.querySelector('canvas.drawing-canvas')?.dataset.entityCount === '0');
    await page.locator('input[type=file]').setInputFiles({ name: 'local-cleanup.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture) });
    await waitLoaded();
    await page.getByRole('button', { name: 'Open application menu' }).click();
    await page.getByRole('button', { name: 'File', exact: true }).hover();
    const downloaded = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download DXF', exact: true }).click();
    checkDxf(await readFile(await (await downloaded).path(), 'utf8'));
    await page.locator('input[type=file]').setInputFiles({ name: 'unsafe.dxf', mimeType: 'application/dxf', buffer: Buffer.from(unsafe) });
    await page.locator('.dxfer-command-prompt').filter({ hasText: 'Production cleanup requires review' }).waitFor();
    assert.equal(await canvas.getAttribute('data-entity-count'), '9', 'refusal retains previous drawing');
    for (const width of [1440, 390]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(base);
        await page.waitForFunction(() => document.querySelector('canvas.drawing-canvas')?.dataset.entityCount === '0');
        await page.locator('input[type=file]').setInputFiles({ name: 'local-cleanup.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture) });
        await waitLoaded();
        await page.screenshot({ path: `${output}/local-${width}.png` });
        const colors = await canvas.evaluate(c => {
            const data = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
            const colors = new Set();
            for (let i = 0; i < data.length; i += 4) colors.add(`${data[i]},${data[i+1]},${data[i+2]},${data[i+3]}`);
            return colors.size;
        });
        assert.ok(colors > 10, 'nonblank canvas');
    }
    await page.goto(launch('/unsafe', false));
    await page.locator('.dxfer-sync-control-bar [role=status]').filter({ hasText: 'Production cleanup requires review' }).waitFor();
    assert.ok(await page.locator('.dxfer-sync-send-button').isDisabled());
    assert.equal(await canvas.getAttribute('data-entity-count'), '0');
    assert.deepEqual(errors, []);
    console.log('PASS API, local Open/download, Sync both normalization modes/callback, refusal and desktop/mobile canvas.');
} finally {
    await browser.close();
    source.closeAllConnections();
    await new Promise(resolve => source.close(resolve));
}
