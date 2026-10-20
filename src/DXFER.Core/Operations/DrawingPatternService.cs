using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Operations;

/// <summary>Snapshot-based directional patterns. Never solves or silently detaches dependent geometry.</summary>
public static class DrawingPatternService
{
    public const int MaxPatterns = 64;
    public const int MaxSources = 128;
    public const int MaxInstances = 1024;
    public const int MaxGeneratedEntities = 4096;
    public const int MaxDocumentEntities = 100000;
    private const double Tolerance = 1e-7;

    public static bool IsGeneratedMember(DrawingDocument document, string entityId) =>
        document.Metadata.Patterns.Any(p => p.Members.Any(m => m.EntityId == entityId));

    public static bool TryUpsert(DrawingDocument document, PatternDefinition definition,
        out DrawingDocument result, out string reason) =>
        TrySet(document, definition, document.Metadata.Patterns.Any(p => p.Id == definition.Id), out result, out reason);

    public static bool TryCreate(DrawingDocument document, PatternDefinition definition,
        out DrawingDocument result, out string reason) => TrySet(document, definition, false, out result, out reason);

    public static bool TryUpdate(DrawingDocument document, PatternDefinition definition,
        out DrawingDocument result, out string reason) => TrySet(document, definition, true, out result, out reason);

    public static bool TryDissolve(DrawingDocument document, string patternId,
        out DrawingDocument result, out string reason)
    {
        result = document;
        if (!TryValidate(document, out reason)) return false;
        var pattern = document.Metadata.Patterns.FirstOrDefault(p => p.Id == patternId);
        if (pattern is null) return Refuse("Pattern no longer exists.", out reason);
        var layers = DrawingLayerService.GetLayers(document);
        if (document.Metadata.Mode != DrawingDocumentMode.Editable
            || pattern.SourceEntityIds.Concat(pattern.Members.Select(m => m.EntityId)).Any(id => !CanEdit(document, id, layers)))
            return Refuse("Show and unlock the pattern geometry before unlinking it.", out reason);
        result = WithPatterns(document, document.Metadata.Patterns.Where(p => p.Id != patternId));
        reason = "Pattern unlinked; generated geometry is now independent.";
        return true;
    }

