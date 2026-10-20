import { execFileSync } from 'node:child_process';
import { performance } from 'node:perf_hooks';
import { fileURLToPath } from 'node:url';

// An opt-in bounded smoke benchmark, not a timing-sensitive CI assertion.
// Each sample has a fresh process and a five-second wall-clock limit.
if (process.argv[2] !== '--sample') {
    for (const kind of ['Rectangular', 'Radial']) for (const count of [16, 64, 128, 256, 512]) {
        try {
            const output = execFileSync(process.execPath,
                ['--max-old-space-size=128', '--wasm-max-mem-pages=4096',
                    fileURLToPath(import.meta.url), '--sample', kind, String(count)],
                { encoding: 'utf8', timeout: 5000, maxBuffer: 1024 * 1024 });
            console.log(output.trim());
        } catch (error) {
            console.log(JSON.stringify({ kind, count, status: 'bounded-failure',
                reason: error.code ?? error.signal ?? error.status }));
            process.exitCode = 1;
            break;
        }
    }
} else {
    const { createPlaneGcsSolver } = await import('../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js');
    const kind = process.argv[3], count = Number(process.argv[4]);
    const pattern = { id: 'bench', kind, sourceEntityIds: ['seed'], rows: count / 8, columns: 8,
        xPitch: 10, yPitch: 10, angleDegrees: 0, count, center: { x: 0, y: 0 },
        stepAngleDegrees: 360 / count, fullCircle: true, members: [] };
    const entities = [{ id: 'seed', kind: 'circle', points: [], center: { x: 30, y: 20 }, radius: 1 }];
    for (let index = 1; index < count; index++) {
        const radial = kind === 'Radial', row = radial ? 0 : Math.floor(index / 8);
        const column = radial ? index : index % 8, angle = column * 2 * Math.PI / count;
        const center = radial ? { x: 30 * Math.cos(angle) - 20 * Math.sin(angle),
            y: 30 * Math.sin(angle) + 20 * Math.cos(angle) } : { x: 30 + column * 10, y: 20 + row * 10 };
        const id = `copy-${index}`;
        entities.push({ id, kind: 'circle', points: [], center, radius: 1, isGenerated: true });
        pattern.members.push({ sourceEntityId: 'seed', entityId: id, row, column });
    }
    const start = performance.now();
    const solver = await createPlaneGcsSolver(), ready = performance.now();
    const result = solver.solve({ document: { entities, constraints: [], dimensions: [],
        hasPatterns: true, metadata: { patterns: [pattern] } },
    drag: { key: 'seed:center', start: { x: 30, y: 20 }, end: { x: 31, y: 21 } } });
    const solved = performance.now();
    solver.dispose();
    console.log(JSON.stringify({ kind, count, status: result.status,
        initializationMs: +(ready - start).toFixed(2), solveMs: +(solved - ready).toFixed(2),
        rssMiB: +(process.memoryUsage().rss / 1024 ** 2).toFixed(1),
        diagnostics: result.status === 'solved' ? undefined : result.diagnostics }));
    if (result.status !== 'solved') process.exitCode = 1;
}
