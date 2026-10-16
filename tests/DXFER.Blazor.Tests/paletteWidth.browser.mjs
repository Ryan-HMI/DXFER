import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
const {chromium} = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const url = process.env.DXFER_QA_URL ?? 'http://127.0.0.1:5037';
assert.ok(['127.0.0.1','localhost','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
const out='/tmp/dxfer-palette-width-evidence';
await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true});
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000},extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  const ready=async()=>{await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);await page.waitForTimeout(500);};
  const group=n=>page.locator(`[data-palette-index="${n}"]`);
  const width=async n=>{await page.waitForTimeout(300);return (await group(n).boundingBox()).width;};
  const close=(actual,expected)=>assert.ok(Math.abs(actual-expected)<2,`${actual} != ${expected}`);
  const drag=async(n,delta)=>{
    const handle=group(n).getByRole('separator'); await handle.scrollIntoViewIfNeeded();
    const b=await handle.boundingBox();
    await page.mouse.move(b.x+b.width/2,b.y+12); await page.mouse.down(); await page.waitForTimeout(150);
    await page.mouse.move(b.x+b.width/2+delta,b.y+12,{steps:15}); await page.waitForTimeout(100);
    await page.mouse.up(); await page.waitForTimeout(300);
  };
  await page.goto(url); await ready();
  const build=await page.locator('.app-brand').getAttribute('title');
  if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
  const compact=await width(1);
  await page.getByRole('button',{name:'Show Sketch tool labels',exact:true}).click();
  close(await width(1),232);
  await drag(1,112); close(await width(1),344); close(await width(0),compact);
  await page.getByRole('button',{name:'Show Cleanup tool labels',exact:true}).click();
  await drag(6,-64); close(await width(6),296); close(await width(1),344);
  await drag(6,300);
  const minimum=Number(await group(6).getByRole('separator').getAttribute('aria-valuemin'));
  close(await width(6),minimum);
  const wrapped=await group(6).locator('.dxfer-icon-button-label').evaluateAll(es=>es.some(e=>e.getBoundingClientRect().height>parseFloat(getComputedStyle(e).lineHeight)+1));
  assert.equal(wrapped,false,'longest label must fit on one line at the resize minimum');
  const clipped=await group(6).locator('.dxfer-icon-button-label').evaluateAll(es=>es.some(e=>e.scrollWidth>e.clientWidth+1));
  assert.equal(clipped,false);
  await page.screenshot({path:`${out}/narrow-label-wrap.png`});
  await drag(6,-(296-minimum)); close(await width(6),296);
  await page.reload(); await ready(); close(await width(1),344); close(await width(6),296);
  for(const side of ['left','right']) {
    const metrics=await page.locator(`.dxfer-tool-dock-zone-${side}`).evaluate(e=>({client:e.clientWidth,scroll:e.scrollWidth}));
    assert.ok(metrics.scroll<=metrics.client+1,JSON.stringify(metrics));
  }
  await page.getByRole('button',{name:'Show Sketch tool labels',exact:true}).click(); close(await width(1),compact);
  assert.equal(await group(1).getByRole('separator').count(),0);
  await page.getByRole('button',{name:'Show Sketch tool labels',exact:true}).click(); close(await width(1),344);
  await page.getByRole('button',{name:'Compact Sketch tools',exact:true}).click(); close(await width(1),compact);
  await page.getByRole('button',{name:'Expand Sketch tools',exact:true}).click(); close(await width(1),344);
  const handle=group(1).getByRole('separator'); await handle.focus(); await page.keyboard.press('ArrowRight');
  close(await width(1),352); await page.keyboard.press('Home'); close(await width(1),232);
  await page.keyboard.press('Tab'); assert.equal(await handle.evaluate(e=>e===document.activeElement),false);
  // Docking is a real pointer gesture, independent of resizing.
  const grip=await group(1).locator('.dxfer-tool-group-grip').boundingBox();
  await page.mouse.move(grip.x+3,grip.y+8); await page.mouse.down(); await page.waitForTimeout(150);
  await page.mouse.move(650,240,{steps:20}); await page.mouse.up(); await page.waitForTimeout(300);
  assert.match(await group(1).getAttribute('class'),/group-floating/);
  await drag(1,128); close(await width(1),360);
  await page.reload(); await ready(); close(await width(1),360);
  assert.match(await group(1).getAttribute('class'),/group-floating/);
  await group(1).getByRole('separator').focus(); await page.keyboard.press('End'); close(await width(1),480);
  await page.setViewportSize({width:1100,height:705}); await page.waitForTimeout(500);
  assert.ok(await width(1)<480); const smaller=await group(1).boundingBox(); assert.ok(smaller.x+smaller.width<=1100);
  await page.screenshot({path:`${out}/labeled-narrow-desktop.png`});
  await page.setViewportSize({width:1440,height:1000}); await page.waitForTimeout(300); close(await width(1),480);
  await page.screenshot({path:`${out}/independent-widths.png`});
  const minimums=[];
  for(const [index,name] of ['View','Sketch','Modify','Transform','Pattern','Constraints','Cleanup','Grain'].entries()) {
    const toggle=page.getByRole('button',{name:`Show ${name} tool labels`,exact:true});
    if(await toggle.getAttribute('aria-pressed')!=='true')await toggle.click();
    const handle=group(index).getByRole('separator');await handle.focus();await page.waitForTimeout(200);
    const right=(await group(index).getAttribute('class')).includes('group-right');
    for(let i=0;i<16;i++)await page.keyboard.press(right?'Shift+ArrowRight':'Shift+ArrowLeft');
    const minimum=Number(await handle.getAttribute('aria-valuemin'));minimums.push(minimum);close(await width(index),minimum);
    const wrapped=await group(index).locator('.dxfer-icon-button-label').evaluateAll(es=>es.some(e=>e.scrollWidth>e.clientWidth+1||e.getBoundingClientRect().height>parseFloat(getComputedStyle(e).lineHeight)+1));
    assert.equal(wrapped,false,`${name} must keep longest label on one line at minimum`);
  }
  assert.ok(new Set(minimums).size>1,'minimums must depend on actual palette contents');
  await page.reload();await ready();for(let index=0;index<minimums.length;index++)close(await width(index),minimums[index]);
  await page.screenshot({path:`${out}/content-minimums.png`});
  assert.equal(await page.locator('canvas.drawing-canvas').getAttribute('data-entity-count'),'0');
  assert.ok(await page.getByRole('button',{name:'Undo',exact:true,includeHidden:true}).isDisabled());
  assert.deepEqual(errors,[]);
  console.log('PASS left/right/floating pointer resizing, independent persistence, compact/collapse restore, keyboard/Tab, viewport clamp, unchanged document/history. Build:',build);
} finally {await browser.close();}
