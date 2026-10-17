using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

// Connectivity means a coordinate difference is fixed, not that the coordinates are equal.
// This is a conservative orthogonal dependency proof, not a general constraint-rank solver.
internal sealed class SketchOrthogonalDistanceDependencies
{
    private readonly DrawingDocument document;
    private readonly SketchFixedReferences fixedReferences;
    private readonly CoordinateDependencies x = new();
    private readonly CoordinateDependencies y = new();
    private readonly List<(bool Perpendicular, PointPair First, PointPair Second)> orientations = new();
    private SketchReference? fixedPoint;

    private SketchOrthogonalDistanceDependencies(DrawingDocument document)
    {
        this.document = document;
        fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints, document.Entities);
    }

    public static bool IsRedundant(DrawingDocument document, SketchDimension dimension, double valueTolerance)
    {
        var excludedIds = new HashSet<string>(StringComparer.Ordinal) { dimension.Id };
        if (!HasFixedComponents(document, dimension, excludedIds, valueTolerance))
        {
            return false;
        }

        // A matching measurement alone does not make redundant length intent valid.
        // Exclude the edited ID in both passes so it cannot invalidate its own evidence.
        // Use the raw proof to conservatively discard dependencies without recursive validation.
        foreach (var existing in document.Dimensions)
        {
            if (existing.IsDriving && existing.Kind == SketchDimensionKind.LinearDistance
                && !StringComparer.Ordinal.Equals(existing.Id, dimension.Id)
                && HasFixedComponents(document, existing,
                    new HashSet<string>(StringComparer.Ordinal) { dimension.Id, existing.Id }, valueTolerance))
            {
                excludedIds.Add(existing.Id);
            }
        }

        return excludedIds.Count == 1 || HasFixedComponents(document, dimension, excludedIds, valueTolerance);
    }

    private static bool HasFixedComponents(DrawingDocument document, SketchDimension dimension,
        IReadOnlySet<string> excludedIds, double valueTolerance)
    {
        var dependencies = new SketchOrthogonalDistanceDependencies(document);
        if (!dependencies.TryGetPair(dimension.ReferenceKeys, out var first, out var second))
        {
            return false;
        }

        dependencies.AddConstraints();
        dependencies.AddDimensions(excludedIds, valueTolerance);
        return dependencies.x.Connected(first, second) && dependencies.y.Connected(first, second);
    }

    private void AddConstraints()
    {
        foreach (var constraint in document.Constraints)
        {
            if (constraint.State == SketchConstraintState.Suppressed)
            {
                continue;
            }

            if (constraint.Kind == SketchConstraintKind.Fix)
            {
                foreach (var key in constraint.ReferenceKeys)
                {
                    TryGetPoint(key, out _, out _);
                }

                continue;
            }

            if (constraint.Kind is not (SketchConstraintKind.Coincident
                    or SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical
                    or SketchConstraintKind.Parallel or SketchConstraintKind.Perpendicular)
                || SketchConstraintService.ValidateConstraint(document, constraint).State != SketchConstraintState.Satisfied)
            {
                continue;
            }

            IReadOnlyList<string> keys = constraint.ReferenceKeys;
            if (constraint.Kind is SketchConstraintKind.Parallel or SketchConstraintKind.Perpendicular)
            {
                if (keys.Count == 2 && TryGetLinePoints(keys[0], out var firstLine)
                    && TryGetLinePoints(keys[1], out var secondLine))
                {
                    orientations.Add((constraint.Kind == SketchConstraintKind.Perpendicular, firstLine, secondLine));
                }

                continue;
            }

            SketchReference first, second;
            if (keys.Count == 1 && TryGetLinePoints(keys[0], out var line))
            {
                first = line.First;
                second = line.Second;
            }
            else if (!TryGetPair(keys, out first, out second))
            {
                continue;
            }

            if (constraint.Kind != SketchConstraintKind.Horizontal)
            {
                x.Connect(first, second);
            }

            if (constraint.Kind != SketchConstraintKind.Vertical)
            {
                y.Connect(first, second);
            }
        }
    }

    private void AddDimensions(IReadOnlySet<string> excludedIds, double valueTolerance)
    {
        var lengths = new List<PointPair>();
        foreach (var dimension in document.Dimensions)
        {
            if (!dimension.IsDriving || excludedIds.Contains(dimension.Id)
                || dimension.Kind is not (SketchDimensionKind.HorizontalDistance
                    or SketchDimensionKind.VerticalDistance or SketchDimensionKind.LinearDistance)
                || !SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var actual)
                || !double.IsFinite(actual) || !double.IsFinite(dimension.Value)
                || Math.Abs(actual - Math.Abs(dimension.Value)) > valueTolerance
                || !TryGetPair(dimension.ReferenceKeys, out var first, out var second))
            {
                continue;
            }

            if (dimension.Kind == SketchDimensionKind.HorizontalDistance)
            {
                x.Connect(first, second);
            }
            else if (dimension.Kind == SketchDimensionKind.VerticalDistance)
            {
                y.Connect(first, second);
            }
            else
            {
                TryGetPoint(dimension.ReferenceKeys[0], out _, out var start);
                TryGetPoint(dimension.ReferenceKeys[1], out _, out var end);
                lengths.Add(new PointPair(first, second, new Point2(end.X - start.X, end.Y - start.Y)));
            }
        }

        // A length supplies an axis component only after its other component is proven
        // fixed at zero. A line merely looking horizontal/vertical is insufficient.
        bool changed;
        do
        {
            changed = false;
            foreach (var (perpendicular, first, second) in orientations)
            {
                changed |= PropagateOrientation(first, second, perpendicular);
                changed |= PropagateOrientation(second, first, perpendicular);
            }

            foreach (var (first, second, delta) in lengths)
            {
                if (Math.Abs(delta.Y) <= SketchGeometryEditor.Tolerance && y.Connected(first, second))
                {
                    changed |= x.Connect(first, second);
                }

                if (Math.Abs(delta.X) <= SketchGeometryEditor.Tolerance && x.Connected(first, second))
                {
                    changed |= y.Connect(first, second);
                }
            }
        } while (changed);
    }

    private bool PropagateOrientation(PointPair source, PointPair target, bool perpendicular)
    {
        var changed = false;
        if (Math.Abs(source.Delta.Y) <= SketchGeometryEditor.Tolerance && y.Connected(source.First, source.Second))
        {
            changed |= (perpendicular ? x : y).Connect(target.First, target.Second);
        }

        if (Math.Abs(source.Delta.X) <= SketchGeometryEditor.Tolerance && x.Connected(source.First, source.Second))
        {
            changed |= (perpendicular ? y : x).Connect(target.First, target.Second);
        }

        return changed;
    }

    private bool TryGetLinePoints(string key, out PointPair pair)
    {
        pair = default;
        if (!SketchReference.TryParse(key, out var reference)
            || !SketchGeometryEditor.TryGetLine(document.Entities, reference, out _, out var line)
            || !TryGetPoint(new SketchReference(reference.EntityId, SketchReferenceTarget.Start,
                reference.SegmentIndex).ToString(), out var first, out _)
            || !TryGetPoint(new SketchReference(reference.EntityId, SketchReferenceTarget.End,
                reference.SegmentIndex).ToString(), out var second, out _))
        {
            return false;
        }

        pair = new PointPair(first, second, new Point2(line.End.X - line.Start.X, line.End.Y - line.Start.Y));
        return true;
    }

    private bool TryGetPair(IReadOnlyList<string> keys, out SketchReference first, out SketchReference second)
    {
        first = second = default;
        return keys.Count == 2
            && TryGetPoint(keys[0], out first, out _)
            && TryGetPoint(keys[1], out second, out _);
    }

    private bool TryGetPoint(string key, out SketchReference identity, out Point2 point)
    {
        // Snapshot-only canvas coordinates (including ellipse/spline samples) are not
        // live point identities and cannot establish dependencies in this solver.
        identity = default;
        point = default;
        if (!SketchReference.TryParse(key, out var reference)
            || !SketchGeometryEditor.TryGetPoint(document.Entities, reference, out point)
            || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        DrawingEntity? entity = null;
        if (!reference.IsOrigin && !SketchGeometryEditor.TryGetEntity(document.Entities, reference, out _, out entity))
        {
            return false;
        }

        identity = reference;
        if (entity is PolylineEntity && reference.SegmentIndex is { } segment)
        {
            // Adjacent segments share an actual vertex, unlike separate coincident points.
            identity = new SketchReference(reference.EntityId, SketchReferenceTarget.Start,
                reference.Target == SketchReferenceTarget.End ? segment + 1 : segment);
        }
        else if (entity is PointEntity)
        {
            identity = new SketchReference(reference.EntityId, SketchReferenceTarget.Center);
        }

        if (fixedReferences.IsFixed(reference) || fixedReferences.IsFixed(identity))
        {
            if (fixedPoint is { } anchor)
            {
                x.Connect(anchor, identity);
                y.Connect(anchor, identity);
            }

            fixedPoint = identity;
        }

        return true;
    }

    private readonly record struct PointPair(SketchReference First, SketchReference Second, Point2 Delta);

    private sealed class CoordinateDependencies
    {
        private readonly Dictionary<SketchReference, SketchReference> roots = new();

        public bool Connected(SketchReference first, SketchReference second) => Root(first) == Root(second);

        public bool Connect(SketchReference first, SketchReference second)
        {
            var firstRoot = Root(first);
            var secondRoot = Root(second);
            if (firstRoot == secondRoot)
            {
                return false;
            }

            roots[firstRoot] = secondRoot;
            return true;
        }

        private SketchReference Root(SketchReference point)
        {
            var root = point;
            while (roots.TryGetValue(root, out var parent))
            {
                root = parent;
            }

            if (point != root)
            {
                roots[point] = root;
            }

            return root;
        }
    }
}
