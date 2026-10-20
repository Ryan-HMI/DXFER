import { pathToFileURL } from 'node:url';

const messageLimit = 8 * 1024 * 1024;
const modulePath = process.argv[2];
if (!modulePath) throw new Error('PlaneGCS module path is required.');
// Keep stdout exclusively for framed responses, including during WASM startup.
console.log = (...args) => console.error(...args);
const { createPlaneGcsSolver } = await import(pathToFileURL(modulePath).href);
const solver = await createPlaneGcsSolver();
try {
  for await (const line of readFrames(process.stdin)) {
    let id = null;
    let result;
    try {
      const message = JSON.parse(line);
      id = message.id;
      if (!Number.isSafeInteger(id)) throw new Error('Invalid request identity.');
      result = solver.solve(message.request);
      // The mapper may convert Emscripten's abort into a failed result. Its
      // native instance is no longer reusable, even when the geometry was preserved.
      if (result?.diagnostics?.some(value => typeof value === 'string' && value.startsWith('Aborted(')))
        throw new WebAssembly.RuntimeError('PlaneGCS native runtime aborted.');
    } catch (error) {
      if (error instanceof WebAssembly.RuntimeError || error instanceof RangeError) throw error;
      result = { status: 'failed', engine: 'PlaneGCS', entities: [],
        diagnostics: ['PlaneGCS could not process this request. Geometry unchanged.'] };
    }
    let response;
    try {
      checkResponseBudget({ id, result });
      response = JSON.stringify({ id, result });
    } catch {
      response = JSON.stringify({ id, result: { status: 'failed', engine: 'PlaneGCS', entities: [],
        diagnostics: ['PlaneGCS response exceeds the message limit. Geometry unchanged.'] } });
    }
    // Await completion so a slow reader cannot accumulate an unbounded output queue.
    await new Promise((resolve, reject) =>
      process.stdout.write(response + '\n', error => error ? reject(error) : resolve()));
  }
} finally {
  solver.dispose();
}

async function* readFrames(stream) {
  const frame = Buffer.allocUnsafe(messageLimit);
  const decoder = new TextDecoder('utf-8', { fatal: true });
  let used = 0;
  for await (const chunk of stream) {
    let offset = 0;
    while (offset < chunk.length) {
      const newline = chunk.indexOf(10, offset);
      const end = newline < 0 ? chunk.length : newline;
      const length = end - offset;
      if (length > messageLimit - used) throw new Error('Request exceeds the solver message limit.');
      chunk.copy(frame, used, offset, end);
      used += length;
      offset = end + 1;
      if (newline < 0) continue;
      yield decoder.decode(frame.subarray(0, used));
      used = 0;
    }
  }
  if (used !== 0) throw new Error('Incomplete solver request.');
}

// Bound serialization before constructing the complete JSON string. Chunked
// string accounting uses JSON's own escaping and only overcounts split surrogate pairs.
function checkResponseBudget(value) {
  let remaining = messageLimit;
  const consume = count => {
    remaining -= count;
    if (remaining < 0) throw new Error('Response exceeds the solver message limit.');
  };
  const string = value => {
    consume(2);
    if (value.length > remaining) throw new Error('Response exceeds the solver message limit.');
    for (let offset = 0; offset < value.length; offset += 1024)
      consume(Buffer.byteLength(JSON.stringify(value.slice(offset, offset + 1024)), 'utf8') - 2);
  };
  const visit = (value, depth) => {
    if (depth > 64) throw new Error('Response nesting exceeds the solver limit.');
    if (typeof value === 'string') return string(value);
    if (value === null || typeof value === 'number' || typeof value === 'boolean')
      return consume(JSON.stringify(value).length);
    if (Array.isArray(value)) {
      consume(2 + Math.max(0, value.length - 1));
      for (const item of value) visit(item, depth + 1);
    } else if (typeof value === 'object') {
      consume(2);
      let count = 0;
      for (const key of Object.keys(value)) {
        if (value[key] === undefined) continue;
        consume(1 + (count++ > 0 ? 1 : 0));
        string(key);
        visit(value[key], depth + 1);
      }
    } else throw new Error('Unsupported solver response value.');
  };
  visit(value, 0);
}
