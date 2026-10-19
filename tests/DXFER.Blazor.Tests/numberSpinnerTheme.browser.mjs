import assert from 'node:assert/strict';
import {mkdir, writeFile} from 'node:fs/promises';

const {chromium, expect} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['localhost', '127.0.0.1', '::1', '[::1]', 'dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const baseline = process.env.DXFER_SPINNER_MODE !== 'verify';
const output = process.env.DXFER_SCREENSHOTS ?? '/tmp/dxfer-number-spinner-theme';
await mkdir(output, {recursive: true});
const fixture = ['0', 'SECTION', '2', 'HEADER', '9', '$INSUNITS', '70', '1', '0', 'ENDSEC',
  '0', 'SECTION', '2', 'ENTITIES', '0', 'CIRCLE', '5', '100', '8', '0',
  '10', '10', '20', '10', '40', '1', '0', 'ENDSEC', '0', 'EOF', ''].join('\n');
const themes = [
  {name: 'foundry', preset: 'foundry', color: '#172832', panelColor: '#10141b', scheme: 'dark'},
  {name: 'grey', preset: 'neutral', color: '#202020', panelColor: '#181818', scheme: 'dark'},
  // Dark canvas and light panels deliberately distinguish panel from canvas appearance.
  {name: 'custom-light', preset: 'custom', color: '#202020', panelColor: '#eeeeee', scheme: 'light'}
];
const browser = await chromium.launch({headless: true});
const results = [], errors = [];
try {
  for (const theme of baseline ? themes.slice(0, 1) : themes) {
    const context = await browser.newContext({viewport: {width: 1440, height: 1000},
      extraHTTPHeaders: process.env.DXFER_QA_API_KEY ? {'X-DXFER-API-Key': process.env.DXFER_QA_API_KEY} : {}});
    // Theme preferences are isolated to this disposable browser context, never server settings.
    await context.addInitScript(value => localStorage.setItem('dxfer.canvasAppearance.v2', JSON.stringify(value)), theme);
    const page = await context.newPage();
    page.setDefaultTimeout(8000);
    page.on('pageerror', error => errors.push(error.message));
    let evidence;
    try {
      await page.goto(url);
      await page.waitForFunction(() => document.querySelector('input[type=file]')?._blazorInputFileNextFileId !== undefined);
      const build = await page.locator('.app-brand').getAttribute('title');
      if (process.env.DXFER_EXPECTED_REVISION) assert.ok(build?.includes(process.env.DXFER_EXPECTED_REVISION), build);
      const canvas = page.locator('canvas.drawing-canvas');
      await page.locator('input[type=file]').setInputFiles({name: 'spinner-circle.dxf', mimeType: 'application/dxf', buffer: Buffer.from(fixture)});
      await expect(canvas).toHaveAttribute('data-entity-count', '1');
      await page.getByRole('button', {name: 'Fit extents', exact: true}).click();
      await page.waitForTimeout(300);
      const p = await canvas.evaluate(c => ({x: c.getBoundingClientRect().left + 11 * +c.dataset.scale + +c.dataset.offsetX,
        y: c.getBoundingClientRect().top - 10 * +c.dataset.scale + +c.dataset.offsetY}));
      await page.mouse.click(p.x, p.y);
      await expect(canvas).toHaveAttribute('data-selected-count', '1');
      await page.mouse.click(p.x, p.y, {button: 'right'});
      await page.getByRole('dialog', {name: 'Drawing context commands'}).getByRole('button', {name: 'Pattern', exact: true}).click();
      const editor = page.getByRole('region', {name: 'Pattern settings', exact: true});
      await expect(editor).toBeVisible();
      const field = name => editor.getByLabel(name, {exact: true});
      await field('Pattern layout').selectOption('Radial');
      const count = field('Pattern instances');
      await count.fill('2');
      await field('Pattern center X').fill('5.125');
      await field('Pattern center Y').fill('-0.375');
      const arrow = async up => {
        const box = await count.boundingBox();
        // Native Chromium spinner is inside the input's right padding, not a DOM button.
        await page.mouse.move(box.x + box.width - 13, box.y + box.height * (up ? .35 : .65));
        await page.mouse.click(box.x + box.width - 13, box.y + box.height * (up ? .35 : .65));
      };
      await count.hover();
      evidence = {theme, build, baseline, input: await count.evaluate(e => {
        const style = getComputedStyle(e), box = e.getBoundingClientRect();
        return {scheme: style.colorScheme, background: style.backgroundColor, color: style.color,
          appearance: style.appearance, min: e.min, step: e.step, box: box.toJSON(),
          workbenchScheme: getComputedStyle(e.closest('.dxfer-workbench')).colorScheme};
      })};
      await page.screenshot({path: `${output}/${theme.name}-hover.png`});
      await count.screenshot({path: `${output}/${theme.name}-spinner.png`});
      await arrow(true);
      await expect(count).toHaveValue('3');
      await arrow(false);
      await expect(count).toHaveValue('2');
      await arrow(false);
      await expect(count).toHaveValue('2');
      await count.press('ArrowUp');
      await expect(count).toHaveValue('3');
      await count.press('ArrowDown');
      await expect(count).toHaveValue('2');
      await count.press('ArrowDown');
      await expect(count).toHaveValue('2');
      await expect(field('Pattern center X')).toHaveValue('5.125');
      await expect(field('Pattern center Y')).toHaveValue('-0.375');
      assert.deepEqual(await count.boundingBox(), {x: evidence.input.box.x, y: evidence.input.box.y,
        width: evidence.input.box.width, height: evidence.input.box.height}, 'Spinner interaction must not resize/reflow the input');
      await field('Pattern layout').selectOption('Rectangular');
      await field('Pattern X pitch').fill('1.25');
      await field('Pattern Y pitch').fill('-0.375');
      await field('Pattern columns').press('ArrowUp');
      await field('Pattern columns').press('ArrowDown');
      await expect(field('Pattern X pitch')).toHaveValue('1.25');
      await expect(field('Pattern Y pitch')).toHaveValue('-0.375');
      await field('Pattern layout').selectOption('Radial');
      await expect(field('Pattern center X')).toHaveValue('5.125');
      await expect(field('Pattern center Y')).toHaveValue('-0.375');
      await count.hover();
      await page.screenshot({path: `${output}/${theme.name}-after-interactions.png`});
      await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
      await expect(canvas).toHaveAttribute('data-entity-count', '1');
      await expect(page.getByRole('button', {name: 'Undo', exact: true})).toBeDisabled();
      evidence.interactionsPassed = true;
      evidence.themeMatches = evidence.input.scheme === theme.scheme;
      if (!baseline) {
        assert.equal(evidence.input.scheme, theme.scheme, 'Native spinner follows panel brightness');
        assert.equal(evidence.input.workbenchScheme, theme.scheme, 'Shared workbench scheme follows panel brightness');
      }
      results.push({...evidence, passed: true});
      console.log(`${baseline ? 'BASELINE' : 'PASS'} ${theme.name}: ${JSON.stringify(evidence)}`);
    } catch (error) {
      results.push({...evidence, theme, passed: false, error: error.stack});
      await page.screenshot({path: `${output}/${theme.name}-failure.png`});
      console.error(`FAIL ${theme.name}: ${error.message}`);
    } finally {
      await context.close();
      await writeFile(`${output}/results.json`, JSON.stringify({url, baseline, results, errors}, null, 2));
    }
  }
  assert.deepEqual(errors, []);
  assert.ok(results.every(result => result.passed), `See ${output}/results.json`);
} finally {
  await browser.close();
}
