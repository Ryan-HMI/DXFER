import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import { canExtendDimensionSelection, getDimensionDescriptorFromReferences } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const circle = { id: 'curve', kind: 'circle', center: { x: 0, y: 0 }, radius: 2 };
const cases = [
    ['point', { id: 'peer', kind: 'point', points: [{ x: 6, y: 0 }] }, 4],
    ['line', { id: 'peer', kind: 'line', points: [{ x: 6, y: -5 }, { x: 6, y: 5 }] }, 4],
    ['circle', { id: 'peer', kind: 'circle', center: { x: 8, y: 0 }, radius: 1 }, 5],
    ['arc', { id: 'peer', kind: 'arc', center: { x: 8, y: 0 }, radius: 1, startAngleDegrees: 90, endAngleDegrees: 270 }, 5],
    ['concentric', { id: 'peer', kind: 'circle', center: { x: 0, y: 0 }, radius: 5 }, 3]
];

for (const [name, peer, gap] of cases) for (const reverse of [false, true]) {
    test(`perimeter dimension previews and native-solves ${name}, reversed=${reverse}`, async () => {
        const keys = reverse ? ['peer', 'curve'] : ['curve', 'peer'];
        const document = { entities: [circle, peer], constraints: [], dimensions: [] };
        const state = { document, activeTool: 'dimension', view: { scale: 10, offsetX: 0, offsetY: 0 } };
        assert.equal(canExtendDimensionSelection(state, [keys[0]], keys[1]), true);
        const preview = getDimensionDescriptorFromReferences(state, keys, { x: 4, y: 3 }, false, 'gap');
        assert.equal(preview.kind, 'perimeterdistance');
        assert.equal(preview.value, gap);
        const solver = await createPlaneGcsSolver();
        try {
            const dimension = { id: 'gap', kind: 'PerimeterDistance', referenceKeys: keys, value: gap + 1, isDriving: true };
            const request = { document, dimensions: [dimension], fixedReferenceKeys: ['curve'] };
            const snapshot = structuredClone(request);
            const result = solver.solve(request);
            assert.equal(result.status, 'solved', result.diagnostics.join('\n'));
            assert.deepEqual(request, snapshot);
            assert.deepEqual(result.entities[0], circle);
            const reopened = { ...document, entities: result.entities, dimensions: [dimension] };
            const again = solver.solve({ document: reopened, dimensions: [{ ...dimension, value: gap + 2 }], fixedReferenceKeys: ['curve'] });
            assert.equal(again.status, 'solved', again.diagnostics.join('\n'));
            const refused = solver.solve({ document, dimensions: [dimension], fixedReferenceKeys: ['curve', 'peer'] });
            assert.notEqual(refused.status, 'solved');
            assert.deepEqual(refused.entities, document.entities);
        } finally { solver.dispose(); }
    });
}
