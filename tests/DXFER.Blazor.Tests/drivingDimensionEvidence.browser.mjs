import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
const { chromium, expect } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5038';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-driving-browser';
await mkdir(output, { recursive: true });
const native = text => {
    const lines = text.trim().split(/\r?\n/); let hex = '';
    for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
    assert.ok(hex); return JSON.parse(Buffer.from(hex, 'hex'));
};
const fixture = ['0','SECTION','2','HEADER','9','$INSUNITS','70','1','0','ENDSEC',
    '0','SECTION','2','ENTITIES','0','CIRCLE','5','100','8','0','10','10','20','10','40','5',
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
    const build = await page.locator('.app-brand').getAttribute('title'); console.log(build);
    if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
    const tool = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.waitForTimeout(150); };
    const menu = async name => { await tool('Open application menu'); await page.getByRole('button', { name: 'File', exact: true }).hover(); await tool(name); };
    const download = async name => {
        const wait = page.waitForEvent('download'); await menu('Download DXF');
        const text = await readFile(await (await wait).path(), 'utf8');
        await writeFile(`${output}/${name}.dxf`, text); return { text, doc: native(text) };
    };
    const load = async text => {
        await canvas.press('Escape'); await menu('New drawing');
        await expect(canvas).toHaveAttribute('data-entity-count', '0');
        await page.locator('input[type=file]').setInputFiles({ name: 'driving.dxf', mimeType: 'application/dxf', buffer: Buffer.from(text) });
        await expect(canvas).toHaveAttribute('data-entity-count', '1');
        await tool('Fit extents'); await page.waitForTimeout(500); await canvas.press('Escape');
    };
    const click = async (x, y) => {
        const p = await canvas.evaluate((c, p) => ({ x: c.getBoundingClientRect().left + p.x * +c.dataset.scale + +c.dataset.offsetX,
            y: c.getBoundingClientRect().top - p.y * +c.dataset.scale + +c.dataset.offsetY }), { x, y });
        await page.mouse.move(p.x, p.y); await page.waitForTimeout(100); await page.mouse.click(p.x, p.y); await page.waitForTimeout(200);
    };
    const dimension = id => page.locator(`input[data-dimension-id="${id}"]`);
    const context = page.getByRole('dialog', { name: 'Drawing context commands' });
    const mode = async (id, name) => {
        await dimension(id).click({ button: 'right' }); await expect(context).toBeVisible();
        await page.screenshot({ path: `${output}/mode-${name}.png` });
        await context.getByRole('radio', { name, exact: true }).check(); await expect(context).toBeHidden();
    };
    await load(fixture);
    for (const y of [13, 7]) {
        await expect(canvas).toHaveAttribute('data-active-tool', 'select');
        await tool('Dimension');
        await expect(canvas).toHaveAttribute('data-active-tool', 'dimension');
        await click(14.7766824456, 11.4776010333);
        await expect(canvas).toHaveAttribute('data-dimension-draft-complete', 'true');
        await click(13, y);
        await expect(page.locator('input[data-dimension-id]')).toHaveCount(y === 13 ? 1 : 2);
        await canvas.press('Escape');
    }
    await expect(page.locator('input[data-dimension-id]')).toHaveCount(2);
    const failed = await download('redundant-on-placement');
    const [first, second] = failed.doc.dimensions;
    assert.equal(first.isDriving, true); assert.equal(second.isDriving, true);
    assert.equal(first.value, second.value);
    const rejected = doc => assert.ok(doc.solveEvidence?.intents.some(i => i.itemId === second.id
        && i.state === 'Unsatisfied' && i.reason === 'RedundantDrivingDimension'), JSON.stringify(doc.solveEvidence));
    rejected(failed.doc);
    await expect(dimension(second.id)).toHaveClass(/unsatisfied/);
    await page.screenshot({ path: `${output}/redundant-on-placement.png` });
    await tool('Undo'); await expect(page.locator('input[data-dimension-id]')).toHaveCount(1);
    await tool('Redo'); await expect(dimension(second.id)).toHaveClass(/unsatisfied/);
    await load(failed.text); rejected((await download('failed-reopened')).doc);
    await expect(dimension(second.id)).toHaveClass(/unsatisfied/);
    await mode(second.id, 'Driven');
    const driven = await download('converted-driven');
    assert.equal(driven.doc.dimensions.find(d => d.id === second.id).isDriving, false);
    assert.deepEqual(driven.doc.entities, failed.doc.entities);
    await expect(dimension(second.id)).not.toHaveClass(/unsatisfied/);
    await mode(second.id, 'Driving');
    const refused = await download('redundant-promotion-refused');
    assert.equal(refused.doc.dimensions.find(d => d.id === second.id).isDriving, false);
    assert.deepEqual(refused.doc.entities, failed.doc.entities);
    await dimension(first.id).dblclick(); await dimension(first.id).fill('12'); await dimension(first.id).press('Enter'); await canvas.press('Escape');
    const edited = await download('driven-follows-edit');
    assert.ok(edited.doc.dimensions.every(d => Math.abs(d.value - 12) < 1e-7));
    assert.ok(Math.abs(edited.doc.entities[0].radius - 6) < 1e-7);
    await load(edited.text);
    assert.equal((await download('driven-reopened')).doc.dimensions.find(d => d.id === second.id).isDriving, false);
    await page.screenshot({ path: `${output}/driven-reopened.png` });
    assert.deepEqual(errors, []);
    console.log('PASS immediate redundancy, visible failure, Undo/Redo, native reopen, RMB modes, refused redundant promotion and driven measurement following');
} finally { await browser.close(); }
