using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class PatternCenterSnapService
{
    public static bool TryApply(DrawingDocument original, DrawingDocument preview, string patternId,
        IReadOnlyList<CanvasCreationSnapDto> snaps, bool automaticConstraints, ISketchSolver solver,
        Func<SketchConstraintKind, string> nextId, out DrawingDocument result, out string reason)
    {
        result = original;
        reason = string.Empty;
        try
        {
            if (snaps.Count == 0 || !DrawingPatternService.TryExposeCenter(preview, patternId, out var staged, out reason))
                return false;
            if (!DrawingPatternService.TryValidate(staged, out reason)) return false;
            if (!DrawingLayerService.PreservesProtectedGeometry(original, staged))
                throw new InvalidOperationException("Pattern center snap would change protected geometry.");
            var pattern = staged.Metadata.Patterns.Single(p => p.Id == patternId);
            var center = staged.Entities.OfType<PointEntity>().Single(p => p.Id.Value == pattern.CenterPointId);
            var members = pattern.Members.Select(m => m.EntityId).ToHashSet(StringComparer.Ordinal);
            var layers = DrawingLayerService.GetLayers(staged);
            var added = new List<SketchConstraint>();
            foreach (var snap in snaps)
            {
                if (snap is null || snap.ReferenceKeys is not { Length: 1 }
                    || !double.IsFinite(snap.Point.X) || !double.IsFinite(snap.Point.Y)
                    || Math.Abs(snap.Point.X - center.Location.X) > 1e-6 || Math.Abs(snap.Point.Y - center.Location.Y) > 1e-6
                    || !Enum.TryParse<SketchConstraintKind>(snap.Kind, out var kind)
                    || kind is not (SketchConstraintKind.Coincident or SketchConstraintKind.Midpoint
                        or SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical or SketchConstraintKind.PointOnLine)
                    || !SketchReference.TryParse(snap.ReferenceKeys[0], out var reference)
                    || reference.EntityId == center.Id.Value || members.Contains(reference.EntityId))
                    throw new InvalidOperationException("Pattern center snap is invalid or references its own generated geometry.");
                var target = staged.Entities.FirstOrDefault(e => e.Id.Value == reference.EntityId);
                if (!reference.IsOrigin && (target is null
                    || layers[DrawingLayerService.LayerName(staged, target.Id.Value)].IsHidden))
                    throw new InvalidOperationException("Pattern center snap target is missing or hidden.");
                if (reference.SegmentIndex.HasValue && (target is not PolylineEntity path
                    || reference.SegmentIndex.Value < 0 || reference.SegmentIndex.Value >= path.Vertices.Count - 1))
                    throw new InvalidOperationException("Pattern center snap segment no longer exists.");
                var key = reference.ToString();
                var normalized = new CanvasCreationSnapDto(center.Location, snap.Kind, [key]);
                var generated = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point",
                    target is null ? [] : [target], [center], nextId, creationSnaps: [normalized]);
                // Keep the explicit cue only, not incidental inferred relations nearby.
                var constraint = generated.SingleOrDefault(c => c.Kind == kind
                    && c.ReferenceKeys.SequenceEqual(new[] { key, center.Id.Value }))
                    ?? throw new InvalidOperationException("Pattern center snap no longer matches its target.");
                if (!staged.Constraints.Concat(added).Any(c => c.State != SketchConstraintState.Suppressed
                    && c.Kind == kind && SamePair(c.ReferenceKeys, constraint.ReferenceKeys))) added.Add(constraint);
            }

            // The requested center and all external/source geometry are immutable
            // during this acceptance solve; only this pattern's generated members may follow.
            var solved = solver.Solve(new SketchSolveRequest(staged, added, [],
                fixedReferenceKeys: staged.Entities.Where(e => !members.Contains(e.Id.Value)).Select(e => e.Id.Value)));
            if (solved.Status != SketchSolveStatus.Solved)
            {
                reason = string.Join(" ", solved.Diagnostics);
                if (string.IsNullOrWhiteSpace(reason)) reason = "Pattern center snap could not preserve existing intent.";
                return false;
            }
            var withoutAdded = PlaneGcsDiagnostics.RemoveSolvedConstraints(solved.Document, added.Select(c => c.Id));
            // Pattern creation/update already validated the metadata transition.
            // Validate this solve against that staged document, excluding only
            // the new explicit snap intent, not against the pre-pattern metadata.
            if (!DrawingPatternService.TryValidate(solved.Document, out reason)
                || !SketchDirectEditValidationService.TryValidate(staged, withoutAdded, out reason)) return false;
            foreach (var entity in staged.Entities.Where(e => !members.Contains(e.Id.Value)))
            {
                if (!Equals(entity, solved.Document.Entities.SingleOrDefault(e => e.Id == entity.Id)))
                    throw new InvalidOperationException("Pattern center snap would move source or target geometry.");
            }
            foreach (var constraint in added)
                if (SketchConstraintService.ValidateConstraint(solved.Document, constraint).State != SketchConstraintState.Satisfied)
                    throw new InvalidOperationException("Pattern center snap failed final validation.");
            result = automaticConstraints ? solved.Document : withoutAdded;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool SamePair(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.Count == 2 && b.Count == 2
        && (a.SequenceEqual(b) || a.SequenceEqual(b.Reverse()));
}
