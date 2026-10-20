const eps = 1e-7;
const length = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const curve = a => a?.kind === 'circle' || a?.kind === 'arc';
const at = (c, u, sign = 1) => ({ x: c.center.x + sign * c.radius * u.x, y: c.center.y + sign * c.radius * u.y });
const direction = (a, b) => {
    const d = length(a, b);
    return d > eps ? { x: (b.x - a.x) / d, y: (b.y - a.y) / d } : { x: 1, y: 0 };
};
function onArc(c, p) {
    if (c.kind !== 'arc') return true;
    const mod = n => ((n % 360) + 360) % 360;
    const raw = c.endAngleDegrees - c.startAngleDegrees;
    const sweep = raw >= 360 ? 360 : mod(raw);
    const angle = mod(Math.atan2(p.y - c.center.y, p.x - c.center.x) * 180 / Math.PI - c.startAngleDegrees);
    return angle <= sweep + 1e-6 || 360 - angle <= 1e-6;
}

// Straight edges use their supporting line, like existing point-to-line dimensions.
// An arc must contain the radial witness; its invisible supporting circle is not a target.
export function measurePerimeterDistance(a, b) {
    if (!curve(a)) {
        if (!curve(b)) return null;
        const result = measurePerimeterDistance(b, a);
        return result && { ...result, start: result.end, end: result.start };
    }
    if (!(a.radius > 0) || !Number.isFinite(a.radius)) return null;
    let start, end;
    if (curve(b)) {
        if (!(b.radius > 0) || !Number.isFinite(b.radius)) return null;
        const d = length(a.center, b.center), u = direction(a.center, b.center);
        if (d >= a.radius + b.radius - eps) {
            start = at(a, u); end = at(b, u, -1);
        } else if (d <= Math.abs(a.radius - b.radius) + eps) {
            const sign = a.radius >= b.radius ? 1 : -1;
            start = at(a, u, sign); end = at(b, u, sign);
        } else return null;
        if (!onArc(b, end)) return null;
    } else if (b?.kind === 'point') {
        end = b.point;
        start = at(a, direction(a.center, end));
    } else if (b?.kind === 'line') {
        const dx = b.end.x - b.start.x, dy = b.end.y - b.start.y;
        const ll = dx * dx + dy * dy;
        if (ll <= eps * eps) return null;
        const t = ((a.center.x - b.start.x) * dx + (a.center.y - b.start.y) * dy) / ll;
        end = { x: b.start.x + t * dx, y: b.start.y + t * dy };
        if (length(a.center, end) < a.radius - eps) return null;
        start = at(a, direction(a.center, end));
    } else return null;
    if (!onArc(a, start)) return null;
    const value = length(start, end);
    return Number.isFinite(value) ? { start, end, value } : null;
}
