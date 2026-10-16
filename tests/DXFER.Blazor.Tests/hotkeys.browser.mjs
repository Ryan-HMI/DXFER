import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
await mkdir('/tmp/dxfer-hotkeys-evidence',{recursive:true});
const browser=await chromium.launch({headless:true});
try {
    const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(url);await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    const build=await page.locator('.app-brand').getAttribute('title');console.log(build);
    const canvas=page.locator('canvas.drawing-canvas');
    const tool=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.waitForTimeout(200);};
    for(const [key,active] of [['c','centercircle'],['r','twopointrectangle'],['m','midpointline'],['Shift+A','line'],['q','measure']]){
        await canvas.focus();await page.keyboard.press(key);await expect(canvas).toHaveAttribute('data-active-tool',active);
        await page.keyboard.press('Escape');await expect(canvas).toHaveAttribute('data-active-tool','select');console.log(`${key} activates ${active}; Escape returns to select`);
    }
    await tool('Tool hotkeys');const panel=page.getByRole('region',{name:'Tool hotkey settings'});await expect(panel).toBeVisible();
    const line=page.getByRole('textbox',{name:'Line hotkey',exact:true});await expect(line).toHaveValue('Shift+A');
    await line.focus();await line.press('l');await expect(line).toHaveValue('L');
    await tool('Close tool hotkey settings');await canvas.focus();await page.keyboard.press('l');await expect(canvas).toHaveAttribute('data-active-tool','line');
    await page.keyboard.press('Escape');await page.reload();await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
    await canvas.focus();await page.keyboard.press('l');await expect(canvas).toHaveAttribute('data-active-tool','line');
    await page.keyboard.press('Escape');await tool('Tool hotkeys');await expect(line).toHaveValue('L');
    await page.screenshot({path:'/tmp/dxfer-hotkeys-evidence/settings.png'});await tool('Close tool hotkey settings');
    console.log('Custom L assignment and reload persistence pass (disposable browser storage only).');
    await canvas.focus();await page.keyboard.press('c');await expect(canvas).toHaveAttribute('data-active-tool','centercircle');
    const r=await canvas.boundingBox();await page.mouse.click(r.x+r.width*.5,r.y+r.height*.45);await page.mouse.move(r.x+r.width*.6,r.y+r.height*.5);
    const radius=page.locator('input[data-dimension-key="radius"]');await expect(radius).toBeVisible();await radius.fill('3');
    await radius.press('Escape');await expect(canvas).toHaveAttribute('data-active-tool','select');
    await page.keyboard.press('r');await expect(canvas).toHaveAttribute('data-active-tool','twopointrectangle');
    console.log('Escape from radius restores tool hotkeys.');
    await page.keyboard.press('Escape');await expect(canvas).toHaveAttribute('data-active-tool','select');
    await page.keyboard.press('c');await expect(canvas).toHaveAttribute('data-active-tool','centercircle');
    await page.mouse.click(r.x+r.width*.5,r.y+r.height*.45);await page.waitForTimeout(150);
    await page.mouse.move(r.x+r.width*.6,r.y+r.height*.5,{steps:8});
    await expect(radius).toBeVisible();await radius.fill('3');await radius.press('Enter');
    await expect(canvas).toHaveAttribute('data-entity-count','1');await page.keyboard.press('Escape');
    await page.keyboard.press('Control+z');await expect(canvas).toHaveAttribute('data-entity-count','0');
    await page.keyboard.press('Control+Shift+z');await expect(canvas).toHaveAttribute('data-entity-count','1');
    console.log('Ctrl+Z / Ctrl+Shift+Z undo and redo the created circle.');
    assert.deepEqual(errors,[]);
} finally {await browser.close();}
