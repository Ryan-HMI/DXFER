import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

// Run in a disposable bounded child with --experimental-wasm-type-reflection.
const benchmark = resolve(process.argv[2]);
const kind = process.argv[3] ?? 'Rectangular';
const count = process.argv[4] ?? '256';
const memories = [];
const instantiate = WebAssembly.instantiate;
WebAssembly.instantiate = async function (...args) {
    const result = await Reflect.apply(instantiate, WebAssembly, args);
    const instance = result instanceof WebAssembly.Instance ? result : result.instance;
    const module = args[0] instanceof WebAssembly.Module ? args[0] : new WebAssembly.Module(args[0]);
    for (const entry of WebAssembly.Module.exports(module).filter(entry => entry.kind === 'memory')) {
        const memory = instance.exports[entry.name];
        memories.push({ declaration: entry, memory, initialBytes: memory.buffer.byteLength });
    }
    return result;
};
try {
    process.argv = [process.execPath, benchmark, '--sample', kind, count];
    await import(pathToFileURL(benchmark).href);
} finally {
    WebAssembly.instantiate = instantiate;
}
assert.equal(process.exitCode ?? 0, 0, 'The real benchmark must solve before memory checks.');
assert.equal(memories.length, 1, 'Reassess aggregate memory if the library introduces more memories.');
const { declaration, memory, initialBytes } = memories[0];
assert.equal(declaration.type?.minimum, 256, 'Run with type reflection enabled.');
assert.equal(declaration.type?.maximum, 4096);
const afterSolveBytes = memory.buffer.byteLength;
assert.ok(afterSolveBytes > initialBytes, 'This fixture must actually exercise native memory growth.');
assert.ok(afterSolveBytes <= 256 * 1024 * 1024);
// The benchmark has disposed the solver. Test the memory boundary independently,
// without using the detached pre-growth module views for any more native calls.
memory.grow(4096 - afterSolveBytes / 65536);
assert.throws(() => memory.grow(1), RangeError);
console.log(JSON.stringify({
    evidence: 'real-wasm-growth-and-hard-maximum', kind, count: Number(count),
    declaration, initialBytes, afterSolveBytes, cappedBytes: memory.buffer.byteLength,
    growAboveMaximum: 'rejected'
}));
