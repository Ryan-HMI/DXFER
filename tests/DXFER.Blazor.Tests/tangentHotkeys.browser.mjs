import assert from 'node:assert/strict';
import {mkdir, writeFile} from 'node:fs/promises';
const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL;
assert.ok(url && ['127.0.0.1', 'localhost', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-tangent-hotkeys';
await mkdir(output, {recursive: true});
const result = {passed: false, errors: []};
const browser = await chromium.launch({headless: true, executablePath: process.env.DXFER_CHROMIUM});
let page;
try {
  page = await browser.newPage({viewport: {width: 1188, height: 761},
    extraHTTPHeaders: {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY}});
  page.on('pageerror', error => result.errors.push(error.message));
  const ready = () => page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
  const button = name => page.getByRole('button', {name, exact: true});
  const canvas = page.locator('canvas.drawing-canvas');
  const panel = page.getByRole('dialog', {name: 'Tool hotkey settings'});
  const search = page.getByRole('searchbox', {name: 'Search hotkeys'});
  const constraint = page.getByRole('textbox', {name: 'Tangent constraint hotkey', exact: true});
  const arc = page.getByRole('textbox', {name: 'Tangent arc hotkey', exact: true});
  const open = async () => {
    await button('Open application menu').click(); await button('Hotkeys').click();
    await expect(search).toBeFocused(); await search.fill('tangent');
    await expect(panel.locator('.dxfer-hotkey-row')).toHaveCount(2);
  };
  await page.goto(url); await ready();
  result.build = await page.locator('.app-brand').getAttribute('title');
  if (process.env.DXFER_EXPECTED_REVISION) assert.ok(result.build.includes(process.env.DXFER_EXPECTED_REVISION));
  await open(); await expect(constraint).toHaveValue(''); await expect(arc).toHaveValue('');
  await constraint.press('Alt+t'); await expect(constraint).toHaveValue('Alt+T');
  await arc.press('Alt+t'); await expect(page.getByRole('alert')).toContainText('already assigned');
  await expect(arc).toHaveValue('');
  await arc.press('Alt+a'); await expect(arc).toHaveValue('Alt+A');
  await page.screenshot({path: `${output}/tangent-bindings.png`});
  await search.press('Escape'); await expect(panel).not.toBeVisible();
  for (const [key, tool] of [['Alt+t', 'tangent'], ['Alt+a', 'tangentarc'], ['Shift+a', 'line']]) {
    await canvas.focus(); await page.keyboard.press(key);
    await expect(canvas).toHaveAttribute('data-active-tool', tool);
    await page.keyboard.press('Escape'); await expect(canvas).toHaveAttribute('data-active-tool', 'select');
  }
  await page.reload(); await ready(); await open();
  await expect(constraint).toHaveValue('Alt+T'); await expect(arc).toHaveValue('Alt+A');
  await search.press('Escape');
  await canvas.focus(); await page.keyboard.press('Alt+a');
  await expect(canvas).toHaveAttribute('data-active-tool', 'tangentarc');
  await expect(button('Line')).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('Escape');
  await canvas.focus(); await page.keyboard.press('Alt+t');
  await expect(canvas).toHaveAttribute('data-active-tool', 'tangent');
  await page.keyboard.press('Escape');
  await expect(canvas).toHaveAttribute('data-entity-count', '0'); await expect(button('Undo')).toBeDisabled();
  assert.deepEqual(result.errors, []); result.passed = true;
} catch (error) {
  result.failure = error.stack; await page?.screenshot({path: `${output}/failure.png`}).catch(() => {});
  process.exitCode = 1;
} finally {
  await writeFile(`${output}/results.json`, JSON.stringify(result, null, 2)); await browser.close();
  console.log(JSON.stringify(result));
}
