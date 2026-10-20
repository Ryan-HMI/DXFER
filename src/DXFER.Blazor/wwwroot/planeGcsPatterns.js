const lower = value => String(value ?? "").toLowerCase();
const radians = Math.PI / 180;
const parameter = (node, prop) => ({ o_id: node.id, prop });

// Pattern settings remain explicit document parameters. Members and seeds share
// simultaneous affine equations so another relation can move either side safely.
export function mapPlaneGcsPatterns(context) {
    const { patterns, byId, model, relation, point, add, requireSupported, seedOperations = [],
        fixedParameterIds = new Set() } = context;
    requireSupported(Array.isArray(patterns) && patterns.length <= 64, "Invalid PlaneGCS pattern list.");
    const owned = new Set(), patternIds = new Set();
    const coefficientLines = new Map();
    for (const pattern of patterns) {
        requireSupported(typeof pattern.id === "string" && pattern.id.length > 0
            && !patternIds.has(pattern.id), "Pattern IDs must be unique.");
        patternIds.add(pattern.id);
        const kind = lower(pattern.kind), radial = kind === "radial";
        requireSupported(["rectangular", "staggered", "radial"].includes(kind), "Unsupported pattern kind.");
        const rows = radial ? 1 : pattern.rows, columns = radial ? pattern.count : pattern.columns;
        const sources = pattern.sourceEntityIds, members = pattern.members;
        requireSupported(Number.isInteger(rows) && Number.isInteger(columns) && rows >= 1 && columns >= 1
            && rows * columns >= 2 && rows * columns <= 1024, "Invalid pattern instance counts.");
        requireSupported(Array.isArray(sources) && sources.length > 0 && sources.length <= 128
            && new Set(sources).size === sources.length && sources.every(id => byId.has(id)),
        "Invalid pattern source identities.");
        requireSupported(Array.isArray(members) && members.length === (rows * columns - 1) * sources.length
            && members.length <= 4096, "Pattern members do not match the configured instance count.");
        const numeric = radial ? [pattern.center?.x, pattern.center?.y, pattern.stepAngleDegrees]
            : [pattern.xPitch, pattern.yPitch, pattern.angleDegrees];
        requireSupported(numeric.every(Number.isFinite), "Pattern distances and angles must be finite.");
        const origin = radial ? point(pattern.center) : null;
        if (origin) origin.fixed = true;
        const slots = new Set();
        for (const member of members) {
            const { sourceEntityId, entityId, row, column } = member;
            requireSupported(sources.includes(sourceEntityId) && byId.has(entityId) && !owned.has(entityId)
                && !sources.includes(entityId) && Number.isInteger(row) && Number.isInteger(column)
                && row >= 0 && row < rows && column >= 0 && column < columns
                && (row !== 0 || column !== 0), "Invalid or multiply-owned pattern member.");
            const slot = JSON.stringify([sourceEntityId, row, column]);
            requireSupported(!slots.has(slot), "Duplicate pattern instance position.");
            slots.add(slot); owned.add(entityId);
            const source = model(sourceEntityId), target = model(entityId);
            requireSupported(source.kind === target.kind
                && ["point", "line", "polyline", "circle", "arc", "ellipse", "spline", "polygon"].includes(source.kind)
                && source.points.length === target.points.length, "Unsupported pattern member geometry.");
            if (source.kind === "polygon") {
                requireSupported(source.entity.sideCount === target.entity.sideCount
                    && source.entity.circumscribed === target.entity.circumscribed,
                "Pattern polygon topology differs from its seed; stage topology edits for the complete pattern first.");
                requireSupported(source.polygonGuide && target.polygonGuide && source.guide && target.guide
                    && source.points.length === 2 && target.points.length === 2,
                "Unsupported pattern polygon representation.");
            }
            if (source.kind === "spline") {
                for (const field of ["splineDegree", "splineKnots", "splineWeights"])
                    requireSupported(JSON.stringify(source.entity[field]) === JSON.stringify(target.entity[field]),
                        `Pattern spline ${field} differs from its seed.`);
                requireSupported(Boolean(source.spline) === Boolean(target.spline),
                    "Pattern spline representations differ.");
            }
            const angle = radial
                ? column * (pattern.fullCircle ? 360 / columns : pattern.stepAngleDegrees) * radians : 0;
            const c = Math.cos(angle), s = Math.sin(angle);
            let dx, dy;
            if (radial) {
                dx = pattern.center.x * (1 - c) + pattern.center.y * s;
                dy = pattern.center.y * (1 - c) - pattern.center.x * s;
            } else {
                const x = (column + (kind === "staggered" && row % 2 ? 0.5 : 0)) * pattern.xPitch;
                const y = row * pattern.yPitch, direction = pattern.angleDegrees * radians;
                dx = x * Math.cos(direction) - y * Math.sin(direction);
                dy = x * Math.sin(direction) + y * Math.cos(direction);
            }
            const label = `Pattern ${pattern.id} member ${entityId}`;
            // Arc endpoints are already determined by their native curve rules.
            // Map the defining parameters, not a redundant second endpoint system.
            const sourcePoints = source.curve ? [source.center]
                : source.ellipse ? [source.center, source.focus]
                    : source.polygonGuide ? [source.center, source.guide] : source.points;
            const targetPoints = target.curve ? [target.center]
                : target.ellipse ? [target.center, target.focus]
                    : target.polygonGuide ? [target.center, target.guide] : target.points;
            // Polygon guide incidence already determines its radius. Mapping both
            // defining points carries guide/apothem size and orientation without
            // a redundant radius equality or vertex reconstruction.
            sourcePoints.forEach((p, index) => mapPoint(p, targetPoints[index], c, s, dx, dy, label, origin));
            if (source.curve) {
                relation("equal", { param1: parameter(source.curve, "radius"), param2: parameter(target.curve, "radius") },
                    label, get => Math.abs(get(source.curve.id).radius - get(target.curve.id).radius));
                if (source.kind === "arc") {
                    const angleDifferences = {};
                    for (const prop of ["start_angle", "end_angle"]) {
                        // Native representations may differ by full turns.
                        const initial = target.curve[prop] - source.curve[prop];
                        const difference = angle + Math.round((initial - angle) / (2 * Math.PI)) * 2 * Math.PI;
                        angleDifferences[prop] = difference;
                        relation("difference", { param1: parameter(source.curve, prop),
                            param2: parameter(target.curve, prop), difference }, label,
                        get => Math.abs(get(target.curve.id)[prop] - get(source.curve.id)[prop] - difference));
                    }
                    seedOperations.push(() => {
                        const values = {
                            radius: source.curve.radius,
                            start_angle: source.curve.start_angle + angleDifferences.start_angle,
                            end_angle: source.curve.end_angle + angleDifferences.end_angle
                        };
                        if (fixedParameterIds.has(target.curve.id)) {
                            if (Object.entries(values).some(([prop, value]) => target.curve[prop] !== value)) return false;
                        } else Object.assign(target.curve, values);
                        for (const [endpoint, prop] of [[target.start, "start_angle"], [target.end, "end_angle"]]) {
                            const x = target.center.x + values.radius * Math.cos(values[prop]);
                            const y = target.center.y + values.radius * Math.sin(values[prop]);
                            if (endpoint.fixed || fixedParameterIds.has(endpoint.id)) {
                                if (Math.hypot(endpoint.x - x, endpoint.y - y) > 1e-6) return false;
                            } else Object.assign(endpoint, { x, y });
                        }
                        return true;
                    });
                }
            }
            if (source.ellipse) {
                requireSupported(target.ellipse?.type === source.ellipse.type,
                    "Pattern ellipse representations differ.");
                const props = source.ellipse.type === "arc_of_ellipse"
                    ? ["radmin", "start_angle", "end_angle"] : ["radmin"];
                for (const prop of props)
                    relation("equal", { param1: parameter(source.ellipse, prop), param2: parameter(target.ellipse, prop) },
                        label, get => Math.abs(get(source.ellipse.id)[prop] - get(target.ellipse.id)[prop]));
            }
        }
    }
    requireSupported(patterns.every(p => p.sourceEntityIds.every(id => !owned.has(id))),
        "Nested or cyclic patterns are unsupported.");
    requireSupported([...byId.values()].every(entity => !entity.isGenerated || owned.has(entity.id)),
        "Generated geometry has no valid pattern owner.");
    return owned;

    function mapPoint(source, target, c, s, dx, dy, label, origin) {
        seedOperations.push(() => {
            const x = c * source.x - s * source.y + dx;
            const y = s * source.x + c * source.y + dy;
            if (target.fixed) return Math.hypot(target.x - x, target.y - y) <= 1e-6;
            Object.assign(target, { x, y });
            return true;
        });
        if (Math.abs(s) < 1e-14 && Math.abs(c - 1) < 1e-14) {
            for (const [prop, difference] of [["x", dx], ["y", dy]])
                relation("difference", { param1: parameter(source, prop), param2: parameter(target, prop), difference },
                    label, get => Math.abs(get(target.id)[prop] - get(source.id)[prop] - difference));
            return;
        }
        if (origin && Math.hypot(source.x - origin.x, source.y - origin.y) > 1e-7
            && Math.hypot(target.x - origin.x, target.y - origin.y) > 1e-7) {
            const from = add({ type: "line", p1_id: origin.id, p2_id: source.id });
            const to = add({ type: "line", p1_id: origin.id, p2_id: target.id });
            const angle = Math.atan2(s, c);
            relation("equal_length", { l1_id: from.id, l2_id: to.id }, label, get => {
                const a = get(source.id), b = get(target.id), o = get(origin.id);
                return Math.abs(Math.hypot(a.x - o.x, a.y - o.y) - Math.hypot(b.x - o.x, b.y - o.y));
            });
            relation("l2l_angle_ll", { l1_id: from.id, l2_id: to.id, angle }, label, get => {
                const a = get(source.id), b = get(target.id), o = get(origin.id);
                const residual = Math.atan2(b.y - o.y, b.x - o.x) - Math.atan2(a.y - o.y, a.x - o.x) - angle;
                return Math.abs(Math.atan2(Math.sin(residual), Math.cos(residual)));
            });
            return;
        }
        // Fixed-slope incidence expresses multiplication without an angle/distance
        // singularity at the center. The pinned engine's free-parameter
        // Proportional primitive incorrectly reduces to equality in this case.
        const a = point({ x: c * source.x, y: s * source.x });
        const b = point({ x: s * source.y, y: c * source.y });
        const rotated = point({ x: a.x - b.x, y: a.y + b.y });
        for (const [node, prop, input, ratio] of [
            [a, "x", "x", c], [a, "y", "x", s], [b, "x", "y", s], [b, "y", "y", c]
        ]) {
            let line = coefficientLines.get(ratio);
            if (!line) {
                const origin = point({ x: 0, y: 0 }), endpoint = point({ x: 1, y: ratio });
                origin.fixed = true; endpoint.fixed = true;
                line = [origin, endpoint];
                coefficientLines.set(ratio, line);
            }
            const scaled = point({ x: source[input], y: ratio * source[input] });
            relation("equal", { param1: parameter(scaled, "x"), param2: parameter(source, input) },
                label, get => Math.abs(get(scaled.id).x - get(source.id)[input]));
            relation("equal", { param1: parameter(scaled, "y"), param2: parameter(node, prop) },
                label, get => Math.abs(get(scaled.id).y - get(node.id)[prop]));
            relation("point_on_line_ppp", { p_id: scaled.id, lp1_id: line[0].id, lp2_id: line[1].id },
                label, get => Math.abs(get(scaled.id).y - ratio * get(scaled.id).x));
        }
        relation("difference", { param1: parameter(b, "x"), param2: parameter(a, "x"),
            difference: parameter(rotated, "x") }, label,
        get => Math.abs(get(a.id).x - get(b.id).x - get(rotated.id).x));
        relation("difference", { param1: parameter(a, "y"), param2: parameter(rotated, "y"),
            difference: parameter(b, "y") }, label,
        get => Math.abs(get(rotated.id).y - get(a.id).y - get(b.id).y));
        for (const [prop, difference] of [["x", dx], ["y", dy]])
            relation("difference", { param1: parameter(rotated, prop), param2: parameter(target, prop), difference },
                label, get => Math.abs(get(target.id)[prop] - get(rotated.id)[prop] - difference));
    }
}
