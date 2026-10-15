// Explicit isolated-app QA. Fixtures are native two-line length-10 corners with driving/reference dimensions and unspecified units.
import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-inspector-evidence';
await mkdir(output, {recursive: true});
const browser = await chromium.launch({headless: true});
try {
    const page = await browser.newPage({viewport: {width: 1440, height: 900},
        extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(url);
    await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
    const canvas = page.locator('canvas.drawing-canvas');
    const inspector = page.getByRole('complementary', {name: 'Active operation'});
    const input = page.locator('.dxfer-corner-size-control input');
    const menu = async name => {
        await page.getByRole('button', {name: 'Open application menu'}).click();
        await page.getByRole('button', {name: 'File', exact: true}).hover();
        await page.getByRole('button', {name, exact: true}).click();
    };
    const download = async () => {
        const event = page.waitForEvent('download');
        await menu('Download DXF');
        return readFile(await (await event).path(), 'utf8');
    };
    const native = text => {
        const lines = text.trim().split(/\r?\n/); let hex = '';
        for (let i = 0; i < lines.length; i += 2) if (lines[i].trim() === '310') hex += lines[i + 1].trim();
        return JSON.parse(Buffer.from(hex, 'hex').toString('utf8'));
    };
    const state = async expected => page.waitForFunction(expected => document.querySelector('.document-change-state')?.textContent === expected, expected);
    const clickCorner = async () => {
        await page.getByRole('button', {name: 'Fit extents', exact: true}).click();
        await page.waitForTimeout(200);
        const point = await canvas.evaluate(c => {
            const bounds = c.getBoundingClientRect();
            return {x: bounds.x + Number(c.dataset.offsetX), y: bounds.y + Number(c.dataset.offsetY), scale: Number(c.dataset.scale)};
        });
        await page.mouse.click(point.x + 4 * point.scale, point.y - 4 * point.scale);
        await page.waitForTimeout(150);
        await page.mouse.click(point.x, point.y);
        await page.waitForTimeout(200);
    };
    const load = async (file, name) => {
        await page.locator('input[type=file]').setInputFiles({name, mimeType: 'application/dxf', buffer: await readFile(file)});
        await page.waitForFunction(() => document.querySelector('canvas').dataset.entityCount === '2');
        await page.waitForFunction(name => document.querySelector('.document-filename')?.textContent === name, name);
        await state('Unchanged');
    };
    assert.equal(await page.locator('.document-filename').textContent(), 'Untitled.dxf');
    assert.equal(await inspector.count(), 0);
    const longName = 'DXFER-desktop-inspector-long-filename-'.repeat(5) + '.dxf';
    await load(process.env.DXFER_DRIVING_FIXTURE ?? '/tmp/dxfer-corner-driving.dxf', longName);
    assert.equal(await page.locator('.document-filename').getAttribute('title'), longName);
    assert.equal(await page.locator('.document-units').textContent(), 'Unspecified');
    const original = native(await download());
    await page.getByRole('button', {name: 'Fillet', exact: true}).click();
    await inspector.waitFor();
    assert.equal(await page.getByRole('status').count(), 1);
    await input.fill('0');
    await page.getByRole('button', {name: 'Hide operation inspector'}).click();
    await page.getByRole('button', {name: 'Show operation inspector'}).waitFor();
    assert.equal(await inspector.count(), 0);
    assert.equal(await input.inputValue(), '0');
    await page.getByRole('button', {name: 'Show operation inspector'}).click();
    await inspector.waitFor();
    assert.equal(await input.inputValue(), '0');
    assert.equal(await input.getAttribute('aria-invalid'), 'true');
    await clickCorner();
    assert.equal(await page.getByRole('button', {name: 'Undo', exact: true}).isDisabled(), true);
    await input.fill('2');
    await clickCorner();
    assert.match(await page.getByRole('status').innerText(), /unchanged/i);
    for (const [width, height] of [[1440,900], [1280,720], [1188,761], [1100,705], [960,540]]) {
        await page.setViewportSize({width, height});
        await page.waitForTimeout(180);
        await clickCorner();
        assert.match(await page.getByRole('status').innerText(), /unchanged/i);
        const panel = await inspector.boundingBox(), drawing = await canvas.boundingBox();
        assert.ok(drawing.x + drawing.width <= panel.x + 1);
        assert.ok(Math.abs(panel.width - 296) < 1);
        assert.equal(Math.round((await page.locator('.app-header').boundingBox()).height), 48);
        const exit = await page.getByRole('button', {name: 'Exit tool', exact: true}).boundingBox();
        assert.ok(exit.y + exit.height <= height);
        assert.equal(await page.locator('.dxfer-operation-heading h2').textContent(), 'Fillet');
        await page.screenshot({path: `${output}/rejected-${width}.png`});
    }
    assert.deepEqual(native(await download()).entities, original.entities);
    await page.setViewportSize({width: 1188, height: 761});
    await page.waitForTimeout(180);
    const view = page.locator('[data-palette-index="0"]');
    const header = view.locator('.dxfer-tool-group-drag-header');
    await header.scrollIntoViewIfNeeded();
    const grip = await header.boundingBox(), drawing = await canvas.boundingBox();
    await page.mouse.move(grip.x + 16, grip.y + 16);
    await page.mouse.down(); await page.waitForTimeout(100);
    await page.mouse.move(drawing.x + drawing.width - 30, 320, {steps: 10});
    await page.waitForTimeout(100); await page.mouse.up();
    await page.waitForFunction(() => JSON.parse(localStorage.getItem('dxfer.paletteLayout.v1'))?.groups.find(x => x.groupName === 'View')?.side === 2);
    const preferred = await page.evaluate(() => localStorage.getItem('dxfer.paletteLayout.v1'));
    const right = await view.boundingBox(), panel = await inspector.boundingBox();
    assert.ok(right.x + right.width <= panel.x + 1, 'right-docked palette must stay outside the inspector');
    await page.screenshot({path: `${output}/right-docked-1188.png`});
    await page.getByRole('button', {name: 'Hide operation inspector'}).click();
    await page.getByRole('button', {name: 'Show operation inspector'}).click();
    await inspector.waitFor();
    assert.equal(await page.evaluate(() => localStorage.getItem('dxfer.paletteLayout.v1')), preferred);
    await page.getByRole('button', {name: 'Exit tool', exact: true}).click();
    await page.waitForFunction(() => !document.querySelector('.dxfer-operation-inspector'));
    assert.equal(await canvas.evaluate(c => document.activeElement === c), true);
    assert.equal(await page.getByRole('button', {name: 'Undo', exact: true}).isDisabled(), true);
    await page.setViewportSize({width: 1440, height: 900});
    await load(process.env.DXFER_REFERENCE_FIXTURE ?? '/tmp/dxfer-corner-reference.dxf', 'reference-corner.dxf');
    await page.getByRole('button', {name: 'Fillet', exact: true}).click();
    await inspector.waitFor();
    await input.fill('2');
    await clickCorner();
    await state('Modified');
    await page.getByRole('button', {name: 'Exit tool', exact: true}).click();
    await state('Modified');
    const changedFile = await download(), changed = native(changedFile);
    assert.equal(changed.entities.length, 3);
    assert.equal(changed.dimensions.find(d => d.id === 'length').value, 8);
    await state('Unchanged');
    await page.getByRole('button', {name: 'Undo', exact: true}).click();
    await state('Modified');
    await page.getByRole('button', {name: 'Redo', exact: true}).click();
    await state('Unchanged');
    await menu('New drawing');
    await page.locator('input[type=file]').setInputFiles({name: 'roundtrip.dxf', mimeType: 'application/dxf', buffer: Buffer.from(changedFile)});
    await page.waitForFunction(() => document.querySelector('canvas').dataset.entityCount === '3');
    assert.deepEqual(native(await download()).entities, changed.entities);
    await page.getByRole('button', {name: 'Chamfer', exact: true}).click();
    await input.fill('-1');
    await input.press('Escape');
    await page.waitForFunction(() => !document.querySelector('.dxfer-operation-inspector'));
    assert.deepEqual(native(await download()).entities, changed.entities);
    assert.equal(await page.getByRole('button', {name: 'Undo', exact: true}).isDisabled(), true);
    for (const [unit, label] of [[1, 'in'], [4, 'mm'], [0, 'Unspecified']]) {
        const dxf = ['0','SECTION','2','HEADER','9','$INSUNITS','70',unit,'0','ENDSEC',
            '0','SECTION','2','ENTITIES','0','LINE','8','0','10','0','20','0','11','10','21','0','0','ENDSEC','0','EOF',''].join('\n');
        await page.locator('input[type=file]').setInputFiles({name: `units-${unit}.dxf`, mimeType: 'application/dxf', buffer: Buffer.from(dxf)});
        await page.waitForFunction(name => document.querySelector('.document-filename')?.textContent === name, `units-${unit}.dxf`);
        assert.equal(await page.locator('.document-units').textContent(), label);
        await state('Unchanged');
    }
    await page.locator('input[type=file]').setInputFiles(process.env.DXFER_READONLY_FIXTURE ?? '/tmp/dxfer-header-reference.dxf');
    await page.waitForFunction(() => document.querySelector('.document-access-state')?.textContent === 'Read-only');
    assert.equal(await page.locator('.document-units').textContent(), 'mm');
    await state('Unchanged');
    await page.screenshot({path: `${output}/reference-header.png`});
    await menu('New drawing');
    await page.waitForFunction(() => document.querySelector('.document-filename')?.textContent === 'Untitled.dxf');
    assert.equal(await page.locator('.document-access-state').textContent(), '');
    await state('Unchanged');
    assert.deepEqual(errors, []);
    console.log('PASS: truthful header, inspector state/invalid input/constraint refusal, desktop bounds, Exit/Escape/history/download/reopen. Build:', await page.locator('.app-brand').getAttribute('title'));
} finally {
    await browser.close();
}
