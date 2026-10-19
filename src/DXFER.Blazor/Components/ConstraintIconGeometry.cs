namespace DXFER.Blazor.Components;

// The toolbar and canvas share the same 24-unit paths.
public static class ConstraintIconGeometry
{
    public sealed record Shape(string Outline, string? Fill = null);

    public static IReadOnlyDictionary<string, Shape> All { get; } = new Dictionary<string, Shape>
    {
        ["offset"] = new("M3 18V6h14M7 22V10h14M12 14v5m-2-2 2 2 2-2"),
        ["coincident"] = new("M3 12h18M12 3v18", "M14.8 12a2.8 2.8 0 1 0-5.6 0a2.8 2.8 0 1 0 5.6 0Z"),
        ["concentric"] = new("M20 12a8 8 0 1 0-16 0a8 8 0 1 0 16 0ZM16 12a4 4 0 1 0-8 0a4 4 0 1 0 8 0Z", Dot(12, 12)),
        ["parallel"] = new("M4 17 16 5M8 21 20 9"),
        ["tangent"] = new("M18 9a6 6 0 1 0-12 0a6 6 0 1 0 12 0ZM3 15h18", Dot(12, 15)),
        ["horizontal"] = new("M3 12h18", Dot(3, 12) + Dot(21, 12)),
        ["vertical"] = new("M12 3v18", Dot(12, 3) + Dot(12, 21)),
        ["perpendicular"] = new("M5 3v16h16M5 13h6v6"),
        ["equal"] = new("M4 8h16M4 16h16"),
        ["midpoint"] = new("M12 4.205771366 21 19.794228634H3Z"),
        ["fix"] = new("M7 10h10a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2ZM8 10V7a4 4 0 0 1 8 0v3M13 15a1 1 0 1 0-2 0a1 1 0 1 0 2 0Z")
    };

    private static string Dot(int x, int y) => FormattableString.Invariant($"M{x + 1.4} {y}a1.4 1.4 0 1 0-2.8 0a1.4 1.4 0 1 0 2.8 0Z");
}
