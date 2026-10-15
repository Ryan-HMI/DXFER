// Run explicitly against an isolated local full-editing app, never a deployed environment.
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_LOCAL_URL ?? 'http://127.0.0.1:5187';
assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-palette-evidence';
await mkdir(output, {recursive: true});
const browser = await chromium.launch({headless: true, executablePath: process.env.CHROMIUM_PATH});
try {
    const context = await browser.newContext({viewport: {width: 1440, height: 900},
        extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_LOCAL_API_KEY ?? 'palette-local-check'}});
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const key = 'dxfer.paletteLayout.v1';
    const names = ['View', 'Sketch', 'Modify', 'Transform', 'Pattern', 'Constraints', 'Cleanup', 'Grain'];
    const group = name => page.locator(`[data-palette-index="${names.indexOf(name)}"]`);
    const read = () => page.evaluate(key => JSON.parse(localStorage.getItem(key)), key);
    const waitState = (name, fields) => page.waitForFunction(({key, name, fields}) => {
        const state = JSON.parse(localStorage.getItem(key))?.groups.find(s => s.groupName === name);
        return state && Object.entries(fields).every(([field, value]) => state[field] === value);
    }, {key, name, fields});
    const ready = async () => {
        await page.waitForSelector('[data-palette-index="7"]');
        // Wait for the interactive render, not just the prerendered markup.
        await page.waitForFunction(() => window.Blazor && document.querySelector('canvas'));
        await page.waitForTimeout(350);
    };
    const drag = async (name, x, y) => {
        const header = group(name).locator('.dxfer-tool-group-drag-header');
        await header.scrollIntoViewIfNeeded();
        const box = await header.boundingBox();
        await page.mouse.move(box.x + 16, box.y + 16);
        await page.mouse.down();
        await page.waitForTimeout(80);
        await page.mouse.move(x, y, {steps: 8});
        await page.waitForTimeout(150);
        await page.mouse.up();
    };
    const snapshot = () => page.locator('.dxfer-tool-group').evaluateAll(groups => groups.map(group => ({
        id: group.dataset.paletteIndex, classes: group.className, style: group.getAttribute('style'),
        x: group.getBoundingClientRect().x, y: group.getBoundingClientRect().y
    })).sort((a, b) => a.id.localeCompare(b.id)));
    await page.goto(url);
    await ready();
    assert.equal(await page.locator('.dxfer-tool-group').count(), 8);
    const labelToggle = name => page.getByRole('button', {name: `Show ${name} tool labels`, exact: true});
    const inventory = () => page.locator('.dxfer-command-strip button').evaluateAll(buttons =>
        buttons.map(button => button.getAttribute('aria-label')).sort());
    const commands = await inventory();
    assert.equal(await labelToggle('Sketch').getAttribute('aria-pressed'), 'true');
    await labelToggle('View').focus();
    await page.keyboard.press('Enter');
    await waitState('View', {showLabels: false});
    assert.equal(await labelToggle('Sketch').getAttribute('aria-pressed'), 'true');
    assert.equal(await group('View').locator('.dxfer-icon-button-label').count(), 0);
    assert.ok(await group('Sketch').locator('.dxfer-icon-button-label').count() > 0);
    await page.screenshot({path: `${output}/default-1440.png`});
    await drag('Sketch', 450, 250);
    await waitState('Sketch', {side: 0});
    const sketch = (await read()).groups.find(s => s.groupName === 'Sketch');
    await drag('View', 850, 400);
    await waitState('View', {side: 0});
    assert.deepEqual((await read()).groups.find(s => s.groupName === 'Sketch'), sketch);
    await page.getByRole('button', {name: 'Compact Modify tools', exact: true}).click();
    await waitState('Modify', {compaction: 1});
    await drag('Cleanup', 60, 140);
    await waitState('Cleanup', {side: 1, dockOrder: 140});
    const saved = await read();
    const mixed = await snapshot();
    await page.screenshot({path: `${output}/mixed-1440.png`});
    await page.reload();
    await ready();
    await page.waitForSelector('.dxfer-tool-group-floating');
    assert.deepEqual(await snapshot(), mixed);
    assert.deepEqual(await read(), saved);
    assert.deepEqual(await inventory(), commands);
    assert.equal(await labelToggle('View').getAttribute('aria-pressed'), 'false');
    assert.equal(await labelToggle('Sketch').getAttribute('aria-pressed'), 'true');
    await page.screenshot({path: `${output}/reopened-1440.png`});
    const lastTool = group('Sketch').getByRole('button', {name: 'Slot', exact: true});
    await lastTool.scrollIntoViewIfNeeded();
    const lastBox = await lastTool.boundingBox();
    const footer = await page.locator('.dxfer-command-bar').boundingBox();
    assert.ok(lastBox.y + lastBox.height <= footer.y + 1, 'floating palette body must scroll above the footer');
    for (const [width, height] of [[1280, 720], [1000, 700]]) {
        await page.setViewportSize({width, height});
        await page.waitForTimeout(150);
        for (const name of ['Sketch', 'View']) {
            const box = await group(name).locator('.dxfer-tool-group-drag-header').boundingBox();
            assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= width + 1
                && box.y + box.height <= height + 1, `${name} header must remain in ${width}x${height}`);
        }
        assert.deepEqual(await read(), saved, 'resize must not replace preferred coordinates');
        await page.screenshot({path: `${output}/clamped-${width}.png`});
        await page.reload();
        await ready();
        for (const name of ['Sketch', 'View']) {
            const box = await group(name).locator('.dxfer-tool-group-drag-header').boundingBox();
            assert.ok(box.x >= 0 && box.x + box.width <= width + 1 && box.y + box.height <= height + 1);
        }
    }
    await page.setViewportSize({width: 1440, height: 900});
    await page.waitForTimeout(150);
    assert.deepEqual(await snapshot(), mixed);

    for (const zoom of [1.25, 1.5, 2]) {
        // Desktop display scaling reduces available CSS pixels; do not scale a 100vh page with CSS zoom.
        const width = Math.floor(1920 / zoom), height = Math.floor(1080 / zoom);
        await page.setViewportSize({width, height});
        await page.waitForTimeout(200);
        for (const name of ['Sketch', 'View']) {
            const box = await group(name).locator('.dxfer-tool-group-drag-header').boundingBox();
            assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= width + 1 && box.y + box.height <= height + 1);
        }
        assert.deepEqual(await read(), saved);
        await page.screenshot({path: `${output}/scaled-${zoom}.png`});
    }
    await page.evaluate(() => document.body.style.zoom = 1);
    await page.setViewportSize({width: 1440, height: 900});

    for (const [name, x, y, side] of [['Transform', 1270, 200, 2], ['Pattern', 600, 70, 3], ['Constraints', 650, 760, 4]]) {
        await drag(name, x, y);
        await waitState(name, {side});
    }
    const allDocks = await snapshot();
    await page.reload();
    await ready();
    assert.deepEqual(await snapshot(), allDocks);
    for (const side of ['left', 'right', 'top', 'bottom', 'floating'])
        assert.ok(await page.locator(`.dxfer-tool-group-${side}`).count() > 0);
    await page.screenshot({path: `${output}/all-docks-reopened-1440.png`});

    const legacy = {...saved, groups: saved.groups.map(({showLabels, ...state}) => state)};
    await page.evaluate(({key, legacy}) => localStorage.setItem(key, JSON.stringify(legacy)), {key, legacy});
    await page.reload();
    await ready();
    assert.equal(await labelToggle('Sketch').getAttribute('aria-pressed'), 'false');
    assert.match(await group('Sketch').getAttribute('class'), /group-floating/);

    for (const corrupt of ['{', JSON.stringify({version: 99, groups: saved.groups})]) {
        await page.evaluate(({key, corrupt}) => localStorage.setItem(key, corrupt), {key, corrupt});
        await page.reload();
        await ready();
        assert.equal(await page.locator('.dxfer-tool-group-left').count(), 8);
        assert.equal(await page.evaluate(key => localStorage.getItem(key), key), corrupt);
    }
    const partial = {version: 1, groups: saved.groups.filter(s => s.groupName !== 'Grain')};
    partial.groups.find(s => s.groupName === 'Sketch').side = 999;
    partial.groups.push({...sketch, groupName: 'Unknown future group'});
    await page.evaluate(({key, partial}) => localStorage.setItem(key, JSON.stringify(partial)), {key, partial});
    await page.reload();
    await ready();
    assert.equal(await page.locator('.dxfer-tool-group').count(), 8);
    for (const name of ['Sketch', 'Grain']) assert.match(await group(name).getAttribute('class'), /group-left/);
    assert.match(await group('View').getAttribute('class'), /group-floating/);

    await context.addInitScript(key => {
        const get = Storage.prototype.getItem;
        const set = Storage.prototype.setItem;
        Storage.prototype.getItem = function(name) { if (name === key) throw Error('denied'); return get.call(this, name); };
        Storage.prototype.setItem = function(name, value) { if (name === key) throw Error('quota'); return set.call(this, name, value); };
    }, key);
    await page.reload();
    await ready();
    await page.getByRole('button', {name: 'Compact Modify tools', exact: true}).click();
    await page.getByRole('button', {name: 'Expand Modify tools', exact: true}).waitFor();
    assert.equal(await page.locator('.dxfer-tool-group-left').count(), 8);
    assert.deepEqual(errors, []);
    console.log(`PASS: independent drag/compaction/order/reload, viewport recovery, corrupt/unknown/new groups, denied storage. Screenshots: ${output}`);
} finally {
    await browser.close();
}
