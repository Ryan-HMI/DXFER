import assert from 'node:assert/strict';

// Exercise the proposed solveNative hook without editing the parent's mapper.
export async function load(url, context, nextLoad) {
  const result = await nextLoad(url, context);
  if (!url.endsWith('/wwwroot/planeGcsSolver.js')) return result;
  let source = String(result.source);
  const start = source.indexOf('function solveNative(wrapper, primitives) {');
  const end = source.indexOf('\n// Wire contract:', start);
  assert.ok(start >= 0 && end > start, 'solveNative integration anchor changed');
  source = source.slice(0, start)
    + 'const solveNative = solveNativeWithFallback;\n' + source.slice(end);
  const angleMapping = `relation("l2l_angle_ll", { l1_id: a.id, l2_id: b.id, angle: target }, source,
                    get => Math.abs(angleDelta(lineAngle(b, get) - lineAngle(a, get) - target)));`;
  assert.ok(source.includes(angleMapping), 'line angle integration anchor changed');
  source = source.replace(angleMapping, `const angular = relation("l2l_angle_ll", { l1_id: a.id, l2_id: b.id, angle: target,
                    alternativeAngles: (request.dimensions?.some(update => update.id === d.id)
                        || request.attemptedDrivingDimensionIds?.includes(d.id))
                        ? options.filter(option => option !== target) : [] }, source,
                    get => {
                        const solvedTarget = get(angular.id).angle;
                        if (!options.some(option => Math.abs(angleDelta(option - solvedTarget)) < 1e-12)) return Infinity;
                        return Math.abs(angleDelta(lineAngle(b, get) - lineAngle(a, get) - solvedTarget));
                    });`);
  return { ...result, source: 'import { solveNativeWithFallback } from "./planeGcsSolveStrategy.js";\n' + source };
}
