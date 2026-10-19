import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync} from 'node:fs';
import {contextSelectionForTarget, getPersistentDimensionDescriptors} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
import {getDimensionDisplayText} from '../../src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js';

test('context click retains the selected outline but does not add unrelated geometry', () => {
  const selected = new Set(['a', 'b', 'c', 'd']);
  assert.deepEqual(contextSelectionForTarget(selected, {entityId:'b',key:'b|point|start|0|0'}), [...selected]);
  assert.deepEqual(contextSelectionForTarget(selected, {entityId:'e',key:'e|point|start|0|0'}), ['e|point|start|0|0']);
  assert.deepEqual(contextSelectionForTarget(selected, null), []);
  assert.deepEqual(contextSelectionForTarget(selected, {entityId:'$origin',key:'$origin:center'}), []);
});

test('driven dimensions have parenthesized values and retain mode in overlays', () => {
  assert.equal(getDimensionDisplayText({kind:'radius',value:3,isDriving:false}), '(R3)');
  assert.equal(getDimensionDisplayText({kind:'radius',value:3,isDriving:true}), 'R3');
  const state = {document:{entities:[],dimensions:[{id:'d',kind:'LinearDistance',referenceKeys:[],value:3,anchor:{x:0,y:1},isDriving:false}]},view:{scale:1,offsetX:0,offsetY:0}};
  assert.equal(getPersistentDimensionDescriptors(state)[0].isDriving,false);
});

test('radial context choices are independent from driving mode and retain symbol conventions', () => {
  const markup = readFileSync(new URL('../../src/DXFER.Blazor/Components/DrawingWorkbench.razor', import.meta.url), 'utf8');
  assert.match(markup, /dimension.Kind is SketchDimensionKind.Radius or SketchDimensionKind.Diameter/);
  assert.match(markup, /name="dimension-radial-kind"[^\n]+ContextDimensionRadialKind\(SketchDimensionKind.Radius\)/);
  assert.match(markup, /name="dimension-radial-kind"[^\n]+ContextDimensionRadialKind\(SketchDimensionKind.Diameter\)/);
  assert.equal(getDimensionDisplayText({kind:'diameter',value:5,isDriving:true}), '\u23005');
  assert.equal(getDimensionDisplayText({kind:'radius',value:2.5,isDriving:true}), 'R2.5');
  assert.equal(getDimensionDisplayText({kind:'diameter',value:5,isDriving:false}), '(\u23005)');
});
