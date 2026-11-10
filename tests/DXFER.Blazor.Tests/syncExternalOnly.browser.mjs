import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
import http from 'node:http';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const base = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5286';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(base).hostname));
const output = process.env.DXFER_QA_OUTPUT ?? '/tmp/dxfer-external-only-20261110';
const fixture = await readFile(new URL('../Fixtures/production-cleanup-louver.dxf', import.meta.url));
const token = 'synthetic-download-only-token';
const downloads = [];
const callbacks = [];
const attempts = new Map();
const source = http.createServer(async (req, res) => {
    if (req.method !== 'GET') {
        callbacks.push(req.url);
        for await (const chunk of req) { /* Drain synthetic callback payload. */ }
        return res.writeHead(403).end('Download-only token cannot write');
    }
    downloads.push(req.url);
    const url = new URL(req.url, 'http://localhost');
    const count = (attempts.get(url.pathname) ?? 0) + 1;
    attempts.set(url.pathname, count);
    const deniedStatus = Number(url.searchParams.get('deny'));
    if (url.searchParams.get('editToken') !== token || (deniedStatus && count === 1)) {
        return res.writeHead(deniedStatus || 401).end(token);
    }
    res.writeHead(200, { 'Content-Type': 'application/dxf', 'Content-Disposition': 'attachment; filename="external-fixture.dxf"' }).end(fixture);
});
await new Promise(resolve => source.listen(0, '127.0.0.1', resolve));
const sourceUrl = `http://127.0.0.1:${source.address().port}`;
const launch = (job, autoNormalize = false, deny = 0) => `${base}/?${new URLSearchParams({
    syncBaseUrl: sourceUrl, artifactId: '1', jobId: job, editToken: token,
    downloadUrl: `${sourceUrl}/${job}?editToken=${token}&deny=${deny}`,
    saveToSync: 'false', autoNormalize: String(autoNormalize)
})}`;
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_PATH ?? '/usr/bin/google-chrome' });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const status = page.locator('.dxfer-sync-control-bar [role=status]');
    const canvas = page.locator('canvas.drawing-canvas');
    const loaded = () => page.waitForFunction(() => document.querySelector('canvas.drawing-canvas')?.dataset.entityCount === '9');
    const openFileMenu = async () => {
        await page.getByRole('button', { name: 'Open application menu' }).click();
        await page.getByRole('button', { name: 'File', exact: true }).hover();
    };
    const download = async () => {
        await openFileMenu();
        const ready = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Download DXF', exact: true }).click();
        const file = await ready;
        assert.equal(file.suggestedFilename(), 'external-fixture.dxf');
        return readFile(await file.path(), 'utf8');
    };
    // Role policy belongs to Sync; both role launch shapes carry the same capability.
    for (const [job, normalize] of [['production', false], ['estimating', true]]) {
        await page.goto(launch(job, normalize));
        await loaded();
        assert.equal(await status.textContent(), 'External save only');
        assert.equal(await page.locator('.dxfer-sync-send-button').count(), 0);
        assert.equal(await page.getByRole('button', { name: 'Save to Sync', exact: true, includeHidden: true }).count(), 0);
        const initial = await download();
        assert.ok(initial.includes('EOF'));
        assert.ok(!initial.includes('SHEETMETAL_FORM'), 'existing import cleanup remains active');
        await page.getByRole('button', { name: 'Rotate 90 CW', exact: true }).click();
        const edited = await download();
        assert.notEqual(edited, initial, 'external export contains the local geometry edit');
        await page.getByRole('button', { name: 'Undo', exact: true }).click();
        assert.equal(await download(), initial, 'download does not clear undo history');
        await page.getByRole('button', { name: 'Redo', exact: true }).click();
        assert.equal(await download(), edited);
        assert.ok(!(await page.locator('body').innerText()).includes('Saved to Sync'));
        assert.deepEqual(callbacks, [], 'external-only UI never makes a callback request');
    }

    for (const code of [401, 403]) {
        await page.goto(launch(`denied-${code}`, false, code));
        await status.filter({ hasText: `HTTP ${code}` }).waitFor();
        assert.equal(await canvas.getAttribute('data-entity-count'), '0');
        assert.equal(await page.locator('.dxfer-sync-send-button').count(), 0);
        assert.ok(!(await page.locator('body').innerText()).includes(token));
        const deniedUrl = downloads.at(-1);
        await page.getByRole('button', { name: 'Retry Sync download', exact: true }).click();
        await loaded();
        assert.equal(downloads.at(-1), deniedUrl, 'retry retains the exact signed URL');
        assert.equal(await status.textContent(), 'External save only');
    }

    for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 844 });
        await page.getByRole('button', { name: 'Fit', exact: true }).click();
        const metrics = await page.locator('.dxfer-sync-control-bar').evaluate(bar => {
            const label = bar.querySelector('[role=status]');
            const rect = bar.getBoundingClientRect();
            const overlaps = (a, b) => a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
            return {
                bounded: rect.left >= 0 && rect.right <= innerWidth,
                clipped: label.scrollWidth > label.clientWidth + 1 || label.scrollHeight > label.clientHeight + 1,
                coversPalette: [...document.querySelectorAll('.dxfer-tool-group')].some(group => overlaps(rect, group.getBoundingClientRect()))
            };
        });
        assert.deepEqual(metrics, { bounded: true, clipped: false, coversPalette: false });
        const colors = await canvas.evaluate(c => {
            const pixels = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
            const colors = new Set();
            for (let i = 0; i < pixels.length; i += 4) colors.add(`${pixels[i]},${pixels[i + 1]},${pixels[i + 2]}`);
            return colors.size;
        });
        assert.ok(colors > 10, 'nonblank edited canvas');
        await page.screenshot({ path: `${output}/external-only-${width}.png` });
        await openFileMenu();
        assert.ok(await page.getByRole('button', { name: 'Download DXF', exact: true }).isVisible());
        assert.equal(await page.getByRole('button', { name: 'Save to Sync', exact: true, includeHidden: true }).count(), 0);
        await page.screenshot({ path: `${output}/external-file-menu-${width}.png` });
        await page.mouse.move(width - 2, 300);
    }
    const capabilities = await (await page.request.get(`${base}/api/dxfer/capabilities`)).json();
    assert.ok(capabilities.launchParameters.includes('saveToSync'));
    assert.deepEqual(callbacks, []);
    assert.deepEqual(errors, []);
    console.log('PASS external-only signed load/denial/retry, edited DXF download, history, hidden save actions, zero callbacks and desktop/mobile canvas/layout.');
    console.log(`Screenshots: ${output}`);
} finally {
    await browser.close();
    source.closeAllConnections();
    await new Promise(resolve => source.close(resolve));
}
