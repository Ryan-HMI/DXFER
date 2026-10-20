import { performance } from 'node:perf_hooks';
import { resourceUsage } from 'node:process';
import { GcsWrapper } from '../../../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/sketch/gcs_wrapper.js';

// Diagnostic-only prototype instrumentation in this disposable benchmark process.
const phases = {};
const debug = process.argv.includes('--native-debug');
const hardGoals = process.argv.includes('--diagnostic-hard-goals');
const beginning = performance.now();
if (debug) {
    const log = console.log;
    console.log = (...args) => log(JSON.stringify({ elapsedMs: performance.now() - beginning, message: args }));
}
let memory;
const instantiate = WebAssembly.instantiate;
WebAssembly.instantiate = async function (...args) {
    const result = await Reflect.apply(instantiate, WebAssembly, args);
    const instance = result instanceof WebAssembly.Instance ? result : result.instance;
    memory = Object.values(instance.exports).find(value => value instanceof WebAssembly.Memory);
    return result;
};
let primitiveCounts = {};
let params;
let wasmBytes;
let temporaryGoals;
for (const method of ['push_primitives_and_params', 'solve', 'apply_solution', 'clear_data']) {
    const original = GcsWrapper.prototype[method];
    GcsWrapper.prototype[method] = function (...args) {
        if (method === 'push_primitives_and_params') {
            temporaryGoals = args[0].filter(primitive => primitive.temporary).length;
            // Deliberately different intent semantics: isolate the priority-solver
            // path only. This is never an application change or capacity pass.
            if (hardGoals) args[0] = args[0].map(primitive => ({ ...primitive, temporary: false }));
            primitiveCounts = {};
            for (const primitive of args[0]) {
                primitiveCounts[primitive.type] = (primitiveCounts[primitive.type] ?? 0) + 1;
            }
        }
        if (method === 'solve' && debug) this.debug_mode = 2;
        const start = performance.now();
        try {
            return original.apply(this, args);
        } finally {
            phases[method] = (phases[method] ?? 0) + performance.now() - start;
            if (method === 'solve') {
                params = this.gcs.params_size();
                wasmBytes = memory?.buffer.byteLength;
            }
        }
    };
}
const count = process.argv[2] ?? '128';
process.argv = [process.argv[0], process.argv[1], '--sample', 'Radial', count];
const cpu = process.cpuUsage();
const start = performance.now();
await import('../../../../tests/DXFER.Blazor.Tests/planeGcsBenchmark.mjs');
console.log(JSON.stringify({
    diagnostic: 'radial-native-phase-timing',
    count: Number(count),
    variant: hardGoals ? 'diagnostic-hard-goals-not-production-semantics' : 'unaltered-priority-drag',
    totalMs: performance.now() - start,
    phasesMs: phases,
    primitiveCounts,
    params,
    temporaryGoals,
    wasmBytes,
    cpuMicroseconds: process.cpuUsage(cpu),
    maxRssKiB: resourceUsage().maxRSS
}));
