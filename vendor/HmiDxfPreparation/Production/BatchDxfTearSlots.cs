using System.Globalization;
using static HMISync.Web.Infrastructure.DxfCompatibility;
using static HMISync.Web.Production.BatchDxfLayers;

namespace HMISync.Web.Production;

// Narrow Onshape louver contract, not a general-purpose offset/repair operation.
internal static class BatchDxfTearSlots
{
    internal const double Width = .040;
    private const double Radius = Width / 2;
    private const double Tolerance = .000001;
    internal sealed record Result(Dictionary<int, string> Replacements, int Created, int Existing, bool Active)
    {
        internal bool CleanupLayer(string layer) => Form(layer) || (Active && Tear(layer));
    }
    private static bool Tear(string layer) => Token(layer) == "SHEETMETALFORMTEAR";
    private static bool Form(string layer) => Token(layer) is "SHEETMETALFORMUP" or "SHEETMETALFORMDOWN" or "SHEETMETALFORMCENTERMARK";
    private readonly record struct Point(double X, double Y)
    {
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, double b) => new(a.X * b, a.Y * b);
        internal double Length => Math.Sqrt(X * X + Y * Y);
    }
    private sealed record Segment(Entity Entity, Point A, Point B);
    private sealed record Arc(Entity Entity, Point Center, double Radius, double Start, double Sweep);
    private sealed record Bounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal bool Near(Segment line) => Math.Max(line.A.X, line.B.X) + Width >= MinX && Math.Min(line.A.X, line.B.X) - Width <= MaxX &&
            Math.Max(line.A.Y, line.B.Y) + Width >= MinY && Math.Min(line.A.Y, line.B.Y) - Width <= MaxY;
    }

    internal static Result Prepare(IReadOnlyList<Entity> entities, IReadOnlyList<Pair> pairs, CancellationToken ct)
    {
        var tears = entities.Where(e => Tear(e.Layer)).ToList();
        var replacements = entities.Where(e => Form(e.Layer)).ToDictionary(e => e.Start, _ => "");
        if (tears.Count == 0) return new(replacements, 0, 0, false);
        if (tears.Count > 1000 || entities.Count > 20000) throw Review("Too many entities for automatic tear-slot preparation.");
        // HMI's unitless Onshape exports are inches. Explicit other units are never rescaled here.
        for (var i = 0; i < pairs.Count; i++)
            if (pairs[i].Code == 9 && pairs[i].Value == "$INSUNITS" &&
                (i + 1 >= pairs.Count || pairs[i + 1].Code != 70 || pairs[i + 1].Value is not ("0" or "1")))
                throw Review("Tear-slot conversion requires inches; this DXF declares different units.");

        var lines = tears.Select(ReadLine).ToList();
        var candidates = entities.Where(e => !Tear(e.Layer) && !Form(e.Layer) && !Bend(e.Layer)).ToList();
        // Unknown retained geometry could already contain a slot. Do not add cuts through it.
        if (candidates.Any(e => e.Type is not ("LINE" or "ARC" or "CIRCLE" or "SPLINE")))
            throw Review("Tear-slot conversion needs review beside polyline or other retained geometry.");
        var cutLines = candidates.Where(e => e.Type == "LINE").Select(ReadLine).ToList();
        var arcs = candidates.Where(e => e.Type is "ARC" or "CIRCLE").Select(ReadArc).ToList();
        var splineBounds = candidates.Where(e => e.Type == "SPLINE").Select(ReadSplineBounds).ToList();
        var used = new HashSet<int>();
        // Readers may assign missing handles over existing block/entity IDs. Reserve every
        // original handle, including removed entities, before serializing new slot edges.
        var lastHandle = pairs.Where(p => p.Code is 5 or 105 or >= 320 and <= 369)
            .Select(p => ulong.TryParse(p.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value : throw Review("Invalid DXF handle requires review."))
            .DefaultIfEmpty(255UL).Max();
        string NextHandle()
        {
            if (lastHandle == ulong.MaxValue) throw Review("DXF handle range is exhausted.");
            return (++lastHandle).ToString("X", CultureInfo.InvariantCulture);
        }
        var created = 0;
        var existing = 0;
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (splineBounds.Any(bounds => bounds.Near(line))) throw Review("A cutting spline's control envelope overlaps a tear slot.");
            if (lines.Any(other => other != line && SegmentDistance(line.A, line.B, other.A, other.B) <= Width + Tolerance))
                throw Review("Joined, overlapping or closely spaced tear lines require manual preparation.");
            var matches = new List<int[]>();
            foreach (var edge in cutLines.Where(edge => Cut(edge.Entity.Layer)))
                foreach (var (a, b) in new[] { (edge.A, edge.B), (edge.B, edge.A) })
                {
                    var shift = a - line.A;
                    var direction = (line.B - line.A) * (1 / (line.B - line.A).Length);
                    if (!Near(b - line.B, shift) || Math.Abs(shift.Length - Width) > Tolerance || Math.Abs(Dot(shift, direction)) > Tolerance) continue;
                    var first = arcs.Where(arc => Cut(arc.Entity.Layer) && Cap(arc, line.A + shift * .5, line.A, a, direction * -1)).ToList();
                    var last = arcs.Where(arc => Cut(arc.Entity.Layer) && Cap(arc, line.B + shift * .5, line.B, b, direction)).ToList();
                    if (first.Count == 1 && last.Count == 1) matches.Add([edge.Entity.Start, first[0].Entity.Start, last[0].Entity.Start]);
                }
            if (matches.Count > 1) throw Review("Multiple possible existing slots surround a tear line.");
            if (matches.Count == 1)
            {
                var match = matches[0];
                if (match.Any(used.Contains)) throw Review("Existing slots share geometry; review before exporting.");
                if (cutLines.Any(edge => !match.Contains(edge.Entity.Start) && SegmentDistance(line.A, line.B, edge.A, edge.B) <= Width + Tolerance) ||
                    arcs.Any(arc => !match.Contains(arc.Entity.Start) && NearArc(line, arc)))
                    throw Review("Additional geometry overlaps an existing tear slot.");
                foreach (var id in match) used.Add(id);
                // Keep all four original edges, including the tear edge. Never strip it alone.
                existing++;
                continue;
            }
            if (cutLines.Any(edge => SegmentDistance(line.A, line.B, edge.A, edge.B) <= Width + Tolerance) ||
                arcs.Any(arc => NearArc(line, arc)))
                throw Review("A tear line touches existing or partially prepared slot geometry. Review it before conversion.");
            replacements.Add(line.Entity.Start, Slot(line, NextHandle));
            created++;
        }
        return new(replacements, created, existing, true);
    }

    private static InvalidDataException Review(string message) => new(message + " No tear geometry was exported for this part.");
    private static Bounds ReadSplineBounds(Entity entity)
    {
        double[] Values(int code) => entity.Fields.Where(p => p.Code == code).Select(p =>
            double.TryParse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && Math.Abs(number) <= 1_000_000
                ? number : throw Review("Invalid cutting spline.")).ToArray();
        var xs = Values(10); var ys = Values(20); var zs = Values(30); var weights = Values(41); var knots = Values(40);
        var degree = Value(entity, 71);
        if (degree != Math.Floor(degree) || degree < 1 || xs.Length <= degree || xs.Length != ys.Length ||
            Value(entity, 73) != xs.Length || Value(entity, 72) != knots.Length || knots.Length != xs.Length + degree + 1 ||
            !knots.SequenceEqual(knots.Order()) || knots[0] == knots[^1] ||
            (zs.Length != 0 && (zs.Length != xs.Length || zs.Any(z => Math.Abs(z) > Tolerance))) ||
            (weights.Length != 0 && (weights.Length != xs.Length || weights.Any(w => w <= 0))) ||
            entity.Fields.Any(p => p.Code is 102 or >= 1000) || Value(entity, 67, 0) != 0)
            throw Review("Unbounded or malformed cutting spline requires review.");
        // Positive-weight B-splines stay inside their control hull. Only accept a disjoint envelope;
        // retain original spline bytes, never flatten or infer a contour through that envelope.
        return new(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
    private static double Value(Entity entity, int code, double? fallback = null)
    {
        var values = entity.Fields.Where(p => p.Code == code).ToList();
        if (values.Count == 0 && fallback.HasValue) return fallback.Value;
        if (values.Count != 1 || !double.TryParse(values[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || Math.Abs(value) > 1_000_000) throw Review("Invalid or ambiguous tear/cutting coordinates.");
        return value;
    }
    private static void Planar(Entity entity)
    {
        if (Math.Abs(Value(entity, 30, 0)) > Tolerance || Math.Abs(Value(entity, 31, 0)) > Tolerance ||
            Math.Abs(Value(entity, 39, 0)) > Tolerance || Math.Abs(Value(entity, 210, 0)) > Tolerance ||
            Math.Abs(Value(entity, 220, 0)) > Tolerance || Math.Abs(Value(entity, 230, 1) - 1) > Tolerance ||
            Value(entity, 67, 0) != 0 || entity.Fields.Any(p => p.Code is 102 or >= 1000))
            throw Review("Non-planar, paper-space or extended tear/cutting geometry requires review.");
    }
    private static Segment ReadLine(Entity entity)
    {
        if (entity.Type != "LINE") throw Review("Curved or compound form tears require manual preparation.");
        Planar(entity);
        var a = new Point(Value(entity, 10), Value(entity, 20));
        var b = new Point(Value(entity, 11), Value(entity, 21));
        if ((b - a).Length <= Tolerance) throw Review("Zero-length tear/cutting line.");
        return new(entity, a, b);
    }
    private static Arc ReadArc(Entity entity)
    {
        Planar(entity);
        var radius = Value(entity, 40);
        if (radius <= Tolerance) throw Review("Invalid cutting arc radius.");
        var start = entity.Type == "CIRCLE" ? 0 : Value(entity, 50);
        var sweep = entity.Type == "CIRCLE" ? 360 : Angle(Value(entity, 51) - start);
        if (sweep < Tolerance) throw Review("Ambiguous cutting arc sweep.");
        return new(entity, new(Value(entity, 10), Value(entity, 20)), radius, start, sweep);
    }
    private static bool Cap(Arc arc, Point center, Point a, Point b, Point outward) =>
        Near(arc.Center, center) && Math.Abs(arc.Radius - Radius) <= Tolerance && Math.Abs(arc.Sweep - 180) <= Tolerance &&
        ((Near(At(arc, arc.Start), a) && Near(At(arc, arc.Start + arc.Sweep), b)) ||
         (Near(At(arc, arc.Start), b) && Near(At(arc, arc.Start + arc.Sweep), a))) &&
        Near(At(arc, arc.Start + arc.Sweep / 2), center + outward * Radius);
    private static Point At(Arc arc, double angle) => arc.Center + new Point(Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180)) * arc.Radius;
    private static bool NearArc(Segment line, Arc arc)
    {
        // Conservative circle envelope, including arc endpoints: uncertainty must hold, not add a cut.
        var closest = PointDistance(arc.Center, line.A, line.B);
        var farthest = Math.Max((arc.Center - line.A).Length, (arc.Center - line.B).Length);
        return closest <= arc.Radius + Width + Tolerance && farthest >= arc.Radius - Width - Tolerance;
    }
    private static bool Near(Point a, Point b) => (a - b).Length <= Tolerance;
    private static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(Point a, Point b) => a.X * b.Y - a.Y * b.X;
    private static double PointDistance(Point p, Point a, Point b)
    {
        var d = b - a;
        var t = Math.Clamp(Dot(p - a, d) / Dot(d, d), 0, 1);
        return (p - (a + d * t)).Length;
    }
    private static double SegmentDistance(Point a, Point b, Point c, Point d)
    {
        var denominator = Cross(b - a, d - c);
        if (Math.Abs(denominator) > 1e-15)
        {
            var t = Cross(c - a, d - c) / denominator;
            var u = Cross(c - a, b - a) / denominator;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return 0;
        }
        return Math.Min(Math.Min(PointDistance(a, c, d), PointDistance(b, c, d)), Math.Min(PointDistance(c, a, b), PointDistance(d, a, b)));
    }
    private static double Angle(double angle) => (angle % 360 + 360) % 360;
    private static string Slot(Segment line, Func<string> nextHandle)
    {
        var d = line.B - line.A;
        var n = new Point(-d.Y, d.X) * (Radius / d.Length);
        var angle = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
        string Number(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
        string PointFields(Point p) => $"10\n{Number(p.X)}\n20\n{Number(p.Y)}\n30\n0\n";
        const string layer = "8\nSHEETMETAL_CUT_LINES\n";
        var owners = line.Entity.Fields.Where(p => p.Code == 330).ToArray();
        if (owners.Length > 1) throw Review("Ambiguous tear entity owner.");
        var owner = owners.Length == 1 ? $"330\n{owners[0].Value}\n" : "";
        string Identity() => $"5\n{nextHandle()}\n{owner}100\nAcDbEntity\n";
        string Edge(Point a, Point b) => $"0\nLINE\n{Identity()}{layer}100\nAcDbLine\n{PointFields(a)}11\n{Number(b.X)}\n21\n{Number(b.Y)}\n31\n0\n";
        string End(Point p, double start) => $"0\nARC\n{Identity()}{layer}100\nAcDbCircle\n{PointFields(p)}40\n0.02\n100\nAcDbArc\n50\n{Number(Angle(start))}\n51\n{Number(Angle(start + 180))}\n";
        return Edge(line.A + n, line.B + n) + End(line.B, angle - 90) + Edge(line.B - n, line.A - n) + End(line.A, angle + 90);
    }
}
