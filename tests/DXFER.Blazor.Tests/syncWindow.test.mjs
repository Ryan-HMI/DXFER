import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../../src/DXFER.Blazor/wwwroot/syncWindow.js', import.meta.url), 'utf8');
const module = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

test('standalone replacement removes stale launch query without navigating away', () => {
  const previous = globalThis.window;
  const calls = [];
  const state = { retained: true };
  globalThis.window = {
    location: { pathname: '/', hash: '#drawing', search: '?editToken=old&artifactId=wrong' },
    history: { state, replaceState: (...args) => calls.push(args) }
  };
  try {
    module.clearSyncLaunchQuery();
    assert.deepEqual(calls, [[state, '', '/#drawing']]);
  } finally { globalThis.window = previous; }
});

test('Sync close reports browser refusal without pretending the tab closed', () => {
  const previous = globalThis.window;
  let calls = 0;
  globalThis.window = { close: () => calls++, closed: false };
  try {
    assert.equal(module.closeLaunchedSyncTab(), false);
    assert.equal(calls, 1);
  } finally { globalThis.window = previous; }
});
