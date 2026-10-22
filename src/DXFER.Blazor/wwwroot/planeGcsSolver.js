import { Algorithm, GcsWrapper, init_planegcs_module, SolveStatus } from "./vendor/planegcs/dist/index.js";
import { mapPlaneGcsOffsets } from "./planeGcsOffsets.js";
import { mapPlaneGcsPatterns } from "./planeGcsPatterns.js";
import { measurePerimeterDistance } from "./perimeterDistance.js";

const radians = Math.PI / 180;
const tolerance = 1e-6;
const lower = value => String(value ?? "").toLowerCase();
const copy = value => structuredClone(value);
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const midpoint = (a, b) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
const angleDelta = value => Math.atan2(Math.sin(value), Math.cos(value));
const supportedEntities = new Set(["point", "line", "circle", "arc", "polyline", "ellipse", "spline", "polygon"]);

class Unsupported extends Error {}

class NativeCurveWrapper extends GcsWrapper {
    push_p_params(id, values, fixed = false) {
        return super.push_p_params(id, values, fixed || this.fixedParameterIds?.has(id) === true);
    }

    sketch_primitive_to_gcs(primitive) {
        if (primitive.type !== "bspline") return super.sketch_primitive_to_gcs(primitive);
        const cached = this.bspline_gcs_cache.get(primitive.id);
        if (cached) return cached;
        const poles = primitive.pole_ids.flatMap(id => {
            const address = this.get_primitive_addr(id);
            return [address, address + 1];
        });
        const vector = values => {
            const result = new this.module.IntVector();
            values.forEach(value => result.push_back(value));
            return result;
        };
        const vectors = [
            vector(poles),
            vector(primitive.weights.map(value => this.gcs.push_p_param(value, true))),
            vector(primitive.knots.map(value => this.gcs.push_p_param(value, true))),
            vector(primitive.mult)
        ];
        try {
            // The upstream wrapper allocates detached endpoint unknowns. Clamped
            // splines have endpoint poles, so pass those exact parameter addresses.
            const spline = this.gcs.make_bspline(poles[0], poles[1], poles.at(-2), poles.at(-1),
                ...vectors, primitive.degree, false);
            this.bspline_gcs_cache.set(primitive.id, spline);
            return spline;
        } finally {
            vectors.forEach(value => value.delete());
        }
    }

    clear_data() {
        const splines = [...this.bspline_gcs_cache.values()];
        super.clear_data();
        splines.forEach(spline => spline.delete());
    }

    destroy_gcs_module() {
        this.clear_data();
        this.gcs.delete();
    }
}

function requireSupported(condition, message) {
    if (!condition) throw new Unsupported(message);
}

function finite(value, context) {
    requireSupported(typeof value === "number" && Number.isFinite(value), `${context} must be finite.`);
    return value;
}

function pointValue(value, context) {
    requireSupported(value && typeof value === "object", `${context} requires a point.`);
    return { x: finite(value.x, context), y: finite(value.y, context) };
}

function array(value, context) {
    requireSupported(Array.isArray(value), `${context} must be an array.`);
    return value;
}

function unionById(existing, updates, context) {
    const merged = new Map();
    for (const [items, replacing] of [[existing ?? [], false], [updates ?? [], true]]) {
        const seen = new Set();
        for (const item of array(items, context)) {
            requireSupported(item && typeof item.id === "string" && item.id.length > 0,
                `${context} requires nonempty IDs.`);
            requireSupported(!seen.has(item.id) && (replacing || !merged.has(item.id)),
                `${context} contains duplicate ID ${item.id}.`);
            seen.add(item.id);
            merged.set(item.id, item);
        }
    }
    return [...merged.values()];
}

function solveNative(wrapper, primitives) {
    const preferences = primitives.filter(primitive => primitive.temporary === true);
    wrapper.push_primitives_and_params(primitives.filter(primitive => primitive.temporary !== true));
    wrapper.set_max_iterations(200);
    if (preferences.length) {
        // Diagnose hard intent before adding negative-tagged goals. Native
        // redundancy checks must not mistake pointer preferences for conflicts.
        const hardStatus = wrapper.solve(Algorithm.LevenbergMarquardt);
        if (wrapper.get_gcs_conflicting_constraints().length) return hardStatus;
        // Underdetermined geometry can need stability goals to converge.
        // Only the final solve and validation may publish geometry.
        if ([SolveStatus.Success, SolveStatus.Converged].includes(hardStatus))
            wrapper.apply_solution();
        wrapper.push_primitives_and_params(preferences);
    }
    return wrapper.solve(Algorithm.LevenbergMarquardt);
}

// Wire contract: {document:{entities,constraints,dimensions,metadata?,hasPatterns?},
// constraints?,dimensions?,fixedReferenceKeys?,initialGuesses?:[{referenceKey,point}],
// drag?:{key,start,end,constrain?,temporarySnapIntent?}}. Updates replace by ID.
// One mapper is used by the browser and the Node host. Only initialization is async.
export async function createPlaneGcsSolver({ wasmBinary, wasmPath } = {}) {
    const options = {};
    if (wasmBinary !== undefined) options.wasmBinary = wasmBinary;
    if (wasmPath !== undefined) options.locateFile = () => String(wasmPath);
    const module = await init_planegcs_module(options);
    let wrapper = new NativeCurveWrapper(new module.GcsSystem(), module);
    let disposed = false;
    return {
        solve(request) {
            const original = copy(request?.document?.entities ?? []);
            let redundantDrivingDimensionIds = [];
            const refuse = (status, diagnostics) => ({ status, entities: original, diagnostics, engine: "PlaneGCS",
                redundantDrivingDimensionIds });
            if (disposed) return refuse("failed", ["PlaneGCS solver has been disposed."]);
            try {
                // Each request is a complete snapshot. clear_data alone retains
                // native solve state that can change repeated-snapshot results.
                wrapper.destroy_gcs_module();
                wrapper = new NativeCurveWrapper(new module.GcsSystem(), module);
                const mapped = mapRequest(request);
                if (request.drag) mapped.tryPatternSeed();
                wrapper.fixedParameterIds = mapped.fixedParameterIds;
                const status = solveNative(wrapper, mapped.primitives);
                const conflicts = wrapper.get_gcs_conflicting_constraints();
                const redundant = wrapper.get_gcs_redundant_constraints();
                const partiallyRedundant = wrapper.get_gcs_partially_redundant_constraints();
                redundantDrivingDimensionIds = [...new Set([...redundant, ...partiallyRedundant]
                    .map(id => mapped.relationDimensions.get(id)).filter(id => id !== undefined))];
                const describe = (prefix, ids) => [...new Set(ids.map(id => `${prefix}: ${mapped.sources.get(id) ?? id}.`))];
                const diagnostics = [
                    `PlaneGCS mapped-system degrees of freedom: ${wrapper.gcs.dof()}.`,
                    ...describe("Redundant equation", redundant),
                    ...describe("Partially redundant equation", partiallyRedundant)
                ];
                if (![SolveStatus.Success, SolveStatus.Converged].includes(status) || conflicts.length) {
                    return refuse("failed", [`PlaneGCS did not satisfy the system (status ${status}).`,
                        ...describe("Conflicting intent", conflicts), ...diagnostics]);
                }
                wrapper.apply_solution();
                const get = id => wrapper.sketch_index.get_primitive(id);
                const errors = mapped.validate(get);
                if (errors.length) return refuse("failed", [...errors, ...diagnostics]);
                const attempts = array(request.attemptedDrivingDimensionIds ?? [], "attemptedDrivingDimensionIds");
                requireSupported(attempts.every(value => typeof value === "string" && value.length > 0),
                    "Attempted driving dimension IDs must be nonempty strings.");
                // Native redundant tags name one selected basis, not necessarily
                // the edited intent. Compare native ranks at the same solution
                // with that intent's equations removed, retaining all unknowns.
                const reported = new Set(redundantDrivingDimensionIds);
                for (const dimensionId of new Set(attempts)) {
                    if (mapped.discreteDimensionIds.has(dimensionId)) {
                        diagnostics.push(`Validated discrete topology dimension ${dimensionId}; not a continuous rank equation.`);
                        continue;
                    }
                    const owned = new Set([...mapped.relationDimensions]
                        .filter(([, owner]) => owner === dimensionId).map(([id]) => id));
                    requireSupported(owned.size > 0, `Attempted dimension ${dimensionId} has no mapped driving equations.`);
                    const probe = new NativeCurveWrapper(new module.GcsSystem(), module);
                    try {
                        probe.fixedParameterIds = mapped.fixedParameterIds;
                        const probeStatus = solveNative(probe, copy(mapped.primitives
                            .filter(primitive => !owned.has(primitive.id))
                            .map(primitive => get(primitive.id))));
                        if (![SolveStatus.Success, SolveStatus.Converged].includes(probeStatus)
                            || probe.get_gcs_conflicting_constraints().length) {
                            return refuse("failed", [`PlaneGCS could not verify native rank for dimension ${dimensionId}.`, ...diagnostics]);
                        }
                        const fullDof = wrapper.gcs.dof(), withoutDof = probe.gcs.dof();
                        if (withoutDof < fullDof) {
                            return refuse("failed", [`PlaneGCS returned inconsistent native rank for dimension ${dimensionId}.`, ...diagnostics]);
                        }
                        reported.delete(dimensionId);
                        if (withoutDof === fullDof) reported.add(dimensionId);
                        diagnostics.push(`Native rank check ${dimensionId}: DOF ${fullDof}, without intent ${withoutDof}.`);
                    } finally {
                        probe.destroy_gcs_module();
                    }
                }
                redundantDrivingDimensionIds = [...reported];
                return { status: "solved", entities: mapped.entities(get), diagnostics, engine: "PlaneGCS",
                    redundantDrivingDimensionIds };
            } catch (error) {
                return refuse(error instanceof Unsupported ? "unsupported" : "failed",
                    [error instanceof Error ? error.message : "PlaneGCS could not solve this request."]);
            } finally {
                wrapper.clear_data();
            }
        },
        dispose() {
            if (!disposed) {
                wrapper.destroy_gcs_module();
                disposed = true;
            }
        }
    };
}

