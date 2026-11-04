import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Algorithm, SolveStatus } from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';
import { solveNativeWithFallback } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolveStrategy.js';

function wrapperFor(solve) {
  let serial = 0;
  const calls = [];
  class GcsSystem { constructor() { this.serial = ++serial; } }
  return {
    calls, gcs: new GcsSystem(), module: { GcsSystem }, primitives: [], conflicts: [],
    fixedParameterIds: new Set(['locked']), destroyed: 0,
    push_primitives_and_params(primitives) { this.primitives.push(...primitives); },
    set_max_iterations(value) { this.iterations = value; },
    get_gcs_conflicting_constraints() { return this.conflicts; },
    solve(algorithm) {
      calls.push({ serial: this.gcs.serial, algorithm, primitives: structuredClone(this.primitives), iterations: this.iterations });
      return solve(this, algorithm);
    },
    apply_solution() {
      this.primitives.find(primitive => primitive.id === 'point').x = 999;
    },
    destroy_gcs_module() { this.primitives = []; this.conflicts = []; this.destroyed++; }
  };
}
const geometry = () => [{ id: 'point', type: 'point', x: 1, y: 2 }];

test('successful primary solve keeps the existing single-solve fast path', () => {
  const wrapper = wrapperFor(() => SolveStatus.Success);
  assert.equal(solveNativeWithFallback(wrapper, geometry()), SolveStatus.Success);
  assert.equal(wrapper.calls.length, 1);
  assert.equal(wrapper.calls[0].algorithm, Algorithm.LevenbergMarquardt);
  assert.equal(wrapper.destroyed, 0);
});

test('fallback rebuilds exact hard intent, preferences, and fixed IDs from the pre-attempt seed', () => {
  const primitives = [...geometry(), { id: 'goal', type: 'coordinate_x', p_id: 'point', x: 7, temporary: true }];
  const wrapper = wrapperFor((self, algorithm) => algorithm === Algorithm.DogLeg || self.primitives.length === 1
    ? SolveStatus.Success : SolveStatus.Failed);
  assert.equal(solveNativeWithFallback(wrapper, primitives), SolveStatus.Success);
  assert.deepEqual(wrapper.calls.map(call => call.algorithm),
    [Algorithm.LevenbergMarquardt, Algorithm.LevenbergMarquardt, Algorithm.DogLeg, Algorithm.DogLeg]);
  assert.equal(wrapper.calls[1].primitives[0].x, 999, 'primary hard solution seeded its goals');
  assert.equal(wrapper.calls[2].primitives[0].x, 1, 'fallback must not start from the failed primary seed');
  assert.deepEqual(wrapper.calls[3].primitives[1], primitives[1]);
  assert.ok(wrapper.calls[0].serial !== wrapper.calls[2].serial);
  assert.deepEqual([...wrapper.fixedParameterIds], ['locked']);
  assert.ok(wrapper.calls.every(call => call.iterations === 200));
});

test('native apparent convergence with conflicts still retries and remains a refusal if unresolved', () => {
  const wrapper = wrapperFor(self => {
    self.conflicts = ['hard-conflict'];
    return SolveStatus.Converged;
  });
  assert.equal(solveNativeWithFallback(wrapper, geometry()), SolveStatus.Converged);
  assert.equal(wrapper.calls.length, 2);
  assert.deepEqual(wrapper.get_gcs_conflicting_constraints(), ['hard-conflict']);
});

test('equivalent angle search is finite and never searches combinations of edited angles', () => {
  const angle = id => ({ id, type: 'l2l_angle_ll', angle: 0,
    alternativeAngles: [Math.PI, -Math.PI, Math.PI / 2, -Math.PI / 2] });
  const one = wrapperFor(() => SolveStatus.Failed);
  solveNativeWithFallback(one, [...geometry(), angle('angle')]);
  assert.equal(one.calls.length, 6, 'primary pair plus two unique alternatives from at most three entries');
  assert.equal(one.destroyed, 5);
  const two = wrapperFor(() => SolveStatus.Failed);
  solveNativeWithFallback(two, [...geometry(), angle('one'), angle('two')]);
  assert.equal(two.calls.length, 2);
});

test('alternate angle success keeps every other hard parameter and intent unchanged', () => {
  const original = [...geometry(), { id: 'angle', type: 'l2l_angle_ll', angle: 0, alternativeAngles: [Math.PI] },
    { id: 'length', type: 'p2p_distance', distance: 8 }];
  const saved = structuredClone(original);
  const wrapper = wrapperFor(self => self.primitives.find(primitive => primitive.id === 'angle').angle === Math.PI
    ? SolveStatus.Success : SolveStatus.Failed);
  assert.equal(solveNativeWithFallback(wrapper, original), SolveStatus.Success);
  assert.deepEqual(original, saved);
  assert.deepEqual(wrapper.primitives.filter(primitive => primitive.id !== 'angle'),
    original.filter(primitive => primitive.id !== 'angle'));
  assert.equal(wrapper.calls.length, 3);
});
