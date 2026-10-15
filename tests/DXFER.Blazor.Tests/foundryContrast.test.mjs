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

test('Foundry geometry and selection colors remain readable at the brightest field point',()=>{
    assert.match(css,/radial-gradient\(ellipse at center, #172832 0%, #0b1016 70%\)/);
    const defaultColors=source.match(/strokeStyle: affectedByDiagnostics[^\n]+/)[0].match(/#[a-f0-9]{6}/g);
    const selectedColors=source.match(/strokeStyle: isActive \?[^\n]+/)[0].match(/#[a-f0-9]{6}/g);
    const hoverColors=source.match(/strokeStyle: isPowerTrimExtendHover[^\n]+/)[0].match(/#[a-f0-9]{6}/g);
    for(const color of [...defaultColors,...selectedColors,...hoverColors]) {
        assert.ok(contrast(color,'#172832')>=4.5,`${color} against field center`);
    }
});
