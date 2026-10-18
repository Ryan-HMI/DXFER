import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source=readFileSync(new URL('../../src/DXFER.Blazor/wwwroot/drawingCanvas.js',import.meta.url),'utf8');
const css=readFileSync(new URL('../../src/DXFER.Blazor/Components/DrawingCanvas.razor.css',import.meta.url),'utf8');
const luminance=hex=>{
    const rgb=hex.match(/[a-f0-9]{2}/gi).map(v=>parseInt(v,16)/255)
        .map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);
    return rgb[0]*.2126+rgb[1]*.7152+rgb[2]*.0722;
};
const contrast=(a,b)=>(Math.max(luminance(a),luminance(b))+.05)/(Math.min(luminance(a),luminance(b))+.05);

test('Foundry overlays remain readable on the gradient and neutral fields',()=>{
    assert.match(css,/background: var\(--dx-canvas-background, radial-gradient\(ellipse at center, #172832 0%, #0b1016 70%\)\)/);
    for(const color of ['#ff6c7c','#97a5b6','#d3e1ed','#b7efff','#66d9ff','#ffc56c']) {
        assert.ok(source.includes(color));
        for(const background of ['#202020','#172832','#0b1016'])
            assert.ok(contrast(color,background)>=4.5,`${color} against ${background}`);
    }
});