function mapRequest(request) {
    const document = request?.document;
    requireSupported(document && typeof document === "object", "A full document snapshot is required.");
    const patterns = document.metadata?.patterns ?? document.metadata?.Patterns ?? [];
    requireSupported(!document.hasPatterns || patterns.length > 0, "Pattern metadata is missing from the document snapshot.");
    const originals = array(document.entities, "document.entities");
    const byId = new Map();
    for (const entity of originals) {
        requireSupported(entity && typeof entity.id === "string" && entity.id.length && !byId.has(entity.id)
            && entity.id !== "$origin", "Entity IDs must be unique, nonempty, and not reserved.");
        byId.set(entity.id, entity);
    }
    const constraints = unionById(document.constraints, request.constraints, "Constraints");
    const dimensions = unionById(document.dimensions, request.dimensions, "Dimensions");
    const ellipseAxisModels = new Set();
    const ellipseAxisPatternMembers = new Set();
    const ellipseSymmetryParents = new Map();
    const ellipseSymmetryRoot = id => {
        if (!ellipseSymmetryParents.has(id)) ellipseSymmetryParents.set(id, id);
        while (ellipseSymmetryParents.get(id) !== id) id = ellipseSymmetryParents.get(id);
        return id;
    };
    function referenceEntity(key) {
        if (typeof key !== "string") return undefined;
        const raw = key.trim();
        if (byId.has(raw)) return raw;
        const marker = raw.indexOf("|point|"), colon = raw.lastIndexOf(":");
        let base = marker >= 0 ? raw.slice(0, marker) : colon > 0 ? raw.slice(0, colon) : raw;
        const segment = base.indexOf("|segment|");
        if (segment >= 0) base = base.slice(0, segment);
        return byId.has(base) ? base : undefined;
    }
    const references = [...constraints, ...dimensions].flatMap(item => array(item.referenceKeys ?? [], "referenceKeys"))
        .concat(array(request.fixedReferenceKeys ?? [], "fixedReferenceKeys"),
            array(request.initialGuesses ?? [], "initialGuesses").map(guess => guess.referenceKey));
    if (request.drag) references.push(request.drag.key ?? request.drag.selectionKey);
    const draggedEntity = referenceEntity(request.drag?.key ?? request.drag?.selectionKey);
    const draggedEllipseFamily = new Set(draggedEntity ? [draggedEntity] : []);
    const focalReferences = new Set(references.filter(key => typeof key === "string"
        && /(?::focus1$|\|point\|focus1\|)/i.test(key.trim())).map(referenceEntity));
    for (const entity of originals) {
        if (lower(entity.kind) !== "ellipse") continue;
        const gripKey = request.drag?.key ?? request.drag?.selectionKey;
        const axisDrag = referenceEntity(gripKey) === entity.id
            && /(?::|\|point\|)(?:major-|minor-|quadrant-|start(?:$|\|)|end(?:$|\|))/i.test(gripKey);
        const axisDimension = dimensions.some(d => d.isDriving === true && (d.referenceKeys ?? []).some(key =>
            referenceEntity(key) === entity.id && /(?::|\|point\|)(?:major-|minor-|quadrant-)/i.test(key)));
        if (Math.abs(entity.minorRadiusRatio - 1) < 1e-8 || (!focalReferences.has(entity.id) && (axisDrag || axisDimension)))
            ellipseAxisModels.add(entity.id);
    }
    const fullEllipse = entity => Math.abs(angleDelta(((entity.endAngleDegrees ?? 360)
        - (entity.startAngleDegrees ?? 0)) * radians)) < 1e-10;
    for (const constraint of constraints) {
        if (lower(constraint.kind) !== "symmetric" || lower(constraint.state) === "suppressed"
            || constraint.referenceKeys?.length !== 3) continue;
        const [a, b] = constraint.referenceKeys.map(key => byId.get(key));
        if (lower(a?.kind) !== "ellipse" || lower(b?.kind) !== "ellipse" || !fullEllipse(a) || !fullEllipse(b)
            || focalReferences.has(a.id) || focalReferences.has(b.id)) continue;
        ellipseAxisModels.add(a.id); ellipseAxisModels.add(b.id);
        const rootA = ellipseSymmetryRoot(a.id), rootB = ellipseSymmetryRoot(b.id);
        if (rootA !== rootB) ellipseSymmetryParents.set(rootB, rootA);
    }
    // Reflection already preserves orthogonal axes. Keep one independent frame
    // equation per connected family, even when symmetry relations form a cycle.
    const ellipseSymmetryMembers = new Set([...ellipseSymmetryParents.keys()].filter(id => ellipseSymmetryRoot(id) !== id));
    // A pattern family must use the same independent parameters on both sides.
    // Settings and ownership are validated by the pattern mapper below.
    if (Array.isArray(patterns)) {
        for (let changed = true; changed;) {
            changed = false;
            for (const pattern of patterns) for (const member of array(pattern?.members ?? [], "pattern.members")) {
                if (draggedEllipseFamily.has(member.sourceEntityId) || draggedEllipseFamily.has(member.entityId)) {
                    for (const key of [member.sourceEntityId, member.entityId]) {
                        if (!draggedEllipseFamily.has(key)) { draggedEllipseFamily.add(key); changed = true; }
                    }
                }
                if (!ellipseAxisModels.has(member.sourceEntityId) && !ellipseAxisModels.has(member.entityId)) continue;
                for (const key of [member.sourceEntityId, member.entityId]) {
                    if (lower(byId.get(key)?.kind) === "ellipse" && !ellipseAxisModels.has(key)) {
                        ellipseAxisModels.add(key); changed = true;
                    }
                }
            }
        }
        for (const pattern of patterns) for (const member of pattern.members ?? []) {
            if (!ellipseAxisModels.has(member.sourceEntityId)) continue;
            const source = byId.get(member.sourceEntityId), target = byId.get(member.entityId);
            if (lower(target?.kind) !== "ellipse") continue;
            ellipseAxisPatternMembers.add(member.entityId);
            // This representation preserves the document's eccentric parameters;
            // unlike focal arc rules it does not introduce angular unknowns.
            for (const [field, fallback] of [["startAngleDegrees", 0], ["endAngleDegrees", 360]]) {
                const a = finite(source[field] ?? fallback, field), b = finite(target[field] ?? fallback, field);
                requireSupported(Math.abs(angleDelta((a - b) * radians)) < 1e-10,
                    "Pattern axis-model ellipse eccentric parameters differ from its seed.");
            }
        }
    }
    const geometries = [];
    const relations = [];
    const sources = new Map();
    const relationDimensions = new Map();
    const discreteDimensionIds = new Set();
    let dimensionOwner = null;
    const nodes = new Map();
    const initialPoints = new Map();
    const models = new Map();
    const wholeEntityFixes = new Map();
    const fixedShapes = new Set();
    const axisLinePreferences = new Map();
    const checks = [];
    let sequence = 0;
    const id = () => `g${++sequence}`;
    const add = primitive => {
        const node = { id: id(), ...primitive };
        nodes.set(node.id, node);
        geometries.push(node);
        return node;
    };
    const point = value => {
        const coordinates = pointValue(value, "Geometry");
        const node = add({ type: "point", ...coordinates, fixed: false });
        initialPoints.set(node.id, coordinates);
        return node;
    };
    const relation = (type, values, source, check, temporary = false) => {
        const item = { id: id(), type, ...values, temporary };
        relations.push(item);
        sources.set(item.id, source);
        const dimensionId = values.drivingDimensionId ?? (dimensionOwner?.source === source ? dimensionOwner.id : null);
        if (dimensionId !== null && dimensionId !== undefined && !temporary) relationDimensions.set(item.id, dimensionId);
        if (!temporary && check) checks.push({ source, check });
        return item;
    };
    const samePoint = (a, b, source, temporary = false) =>
        relation("p2p_coincident", { p1_id: a.id, p2_id: b.id }, source,
            get => distance(get(a.id), get(b.id)), temporary);
    const equalParam = (node, prop, value, source) =>
        relation("equal", { param1: { o_id: node.id, prop }, param2: value }, source,
            get => Math.abs(get(node.id)[prop] - value));
    const fixedPoints = new Set();
    function fixPoint(node, source) {
        if (fixedPoints.has(node.id)) return;
        const before = initialPoints.get(node.id);
        Object.assign(node, before);
        node.fixed = true;
        fixedPoints.add(node.id);
        checks.push({ source, check: get => distance(get(node.id), before) });
    }
    const fixedParameterIds = new Set();
    function fixParameters(node, props, source) {
        fixedParameterIds.add(node.id);
        for (const prop of props) {
            const value = node[prop];
            checks.push({ source, check: get => get(node.id)[prop] === value ? 0 : Infinity });
        }
    }
    function fixShape(shape, source) {
        if (fixedShapes.has(shape.entity.id)) return;
        fixedShapes.add(shape.entity.id);
        // Ellipse endpoints are derived by native arc rules. Fix its independent
        // focal parameters, not both those parameters and the derived endpoints.
        for (const p of shape.ellipse ? [shape.center, shape.focus] : shape.points) fixPoint(p, source);
        if (shape.ellipse) {
            fixParameters(shape.ellipse, shape.ellipse.type === "arc_of_ellipse"
                ? ["radmin", "start_angle", "end_angle"] : ["radmin"], source);
        }
        if (shape.curve) {
            fixParameters(shape.curve, shape.kind === "arc"
                ? ["radius", "start_angle", "end_angle"] : ["radius"], source);
        }
        if (shape.polygonGuide) fixParameters(shape.polygonGuide, ["radius"], source);
    }
    function model(entityId) {
        if (models.has(entityId)) return models.get(entityId);
        const entity = byId.get(entityId);
        requireSupported(entity, `Unknown entity reference: ${entityId}.`);
        const kind = lower(entity.kind);
        requireSupported(supportedEntities.has(kind), `Constrained entity ${entityId} has unsupported kind ${entity.kind}.`);
        const shape = { entity, kind, points: [], lines: [], midpoints: new Map() };
        models.set(entityId, shape);
        if (kind === "point" || kind === "line" || kind === "polyline") {
            const input = array(entity.points, `${entityId}.points`);
            requireSupported(kind === "point" ? input.length === 1 : kind === "line" ? input.length === 2 : input.length >= 2,
                `Invalid ${kind} point count for ${entityId}.`);
            requireSupported(!entity.bulges?.some(value => value !== 0),
                `Curved polyline segments are not mapped: ${entityId}.`);
            // Native closed polylines repeat their first vertex; keep that shared identity.
            const closed = kind === "polyline" && input.length > 2 && distance(input[0], input.at(-1)) === 0;
            shape.vertices = [];
            for (let index = 0; index < input.length; index++) {
                shape.vertices.push(closed && index === input.length - 1 ? shape.vertices[0] : point(input[index]));
            }
            shape.points = [...new Set(shape.vertices)];
            if (kind !== "point") {
                const count = input.length - 1;
                for (let index = 0; index < count; index++) {
                    shape.lines.push(add({ type: "line", p1_id: shape.vertices[index].id, p2_id: shape.vertices[index + 1].id }));
                }
                requireSupported(!entity.isClosed || shape.vertices[0] === shape.vertices.at(-1),
                    `Closed polyline ${entityId} must repeat its first vertex.`);
            }
        } else if (kind === "spline") {
            requireSupported(!entity.periodic && !entity.isPeriodic, `${entityId}: periodic B-splines are not mapped.`);
            const controls = array(entity.controlPoints, `${entityId}.controlPoints`);
            const degree = entity.splineDegree;
            requireSupported(Number.isInteger(degree) && degree >= 1 && controls.length > degree,
                `${entityId}: invalid native B-spline degree/control count.`);
            const knots = array(entity.splineKnots, `${entityId}.splineKnots`);
            const weights = array(entity.splineWeights, `${entityId}.splineWeights`);
            requireSupported(knots.length === controls.length + degree + 1 && weights.length === controls.length
                && knots.every(Number.isFinite) && weights.every(value => Number.isFinite(value) && value > 0)
                && knots.every((value, index) => !index || value >= knots[index - 1]),
            `${entityId}: invalid native B-spline knots or weights.`);
            const fits = array(entity.fitPoints ?? [], `${entityId}.fitPoints`);
            requireSupported(fits.length !== 1, `${entityId}: a Hermite spline requires at least two fit points.`);
            shape.hermite = fits.length >= 2;
            shape.handles = {};
            for (const name of ["startTangentHandle", "endTangentHandle"]) {
                if (entity[name] != null) shape.handles[name] = point(entity[name]);
            }
            if (shape.hermite) {
                // Fit points/handles are the authoritative Hermite representation
                // in SplineEntity. Keep them exact; never substitute sampled poles.
                shape.fitPoints = fits.map(point);
                shape.poles = controls.map((value, index) => {
                    const coordinates = pointValue(value, `${entityId}.controlPoints`);
                    return fits.length === controls.length && distance(coordinates, fits[index]) === 0
                        ? shape.fitPoints[index] : point(coordinates);
                });
                shape.start = shape.fitPoints[0];
                shape.end = shape.fitPoints.at(-1);
                shape.points = [...new Set([...shape.fitPoints, ...shape.poles, ...Object.values(shape.handles)])];
            } else {
                const first = knots[degree], last = knots[controls.length];
                const unique = [], mult = [];
                for (const value of knots) {
                    if (unique.length && unique.at(-1) === value) mult[mult.length - 1]++;
                    else { unique.push(value); mult.push(1); }
                }
                requireSupported(last > first && unique[0] === first && unique.at(-1) === last
                    && mult[0] === degree + 1 && mult.at(-1) === degree + 1
                    && mult.slice(1, -1).every(value => value <= degree),
                `${entityId}: only continuous, nonperiodic, clamped native B-splines are mapped.`);
                shape.poles = controls.map(point);
                shape.points = [...shape.poles];
                shape.spline = add({ type: "bspline", pole_ids: shape.poles.map(p => p.id),
                    weights: [...weights], knots: unique, mult, degree, periodic: false });
                shape.start = point(controls[0]);
                shape.end = point(controls.at(-1));
                for (const [p, u, pole] of [[shape.start, first, shape.poles[0]], [shape.end, last, shape.poles.at(-1)]]) {
                    relation("point_on_bspline", { p_id: p.id, b_id: shape.spline.id, pointparam: u },
                        `${entityId} native spline endpoint`, get => distance(get(p.id), get(pole.id)));
                }
                shape.points.push(...Object.values(shape.handles));
            }
        } else if (kind === "polygon") {
            requireSupported(Number.isInteger(entity.sideCount) && entity.sideCount >= 3 && entity.sideCount <= 64
                && typeof entity.circumscribed === "boolean", `${entityId}: invalid regular polygon topology.`);
            const radius = finite(entity.radius, `${entityId}.radius`);
            requireSupported(radius > tolerance, `${entityId}: polygon guide radius must be positive.`);
            const angle = finite(entity.rotationAngleDegrees, `${entityId}.rotationAngleDegrees`) * radians;
            shape.center = point(entity.center);
            shape.guide = point({ x: shape.center.x + radius * Math.cos(angle), y: shape.center.y + radius * Math.sin(angle) });
            shape.points = [shape.center, shape.guide];
            shape.polygonGuide = add({ type: "circle", c_id: shape.center.id, radius });
            relation("point_on_circle", { p_id: shape.guide.id, c_id: shape.polygonGuide.id },
                `${entityId} polygon guide radius`, get => Math.abs(distance(get(shape.center.id), get(shape.guide.id))
                    - get(shape.polygonGuide.id).radius));
            for (const prop of ["x", "y"])
                relation("difference", { param1: { o_id: shape.center.id, prop }, param2: { o_id: shape.guide.id, prop },
                    difference: shape.guide[prop] - shape.center[prop], scale: 1e-4 },
                `${entityId} polygon guide stability`, null, true);
            softParam(shape.polygonGuide, "radius", radius, `${entityId} polygon radius stability`, 1e-4);
        } else if (kind === "ellipse") {
            const center = point(entity.center);
            const major = pointValue(entity.majorAxisEndPoint, `${entityId}.majorAxisEndPoint`);
            const a = Math.hypot(major.x, major.y), ratio = finite(entity.minorRadiusRatio, `${entityId}.minorRadiusRatio`);
            requireSupported(a > tolerance && a * ratio > tolerance && Number.isFinite(a * ratio),
                `${entityId}: ellipse mapping requires nonzero axes and a positive finite ratio.`);
            shape.center = center;
            shape.ellipsePoints = new Map();
            if (ellipseAxisModels.has(entityId)) {
                requireSupported(!focalReferences.has(entityId),
                    `${entityId}: focus references at equal axes or across an axis-model pattern are unsupported.`);
                const majorEnd = point({ x: center.x + major.x, y: center.y + major.y });
                const minorEnd = point({ x: center.x - major.y * ratio, y: center.y + major.x * ratio });
                const majorLine = add({ type: "line", p1_id: center.id, p2_id: majorEnd.id });
                const minorLine = add({ type: "line", p1_id: center.id, p2_id: minorEnd.id });
                shape.ellipseAxes = { majorEnd, minorEnd, majorLine, minorLine };
                shape.points = [center, majorEnd, minorEnd];
                finite(entity.startAngleDegrees ?? 0, `${entityId}.startAngleDegrees`);
                finite(entity.endAngleDegrees ?? 360, `${entityId}.endAngleDegrees`);
                const axisResidual = get => {
                    const c = get(center.id), u = get(majorEnd.id), v = get(minorEnd.id);
                    return Math.abs((u.x - c.x) * (v.x - c.x) + (u.y - c.y) * (v.y - c.y))
                        / Math.max(distance(c, u) * distance(c, v), tolerance);
                };
                // Validated rigid pattern equations already preserve the seed's
                // perpendicular frame. Duplicating it confuses native initial rank
                // when a gesture seeds the source ahead of its coupled members.
                if (!ellipseAxisPatternMembers.has(entityId) && !ellipseSymmetryMembers.has(entityId))
                    relation("perpendicular_ll", { l1_id: majorLine.id, l2_id: minorLine.id },
                        `${entityId} ellipse axes`, axisResidual);
                else checks.push({ source: `${entityId} patterned ellipse axes`, check: axisResidual });
                checks.push({ source: `${entityId} directed ellipse frame`, check: get => {
                    const c = get(center.id), u = get(majorEnd.id), v = get(minorEnd.id);
                    return (u.x - c.x) * (v.y - c.y) - (u.y - c.y) * (v.x - c.x) > 0 ? 0 : Infinity;
                } });
                if (!draggedEllipseFamily.has(entityId))
                    for (const p of shape.points) softPoint(p, p, `${entityId} ellipse edit stability`, 1e-4);
            } else {
                const swapped = ratio > 1;
                const nativeMajor = swapped ? { x: -major.y * ratio, y: major.x * ratio } : major;
                const nativeRatio = swapped ? 1 / ratio : ratio;
                const b = swapped ? a : a * ratio, focalRatio = Math.sqrt(1 - nativeRatio * nativeRatio);
                shape.ellipseAxisSwapped = swapped;
                shape.ellipseParameterShift = swapped ? -Math.PI / 2 : 0;
                shape.focus = point({ x: center.x + nativeMajor.x * focalRatio, y: center.y + nativeMajor.y * focalRatio });
                shape.points = [center, shape.focus];
                const start = finite(entity.startAngleDegrees ?? 0, `${entityId}.startAngleDegrees`) * radians;
                const end = finite(entity.endAngleDegrees ?? 360, `${entityId}.endAngleDegrees`) * radians;
                let sweep = (end - start) % (2 * Math.PI);
                if (sweep <= 0) sweep += 2 * Math.PI;
                const full = Math.abs(sweep - 2 * Math.PI) <= 1e-10;
                const params = { type: full ? "ellipse" : "arc_of_ellipse", c_id: center.id,
                    focus1_id: shape.focus.id, radmin: b };
                if (!full) {
                    const at = u => ({ x: center.x + major.x * Math.cos(u) - major.y * ratio * Math.sin(u),
                        y: center.y + major.y * Math.cos(u) + major.x * ratio * Math.sin(u) });
                    shape.start = point(at(start));
                    shape.end = point(at(end));
                    shape.points.push(shape.start, shape.end);
                    Object.assign(params, { start_id: shape.start.id, end_id: shape.end.id,
                        start_angle: start + shape.ellipseParameterShift, end_angle: start + sweep + shape.ellipseParameterShift });
                }
                shape.ellipse = add(params);
                if (!full) {
                    relation("arc_of_ellipse_rules", { a_id: shape.ellipse.id }, `${entityId} native ellipse endpoints`,
                        get => Math.max(distance(get(shape.start.id), ellipseAt(shape, get(shape.ellipse.id).start_angle, get)),
                            distance(get(shape.end.id), ellipseAt(shape, get(shape.ellipse.id).end_angle, get))));
                }
            }
        } else {
            const center = point(entity.center);
            const radius = finite(entity.radius, `${entityId}.radius`);
            requireSupported(radius > 0, `${entityId} requires a positive radius.`);
            shape.points = [center];
            shape.center = center;
            const params = { type: kind, c_id: center.id, radius };
            if (kind === "arc") {
                const start = finite(entity.startAngleDegrees, `${entityId}.startAngleDegrees`) * radians;
                let sweep = (finite(entity.endAngleDegrees, `${entityId}.endAngleDegrees`) * radians - start) % (2 * Math.PI);
                if (sweep <= 0) sweep += 2 * Math.PI;
                const end = start + sweep;
                const at = angle => point({ x: center.x + radius * Math.cos(angle), y: center.y + radius * Math.sin(angle) });
                shape.start = at(start);
                shape.end = at(end);
                shape.points.push(shape.start, shape.end);
                Object.assign(params, { start_angle: start, end_angle: end, start_id: shape.start.id, end_id: shape.end.id });
            }
            shape.curve = add(params);
            if (kind === "arc") {
                relation("arc_rules", { a_id: shape.curve.id }, `${entityId} arc endpoints`, get => {
                    const arc = get(shape.curve.id), center = get(arc.c_id);
                    const at = angle => ({ x: center.x + arc.radius * Math.cos(angle), y: center.y + arc.radius * Math.sin(angle) });
                    return Math.max(distance(get(arc.start_id), at(arc.start_angle)), distance(get(arc.end_id), at(arc.end_angle)));
                });
            }
        }
        if (entity.isLocked || entity.isHidden) fixShape(shape, `${entityId} locked/hidden geometry`);
        if (wholeEntityFixes.has(entityId)) fixShape(shape, wholeEntityFixes.get(entityId));
        return shape;
    }
    function ellipseAt(shape, u, get = key => nodes.get(key)) {
        const ellipse = get(shape.ellipse.id), center = get(ellipse.c_id), focus = get(ellipse.focus1_id);
        const f = distance(center, focus), a = Math.hypot(f, ellipse.radmin);
        const ux = (focus.x - center.x) / f, uy = (focus.y - center.y) / f;
        return { x: center.x + a * ux * Math.cos(u) - ellipse.radmin * uy * Math.sin(u),
            y: center.y + a * uy * Math.cos(u) + ellipse.radmin * ux * Math.sin(u) };
    }
    function ellipsePoint(shape, parameter) {
        if (shape.ellipsePoints.has(parameter)) return shape.ellipsePoints.get(parameter);
        if (shape.ellipseAxes) {
            const { majorEnd, minorEnd } = shape.ellipseAxes;
            const c = Math.cos(parameter), s = Math.sin(parameter);
            let p;
            if (Math.abs(s) < 1e-12 && c > 0) p = majorEnd;
            else if (Math.abs(c) < 1e-12 && s > 0) p = minorEnd;
            else {
                const center = shape.center;
                p = point({ x: center.x + c * (majorEnd.x - center.x) + s * (minorEnd.x - center.x),
                    y: center.y + c * (majorEnd.y - center.y) + s * (minorEnd.y - center.y) });
                const source = `${shape.entity.id} exact ellipse parameter ${parameter}`;
                if (Math.abs(s) < 1e-12) symmetric(majorEnd, p, center, source);
                else if (Math.abs(c) < 1e-12) symmetric(minorEnd, p, center, source);
                else {
                    // Fixed-slope incidence is native linear algebra; unlike the
                    // pinned Proportional primitive it works with both operands free.
                    const origin = point({ x: 0, y: 0 });
                    const slopeC = point({ x: 1, y: c }), slopeS = point({ x: 1, y: s });
                    for (const fixed of [origin, slopeC, slopeS]) fixed.fixed = true;
                    for (const prop of ["x", "y"]) {
                        const u = point({ x: majorEnd[prop] - center[prop], y: c * (majorEnd[prop] - center[prop]) });
                        const v = point({ x: minorEnd[prop] - center[prop], y: s * (minorEnd[prop] - center[prop]) });
                        const sum = point({ x: u.y + v.y, y: 0 }); equalParam(sum, "y", 0, source);
                        const param = (node, field) => ({ o_id: node.id, prop: field });
                        for (const [axis, term, slope] of [[majorEnd, u, slopeC], [minorEnd, v, slopeS]]) {
                            relation("difference", { param1: param(center, prop), param2: param(axis, prop),
                                difference: param(term, "x") }, source);
                            relation("point_on_line_ppp", { p_id: term.id, lp1_id: origin.id, lp2_id: slope.id }, source);
                        }
                        relation("difference", { param1: param(u, "y"), param2: param(sum, "x"),
                            difference: param(v, "y") }, source);
                        relation("difference", { param1: param(center, prop), param2: param(p, prop),
                            difference: param(sum, "x") }, source);
                    }
                }
                checks.push({ source, check: get => {
                    const center = get(shape.center.id), u = get(majorEnd.id), v = get(minorEnd.id);
                    return distance(get(p.id), { x: center.x + c * (u.x - center.x) + s * (v.x - center.x),
                        y: center.y + c * (u.y - center.y) + s * (v.y - center.y) });
                } });
            }
            shape.ellipsePoints.set(parameter, p);
            return p;
        }
        const nativeParameter = parameter + shape.ellipseParameterShift;
        const p = point(ellipseAt(shape, nativeParameter));
        shape.ellipsePoints.set(parameter, p);
        relation("curve_value", { p_id: p.id, a_id: shape.ellipse.id, u: nativeParameter },
            `${shape.entity.id} native ellipse parameter ${parameter}`, get => distance(get(p.id), ellipseAt(shape, nativeParameter, get)));
        return p;
    }
    function splineTangentPoint(shape, atStart) {
        if (shape.hermite) return shape.handles[atStart ? "startTangentHandle" : "endTangentHandle"];
        const endpoint = atStart ? shape.poles[0] : shape.poles.at(-1);
        const adjacent = atStart ? shape.poles[1] : shape.poles.at(-2);
        requireSupported(shape.entity.splineDegree >= 2 && distance(endpoint, adjacent) > tolerance,
            `${shape.entity.id}: this endpoint has no distinct native control tangent handle.`);
        shape.tangentPoints ??= new Map();
        shape.tangentHalves ??= new Map();
        if (!shape.tangentPoints.has(atStart)) {
            const half = point(midpoint(endpoint, adjacent));
            const handle = point(midpoint(endpoint, half));
            symmetric(endpoint, adjacent, half, `${shape.entity.id} tangent half-control`);
            symmetric(endpoint, half, handle, `${shape.entity.id} tangent quarter-control`);
            shape.tangentPoints.set(atStart, handle);
            shape.tangentHalves.set(atStart, half);
        }
        return shape.tangentPoints.get(atStart);
    }
    function midpointNode(line) {
        const shapeKey = `mid:${line.id}`;
        if (nodes.has(shapeKey)) return nodes.get(shapeKey);
        const p = point(midpoint(nodes.get(line.p1_id), nodes.get(line.p2_id)));
        nodes.set(shapeKey, p);
        symmetric(nodes.get(line.p1_id), nodes.get(line.p2_id), p, `Derived midpoint ${line.id}`);
        return p;
    }
    function symmetric(a, b, middle, source, temporary = false) {
        relation("p2p_symmetric_ppp", { p1_id: a.id, p2_id: b.id, p_id: middle.id }, source,
            get => distance(get(middle.id), midpoint(get(a.id), get(b.id))), temporary);
    }
    let origin;
    function resolve(key, dragging = false) {
        requireSupported(typeof key === "string" && key.length > 0, "Reference keys must be nonempty strings.");
        const raw = key.trim();
        if (raw === "$origin:center" || raw === "$origin|point|center|0|0") {
            if (!origin) {
                origin = point({ x: 0, y: 0 });
                fixPoint(origin, "Intrinsic origin");
            }
            return { point: origin };
        }
        let base = raw, target = "";
        const marker = raw.indexOf("|point|");
        if (marker >= 0) {
            base = raw.slice(0, marker);
            const parts = raw.slice(marker + 7).split("|");
            requireSupported(parts.length === 3 && parts[1].trim() !== "" && parts[2].trim() !== ""
                && Number.isFinite(Number(parts[1])) && Number.isFinite(Number(parts[2])),
            `Invalid canvas point key: ${raw}.`);
            target = lower(parts[0]);
        } else if (!byId.has(raw)) {
            const colon = raw.lastIndexOf(":");
            if (colon > 0) {
                base = raw.slice(0, colon);
                target = lower(raw.slice(colon + 1));
            }
        }
        let segment;
        const segmentAt = base.indexOf("|segment|");
        if (segmentAt >= 0) {
            const indexText = base.slice(segmentAt + 9);
            requireSupported(/^\d+$/.test(indexText), `Invalid segment key: ${raw}.`);
            segment = Number(indexText);
            base = base.slice(0, segmentAt);
        }
        const shape = model(base);
        let line = shape.kind === "line" ? shape.lines[0] : null;
        if (segment !== undefined) {
            requireSupported(shape.kind === "polyline" && segment < shape.lines.length, `Unknown polyline segment: ${raw}.`);
            line = shape.lines[segment];
        }
        if (!target) {
            if (line) return { line, shape };
            if (shape.kind === "point") return { point: shape.vertices[0], shape };
            return { curve: shape.curve ?? shape.polygonGuide, shape, grip: dragging && shape.curve ? "radius" : null,
                ellipseGrip: dragging && shape.kind === "ellipse" ? "body" : null,
                polygonGrip: dragging && shape.polygonGuide ? "translate" : null };
        }
        if (dragging && shape.curve && (["perimeter", "radius"].includes(target)
            || /^quadrant-(0|90|180|270)$/.test(target)
            || (shape.kind === "arc" && target === "mid"))) {
            return { curve: shape.curve, shape, grip: "radius" };
        }
        let p;
        if (line) {
            if (target === "start") p = nodes.get(line.p1_id);
            if (target === "end") p = nodes.get(line.p2_id);
            if (target === "mid" || target === "midpoint") p = midpointNode(line);
        } else if (shape.kind === "point" && target === "center") p = shape.vertices[0];
        else if (shape.kind === "spline") {
            if (target === "start") p = shape.start;
            if (target === "end") p = shape.end;
            const control = (shape.hermite ? /^fit-(\d+)$/ : /^control-(\d+)$/).exec(target);
            if (control) p = (shape.hermite ? shape.fitPoints : shape.poles)[Number(control[1])];
            if (["tangent-start", "tangent-end"].includes(target)) {
                p = splineTangentPoint(shape, target === "tangent-start");
                if (dragging && p) return { point: p, shape, splineTangentGrip: target };
            }
        } else if (shape.polygonGuide) {
            if (target === "center") {
                if (dragging) return { point: shape.center, shape, polygonGrip: "translate" };
                p = shape.center;
            }
            const mid = /^mid-(\d+)$/.exec(target);
            if (dragging && mid && Number(mid[1]) < shape.entity.sideCount)
                return { shape, polygonGrip: "translate" };
        } else if (shape.kind === "ellipse") {
            if (target === "center") p = shape.center;
            if (target === "focus1") p = shape.focus;
            const axes = { "major-end": 0, "major-start": Math.PI, "minor-end": Math.PI / 2, "minor-start": -Math.PI / 2 };
            if (Object.hasOwn(axes, target)) p = ellipsePoint(shape, axes[target]);
            const quadrant = /^quadrant-(0|90|180|270)$/.exec(target);
            if (quadrant) p = ellipsePoint(shape, Number(quadrant[1]) * radians);
            if (target === "start") p = shape.start ?? ellipsePoint(shape, (shape.entity.startAngleDegrees ?? 0) * radians);
            if (target === "end") p = shape.end ?? ellipsePoint(shape, (shape.entity.endAngleDegrees ?? 360) * radians);
        }
        else if (shape.curve) {
            if (target === "center") p = shape.center;
            if (shape.kind === "arc" && target === "start") p = shape.start;
            if (shape.kind === "arc" && target === "end") p = shape.end;
        } else if (shape.kind === "polyline") {
            const vertex = /^vertex-(\d+)$/.exec(target);
            const mid = /^(?:mid|midpoint)-(\d+)$/.exec(target);
            if (vertex) p = shape.vertices[Number(vertex[1])];
            if (mid && shape.lines[Number(mid[1])]) p = midpointNode(shape.lines[Number(mid[1])]);
        }
        requireSupported(p, `Unsupported or missing point reference: ${raw}.`);
        return { point: p, shape, grip: dragging && shape.curve ? target : null,
            ellipseGrip: dragging && shape.kind === "ellipse" ? target : null };
    }
    const getPoint = ref => {
        requireSupported(ref.point, "This relation requires point references.");
        return ref.point;
    };
    const getLine = ref => {
        requireSupported(ref.line, "This relation requires line or polyline segment references.");
        return ref.line;
    };
    const getCurve = ref => {
        requireSupported(ref.curve, "This relation requires a circle or arc.");
        return ref.curve;
    };
    const endpoints = (line, get = key => nodes.get(key)) => [get(line.p1_id), get(line.p2_id)];
    const lineLength = (line, get) => distance(...endpoints(line, get));
    const signedDistance = (p, line, get = key => nodes.get(key)) => {
        const [a, b] = endpoints(line, get);
        return ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) / distance(a, b);
    };
    const lineAngle = (line, get = key => nodes.get(key)) => {
        const [a, b] = endpoints(line, get);
        return Math.atan2(b.y - a.y, b.x - a.x);
    };
    function onLine(p, line, source, temporary = false) {
        relation("point_on_line_pl", { p_id: p.id, l_id: line.id }, source,
            get => Math.abs(signedDistance(get(p.id), line, get)), temporary);
    }
    function mirrorPoint(a, b, axis, source, temporary) {
        requireSupported(lineLength(axis) > tolerance, `${source} requires a nonzero symmetry axis.`);
        relation("p2p_symmetric_ppl", { p1_id: a.id, p2_id: b.id, l_id: axis.id }, source, get => {
            const [start, end] = endpoints(axis, get), p = get(a.id), q = get(b.id);
            const dx = end.x - start.x, dy = end.y - start.y, squared = dx * dx + dy * dy;
            if (squared <= tolerance * tolerance) return Infinity;
            const t = ((p.x - start.x) * dx + (p.y - start.y) * dy) / squared;
            return distance(q, { x: 2 * (start.x + t * dx) - p.x, y: 2 * (start.y + t * dy) - p.y });
        }, temporary);
    }
    function validateRelationCurve(ref, source) {
        const entity = ref.shape?.entity;
        requireSupported(["circle", "arc"].includes(ref.shape?.kind)
            && Number.isFinite(entity?.radius) && entity.radius > tolerance,
        `${source} requires a nondegenerate circle or arc.`);
        if (ref.shape.kind === "arc") {
            const sweep = entity.endAngleDegrees - entity.startAngleDegrees;
            requireSupported(Number.isFinite(entity.startAngleDegrees) && Number.isFinite(entity.endAngleDegrees)
                && Number.isFinite(sweep) && sweep !== 0 && sweep > -360 && sweep <= 360,
            `${source} requires a valid finite arc sweep.`);
        }
    }
    function linePair(a, b, perpendicular, source, temporary = false) {
        relation(perpendicular ? "perpendicular_ll" : "parallel", { l1_id: a.id, l2_id: b.id }, source,
            get => Math.abs((perpendicular ? Math.cos : Math.sin)(lineAngle(a, get) - lineAngle(b, get))), temporary);
    }
    function joinedArcEndpoint(line, curve) {
        if (curve.type !== "arc") return null;
        for (const c of constraints) {
            if (lower(c.kind) !== "coincident" || lower(c.state) === "suppressed" || c.referenceKeys?.length !== 2) continue;
            const points = c.referenceKeys.map(resolve).map(ref => ref.point?.id);
            for (const [linePoint, arcPoint] of [points, [...points].reverse()]) {
                if ((linePoint === line.p1_id || linePoint === line.p2_id)
                    && (arcPoint === curve.start_id || arcPoint === curve.end_id)) return arcPoint;
            }
        }
        return null;
    }
    function fixReference(ref, source) {
        if (ref.point) fixPoint(ref.point, source);
        else if (ref.line) for (const p of endpoints(ref.line)) fixPoint(p, source);
        else fixShape(ref.shape, source);
    }
    function registerFixedReference(key, source) {
        const entityId = typeof key === "string" ? key.trim() : null;
        if (byId.has(entityId)) {
            // Uninvolved whole entities remain byte-for-byte copies of the
            // snapshot. Discover/map them only if another intent needs them.
            wholeEntityFixes.set(entityId, source);
            if (models.has(entityId)) fixShape(models.get(entityId), source);
        } else fixReference(resolve(key), source);
    }
    function mapConstraint(c, temporary = false) {
        const kind = lower(c.kind), source = `Constraint ${c.id} (${c.kind})`;
        if (lower(c.state) === "suppressed") return;
        if (kind === "offset") {
            requireSupported(!temporary, "Temporary Offset snap intent is not mapped by PlaneGCS.");
            return;
        }
        if (kind === "fix") {
            requireSupported(!temporary, "A temporary Fix intent is unsupported.");
            const keys = array(c.referenceKeys, source);
            requireSupported(keys.length > 0, `${source} requires references.`);
            keys.forEach(key => registerFixedReference(key, source));
            return;
        }
        const refs = array(c.referenceKeys, source).map(resolve);
        const count = (...counts) => requireSupported(counts.includes(refs.length), `${source} has an unsupported reference count.`);
        switch (kind) {
            case "symmetric": {
                requireSupported(refs.length >= 3 && refs.length % 2 === 1,
                    `${source} requires paired geometry followed by a symmetry axis.`);
                const axis = getLine(refs.at(-1)), peers = refs.slice(0, -1);
                requireSupported(peers.every(ref => ref.shape !== refs.at(-1).shape),
                    `${source} requires an independent symmetry axis.`);
                if (peers.every(ref => ref.point)) {
                    for (let i = 0; i < peers.length; i += 2) {
                        requireSupported(peers[i].point.id !== peers[i + 1].point.id
                            && peers[i].shape !== peers[i + 1].shape,
                        `${source} requires points on distinct entities.`);
                        for (const index of [i, i + 1]) {
                            const ref = peers[index], key = c.referenceKeys[index];
                            requireSupported(!ref.shape || supportedEntities.has(ref.shape.kind),
                                `${source} has an unsupported point reference.`);
                            if (ref.shape?.kind === "polyline") requireSupported(key.includes("|segment|"),
                                `${source} requires a canonical segment point reference.`);
                            if (peers.length > 2) requireSupported(["line", "polyline"].includes(ref.shape?.kind)
                                && /:(start|end)$/.test(key), `${source} requires paired line endpoints.`);
                        }
                        mirrorPoint(getPoint(peers[i]), getPoint(peers[i + 1]), axis, source, temporary);
                    }
                } else if (peers.length === 2 && peers[0].shape?.kind === "spline" && peers[1].shape?.kind === "spline") {
                    const [a, b] = peers.map(ref => ref.shape);
                    requireSupported(a !== b && a.hermite === b.hermite, `${source} requires distinct splines with matching representations.`);
                    const same = (xs, ys) => xs.length === ys.length && xs.every((v, i) => Math.abs(v - ys[i]) <= 1e-10);
                    const normalizedKnots = e => {
                        const start = e.splineKnots[e.splineDegree], span = e.splineKnots[e.controlPoints.length] - start;
                        return e.splineKnots.map(v => (v - start) / span);
                    };
                    requireSupported(a.entity.splineDegree === b.entity.splineDegree && a.poles.length === b.poles.length
                        && (a.hermite || same(normalizedKnots(a.entity), normalizedKnots(b.entity))
                            && same(a.entity.splineWeights.map(w => w / a.entity.splineWeights[0]),
                                b.entity.splineWeights.map(w => w / b.entity.splineWeights[0]))),
                    `${source}: spline degree, control count, normalized knots and weights must match; no approximation is applied.`);
                    const pairs = a.poles.map((p, i) => [p, b.poles[i]]);
                    if (a.hermite) {
                        requireSupported(a.fitPoints.length === b.fitPoints.length, `${source}: spline fit-point counts differ.`);
                        pairs.push(...a.fitPoints.map((p, i) => [p, b.fitPoints[i]]));
                    }
                    for (const name of ["startTangentHandle", "endTangentHandle"]) {
                        requireSupported(Boolean(a.handles[name]) === Boolean(b.handles[name]), `${source}: spline tangent-handle representations differ.`);
                        if (a.handles[name]) pairs.push([a.handles[name], b.handles[name]]);
                    }
                    const seen = new Set();
                    for (const [p, q] of pairs) if (!seen.has(`${p.id}:${q.id}`)) {
                        mirrorPoint(p, q, axis, source, temporary); seen.add(`${p.id}:${q.id}`);
                    }
                } else if (peers.length === 2 && peers.every(ref => ref.shape?.kind === "polygon")) {
                    const [a, b] = peers.map(ref => ref.shape);
                    requireSupported(a !== b && a.entity.sideCount === b.entity.sideCount
                        && a.entity.circumscribed === b.entity.circumscribed, `${source}: polygon topology must match.`);
                    mirrorPoint(a.center, b.center, axis, source, temporary);
                    mirrorPoint(a.guide, b.guide, axis, source, temporary);
                } else if (peers.length === 2 && peers.every(ref => ref.shape?.kind === "ellipse")) {
                    const [a, b] = peers.map(ref => ref.shape);
                    requireSupported(a !== b && fullEllipse(a.entity) === fullEllipse(b.entity), `${source}: ellipse topology must match.`);
                    mirrorPoint(a.center, b.center, axis, source, temporary);
                    if (a.ellipseAxes && b.ellipseAxes && fullEllipse(a.entity)) {
                        mirrorPoint(a.ellipseAxes.majorEnd, b.ellipseAxes.majorEnd, axis, source, temporary);
                        mirrorPoint(a.ellipseAxes.minorEnd, ellipsePoint(b, -Math.PI / 2), axis, source, temporary);
                    } else {
                        requireSupported(a.ellipse && b.ellipse && a.ellipse.type === b.ellipse.type,
                            `${source}: partial equal-axis ellipses are not mapped for symmetry.`);
                        mirrorPoint(a.focus, b.focus, axis, source, temporary);
                        relation("equal", { param1: { o_id: a.ellipse.id, prop: "radmin" }, param2: { o_id: b.ellipse.id, prop: "radmin" } },
                            source, get => Math.abs(get(a.ellipse.id).radmin - get(b.ellipse.id).radmin), temporary);
                        if (a.ellipse.type === "arc_of_ellipse") {
                            // Keep the nearest equivalent turn after normalized native
                            // save/reopen, instead of asking the solver to unwind 360 degrees.
                            const turn = 2 * Math.PI;
                            const sum = turn * Math.round((a.ellipse.start_angle + b.ellipse.end_angle
                                + a.ellipse.end_angle + b.ellipse.start_angle) / (2 * turn));
                            const total = point({x: sum, y: 0}); total.fixed = true;
                            for (const [left, right] of [["start_angle", "end_angle"], ["end_angle", "start_angle"]])
                                relation("difference", { param1: { o_id: b.ellipse.id, prop: right },
                                    param2: { o_id: total.id, prop: "x" }, difference: { o_id: a.ellipse.id, prop: left } },
                                source, get => Math.abs(get(a.ellipse.id)[left] + get(b.ellipse.id)[right] - sum), temporary);
                        }
                    }
                } else {
                    count(3);
                    const [a, b] = peers.map(getCurve);
                    requireSupported(peers[0].shape !== peers[1].shape && a.type === b.type
                        && ["circle", "arc"].includes(peers[0].shape?.kind)
                        && peers[0].shape.kind === peers[1].shape?.kind,
                    `${source} requires two distinct matching circles or arcs.`);
                    peers.forEach(ref => validateRelationCurve(ref, source));
                    mirrorPoint(nodes.get(a.c_id), nodes.get(b.c_id), axis, source, temporary);
                    if (a.type === "arc") {
                        // Reflection reverses winding, so starts correspond to opposite ends.
                        mirrorPoint(nodes.get(a.start_id), nodes.get(b.end_id), axis, source, temporary);
                        // Mirroring both endpoint pairs adds a redundant radius equation.
                        // Equal sweep supplies only the remaining angular freedom.
                        const sweep = point({ x: a.end_angle - a.start_angle, y: 0 });
                        equalParam(sweep, "y", 0, source);
                        for (const curve of [a, b]) relation("difference", {
                            param1: { o_id: curve.id, prop: "start_angle" },
                            param2: { o_id: curve.id, prop: "end_angle" },
                            difference: { o_id: sweep.id, prop: "x" }
                        }, source, get => Math.abs(get(curve.id).end_angle - get(curve.id).start_angle - get(sweep.id).x), temporary);
                    } else {
                        relation("equal", { param1: { o_id: a.id, prop: "radius" }, param2: { o_id: b.id, prop: "radius" } },
                            source, get => Math.abs(get(a.id).radius - get(b.id).radius), temporary);
                    }
                }
                break;
            }
            case "normal": {
                count(2);
                const key = c.referenceKeys[0];
                requireSupported(/:(start|end)$/.test(key), `${source} requires a line endpoint followed by a circle or arc.`);
                const contact = getPoint(refs[0]), line = getLine(resolve(key.replace(/:(start|end)$/, "")));
                const curve = getCurve(refs[1]);
                validateRelationCurve(refs[1], source);
                requireSupported(["circle", "arc"].includes(refs[1].shape?.kind) && lineLength(line) > tolerance,
                    `${source} requires a nonzero line and a circle or arc.`);
                onLine(nodes.get(curve.c_id), line, source, temporary);
                relation(curve.type === "arc" ? "point_on_arc" : "point_on_circle",
                    { p_id: contact.id, [curve.type === "arc" ? "a_id" : "c_id"]: curve.id }, source, get => {
                        const p = get(contact.id), solved = get(curve.id), center = get(curve.c_id);
                        if (lineLength(line, get) <= tolerance) return Infinity;
                        if (curve.type === "arc") {
                            const turn = 2 * Math.PI, wrap = value => ((value % turn) + turn) % turn;
                            const angle = Math.atan2(p.y - center.y, p.x - center.x);
                            const span = wrap(solved.end_angle - solved.start_angle) || turn;
                            const at = angle => ({x: center.x + solved.radius * Math.cos(angle),
                                y: center.y + solved.radius * Math.sin(angle)});
                            if (wrap(angle - solved.start_angle) > span
                                && distance(p, at(solved.start_angle)) > tolerance
                                && distance(p, at(solved.end_angle)) > tolerance) return Infinity;
                        }
                        return Math.abs(distance(p, center) - solved.radius);
                    }, temporary);
                break;
            }
            case "coincident":
                count(2);
                samePoint(getPoint(refs[0]), getPoint(refs[1]), source, temporary);
                break;
            case "horizontal":
            case "vertical": {
                count(1, 2);
                const horizontal = kind === "horizontal";
                const [a, b] = refs.length === 1 ? endpoints(getLine(refs[0])) : refs.map(getPoint);
                relation(horizontal ? "horizontal_pp" : "vertical_pp", { p1_id: a.id, p2_id: b.id }, source,
                    get => Math.abs(get(a.id)[horizontal ? "y" : "x"] - get(b.id)[horizontal ? "y" : "x"]), temporary);
                const line = refs.length === 1 ? refs[0].line
                    : refs[0].shape && refs[0].shape === refs[1].shape
                        ? refs[0].shape.lines.find(l => (l.p1_id === a.id && l.p2_id === b.id) || (l.p1_id === b.id && l.p2_id === a.id))
                        : null;
                if (!temporary && line) axisLinePreferences.set(line.id, { line, horizontal });
                break;
            }
            case "parallel":
            case "perpendicular":
            case "collinear": {
                count(2);
                const [a, b] = refs.map(getLine);
                if (kind === "collinear") {
                    onLine(nodes.get(b.p1_id), a, source, temporary);
                    onLine(nodes.get(b.p2_id), a, source, temporary);
                } else linePair(a, b, kind === "perpendicular", source, temporary);
                break;
            }
            case "pointonline": {
                count(2);
                const p = refs.find(ref => ref.point), l = refs.find(ref => ref.line);
                requireSupported(p && l, `${source} requires a point and line.`);
                onLine(p.point, l.line, source, temporary);
                break;
            }
            case "pointoncircle": {
                count(2);
                requireSupported(refs.every(ref => ref.shape?.kind !== "polygon"),
                    `${source}: polygon boundary incidence requires its edge scaffold, not its guide circle.`);
                const p = refs.find(ref => ref.point), curve = refs.find(ref => ref.curve)?.curve;
                requireSupported(p && curve, `${source} requires a point and circle/arc.`);
                relation(curve.type === "arc" ? "point_on_arc" : "point_on_circle",
                    { p_id: p.point.id, [curve.type === "arc" ? "a_id" : "c_id"]: curve.id }, source,
                    get => Math.abs(distance(get(p.point.id), get(curve.c_id)) - get(curve.id).radius), temporary);
                break;
            }
            case "concentric": {
                count(2);
                const centers = refs.map(ref => origin && ref.point === origin ? ref.point
                    : ref.shape?.kind === "ellipse" && !ref.point ? ref.shape.center : nodes.get(getCurve(ref).c_id));
                samePoint(centers[0], centers[1], source, temporary);
                break;
            }
            case "equal": {
                count(2);
                if (refs.every(ref => ref.line)) {
                    const [a, b] = refs.map(getLine);
                    relation("equal_length", { l1_id: a.id, l2_id: b.id }, source,
                        get => Math.abs(lineLength(a, get) - lineLength(b, get)), temporary);
                } else {
                    const [a, b] = refs.map(getCurve);
                    relation("equal", { param1: { o_id: a.id, prop: "radius" }, param2: { o_id: b.id, prop: "radius" } }, source,
                        get => Math.abs(get(a.id).radius - get(b.id).radius), temporary);
                }
                break;
            }
            case "midpoint":
            case "midpoint2":
            case "midpoint3": {
                count(...(kind === "midpoint2" ? [2] : kind === "midpoint3" ? [3] : [2, 3]));
                if (refs.length === 3) {
                    symmetric(getPoint(refs[1]), getPoint(refs[2]), getPoint(refs[0]), source, temporary);
                } else {
                    const p = refs.find(ref => ref.point), l = refs.find(ref => ref.line);
                    requireSupported(p && l, `${source} requires a point and line.`);
                    symmetric(...endpoints(l.line), p.point, source, temporary);
                }
                break;
            }
            case "tangent": {
                count(2);
                requireSupported(refs.every(ref => ref.shape?.kind !== "polygon"),
                    `${source}: polygon tangency requires its edge scaffold, not its guide circle.`);
                const l = refs.find(ref => ref.line), curves = refs.filter(ref => ref.curve).map(getCurve);
                if (l && curves.length === 1) {
                    const curve = curves[0];
                    const joined = joinedArcEndpoint(l.line, curve);
                    const tangentResidual = get => Math.abs(Math.abs(signedDistance(get(curve.c_id), l.line, get)) - get(curve.id).radius);
                    if (joined) {
                        // At a declared shared endpoint, distance-to-radius has a singular
                        // Jacobian. Perpendicular radius/line expresses the same tangency
                        // with an independent first-order equation and stable native rank.
                        relation("perpendicular_pppp", { l1p1_id: l.line.p1_id, l1p2_id: l.line.p2_id,
                            l2p1_id: curve.c_id, l2p2_id: joined }, source, get => Math.max(tangentResidual(get),
                                Math.abs(Math.cos(lineAngle(l.line, get) - Math.atan2(get(joined).y - get(curve.c_id).y,
                                    get(joined).x - get(curve.c_id).x)))), temporary);
                    } else {
                        relation(curve.type === "arc" ? "tangent_la" : "tangent_lc",
                            { l_id: l.line.id, [curve.type === "arc" ? "a_id" : "c_id"]: curve.id }, source,
                            tangentResidual, temporary);
                    }
                } else {
                    requireSupported(curves.length === 2, `${source} requires line/circle or two circle-like references.`);
                    const [a, b] = curves, d = distance(nodes.get(a.c_id), nodes.get(b.c_id));
                    const internal = Math.abs(d - Math.abs(a.radius - b.radius)) < Math.abs(d - a.radius - b.radius);
                    relation("tangent_circumf", { p1_id: a.c_id, p2_id: b.c_id,
                        rd1: { o_id: a.id, prop: "radius" }, rd2: { o_id: b.id, prop: "radius" }, internal }, source,
                    get => Math.abs(distance(get(a.c_id), get(b.c_id))
                        - (internal ? Math.abs(get(a.id).radius - get(b.id).radius) : get(a.id).radius + get(b.id).radius)), temporary);
                }
                break;
            }
            default:
                throw new Unsupported(`${source} is not mapped by PlaneGCS.`);
        }
    }
    function mapDimension(d) {
        if (lower(d.state) === "suppressed") return;
        const kind = lower(d.kind), source = `Dimension ${d.id} (${d.kind})`;
        if (kind === "offset") return;
        if (kind === "count") {
            const keys = array(d.referenceKeys, source);
            const polygon = keys.length === 1 && typeof keys[0] === "string" ? byId.get(keys[0].trim()) : null;
            requireSupported(polygon && lower(polygon.kind) === "polygon"
                && Number.isInteger(polygon.sideCount) && polygon.sideCount >= 3 && polygon.sideCount <= 64,
            `${source} requires one regular polygon with valid existing side count.`);
            const count = finite(d.value, source);
            requireSupported(count >= 0, `${source} requires a nonnegative value.`);
            if (d.isDriving === true) {
                requireSupported(Number.isInteger(count) && count === polygon.sideCount,
                    `${source}: Count changes require the discrete topology edit path before continuous solving.`);
                discreteDimensionIds.add(d.id);
            }
            return;
        }
        requireSupported(["lineardistance", "horizontaldistance", "verticaldistance", "pointtolinedistance", "perimeterdistance",
            "radius", "diameter", "angle"].includes(kind), `${source} is not mapped by PlaneGCS.`);
        const refs = array(d.referenceKeys, source).map(resolve);
        const value = finite(d.value, source);
        requireSupported(value >= 0, `${source} requires a nonnegative value.`);
        if (d.isDriving !== true) return;
        if (kind === "perimeterdistance") {
            requireSupported(refs.length === 2 && refs.some(r => r.curve), `${source} requires a perimeter and a point, line or perimeter.`);
            const geometry = (ref, get = id => nodes.get(id)) => {
                if (ref.curve) {
                    const c = get(ref.curve.id);
                    return { kind: c.type, center: get(c.c_id), radius: c.radius,
                        startAngleDegrees: c.start_angle / radians, endAngleDegrees: c.end_angle / radians };
                }
                if (ref.line) {
                    const [start, end] = endpoints(ref.line, get);
                    return { kind: "line", start, end };
                }
                return { kind: "point", point: get(getPoint(ref).id) };
            };
            requireSupported(measurePerimeterDistance(...refs.map(ref => geometry(ref))),
                `${source}: no usable perimeter gap; select an arc endpoint explicitly if the nearest radial point is outside its sweep.`);
            const check = get => {
                const measured = measurePerimeterDistance(...refs.map(ref => geometry(ref, get)));
                return measured ? Math.abs(measured.value - value) : Infinity;
            };
            const asCircle = ref => {
                const c = getCurve(ref);
                if (c.type === "circle") return c;
                const alias = add({ type: "circle", c_id: c.c_id, radius: c.radius });
                relation("equal", { param1: { o_id: alias.id, prop: "radius" }, param2: { o_id: c.id, prop: "radius" } },
                    source, get => Math.abs(get(alias.id).radius - get(c.id).radius));
                return alias;
            };
            const curves = refs.filter(r => r.curve);
            const c = asCircle(curves[0]);
            if (curves.length === 2) {
                relation("c2cdistance", { c1_id: c.id, c2_id: asCircle(curves[1]).id, dist: value }, source, check);
            } else {
                const peer = refs.find(r => !r.curve);
                if (peer.line) relation("c2ldistance", { c_id: c.id, l_id: peer.line.id, dist: value }, source, check);
                else relation("p2cdistance", { c_id: c.id, p_id: getPoint(peer).id, distance: value }, source, check);
            }
        } else if (["radius", "diameter"].includes(kind)) {
            requireSupported(refs.length === 1, `${source} requires one circle/arc.`);
            const curve = getCurve(refs[0]), radius = value / (kind === "diameter" ? 2 : 1);
            requireSupported(radius > 0, `${source} requires a positive radius.`);
            equalParam(curve, "radius", radius, source);
        } else if (kind === "angle") {
            const angle = value * radians;
            if (refs.length === 1) {
                const arc = getCurve(refs[0]);
                requireSupported(arc.type === "arc" && value > 0 && value <= 360, `${source} requires an arc sweep in (0, 360].`);
                relation("difference", { param1: { o_id: arc.id, prop: "start_angle" },
                    param2: { o_id: arc.id, prop: "end_angle" }, difference: angle }, source,
                get => Math.abs(get(arc.id).end_angle - get(arc.id).start_angle - angle));
            } else {
                requireSupported(refs.length === 2 && value <= 180, `${source} requires two lines and an angle in [0, 180].`);
                const [a, b] = refs.map(getLine), current = lineAngle(b) - lineAngle(a);
                const options = [angle, -angle, Math.PI - angle, angle - Math.PI];
                const target = options.sort((x, y) => Math.abs(angleDelta(x - current)) - Math.abs(angleDelta(y - current)))[0];
                relation("l2l_angle_ll", { l1_id: a.id, l2_id: b.id, angle: target }, source,
                    get => Math.abs(angleDelta(lineAngle(b, get) - lineAngle(a, get) - target)));
            }
        } else if (kind === "pointtolinedistance") {
            requireSupported(refs.length === 2, `${source} requires two references.`);
            const lines = refs.filter(ref => ref.line).map(getLine);
            const p = refs.find(ref => ref.point)?.point ?? (lines.length === 2 ? midpointNode(lines[1]) : null);
            requireSupported(lines.length > 0 && p, `${source} requires a point and line, or two parallel lines.`);
            if (lines.length === 2) linePair(lines[0], lines[1], false, source);
            const line = lines[0];
            if (value === 0) onLine(p, line, source);
            else relation("p2l_distance", { p_id: p.id, l_id: line.id, distance: value }, source,
                get => Math.abs(Math.abs(signedDistance(get(p.id), line, get)) - value));
        } else {
            const points = refs.length === 1 && refs[0].line ? endpoints(refs[0].line) : refs.map(getPoint);
            requireSupported(points.length === 2, `${source} requires two points or one line.`);
            const [a, b] = points;
            if (kind === "lineardistance") {
                if (value === 0) samePoint(a, b, source);
                else relation("p2p_distance", { p1_id: a.id, p2_id: b.id, distance: value }, source,
                    get => Math.abs(distance(get(a.id), get(b.id)) - value));
            } else {
                const prop = kind === "horizontaldistance" ? "x" : "y";
                const signed = (b[prop] < a[prop] ? -1 : 1) * value;
                relation("difference", { param1: { o_id: a.id, prop }, param2: { o_id: b.id, prop }, difference: signed }, source,
                    get => Math.abs(get(b.id)[prop] - get(a.id)[prop] - signed));
            }
        }
    }
    const seedOperations = [];
    const patternMembers = mapPlaneGcsPatterns({ patterns, byId, model, relation, point, add, requireSupported, resolve, endpoints,
        seedOperations, fixedParameterIds });
    mapPlaneGcsOffsets({ constraints, dimensions, model, nodes, relation, samePoint, requireSupported,
        add, point, fixSource: registerFixedReference });
    constraints.forEach(c => mapConstraint(c));
    dimensions.forEach(d => {
        dimensionOwner = d.isDriving === true ? { id: d.id, source: `Dimension ${d.id} (${d.kind})` } : null;
        try { mapDimension(d); } finally { dimensionOwner = null; }
    });
    for (const key of array(request.fixedReferenceKeys ?? [], "fixedReferenceKeys"))
        registerFixedReference(key, `Fixed reference ${key}`);
    for (const guess of array(request.initialGuesses ?? [], "initialGuesses")) {
        const p = getPoint(resolve(guess.referenceKey)), target = pointValue(guess.point, "Initial guess");
        if (!p.fixed) Object.assign(p, target);
    }
    function softPoint(p, target, source, scale = 1) {
        for (const prop of ["x", "y"]) {
            relation(`coordinate_${prop}`, { p_id: p.id, [prop]: target[prop], scale }, source, null, true);
        }
    }
    function softParam(node, prop, value, source, scale = 1) {
        relation("equal", { param1: { o_id: node.id, prop }, param2: value, scale }, source, null, true);
    }
    function seedPoint(node, target, preserveHardRelations = false) {
        if (node.fixed) return;
        const original = { x: node.x, y: node.y };
        Object.assign(node, target);
        // Pattern dependents are seeded together in tryPatternSeed, which checks
        // all hard intent and restores the original snapshot if that guess fails.
        // Checking here would discard a direction edit before its members follow.
        if (preserveHardRelations && !seedOperations.length && checks.some(({ check }) => {
            const error = check(id => nodes.get(id));
            return !Number.isFinite(error) || error > tolerance;
        })) Object.assign(node, original);
    }
    function curveDrag(ref, end, delta) {
        const { shape, grip } = ref, curve = shape.curve;
        const before = initialPoints.get(shape.center.id);
        const oldRadius = shape.entity.radius;
        const oldStart = curve.start_angle, oldEnd = curve.end_angle;
        const preferenceScale = 1e-4;
        let center = before, radius = oldRadius, start = oldStart, finish = oldEnd;
        if (grip === "center") {
            center = { x: before.x + delta.x, y: before.y + delta.y };
            softPoint(shape.center, center, "Center drag target");
            softParam(curve, "radius", oldRadius, "Radius stability preference", preferenceScale);
        } else {
            radius = distance(before, end);
            requireSupported(radius > tolerance, "Radius/endpoint drag requires a direction away from the curve center.");
            softPoint(shape.center, before, "Center stability preference", preferenceScale);
            if (grip === "radius") {
                // Body/perimeter grips seek pointer contact, not a radius measured
                // from the old center. Hard constraints may move the center or
                // project the pick; center stability is only a weak preference.
                // This auxiliary point and its incidence exist for this solve only.
                const pick = point(end);
                relation(curve.type === "arc" ? "point_on_arc" : "point_on_circle",
                    { p_id: pick.id, [curve.type === "arc" ? "a_id" : "c_id"]: curve.id },
                    "Transient radius grip incidence",
                    get => Math.abs(distance(get(pick.id), get(curve.c_id)) - get(curve.id).radius));
                softPoint(pick, end, "Radius grip pointer target");
            }
            else {
                requireSupported(shape.kind === "arc" && ["start", "end"].includes(grip), "Unsupported curve drag grip.");
                const angle = Math.atan2(end.y - before.y, end.x - before.x);
                if (grip === "start") start = oldStart + angleDelta(angle - oldStart);
                else finish = oldEnd + angleDelta(angle - oldEnd);
                softPoint(grip === "start" ? shape.start : shape.end, end, "Arc endpoint drag target");
            }
        }
        if (shape.kind === "arc") {
            if (grip !== "start") softParam(curve, "start_angle", oldStart, "Start angle stability preference", preferenceScale);
            if (grip !== "end") softParam(curve, "end_angle", oldEnd, "End angle stability preference", preferenceScale);
        }
        // These are initial guesses, not constraints or post-solve corrections.
        // Fixed points and all driving values still come from the original snapshot.
        seedPoint(shape.center, center);
        if (!fixedParameterIds.has(curve.id)) curve.radius = radius;
        if (shape.kind === "arc") {
            if (!fixedParameterIds.has(curve.id)) {
                curve.start_angle = start;
                curve.end_angle = finish;
            }
            const at = angle => ({ x: shape.center.x + radius * Math.cos(angle), y: shape.center.y + radius * Math.sin(angle) });
            seedPoint(shape.start, at(start));
            seedPoint(shape.end, at(finish));
        }
    }
    const snapshotGeometry = () => geometries.map(node => ({ node, values: copy(node) }));
    const restoreGeometry = snapshot => {
        for (const { node, values } of snapshot) {
            for (const key of Object.keys(node)) if (!(key in values)) delete node[key];
            Object.assign(node, copy(values));
        }
    };
    const preDragGeometry = request.drag && seedOperations.length ? snapshotGeometry() : null;
    if (request.drag) {
        const drag = request.drag, ref = resolve(drag.key ?? drag.selectionKey, true);
        requireSupported(!patternMembers.has(ref.shape?.entity.id),
            "Generated pattern members cannot be dragged directly; edit their source or pattern.");
        const start = pointValue(drag.start, "Drag start");
        let end = pointValue(drag.end, "Drag end");
        const delta = { x: end.x - start.x, y: end.y - start.y };
        requireSupported(drag.constrain === undefined || typeof drag.constrain === "boolean", "drag.constrain must be boolean.");
        let constrainedPointTarget;
        if (drag.constrain && ref.shape?.kind === "line" && ref.shape.vertices.includes(ref.point)) {
            const [a, b] = ref.shape.vertices.map(p => initialPoints.get(p.id));
            const dx = b.x - a.x, dy = b.y - a.y, squaredLength = dx * dx + dy * dy;
            requireSupported(squaredLength > tolerance * tolerance, "Constrained endpoint drag requires a nonzero original line.");
            const parameter = ((end.x - a.x) * dx + (end.y - a.y) * dy) / squaredLength;
            constrainedPointTarget = { x: a.x + parameter * dx, y: a.y + parameter * dy };
        } else if (drag.constrain && ref.shape?.kind === "arc" && ["start", "end"].includes(ref.grip)) {
            const center = initialPoints.get(ref.shape.center.id), length = distance(center, end);
            requireSupported(length > tolerance, "Constrained arc endpoint drag requires a direction away from its center.");
            const scale = ref.shape.entity.radius / length;
            end = { x: center.x + (end.x - center.x) * scale, y: center.y + (end.y - center.y) * scale };
        }
        // Shift changes the gesture target only. It never adds an axis or radius
        // constraint, so all native driving/fixed equations remain authoritative.
        if (ref.polygonGrip) {
            const shape = ref.shape, center = initialPoints.get(shape.center.id);
            softPoint(shape.center, { x: center.x + delta.x, y: center.y + delta.y }, "Polygon translation target");
            const guide = initialPoints.get(shape.guide.id);
            // Translation seeks the original relative guide vector, including
            // when the center target is projected. Weak edit-stability goals
            // alone allow native convergence before radius/angle have settled.
            for (const prop of ["x", "y"])
                relation("difference", { param1: { o_id: shape.center.id, prop },
                    param2: { o_id: shape.guide.id, prop }, difference: guide[prop] - center[prop] },
                "Polygon translation shape target", null, true);
            for (const p of shape.points) {
                const before = initialPoints.get(p.id);
                seedPoint(p, { x: before.x + delta.x, y: before.y + delta.y });
            }
        } else if (ref.splineTangentGrip) {
            const shape = ref.shape, atStart = ref.splineTangentGrip === "tangent-start";
            shape.draggedTangentPoint = ref.point.id;
            softPoint(ref.point, end, "Spline tangent handle target");
            if (shape.hermite) {
                for (const p of shape.fitPoints) softPoint(p, initialPoints.get(p.id), "Fit point stability preference", 1e-4);
            } else {
                const index = atStart ? 1 : shape.poles.length - 2;
                const endpoint = initialPoints.get((atStart ? shape.poles[0] : shape.poles.at(-1)).id);
                requireSupported(distance(endpoint, end) > tolerance,
                    "Spline tangent handle must stay distinct from its endpoint.");
                for (const [i, p] of shape.poles.entries()) {
                    if (i !== index) softPoint(p, initialPoints.get(p.id), "Control point stability preference", 1e-3);
                }
                seedPoint(shape.poles[index], { x: endpoint.x + 4 * (end.x - endpoint.x),
                    y: endpoint.y + 4 * (end.y - endpoint.y) });
                const half = shape.tangentHalves.get(atStart);
                seedPoint(half, midpoint(endpoint, shape.poles[index]));
                seedPoint(ref.point, midpoint(endpoint, half));
            }
        } else if (ref.ellipseGrip) {
            const shape = ref.shape;
            if (["body", "center"].includes(ref.ellipseGrip)) {
                for (const p of shape.points) {
                    const before = initialPoints.get(p.id), target = { x: before.x + delta.x, y: before.y + delta.y };
                    softPoint(p, target, "Ellipse translation target");
                    seedPoint(p, target);
                }
                if (shape.ellipse)
                    softParam(shape.ellipse, "radmin", shape.ellipse.radmin, "Ellipse minor radius stability", 1e-4);
            } else {
                requireSupported(shape.ellipseAxes, "Ellipse resize with explicit focus references is unsupported.");
                const center = initialPoints.get(shape.center.id);
                const { majorEnd, minorEnd } = shape.ellipseAxes;
                const major = initialPoints.get(majorEnd.id), minor = initialPoints.get(minorEnd.id);
                let grip = ref.ellipseGrip;
                if (grip === "start" || grip === "end") {
                    const parameter = (shape.entity[grip === "start" ? "startAngleDegrees" : "endAngleDegrees"]
                        ?? (grip === "start" ? 0 : 360)) * radians;
                    requireSupported(Math.abs(Math.sin(parameter)) < 1e-10,
                        "Only partial ellipse endpoints at stored major-axis grips support resizing.");
                    grip = Math.cos(parameter) > 0 ? "major-end" : "major-start";
                }
                if (["major-end", "major-start", "quadrant-0", "quadrant-180"].includes(grip)) {
                    const sign = ["major-start", "quadrant-180"].includes(grip) ? -1 : 1;
                    const target = { x: center.x + sign * (end.x - center.x), y: center.y + sign * (end.y - center.y) };
                    const length = distance(center, target), oldMinor = distance(center, minor);
                    requireSupported(length > tolerance, "Ellipse major axis must stay nonzero.");
                    softPoint(ref.point, end, "Ellipse major-axis pointer target");
                    softPoint(shape.center, center, "Ellipse center stability", 1e-4);
                    const minorTarget = { x: center.x - (target.y - center.y) * oldMinor / length,
                        y: center.y + (target.x - center.x) * oldMinor / length };
                    softPoint(minorEnd, minorTarget, "Ellipse minor-axis stability", 1e-4);
                    seedPoint(majorEnd, target);
                    seedPoint(minorEnd, minorTarget);
                } else {
                    requireSupported(["minor-end", "minor-start", "quadrant-90", "quadrant-270"].includes(grip),
                        "Unsupported ellipse resize grip.");
                    const oldMinor = distance(center, minor);
                    const ux = (minor.x - center.x) / oldMinor, uy = (minor.y - center.y) / oldMinor;
                    const length = Math.abs((end.x - center.x) * ux + (end.y - center.y) * uy);
                    requireSupported(length > tolerance, "Ellipse minor axis must stay nonzero.");
                    const target = { x: center.x + ux * length, y: center.y + uy * length };
                    softPoint(minorEnd, target, "Ellipse minor-axis projected target");
                    softPoint(shape.center, center, "Ellipse center stability", 1e-4);
                    softPoint(majorEnd, major, "Ellipse stored-major stability", 1e-4);
                    seedPoint(minorEnd, target);
                }
                for (const [parameter, p] of shape.ellipsePoints) {
                    seedPoint(p, { x: shape.center.x + Math.cos(parameter) * (majorEnd.x - shape.center.x)
                        + Math.sin(parameter) * (minorEnd.x - shape.center.x),
                    y: shape.center.y + Math.cos(parameter) * (majorEnd.y - shape.center.y)
                        + Math.sin(parameter) * (minorEnd.y - shape.center.y) });
                }
            }
        } else if (ref.grip) curveDrag(ref, end, delta);
        else if (ref.point) {
            const before = initialPoints.get(ref.point.id);
            const target = constrainedPointTarget ?? { x: before.x + delta.x, y: before.y + delta.y };
            softPoint(ref.point, target, "Drag target");
            seedPoint(ref.point, target, true);
        }
        else {
            const points = ref.line ? endpoints(ref.line) : ref.shape.points;
            for (const p of points) {
                const before = initialPoints.get(p.id);
                const target = { x: before.x + delta.x, y: before.y + delta.y };
                softPoint(p, target, "Drag translation target");
                seedPoint(p, target, true);
            }
        }
        if (drag.temporarySnapIntent) mapConstraint(drag.temporarySnapIntent, true);
    }
    for (const shape of models.values()) {
        if (!shape.hermite) continue;
        for (const [name, endpoint] of [["startTangentHandle", shape.start], ["endTangentHandle", shape.end]]) {
            const handle = shape.handles[name];
            if (!handle || handle.fixed || shape.draggedTangentPoint === handle.id) continue;
            const before = initialPoints.get(endpoint.id), originalHandle = initialPoints.get(handle.id);
            for (const prop of ["x", "y"]) {
                relation("difference", { param1: { o_id: endpoint.id, prop }, param2: { o_id: handle.id, prop },
                    difference: originalHandle[prop] - before[prop], scale: 1e-4 },
                "Hermite endpoint handle stability preference", null, true);
            }
        }
    }
    for (const { line, horizontal } of axisLinePreferences.values()) {
        // Resizing must not compete with redundant old-length drag goals.
        if (request.drag) continue;
        const [a, b] = endpoints(line);
        const length = distance(initialPoints.get(a.id), initialPoints.get(b.id));
        if (length <= 1e-10) continue;
        // At an exact 90-degree edit, a distance gradient cannot initiate rotation.
        // Seed off that singularity; all final geometry still comes from PlaneGCS.
        const prop = horizontal ? "x" : "y";
        if (Math.abs(b[prop] - a[prop]) < length * 1e-8) {
            if (!b.fixed) b[prop] = a[prop] + length * 1e-3;
            else if (!a.fixed) a[prop] = b[prop] - length * 1e-3;
        }
        relation("p2p_distance", { p1_id: a.id, p2_id: b.id, distance: length,
            scale: 1 }, `Original length preference ${line.id}`, null, true);
    }
    // The wrapper requires every point before curves, and every curve before constraints.
    const primitives = [...geometries.filter(p => p.type === "point"), ...geometries.filter(p => p.type !== "point"), ...relations];
    return {
        primitives,
        fixedParameterIds,
        sources,
        relationDimensions,
        discreteDimensionIds,
        tryPatternSeed() {
            if (!seedOperations.length) return;
            const before = snapshotGeometry();
            const fixedUnchanged = () => before.every(({ node, values }) =>
                !(node.fixed || fixedParameterIds.has(node.id)) || JSON.stringify(node) === JSON.stringify(values));
            let accepted = false;
            try {
                // Keep dependent initial guesses feasible without changing the
                // native equations, unknowns or soft pointer objective.
                accepted = seedOperations.every(operation => operation())
                    && fixedUnchanged()
                    && this.validate(id => nodes.get(id)).length === 0;
            } finally {
                if (!accepted) {
                    restoreGeometry(before);
                    // If the pointer guess is infeasible, retain a known-valid
                    // starting state. Soft goals still describe the requested drag.
                    if (preDragGeometry?.length === geometries.length
                        && preDragGeometry.every(({ node }, index) => node === geometries[index])) {
                        let restored = false;
                        try {
                            restoreGeometry(preDragGeometry);
                            restored = fixedUnchanged() && this.validate(id => nodes.get(id)).length === 0;
                        } finally {
                            if (!restored) restoreGeometry(before);
                        }
                    }
                }
            }
        },
        validate(get) {
            const errors = [];
            for (const node of geometries) {
                const solved = get(node.id);
                if (!solved || (node.type === "point" && (!Number.isFinite(solved.x) || !Number.isFinite(solved.y)))
                    || (["circle", "arc"].includes(node.type) && (!Number.isFinite(solved.radius) || solved.radius <= 0))
                    || (node.type === "arc" && (!Number.isFinite(solved.start_angle) || !Number.isFinite(solved.end_angle)))
                    || (["ellipse", "arc_of_ellipse"].includes(node.type) && (!Number.isFinite(solved.radmin)
                        || solved.radmin <= 0 || distance(get(node.c_id), get(node.focus1_id)) <= 1e-10))
                    || (node.type === "arc_of_ellipse" && (!Number.isFinite(solved.start_angle) || !Number.isFinite(solved.end_angle)))
                    || (node.type === "line" && lineLength(node, get) <= 1e-10)) {
                    errors.push(`PlaneGCS produced invalid ${node.type} geometry.`);
                }
            }
            for (const { source, check } of checks) {
                const error = check(get);
                if (!Number.isFinite(error) || error > tolerance) errors.push(`${source} was not satisfied (residual ${error}).`);
            }
            return [...new Set(errors)];
        },
        entities(get) {
            return originals.map(entity => {
                const result = copy(entity), shape = models.get(entity.id);
                if (!shape) return result;
                const unchangedPoints = points => points.every(p => {
                    const original = initialPoints.get(p.id), solved = get(p.id);
                    return original.x === solved.x && original.y === solved.y;
                });
                // Avoid round-trip noise from native ellipse parameter conversion
                // only when every independent parameter is exactly unchanged.
                if (shape.ellipseAxes && unchangedPoints(shape.points)) return result;
                if (shape.ellipse && unchangedPoints([shape.center, shape.focus])
                    && ["radmin", ...(shape.ellipse.type === "arc_of_ellipse" ? ["start_angle", "end_angle"] : [])]
                    .every(prop => get(shape.ellipse.id)[prop] === shape.ellipse[prop])) return result;
                const xy = p => {
                    const solved = get(p.id);
                    return { x: solved.x, y: solved.y };
                };
                const degrees = (angle, original) => Math.abs(angleDelta(angle - original * radians)) < 1e-12
                    ? original : ((angle / radians) % 360 + 360) % 360;
                if (shape.polygonGuide) {
                    const center = xy(shape.center), guide = xy(shape.guide);
                    const radius = get(shape.polygonGuide.id).radius;
                    const angle = degrees(Math.atan2(guide.y - center.y, guide.x - center.x), result.rotationAngleDegrees);
                    result.center = { ...result.center, ...center };
                    result.radius = radius;
                    result.rotationAngleDegrees = angle;
                    const vertexRadius = result.circumscribed ? radius / Math.cos(Math.PI / result.sideCount) : radius;
                    const start = angle * radians + (result.circumscribed ? Math.PI / result.sideCount : 0);
                    // PolygonEntity derives vertices from these solved canonical
                    // parameters; this is serialization, not a geometry correction.
                    result.points = Array.from({ length: result.sideCount }, (_, index) => ({
                        x: center.x + vertexRadius * Math.cos(start + index * 2 * Math.PI / result.sideCount),
                        y: center.y + vertexRadius * Math.sin(start + index * 2 * Math.PI / result.sideCount)
                    }));
                } else if (shape.kind === "spline") {
                    result.controlPoints = shape.poles.map(xy);
                    if (shape.hermite) result.fitPoints = shape.fitPoints.map(xy);
                    for (const [name, node] of Object.entries(shape.handles)) result[name] = { ...result[name], ...xy(node) };
                }
                else if (shape.ellipseAxes) {
                    const center = xy(shape.center), major = xy(shape.ellipseAxes.majorEnd), minor = xy(shape.ellipseAxes.minorEnd);
                    result.center = { ...result.center, ...center };
                    result.majorAxisEndPoint = { ...result.majorAxisEndPoint, x: major.x - center.x, y: major.y - center.y };
                    result.minorRadiusRatio = distance(center, minor) / distance(center, major);
                } else if (shape.ellipse) {
                    const ellipse = get(shape.ellipse.id), center = get(ellipse.c_id), focus = get(ellipse.focus1_id);
                    const f = distance(center, focus), a = Math.hypot(f, ellipse.radmin);
                    result.center = { ...result.center, x: center.x, y: center.y };
                    result.majorAxisEndPoint = { ...result.majorAxisEndPoint, ...(shape.ellipseAxisSwapped
                        ? { x: (focus.y - center.y) * ellipse.radmin / f, y: -(focus.x - center.x) * ellipse.radmin / f }
                        : { x: (focus.x - center.x) * a / f, y: (focus.y - center.y) * a / f }) };
                    result.minorRadiusRatio = shape.ellipseAxisSwapped ? a / ellipse.radmin : ellipse.radmin / a;
                    if (ellipse.type === "arc_of_ellipse") {
                        result.startAngleDegrees = degrees(ellipse.start_angle - shape.ellipseParameterShift, result.startAngleDegrees ?? 0);
                        result.endAngleDegrees = degrees(ellipse.end_angle - shape.ellipseParameterShift, result.endAngleDegrees ?? 360);
                    }
                } else if (shape.vertices) result.points = shape.vertices.map(xy);
                else {
                    result.center = { ...result.center, ...xy(shape.center) };
                    result.radius = get(shape.curve.id).radius;
                    if (shape.kind === "arc") {
                        result.startAngleDegrees = degrees(get(shape.curve.id).start_angle, result.startAngleDegrees);
                        result.endAngleDegrees = degrees(get(shape.curve.id).end_angle, result.endAngleDegrees);
                    }
                }
                return result;
            });
        }
    };
}
