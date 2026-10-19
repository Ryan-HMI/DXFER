import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync} from 'node:fs';
import {requiresSharedGeometryDragPreview} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('pattern settings are staged and expose both layouts without implicit commits', () => {
  const ui = readFileSync(new URL('../../src/DXFER.Blazor/Components/PatternEditor.razor', import.meta.url),'utf8');
  for (const label of ['Rectangular','Staggered','Radial','Columns','Rows','Full circle','Confirm','Cancel']) assert.ok(ui.includes(label), label);
  assert.ok(ui.includes('disabled="@(!CanConfirm)"'));
  assert.ok(ui.includes('@onkeydown:stopPropagation="true"'));
});

test('pattern drags use the shared core preview even without explicit sketch constraints', () => {
  assert.equal(requiresSharedGeometryDragPreview({hasPatterns:true, constraints:[]}), true);
  assert.equal(requiresSharedGeometryDragPreview({hasPatterns:false, constraints:[]}), false);
  assert.equal(requiresSharedGeometryDragPreview({constraints:[{kind:'Offset',state:'Satisfied'}]}), true);
  assert.equal(requiresSharedGeometryDragPreview({constraints:[{kind:'Offset',state:'Suppressed'}]}), false);
});
