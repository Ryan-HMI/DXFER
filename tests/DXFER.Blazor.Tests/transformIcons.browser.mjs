import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
const {chromium,expect}=await import(process.env.PLAYWRIGHT_MODULE??'playwright/test');
const url=process.env.DXFER_QA_URL??'http://127.0.0.1:5037';
assert.ok(['localhost','127.0.0.1','dxferdev.sync.harrisonmetals.com'].includes(new URL(url).hostname));
await mkdir('/tmp/dxfer-transform-icons-evidence',{recursive:true});
const browser=await chromium.launch({headless:true});
try{
 for(const deviceScaleFactor of [1,2]){
  const page=await browser.newPage({viewport:{width:1440,height:1000},deviceScaleFactor,extraHTTPHeaders:{'X-DXFER-API-Key':process.env.DXFER_QA_API_KEY}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('input[type=file]')?._blazorInputFileNextFileId!==undefined);
  const build=await page.locator('.app-brand').getAttribute('title');if(process.env.DXFER_EXPECTED_REVISION)assert.ok(build.includes(process.env.DXFER_EXPECTED_REVISION),build);
  const rotate=page.getByRole('button',{name:'Rotate',exact:true}),scale=page.getByRole('button',{name:'Scale',exact:true});
  await expect(rotate.locator('svg path')).toHaveCount(2);
  await expect(rotate.locator('svg path').first()).toHaveAttribute('d','M18.92820323 8A8 8 0 1 1 12 4');
  await expect(scale.locator('svg rect')).toHaveCount(2);await expect(scale.locator('svg path')).toHaveCount(0);
  for(const button of [rotate,scale]){
   await expect(button).toHaveAttribute('title',/.+/);
   const b=await button.boundingBox(),s=await button.locator('svg').boundingBox();
   assert.deepEqual([b.width,b.height,s.width,s.height],[32,32,20,20]);
   const pixels=await button.locator('svg').evaluate(async svg=>{
    const image=new Image();image.src='data:image/svg+xml,'+encodeURIComponent(new XMLSerializer().serializeToString(svg).replaceAll('currentColor','#ffffff'));await image.decode();
    const c=document.createElement('canvas');c.width=c.height=20;const ctx=c.getContext('2d');ctx.drawImage(image,0,0,20,20);const data=ctx.getImageData(0,0,20,20).data;
    let ink=0;for(let i=3;i<data.length;i+=4)if(data[i]>32)ink++;return ink;
   });assert.ok(pixels>40);
  }
  await page.screenshot({path:`/tmp/dxfer-transform-icons-evidence/installed-${deviceScaleFactor}x.png`});
  await page.locator('.dxfer-tool-group').filter({has:rotate}).screenshot({path:`/tmp/dxfer-transform-icons-evidence/transform-${deviceScaleFactor}x.png`});
  assert.deepEqual(errors,[]);console.log(`PASS actual Rotate/Scale paths, visible pixels, tooltip/target sizes at ${deviceScaleFactor}x. ${build}`);await page.close();
 }
}finally{await browser.close();}
