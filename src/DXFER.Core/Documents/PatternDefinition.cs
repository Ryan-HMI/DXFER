using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public enum PatternKind { Rectangular, Staggered, Radial }

/// <summary>Counts include the seed. Members are owned by DrawingPatternService.</summary>
public sealed record PatternDefinition
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceEntityIds { get; init; } = Array.Empty<string>();
    public PatternKind Kind { get; init; }
    public int Rows { get; init; } = 1;
    public int Columns { get; init; } = 2;
    public double XPitch { get; init; } = 1;
    public double YPitch { get; init; } = 1;
    public double AngleDegrees { get; init; }
    public string? UDirectionReference { get; init; }
    public string? VDirectionReference { get; init; }
    public double StaggerFraction { get; init; } = 0.5;
    public int Count { get; init; } = 4;
    public Point2 Center { get; init; }
    public double StepAngleDegrees { get; init; } = 90;
    public bool FullCircle { get; init; } = true;
    public IReadOnlyList<PatternMember> Members { get; init; } = Array.Empty<PatternMember>();
}

/// <summary>Radial instances use Row=0 and Column=instance index; the seed has no member.</summary>
public sealed record PatternMember(string SourceEntityId, int Row, int Column, string EntityId);
