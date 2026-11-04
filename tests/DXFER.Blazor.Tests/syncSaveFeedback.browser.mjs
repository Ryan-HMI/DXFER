import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
import http from 'node:http';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const base = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5276';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(base).hostname));
const output = process.env.DXFER_QA_OUTPUT ?? '/tmp/dxfer-sync-save-feedback';
const fixture = await readFile(new URL('../Fixtures/production-cleanup-louver.dxf', import.meta.url));
const attempts = new Map();
let release;
const source = http.createServer(async (req, res) => {
    if (req.method !== 'POST') return res.writeHead(200, { 'Content-Type': 'application/dxf' }).end(fixture);
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    const job = new URL(req.url, 'http://localhost').pathname.split('/')[1];
    const count = (attempts.get(job) ?? 0) + 1;
    attempts.set(job, count);
    if (job === 'held') await new Promise(resolve => { release = resolve; });
    const status = job === 'unknown' ? 500 : job === 'retry' && count === 1 ? 400 : 200;
    res.writeHead(status, { 'Content-Type': 'application/json' }).end('{}');
});
await new Promise(resolve => source.listen(0, '127.0.0.1', resolve));
const sourceUrl = `http://127.0.0.1:${source.address().port}`;
const launch = job => `${base}/?${new URLSearchParams({ syncBaseUrl: sourceUrl + '/' + job + '/', artifactId: '1', jobId: job,
    editToken: 'synthetic-placeholder', downloadUrl: sourceUrl + '/fixture', autoNormalize: 'false' })}`;
const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_PATH ?? '/usr/bin/google-chrome' });
await mkdir(output, { recursive: true });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    // Simulate browser policy refusing window.close; the UI must still acknowledge the commit.
    await page.addInitScript(() => { window.close = () => {}; });
    const loaded = target => target.waitForFunction(() => document.querySelector('canvas.drawing-canvas')?.dataset.entityCount === '9');
    const save = page.locator('.dxfer-sync-send-button');
    const prompt = page.locator('.dxfer-command-prompt');
    await page.goto(launch('held'));
    await loaded(page);
    const saveWidth = (await save.boundingBox()).width;
    await save.click();
    await prompt.filter({ hasText: 'Saving to Sync' }).waitFor();
    assert.ok(await save.isDisabled());
    assert.equal((await save.boundingBox()).width, saveWidth);
    for (let i = 0; i < 100 && !release; i++) await page.waitForTimeout(25);
    assert.ok(release);
    await save.evaluate(button => button.click());
    assert.equal(attempts.get('held'), 1);
    release();
    await prompt.filter({ hasText: 'Close this tab. Reopen from Sync' }).waitFor();
    assert.equal(await save.textContent(), 'Saved');
    assert.ok(await save.isDisabled());
    assert.equal((await save.boundingBox()).width, saveWidth);
    assert.equal(await page.locator('.dxfer-sync-control-bar [role=status]').textContent(), 'Saved to Sync');
    assert.ok(!(await prompt.textContent()).includes('Removed 0 exact duplicate'));
    await page.screenshot({ path: `${output}/saved-desktop.png` });
    await page.getByRole('button', { name: 'Open application menu' }).click();
    await page.getByRole('button', { name: 'File', exact: true }).hover();
    await page.locator('.app-menu-command').filter({ hasText: /^Save to Sync$/ }).click();
    assert.equal(attempts.get('held'), 1, 'menu must not resubmit a consumed launch');

    await page.goto(launch('retry'));
    await loaded(page);
    await save.click();
    await prompt.filter({ hasText: 'HTTP 400' }).waitFor();
    assert.ok(await save.isEnabled());
    await save.click();
    await prompt.filter({ hasText: 'Saved to Sync' }).waitFor();
    assert.equal(attempts.get('retry'), 2);
    assert.ok(await save.isDisabled());

    await page.goto(launch('unknown'));
    await loaded(page);
    await save.click();
    await prompt.filter({ hasText: 'Save not confirmed (HTTP 500)' }).waitFor();
    assert.ok(await save.isDisabled());
    assert.equal(await save.textContent(), 'Check Sync');
    assert.equal(attempts.get('unknown'), 1);

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(launch('mobile'));
    await loaded(page);
    await save.click();
    await prompt.filter({ hasText: 'Close this tab. Reopen from Sync' }).waitFor();
    await page.screenshot({ path: `${output}/saved-mobile.png` });
    for (const selector of ['.dxfer-sync-control-bar', '.dxfer-command-prompt', '.dxfer-sync-send-button']) {
        const bounds = await page.locator(selector).boundingBox();
        assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 391 && bounds.y + bounds.height <= 845);
    }
    const popupSource = await browser.newPage();
    await popupSource.goto(base);
    const popupPromise = popupSource.waitForEvent('popup');
    await popupSource.evaluate(url => window.open(url, '_blank'), launch('closable'));
    const popup = await popupPromise;
    await loaded(popup);
    const closed = popup.waitForEvent('close');
    await popup.locator('.dxfer-sync-send-button').click();
    await closed;
    assert.equal(attempts.get('closable'), 1);
    console.log('PASS save feedback, pending/consumed guards, rejection/retry, uncertain outcome, desktop/mobile and real popup close.');
} finally {
    release?.();
    await browser.close();
    source.closeAllConnections();
    await new Promise(resolve => source.close(resolve));
}
