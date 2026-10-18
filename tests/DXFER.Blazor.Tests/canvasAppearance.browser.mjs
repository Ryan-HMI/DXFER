import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright/test');
const url=process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5049';
assert.ok(['127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const output=process.env.DXFER_SCREENSHOTS ?? '/tmp/appearances';await mkdir(output,{recursive:true});
const browser=await chromium.launch({headless:true});
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
  const canvas=page.locator('canvas.drawing-canvas'),dialog=page.getByRole('dialog',{name:'Appearances',exact:true});
  const picker=page.getByLabel('Drawing background color',{exact:true}),preset=page.getByLabel('Drawing background preset',{exact:true}),panels=page.getByLabel('Panel color',{exact:true});
  const tool=name=>page.getByRole('button',{name,exact:true}).click();
  const ready=()=>page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  const menu=async name=>{await tool('Open application menu');await page.getByRole('button',{name:'File',exact:true}).hover();await tool(name);};
  const open=async()=>{await menu('Appearances');await expect(picker).toBeEnabled();};
  const close=async()=>{await tool('Close appearances');await expect(dialog).toHaveCount(0);};
  const native=text=>{const a=text.trim().split(/\r?\n/);let h='';for(let i=0;i<a.length;i+=2)if(a[i].trim()==='310')h+=a[i+1].trim();return JSON.parse(Buffer.from(h,'hex'));};
  const download=async name=>{const pending=page.waitForEvent('download');await menu('Download DXF');const text=await readFile(await(await pending).path(),'utf8');await writeFile(`${output}/${name}.dxf`,text);return native(text);};
  const pixel=async(center=false)=>{
    const screenshot=await canvas.screenshot();
    return page.evaluate(async({base64,center})=>{const img=new Image();img.src='data:image/png;base64,'+base64;await img.decode();const c=document.createElement('canvas');c.width=img.width;c.height=img.height;const ctx=c.getContext('2d');ctx.drawImage(img,0,0);return [...ctx.getImageData(center?Math.round(c.width*.5):c.width-3,center?Math.round(c.height*.4):c.height-3,1,1).data];},{base64:screenshot.toString('base64'),center});
  };
  const fade=async()=>{
    assert.match(await canvas.evaluate(c=>getComputedStyle(c).backgroundImage),/radial-gradient.*rgb\(23, 40, 50\).*rgb\(11, 16, 22\)/);
    await expect.poll(()=>pixel()).toEqual([11,16,22,255]);
    const center=await pixel(true);assert.ok(center[2]>37 && center[1]>26,JSON.stringify(center));
  };
  await ready();await open();await expect(preset).toHaveValue('foundry');await close();await fade();
  await page.screenshot({path:`${output}/foundry-1440.png`});
  const fixture=await readFile(new URL('./fixtures/corner-driving10.dxf',import.meta.url));
  await page.locator('input[type=file]').setInputFiles({name:'appearance-qa.dxf',mimeType:'application/dxf',buffer:fixture});
  await expect(page.locator('input[data-dimension-id]')).toHaveCount(1);
  await page.getByLabel('Layer color 0',{exact:true}).fill('#e15e4c');await page.waitForTimeout(200);
  const before=await download('before'),undoEnabled=await page.getByRole('button',{name:'Undo',exact:true}).isEnabled();
  for(const mode of ['neutral','custom','foundry']) {
    await open();await preset.selectOption(mode);
    if(mode==='custom') {await picker.fill('#ffffff');await panels.fill('#eeeeee');}
    await page.screenshot({path:`${output}/${mode}-dialog.png`});await close();
    if(mode==='foundry') await fade();else assert.deepEqual(await pixel(),mode==='custom'?[255,255,255,255]:[32,32,32,255]);
    const surface=await page.locator('.dxfer-information-panel').evaluate(e=>({color:getComputedStyle(e).color,background:getComputedStyle(e).backgroundColor}));
    assert.equal(surface.background,mode==='custom'?'rgb(238, 238, 238)':mode==='neutral'?'rgb(24, 24, 24)':'rgb(16, 20, 27)');
    if(mode==='custom') {
      assert.equal(surface.color,'rgb(24, 24, 24)');
      for (const selector of ['.app-brand','.document-filename','.document-units','.dxfer-command-prompt','.dxfer-live-readout-value','.dxfer-layer-details input:not([type=radio])']) {
        const element=page.locator(selector).first();
        if(await element.count()) {
          const ink=await element.evaluate(e=>getComputedStyle(e).color);
          assert.ok(['rgb(24, 24, 24)','rgb(48, 48, 48)'].includes(ink),`${selector}: ${ink}`);
        }
      }
      assert.equal(await page.locator('.dxfer-layer-details input:not([type=radio])').first().evaluate(e=>getComputedStyle(e).backgroundColor),'rgb(224, 224, 224)');
    }
    assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isEnabled(),undoEnabled);
    await expect(page.locator('.document-change-state')).toHaveText('Unchanged');
    assert.equal(await page.getByLabel('Layer color 0',{exact:true}).inputValue(),'#e15e4c');
    assert.deepEqual(await download(mode),before);
    const ink=await page.locator('input[data-dimension-id]').evaluate(e=>getComputedStyle(e).color);
    assert.equal(ink,mode==='custom'?'rgb(32, 32, 32)':'rgb(243, 246, 251)');
    await page.screenshot({path:`${output}/${mode}-canvas.png`});
  }
  await open();await preset.selectOption('neutral');await panels.fill('#383838');await close();
  await page.reload();await ready();await open();await expect(preset).toHaveValue('neutral');await expect(panels).toHaveValue('#383838');
  await tool('Reset');await expect(preset).toHaveValue('foundry');await expect(panels).toHaveValue('#10141b');await close();await fade();
  await page.reload();await ready();await open();await expect(preset).toHaveValue('foundry');await close();await fade();
  await page.setViewportSize({width:1100,height:705});await open();
  const box=await dialog.boundingBox(),field=await canvas.boundingBox();
  assert.ok(box.x>=field.x && box.x+box.width<=field.x+field.width && box.y>=field.y && box.y+box.height<=field.y+field.height);
  assert.ok(Math.abs(box.x+box.width/2-(field.x+field.width/2))<2);
  await page.screenshot({path:`${output}/appearances-1100.png`});
  await page.getByRole('button',{name:'Reset',exact:true}).focus();await page.keyboard.press('Tab');await expect(page.getByRole('button',{name:'Close appearances',exact:true})).toBeFocused();
  await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(canvas).toBeFocused();
  for(const old of ['#262626','#123456','url(invalid)']) {
    await page.evaluate(old=>{localStorage.removeItem('dxfer.canvasAppearance.v2');localStorage.setItem('dxfer.canvasBackground.v1',old);},old);
    await page.reload();await ready();await open();
    if(old==='#123456') {await expect(preset).toHaveValue('custom');await expect(picker).toHaveValue(old);await close();}
    else {await expect(preset).toHaveValue('foundry');await close();await fade();}
  }
  assert.deepEqual(errors,[]);
  const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION));
  console.log('PASS File/Appearances, original gradient pixels, darker grey/custom canvas/panels, contrast, migration/persistence/reset, unchanged native export/history/layers, centered desktop dialog, Tab/Escape.',build);
}finally{await browser.close();}
