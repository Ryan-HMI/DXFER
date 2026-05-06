using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchCreationConstraintFactory
{
    private const double GeometryTolerance = 0.000001;

    public static IReadOnlyList<SketchConstraint> CreateConstraintsForTool(
        string toolName,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        ArgumentNullException.ThrowIfNull(createdEntities);
        ArgumentNullException.ThrowIfNull(createConstraintId);

        var constraints = new List<SketchConstraint>();
        var normalizedTool = NormalizeToolName(toolName);
        var lines = createdEntities.OfType<LineEntity>().ToArray();

        switch (normalizedTool)
        {
            case "line":
            case "midpointline":
                if (lines.FirstOrDefault() is { } line)
                {
                    AddAxisConstraint(constraints, line, createConstraintId);
                }

                break;
            case "twopointrectangle":
            case "centerrectangle":
                AddRectangleConstraints(constraints, lines, createConstraintId, includeGlobalAxes: true);
                break;
            case "alignedrectangle":
                AddRectangleConstraints(constraints, lines, createConstraintId, includeGlobalAxes: false);
                break;
            case "slot":
                AddSlotConstraints(constraints, lines, createdEntities.OfType<ArcEntity>().ToArray(), createConstraintId);
                break;
        }

        return constraints;
    }

    private static void AddRectangleConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        Func<SketchConstraintKind, string> createConstraintId,
        bool includeGlobalAxes)
    {
        if (lines.Count < 4)
        {
            return;
        }

        AddCoincidentLoopConstraints(constraints, lines.Take(4).ToArray(), createConstraintId);
        AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[0].Id.Value, lines[2].Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[1].Id.Value, lines[3].Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Perpendicular, createConstraintId, lines[0].Id.Value, lines[1].Id.Value);

        if (!includeGlobalAxes)
        {
            return;
        }

        AddAxisConstraint(constraints, lines[0], createConstraintId);
        AddAxisConstraint(constraints, lines[1], createConstraintId);
    }

    private static void AddSlotConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        IReadOnlyList<ArcEntity> arcs,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        if (lines.Count >= 2)
        {
            AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[0].Id.Value, lines[1].Id.Value);
        }

        if (arcs.Count >= 2)
        {
            AddConstraint(constraints, SketchConstraintKind.Equal, createConstraintId, arcs[0].Id.Value, arcs[1].Id.Value);
        }
    }

    private static void AddCoincidentLoopConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var current = lines[index];
            var next = lines[(index + 1) % lines.Count];
            AddConstraint(
                constraints,
                SketchConstraintKind.Coincident,
                createConstraintId,
                $"{current.Id.Value}:end",
                $"{next.Id.Value}:start");
        }
    }

    private static void AddAxisConstraint(
        ICollection<SketchConstraint> constraints,
        LineEntity line,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        if (Math.Abs(line.Start.Y - line.End.Y) <= GeometryTolerance)
        {
            AddConstraint(constraints, SketchConstraintKind.Horizontal, createConstraintId, line.Id.Value);
        }
        else if (Math.Abs(line.Start.X - line.End.X) <= GeometryTolerance)
        {
            AddConstraint(constraints, SketchConstraintKind.Vertical, createConstraintId, line.Id.Value);
        }
    }

    private static void AddConstraint(
        ICollection<SketchConstraint> constraints,
        SketchConstraintKind kind,
        Func<SketchConstraintKind, string> createConstraintId,
        params string[] referenceKeys) =>
        constraints.Add(new SketchConstraint(createConstraintId(kind), kind, referenceKeys, SketchConstraintState.Satisfied));

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