    private static bool TrySet(DrawingDocument document, PatternDefinition definition, bool update,
        out DrawingDocument result, out string reason)
    {
        result = document;
        if (!TryValidate(document, out reason)) return false;
        if (definition is null) return Refuse("Pattern definition is required.", out reason);
        var previous = document.Metadata.Patterns.FirstOrDefault(p => p.Id == definition.Id);
        if (update != (previous is not null))
            return Refuse(update ? "Pattern no longer exists." : "Pattern ID already exists.", out reason);
        if (document.Metadata.Mode != DrawingDocumentMode.Editable)
            return Refuse("Reference-only documents cannot be patterned.", out reason);
        if (!TryBuild(document, definition, out var normalized, out var generated, out reason)) return false;
        var oldMembers = previous?.Members.Select(m => m.EntityId).ToHashSet(StringComparer.Ordinal) ?? new();
        var allMembers = document.Metadata.Patterns.SelectMany(p => p.Members).Select(m => m.EntityId).ToHashSet(StringComparer.Ordinal);
        if (normalized.SourceEntityIds.Any(allMembers.Contains))
            return Refuse("Nested patterns are not supported. Unlink the source pattern first.", out reason);
        var layers = DrawingLayerService.GetLayers(document);
        if (normalized.SourceEntityIds.Concat(oldMembers).Any(id => !CanEdit(document, id, layers)))
            return Refuse("Show and unlock the pattern geometry first.", out reason);
        var retained = document.Entities.Where(e => !oldMembers.Contains(e.Id.Value)).ToArray();
        var retainedIds = retained.Select(e => e.Id.Value).ToHashSet(StringComparer.Ordinal);
        if (generated.Any(e => retainedIds.Contains(e.Id.Value)))
            return Refuse("A generated member ID collides with existing geometry.", out reason);
        var patterns = document.Metadata.Patterns.Where(p => p.Id != normalized.Id).Append(normalized).ToArray();
        if (patterns.Length > MaxPatterns || patterns.Sum(p => p.Members.Count) > MaxGeneratedEntities
            || retained.Length + generated.Count > MaxDocumentEntities)
            return Refuse("Pattern expansion exceeds the document limits.", out reason);
        var styles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles, StringComparer.Ordinal);
        foreach (var id in oldMembers) styles.Remove(id);
        CopyMemberStyles(document, normalized, styles);
        var candidate = new DrawingDocument(retained.Concat(generated), document.Dimensions, document.Constraints,
            document.Metadata with { Patterns = Array.AsReadOnly(patterns), EntityStyles = styles });
        if (!PreservesDependencies(document, candidate, out reason)) return false;
        result = RefreshReferences(candidate);
        reason = update ? "Pattern updated." : "Pattern created.";
        return true;
    }

    /// <summary>Call before accepting ordinary mutations; explicit pattern commands bypass this guard.</summary>
    public static bool TryReconcile(DrawingDocument previous, DrawingDocument candidate,
        out DrawingDocument result, out string reason)
    {
        result = previous;
        if (!Same(previous.Metadata.Patterns, candidate.Metadata.Patterns))
            return Refuse("Use the pattern create, update, or Unlink pattern command to change definitions.", out reason);
        if (previous.Metadata.Patterns.Count == 0)
        {
            result = candidate;
            reason = string.Empty;
            return true;
        }
        if (!TryValidate(previous, out reason)) return false;
        if (previous.Metadata.Mode != DrawingDocumentMode.Editable)
            return Refuse("Reference-only pattern geometry cannot be edited.", out reason);
        if (candidate.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() != candidate.Entities.Count)
            return Refuse("Duplicate entity IDs cannot be reconciled.", out reason);
        var entities = candidate.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        var originals = previous.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        var styles = new Dictionary<string, DxfEntityStyle>(candidate.Metadata.EntityStyles, StringComparer.Ordinal);
        var geometryChanged = false;
        foreach (var pattern in previous.Metadata.Patterns)
        {
            if (!TryBuild(candidate, pattern, out _, out var generated, out reason)) return false;
            var sourcesByMember = pattern.Members.ToDictionary(m => m.EntityId, m => m.SourceEntityId, StringComparer.Ordinal);
            foreach (var member in generated)
            {
                if (!entities.TryGetValue(member.Id.Value, out var edited))
                    return Refuse("Generated pattern members cannot be deleted. Unlink or edit the pattern first.", out reason);
                // Accept untouched dependents or an already-reconciled snapshot, never an independent edit.
                var matchesGenerated = SameEntity(edited, member);
                if (!SameEntity(edited, originals[member.Id.Value]) && !matchesGenerated)
                    return Refuse("Generated pattern members cannot be edited independently. Edit the source or unlink the pattern. This transform is not supported.", out reason);
                var sourceId = sourcesByMember[member.Id.Value];
                var editedStyle = candidate.Metadata.EntityStyles.GetValueOrDefault(member.Id.Value);
                if (!Same(editedStyle, previous.Metadata.EntityStyles.GetValueOrDefault(member.Id.Value))
                    && !Same(editedStyle, SourceStyle(candidate, sourceId)))
                    return Refuse("Generated member styles follow their source. Unlink the pattern to edit them independently.", out reason);
                entities[member.Id.Value] = member;
                geometryChanged |= !matchesGenerated;
            }
            CopyMemberStyles(candidate, pattern, styles);
        }
        var reconciled = !geometryChanged && Same(styles, candidate.Metadata.EntityStyles) ? candidate
            : new DrawingDocument(candidate.Entities.Select(e => entities[e.Id.Value]), candidate.Dimensions,
                candidate.Constraints, candidate.Metadata with { EntityStyles = styles });
        if (geometryChanged && reconciled.Constraints.Count > 0)
        {
            // Follow point attachments only, not arbitrary inverse feature solves.
            // Members remain read-only; validate every dependency below.
            var following = reconciled.Entities.ToArray();
            var pointRelations = reconciled.Constraints.Where(c => c.Kind is
                SketchConstraintKind.Coincident or SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical).ToArray();
            SketchConstraintPropagationService.PropagateFromChanges(candidate.Entities, following,
                pointRelations, SketchFixedReferences.FromDocument(reconciled));
            reconciled = new DrawingDocument(following, reconciled.Dimensions,
                reconciled.Constraints, reconciled.Metadata);
            if (!TryValidate(reconciled, out reason)) return false;
        }
        if (!PreservesDependencies(previous, reconciled, out reason)) return false;
        result = RefreshReferences(reconciled);
        reason = string.Empty;
        return true;
    }

    /// <summary>Native IO validation: reject broken definitions without repairing geometry or intent.</summary>
    public static bool TryValidate(DrawingDocument document, out string reason)
    {
        reason = string.Empty;
        var patterns = document.Metadata.Patterns;
        if (patterns is null || patterns.Count > MaxPatterns || patterns.Any(p => p is null))
            return Refuse("Invalid pattern metadata collection.", out reason);
        if (patterns.Count == 0) return true;
        if (patterns.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != patterns.Count
            || document.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() != document.Entities.Count)
            return Refuse("Duplicate pattern or entity IDs.", out reason);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var actual = document.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            if (!TryBuild(document, pattern, out var expected, out var generated, out reason)) return false;
            if (pattern.Members is null || !pattern.Members.SequenceEqual(expected.Members))
                return Refuse("Pattern member identities do not match its definition.", out reason);
            foreach (var entity in generated)
            {
                if (!members.Add(entity.Id.Value) || !actual.TryGetValue(entity.Id.Value, out var stored)
                    || !MatchesValidatedGeometry(entity, stored))
                    return Refuse("Pattern member geometry is missing, duplicated, or inconsistent with its source.", out reason);
            }
            if (members.Count > MaxGeneratedEntities) return Refuse("Too many generated pattern members.", out reason);
        }
        if (patterns.SelectMany(p => p.SourceEntityIds).Any(members.Contains))
            return Refuse("Nested patterns are not supported.", out reason);
        return true;
    }

    private static bool TryBuild(DrawingDocument document, PatternDefinition definition,
        out PatternDefinition normalized, out IReadOnlyList<DrawingEntity> generated, out string reason)
    {
        normalized = definition;
        generated = Array.Empty<DrawingEntity>();
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128 || !Enum.IsDefined(definition.Kind)
            || definition.SourceEntityIds is null || definition.SourceEntityIds.Count is < 1 or > MaxSources
            || definition.SourceEntityIds.Any(string.IsNullOrWhiteSpace)
            || definition.SourceEntityIds.Distinct(StringComparer.Ordinal).Count() != definition.SourceEntityIds.Count)
            return Refuse("Enter a unique pattern ID and valid source entity IDs.", out reason);
        if (!double.IsFinite(definition.XPitch) || !double.IsFinite(definition.YPitch)
            || !double.IsFinite(definition.AngleDegrees) || !double.IsFinite(definition.StepAngleDegrees)
            || !double.IsFinite(definition.Center.X) || !double.IsFinite(definition.Center.Y))
            return Refuse("Pattern distances, angles, and center must be finite.", out reason);
        var radial = definition.Kind == PatternKind.Radial;
        var rows = radial ? 1 : definition.Rows;
        var columns = radial ? definition.Count : definition.Columns;
        if (rows < 1 || columns < 1 || (long)rows * columns < 2 || (long)rows * columns > MaxInstances
            || ((long)rows * columns - 1) * definition.SourceEntityIds.Count > MaxGeneratedEntities)
            return Refuse("Patterns require at least two instances and counts within the supported expansion limit.", out reason);
        if (!radial && ((columns > 1 || definition.Kind == PatternKind.Staggered && rows > 1) && Math.Abs(definition.XPitch) <= Tolerance
            || rows > 1 && Math.Abs(definition.YPitch) <= Tolerance))
            return Refuse("Repeated rows and columns require nonzero pitch.", out reason);
        if (radial && !definition.FullCircle && columns > 1
            && (Math.Abs(definition.StepAngleDegrees) <= Tolerance || Math.Abs(definition.StepAngleDegrees) * (columns - 1) >= 360 - Tolerance))
            return Refuse("Radial step must be nonzero and must not repeat or pass the seed in one pattern.", out reason);
        if (document.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() != document.Entities.Count)
            return Refuse("Duplicate entity IDs cannot be patterned.", out reason);
        var sourceMap = document.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        if (definition.SourceEntityIds.Any(id => !sourceMap.TryGetValue(id, out var e) || !Supported(e)))
            return Refuse("A pattern source is missing or unsupported. Unlink the pattern before deleting its source.", out reason);
        var members = new List<PatternMember>();
        var entities = new List<DrawingEntity>();
        var rotation = Transform2.RotationDegrees(definition.AngleDegrees % 360);
        try
        {
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                if (row == 0 && column == 0) continue;
                var shift = rotation.Apply(new Point2((column + (definition.Kind == PatternKind.Staggered && row % 2 == 1 ? 0.5 : 0)) * definition.XPitch,
                    row * definition.YPitch));
                var transform = radial
                    ? Transform2.RotationDegreesAbout(column * (definition.FullCircle ? 360.0 / columns : definition.StepAngleDegrees), definition.Center)
                    : Transform2.Translation(shift.X, shift.Y);
                foreach (var sourceId in definition.SourceEntityIds)
                {
                    var identity = JsonSerializer.Serialize(new object[] { definition.Id, sourceId, row, column });
                    var id = "pattern-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
                    var source = sourceMap[sourceId];
                    var transformed = source.Transform(transform);
                    if (SameEntity(source, transformed)
                        || source is CircleEntity circle && transformed is CircleEntity copy
                            && SketchGeometryEditor.Distance(circle.Center, copy.Center) <= Tolerance
                        || source is PointEntity point && transformed is PointEntity pointCopy
                            && SketchGeometryEditor.Distance(point.Location, pointCopy.Location) <= Tolerance)
                        return Refuse("A generated pattern member coincides with its seed. Change the spacing or radial center.", out reason);
                    var member = transformed with { Id = EntityId.Create(id) };
                    // Serialization rejects nonfinite transformed coordinates before they enter a document.
                    _ = JsonSerializer.Serialize(member, member.GetType());
                    entities.Add(member);
                    members.Add(new PatternMember(sourceId, row, column, id));
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return Refuse("Pattern transformation is invalid: " + ex.Message, out reason);
        }
        normalized = definition with { SourceEntityIds = Array.AsReadOnly(definition.SourceEntityIds.ToArray()), Members = members.AsReadOnly() };
        generated = entities.AsReadOnly();
        return true;
    }

    private static bool PreservesDependencies(DrawingDocument previous, DrawingDocument candidate, out string reason)
    {
        reason = string.Empty;
        var next = candidate.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        var changed = previous.Entities.Where(e => !next.TryGetValue(e.Id.Value, out var after) || !SameEntity(e, after))
            .Select(e => e.Id.Value).ToHashSet(StringComparer.Ordinal);
        var styleChanged = previous.Entities.Where(e => !Same(previous.Metadata.EntityStyles.GetValueOrDefault(e.Id.Value),
            candidate.Metadata.EntityStyles.GetValueOrDefault(e.Id.Value))).Select(e => e.Id.Value);
        var oldLayers = DrawingLayerService.GetLayers(previous);
        var nextLayers = DrawingLayerService.GetLayers(candidate);
        foreach (var id in changed.Concat(styleChanged).Distinct(StringComparer.Ordinal))
            if (!CanEdit(previous, id, oldLayers) || next.ContainsKey(id) && !CanEdit(candidate, id, nextLayers))
                return Refuse("Pattern mutation would change hidden or locked geometry.", out reason);
        var removed = changed.Where(id => !next.ContainsKey(id)).ToHashSet(StringComparer.Ordinal);
        if (previous.Dimensions.Any(d => References(d.ReferenceKeys, removed))
            || previous.Constraints.Any(c => References(c.ReferenceKeys, removed)))
            return Refuse("Removed pattern members still have dimensions or constraints. Remove that intent before reducing the pattern.", out reason);
        foreach (var fix in previous.Constraints.Where(c => c.Kind == SketchConstraintKind.Fix && c.State != SketchConstraintState.Suppressed))
        {
            foreach (var key in fix.ReferenceKeys)
            {
                if (!SketchReference.TryParse(key, out var reference) || !changed.Contains(reference.EntityId)) continue;
                var unchanged = reference.IsEntity && !reference.SegmentIndex.HasValue
                    ? !changed.Contains(reference.EntityId)
                    : reference.IsEntity
                        ? SketchGeometryEditor.TryGetLine(previous.Entities, reference, out _, out var beforeLine)
                            && SketchGeometryEditor.TryGetLine(candidate.Entities, reference, out _, out var afterLine)
                            && SketchGeometryEditor.AreClose(beforeLine.Start, afterLine.Start) && SketchGeometryEditor.AreClose(beforeLine.End, afterLine.End)
                        : SketchGeometryEditor.TryGetPoint(previous.Entities, reference, out var beforePoint)
                            && SketchGeometryEditor.TryGetPoint(candidate.Entities, reference, out var afterPoint)
                            && SketchGeometryEditor.AreClose(beforePoint, afterPoint);
                if (!unchanged) return Refuse("Pattern mutation would move a Fixed reference.", out reason);
            }
        }
        var patternIds = candidate.Metadata.Patterns.SelectMany(p => p.SourceEntityIds.Concat(p.Members.Select(m => m.EntityId)))
            .ToHashSet(StringComparer.Ordinal);
        var oldDimensions = previous.Dimensions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var oldConstraints = previous.Constraints.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var keys in candidate.Dimensions.Where(d => !oldDimensions.TryGetValue(d.Id, out var old) || !Same(d, old)).Select(d => d.ReferenceKeys)
            .Concat(candidate.Constraints.Where(c => !oldConstraints.TryGetValue(c.Id, out var old) || !Same(c, old)).Select(c => c.ReferenceKeys)))
            foreach (var key in keys)
                if (SketchReference.TryParse(key, out var reference) && patternIds.Contains(reference.EntityId)) changed.Add(reference.EntityId);
        foreach (var dimension in candidate.Dimensions.Where(d => d.IsDriving && References(d.ReferenceKeys, changed)))
            if (!SketchDimensionSolverService.IsDimensionSatisfied(candidate, dimension))
                return Refuse($"Pattern mutation conflicts with driving dimension '{dimension.Id}'."
                    + (dimension.ReferenceKeys.Any(key => SketchReference.TryParse(key, out var reference)
                        && IsGeneratedMember(candidate, reference.EntityId))
                        ? " Generated members cannot be edited independently; edit the source or pattern instead." : ""), out reason);
        foreach (var constraint in candidate.Constraints.Where(c => c.State != SketchConstraintState.Suppressed && References(c.ReferenceKeys, changed)))
            if (SketchConstraintService.ValidateConstraint(candidate, constraint).State != SketchConstraintState.Satisfied)
                return Refuse($"Pattern mutation conflicts with constraint '{constraint.Id}'.", out reason);
        return true;
    }

    private static DrawingDocument RefreshReferences(DrawingDocument document)
    {
        var changed = false;
        var dimensions = document.Dimensions.Select(d =>
        {
            if (d.IsDriving || !SketchDimensionSolverService.TryGetDimensionMeasurement(document, d, out var value)
                || !double.IsFinite(value) || value == d.Value) return d;
            changed = true;
            return new SketchDimension(d.Id, d.Kind, d.ReferenceKeys, value, d.Anchor, false);
        }).ToArray();
        return changed ? new DrawingDocument(document.Entities, dimensions, document.Constraints, document.Metadata) : document;
    }

    private static bool References(IEnumerable<string> keys, HashSet<string> ids) =>
        keys.Any(key => SketchReference.TryParse(key, out var reference) && ids.Contains(reference.EntityId));

    private static void CopyMemberStyles(DrawingDocument document, PatternDefinition pattern, Dictionary<string, DxfEntityStyle> styles)
    {
        foreach (var member in pattern.Members)
            styles[member.EntityId] = SourceStyle(document, member.SourceEntityId);
    }

    private static DxfEntityStyle SourceStyle(DrawingDocument document, string sourceId) =>
        document.Metadata.EntityStyles.GetValueOrDefault(sourceId, new DxfEntityStyle("0"));

    private static bool CanEdit(DrawingDocument document, string id, IReadOnlyDictionary<string, DxfEntityStyle> layers) =>
        layers[DrawingLayerService.LayerName(document, id)] is { IsHidden: false, IsLocked: false };

    private static DrawingDocument WithPatterns(DrawingDocument document, IEnumerable<PatternDefinition> patterns) =>
        new(document.Entities, document.Dimensions, document.Constraints, document.Metadata with { Patterns = Array.AsReadOnly(patterns.ToArray()) });
    private static bool Supported(DrawingEntity entity) => entity is LineEntity or PolylineEntity or PolygonEntity
        or CircleEntity or ArcEntity or EllipseEntity or SplineEntity or PointEntity;
    private static bool SameEntity(DrawingEntity first, DrawingEntity second) => first.GetType() == second.GetType()
        && JsonSerializer.Serialize(first, first.GetType()) == JsonSerializer.Serialize(second, second.GetType());
    // Engine equations introduce floating-point roundoff. Validation tolerates
    // geometry residuals only; reconciliation/change detection stays exact above.
    private static bool MatchesValidatedGeometry(DrawingEntity expected, DrawingEntity actual)
    {
        if (expected.GetType() != actual.GetType() || expected.Id != actual.Id
            || expected.IsConstruction != actual.IsConstruction) return false;
        return (expected, actual) switch
        {
            (PointEntity a, PointEntity b) => NearPoint(a.Location, b.Location),
            (LineEntity a, LineEntity b) => (a.Start == a.End) == (b.Start == b.End)
                && NearPoint(a.Start, b.Start) && NearPoint(a.End, b.End),
            (PolylineEntity a, PolylineEntity b) => SamePolylineTopology(a.Vertices, b.Vertices)
                && NearPoints(a.Vertices, b.Vertices),
            (CircleEntity a, CircleEntity b) => NearPoint(a.Center, b.Center) && NearPositive(a.Radius, b.Radius),
            (ArcEntity a, ArcEntity b) => NearPoint(a.Center, b.Center) && NearPositive(a.Radius, b.Radius)
                && NearAngularSpan(a.StartAngleDegrees, a.EndAngleDegrees, b.StartAngleDegrees, b.EndAngleDegrees),
            (EllipseEntity a, EllipseEntity b) => NearPoint(a.Center, b.Center)
                && NearPoint(a.MajorAxisEndPoint, b.MajorAxisEndPoint)
                && NearPositive(a.MinorRadiusRatio, b.MinorRadiusRatio)
                && NearAngularSpan(a.StartParameterDegrees, a.EndParameterDegrees,
                    b.StartParameterDegrees, b.EndParameterDegrees, zeroIsFullCircle: true),
            (PolygonEntity a, PolygonEntity b) => a.SideCount == b.SideCount && a.Circumscribed == b.Circumscribed
                && NearPoint(a.Center, b.Center) && NearPositive(a.Radius, b.Radius)
                && NearAngle(a.RotationAngleDegrees, b.RotationAngleDegrees),
            (SplineEntity a, SplineEntity b) => a.Degree == b.Degree && a.Knots.SequenceEqual(b.Knots)
                && a.Weights.SequenceEqual(b.Weights) && NearPoints(a.ControlPoints, b.ControlPoints)
                && NearPoints(a.FitPoints, b.FitPoints) && NearOptionalPoint(a.StartTangentHandle, b.StartTangentHandle)
                && NearOptionalPoint(a.EndTangentHandle, b.EndTangentHandle),
            _ => false
        };
    }

    private static bool NearNumber(double first, double second) =>
        double.IsFinite(first) && double.IsFinite(second) && Math.Abs(first - second) <= Tolerance;

    private static bool NearPositive(double first, double second) => first > 0 && second > 0 && NearNumber(first, second);

    private static bool NearAngle(double first, double second) =>
        double.IsFinite(first) && double.IsFinite(second) && NearNumber(Math.IEEERemainder(first - second, 360), 0);

    private static bool NearAngularSpan(double firstStart, double firstEnd, double secondStart, double secondEnd,
        bool zeroIsFullCircle = false)
    {
        if (!NearAngle(firstStart, secondStart) || !NearAngle(firstEnd, secondEnd)) return false;
        static double Sweep(double start, double end, bool fullCircle)
        {
            var sweep = end - start;
            if (sweep < 0)
            {
                sweep %= 360;
                if (sweep < 0) sweep += 360;
            }
            return fullCircle && sweep == 0 ? 360 : sweep;
        }
        var first = Sweep(firstStart, firstEnd, zeroIsFullCircle);
        var second = Sweep(secondStart, secondEnd, zeroIsFullCircle);
        return (first == 0) == (second == 0) && NearNumber(first, second);
    }

    private static bool NearPoint(Point2 first, Point2 second) =>
        double.IsFinite(first.X) && double.IsFinite(first.Y) && double.IsFinite(second.X) && double.IsFinite(second.Y)
        && (first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y) <= Tolerance * Tolerance;

    private static bool NearOptionalPoint(Point2? first, Point2? second) =>
        first is { } point ? second is { } other && NearPoint(point, other) : second is null;

    private static bool NearPoints(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => NearPoint(pair.First, pair.Second));

    private static bool SamePolylineTopology(IReadOnlyList<Point2> first, IReadOnlyList<Point2> second) =>
        first.Count == second.Count && (first[0] == first[^1]) == (second[0] == second[^1])
        && Enumerable.Range(1, first.Count - 1).All(index =>
            (first[index - 1] == first[index]) == (second[index - 1] == second[index]));

    private static bool Same<T>(T first, T second) => JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
    private static bool Refuse(string message, out string reason) { reason = message; return false; }
}
