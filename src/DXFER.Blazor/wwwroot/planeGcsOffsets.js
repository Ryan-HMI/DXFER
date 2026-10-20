const lower = value => String(value ?? "").toLowerCase();
const distance = (a, b) => Math.hypot(b.x - a.x, b.y - a.y);
const tolerance = 1e-7;
const sameReferences = (a, b) => a.length === b.length && a.every((key, i) => key === b[i]);

// Retained offsets become simultaneous equations, not a second geometry solver.
// Segment correspondence comes from the original contour and is held for this solve.
export function mapPlaneGcsOffsets(context) {
    const { constraints, dimensions, model, nodes, relation, samePoint, requireSupported,
        add, point, fixSource } = context;
    const offsets = constraints.filter(c => lower(c.kind) === "offset" && lower(c.state) !== "suppressed");
    const offsetDimensions = dimensions.filter(d => lower(d.kind) === "offset" && lower(d.state) !== "suppressed");
    const handledDimensions = new Set();
    const endpoints = line => [nodes.get(line.p1_id), nodes.get(line.p2_id)];
    const signedDistance = (p, line, get = key => nodes.get(key)) => {
        const a = get(line.p1_id), b = get(line.p2_id);
        return ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) / distance(a, b);
    };
    const parallel = (a, b, get = key => nodes.get(key)) => {
        const [p, q, r, s] = [a.p1_id, a.p2_id, b.p1_id, b.p2_id].map(get);
        return Math.abs((q.x - p.x) * (s.y - r.y) - (q.y - p.y) * (s.x - r.x))
            / (distance(p, q) * distance(r, s));
    };
    for (const constraint of offsets) {
        const source = `Constraint ${constraint.id} (Offset)`;
        const keys = constraint.referenceKeys;
        requireSupported(Array.isArray(keys) && keys.length >= 2
            && new Set(keys).size === keys.length, `${source} requires distinct source and dependent entities.`);
        const sources = keys.slice(0, -1).sort().map(model), target = model(keys.at(-1));
        const matching = offsetDimensions.filter(d => sameReferences(d.referenceKeys, keys));
        for (const dimension of matching) handledDimensions.add(dimension.id);
        const driving = matching.filter(d => d.isDriving);
        requireSupported(driving.length <= 1, `${source} has multiple driving gap dimensions.`);
        const requestedGap = driving.length ? driving[0].value : null;
        requireSupported(requestedGap === null || Number.isFinite(requestedGap) && requestedGap > tolerance,
            `${source} requires a positive finite gap.`);
        const round = sources.length === 1 && sources[0].curve && target.curve
            && ["circle", "arc"].includes(sources[0].kind) && sources[0].kind === target.kind;
        if (round) {
            const original = sources[0], a = original.curve, b = target.curve;
            const signed = b.radius - a.radius;
            requireSupported(Math.abs(signed) > tolerance
                && distance(original.center, target.center) <= tolerance,
            `${source} is not an intact concentric offset.`);
            const gap = requestedGap ?? Math.abs(signed), delta = Math.sign(signed) * gap;
            samePoint(original.center, target.center, source);
            relation("difference", { param1: { o_id: a.id, prop: "radius" },
                param2: { o_id: b.id, prop: "radius" }, difference: delta,
                drivingDimensionId: driving[0]?.id }, source,
            get => Math.abs(get(b.id).radius - get(a.id).radius - delta));
            if (original.kind === "arc") {
                for (const prop of ["start_angle", "end_angle"]) {
                    requireSupported(Math.abs(a[prop] - b[prop]) <= tolerance,
                        `${source} arc endpoints do not share the same sweep.`);
                    relation("equal", { param1: { o_id: a.id, prop }, param2: { o_id: b.id, prop } },
                        source, get => Math.abs(get(a.id)[prop] - get(b.id)[prop]));
                }
            }
            if (requestedGap !== null && Math.abs(gap - Math.abs(signed)) > tolerance) {
                fixSource(original.entity.id, `${source} gap edit preserves source`);
            }
            continue;
        }
        requireSupported(sources.every(s => ["line", "polyline"].includes(s.kind))
            && ["line", "polyline"].includes(target.kind),
        `${source} contour representation is not supported.`);
        const sourceEdges = sources.flatMap(shape => shape.lines);
        // A retained joined outline must remain joined during the solve, even
        // when its source was imported as separate lines without Coincident.
        const tips = sources.flatMap(shape => [shape.vertices[0], shape.vertices.at(-1)]);
        for (let i = 0; i < tips.length; i++) for (let j = i + 1; j < tips.length; j++) {
            if (tips[i].id !== tips[j].id && distance(tips[i], tips[j]) <= 1e-8)
                samePoint(tips[i], tips[j], `${source} source contour join`);
        }
        requireSupported(sourceEdges.length === target.lines.length && sourceEdges.length > 0,
            `${source} changed topology or has unmatched edges.`);
        const matches = target.lines.map(edge => {
            const [c, d] = endpoints(edge);
            requireSupported(distance(c, d) > tolerance, `${source} has a collapsed dependent edge.`);
            const midpoint = { x: (c.x + d.x) / 2, y: (c.y + d.y) / 2 };
            const candidates = sourceEdges.filter(line => {
                const [a, b] = endpoints(line), length = distance(a, b);
                if (length <= tolerance || parallel(line, edge) > tolerance) return false;
                const projection = ((midpoint.x - a.x) * (b.x - a.x)
                    + (midpoint.y - a.y) * (b.y - a.y)) / length ** 2;
                return projection >= -tolerance && projection <= 1 + tolerance
                    && Math.abs(signedDistance(midpoint, line)) > tolerance;
            }).map(line => ({ line, edge, signed: signedDistance(midpoint, line) }));
            requireSupported(candidates.length > 0, `${source} has no matching source edge.`);
            return candidates;
        });
        // All edge pairs must agree on one gap. Ambiguous correspondence is refused.
        const gaps = [];
        for (const match of matches[0]) {
            const gap = Math.abs(match.signed);
            if (!gaps.some(existing => Math.abs(existing - gap) <= tolerance)) gaps.push(gap);
        }
        const valid = gaps.map(gap => matches.map(candidates =>
            candidates.filter(m => Math.abs(Math.abs(m.signed) - gap) <= tolerance)))
            .filter(rows => rows.every(row => row.length === 1)
                && new Set(rows.map(row => row[0].line.id)).size === rows.length);
        requireSupported(valid.length === 1, `${source} has ambiguous source-edge correspondence.`);
        const pairs = valid[0].map(row => row[0]);
        const originalGap = Math.abs(pairs[0].signed), gap = requestedGap ?? originalGap;
        for (const { line, edge, signed } of pairs) {
            relation("parallel", { l1_id: line.id, l2_id: edge.id }, source,
                get => parallel(line, edge, get));
            const signedGap = Math.sign(signed) * gap;
            relation("p2l_distance", { p_id: edge.p1_id, l_id: line.id, distance: gap,
                drivingDimensionId: driving[0]?.id }, source,
                get => Math.abs(signedDistance(get(edge.p1_id), line, get) - signedGap));
            // Native point/line distance is unsigned. Retain the selected side
            // with a native directed normal so a solve cannot flip the offset.
            const [a, b] = endpoints(line), tip = nodes.get(edge.p1_id);
            const length = distance(a, b), nx = -(b.y - a.y) / length, ny = (b.x - a.x) / length;
            const foot = point({ x: tip.x - nx * signed, y: tip.y - ny * signed });
            relation("point_on_line_pl", { p_id: foot.id, l_id: line.id }, source,
                get => Math.abs(signedDistance(get(foot.id), line, get)));
            const normal = add({ type: "line", p1_id: foot.id, p2_id: tip.id });
            const angle = Math.sign(signed) * Math.PI / 2;
            relation("l2l_angle_ll", { l1_id: line.id, l2_id: normal.id, angle }, source, get => {
                const p = get(line.p1_id), q = get(line.p2_id), f = get(foot.id), t = get(tip.id);
                const residual = Math.atan2(t.y - f.y, t.x - f.x) - Math.atan2(q.y - p.y, q.x - p.x) - angle;
                return Math.abs(Math.atan2(Math.sin(residual), Math.cos(residual)));
            });
        }
        const closed = target.vertices[0] === target.vertices.at(-1);
        if (!closed) {
            for (const [pair, tip] of [[pairs[0], target.vertices[0]], [pairs.at(-1), target.vertices.at(-1)]]) {
                const candidates = endpoints(pair.line).filter(p => {
                    const [a, b] = endpoints(pair.line), length = distance(a, b);
                    return Math.abs((tip.x - p.x) * (b.x - a.x) + (tip.y - p.y) * (b.y - a.y))
                        / length <= tolerance;
                });
                requireSupported(candidates.length === 1, `${source} open endpoint correspondence is ambiguous.`);
                const p = candidates[0], bridge = add({ type: "line", p1_id: p.id, p2_id: tip.id });
                relation("perpendicular_ll", { l1_id: pair.line.id, l2_id: bridge.id }, source, get => {
                    const a = get(pair.line.p1_id), b = get(pair.line.p2_id);
                    const c = get(p.id), d = get(tip.id);
                    return Math.abs((b.x - a.x) * (d.x - c.x) + (b.y - a.y) * (d.y - c.y))
                        / distance(a, b);
                });
            }
        }
        if (requestedGap !== null && Math.abs(gap - originalGap) > tolerance) {
            for (const shape of sources)
                fixSource(shape.entity.id, `${source} gap edit preserves source`);
        }
    }
    requireSupported(offsetDimensions.every(d => handledDimensions.has(d.id)),
        "Offset dimension has no unique active retained Offset relationship.");
}
