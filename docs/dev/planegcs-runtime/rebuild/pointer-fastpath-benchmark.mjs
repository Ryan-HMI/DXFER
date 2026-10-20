import { performance } from 'node:perf_hooks';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';

// Imports only the explicitly supplied isolated runtime copy.
const [runtimeDirectory, countText = '512', mode = 'fast', fixture = 'radial'] = process.argv.slice(2);
if (!runtimeDirectory || !['fast', 'baseline', 'strategy', 'fixed-fast', 'fixed-strategy'].includes(mode)
    || !['radial', 'fixed-center', 'fixed-member'].includes(fixture)) throw new Error('Invalid benchmark arguments.');
const count = Number(countText);
const { createPlaneGcsSolver } = await import(pathToFileURL(resolve(runtimeDirectory, 'planeGcsSolver.js')));
const { GcsWrapper } = await import(pathToFileURL(resolve(runtimeDirectory, 'vendor/planegcs/dist/sketch/gcs_wrapper.js')));
const nativePhases = [];
const insertions = [];
const nativeSolve = GcsWrapper.prototype.solve;
GcsWrapper.prototype.solve = function (...args) {
    const started = performance.now();
    try { return nativeSolve.apply(this, args); }
    finally { nativePhases.push(performance.now() - started); }
};
const push = GcsWrapper.prototype.push_primitives_and_params;
GcsWrapper.prototype.push_primitives_and_params = function (primitives) {
    insertions.push({
        total: primitives.length,
        fixedPoints: primitives.filter(p => p.type === 'point' && p.fixed).length,
        temporary: primitives.filter(p => p.temporary).map(p => ({ type: p.type, scale: p.scale })),
        pointerCoordinates: primitives.filter(p => ['coordinate_x', 'coordinate_y'].includes(p.type))
            .map(p => ({ type: p.type, temporary: p.temporary, x: p.x, y: p.y }))
    });
    return push.call(this, primitives);
};
const pattern = { id: 'bench', kind: 'Radial', sourceEntityIds: ['seed'], rows: count / 8, columns: 8,
    xPitch: 10, yPitch: 10, angleDegrees: 0, count, center: { x: 0, y: 0 },
    stepAngleDegrees: 360 / count, fullCircle: true, members: [] };
const entities = [{ id: 'seed', kind: 'circle', points: [], center: { x: 30, y: 20 }, radius: 1 }];
for (let index = 1; index < count; index++) {
    const angle = index * 2 * Math.PI / count;
    const id = `copy-${index}`;
    entities.push({ id, kind: 'circle', points: [],
        center: { x: 30 * Math.cos(angle) - 20 * Math.sin(angle),
            y: 30 * Math.sin(angle) + 20 * Math.cos(angle) }, radius: 1, isGenerated: true });
    pattern.members.push({ sourceEntityId: 'seed', entityId: id, row: 0, column: index });
}
const request = {
    document: { entities, constraints: [], dimensions: [], hasPatterns: true, metadata: { patterns: [pattern] } },
    drag: { key: 'seed:center', start: { x: 30, y: 20 }, end: { x: 31, y: 21 } },
    ...(fixture === 'fixed-center' ? { fixedReferenceKeys: ['seed:center'] }
        : fixture === 'fixed-member' ? { fixedReferenceKeys: ['copy-1:center'] } : {})
};
const unchanged = JSON.stringify(request);
const initializationStart = performance.now();
const fast = await createPlaneGcsSolver({
    diagnosticPointerOnly: mode === 'fast' || mode === 'strategy',
    diagnosticFixedPointer: mode.startsWith('fixed-')
});
const solveStart = performance.now();
let result;
let firstStatus;
let firstMs;
let fallbackMs = null;
try {
    result = fast.solve(request);
    firstMs = performance.now() - solveStart;
    firstStatus = result.status;
    if (mode.endsWith('strategy') && result.status !== 'solved') {
        console.log(JSON.stringify({ stage: 'fast-attempt-rejected', fixture, count, mode,
            firstStatus, firstMs, diagnostics: result.diagnostics }));
        const fallbackStart = performance.now();
        const original = await createPlaneGcsSolver();
        try { result = original.solve(request); }
        finally { original.dispose(); }
        fallbackMs = performance.now() - fallbackStart;
    }
} finally { fast.dispose(); }
const totalMs = performance.now() - solveStart;
const seed = result.entities.find(e => e.id === 'seed');
let maxPatternResidual = 0;
for (let index = 1; index < count; index++) {
    const angle = index * 2 * Math.PI / count;
    const entity = result.entities.find(e => e.id === `copy-${index}`);
    maxPatternResidual = Math.max(maxPatternResidual, Math.hypot(
        entity.center.x - (seed.center.x * Math.cos(angle) - seed.center.y * Math.sin(angle)),
        entity.center.y - (seed.center.x * Math.sin(angle) + seed.center.y * Math.cos(angle))));
}
console.log(JSON.stringify({
    fixture, count, mode, initializationMs: solveStart - initializationStart,
    firstStatus, firstMs, fallbackMs, totalMs, status: result.status, nativePhases, insertions,
    seedCenter: seed.center, pointerResidual: Math.hypot(seed.center.x - 31, seed.center.y - 21),
    maxPatternResidual, maxRadiusChange: Math.max(...result.entities.map(e => Math.abs(e.radius - 1))),
    requestUnchanged: unchanged === JSON.stringify(request),
    maxRssKiB: process.resourceUsage().maxRSS, diagnostics: result.diagnostics
}));
if (result.status !== 'solved') process.exitCode = 1;
