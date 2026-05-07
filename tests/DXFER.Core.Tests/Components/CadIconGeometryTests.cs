using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class CadIconGeometryTests
{
    [Fact]
    public void ThreePointCircleIconPlacesPickedPointsOnTheCircle()
    {
        var block = GetIconCaseBlock("CadIconName.ThreePointCircle");
        var outer = MatchCircle(block, fillRequired: false);
        outer.Should().NotBeNull();
        var markers = MatchCircles(block, fillRequired: true).ToArray();

        markers.Should().HaveCount(3);
        foreach (var marker in markers)
        {
            var distance = Distance(outer!.Cx, outer.Cy, marker.Cx, marker.Cy);
            distance.Should().BeApproximately(
                outer.R,
                0.05,
                "the three pick markers need to communicate circumference picks, not interior construction points");
        }
    }

    [Fact]
    public void CenterPointArcIconUsesDisplayedCenterAsArcCenter()
    {
        var block = GetIconCaseBlock("CadIconName.CenterPointArc");

        block.Should().Contain("A7 7", "the drawn arc should be a radius-7 arc around the displayed center point");
        block.Should().NotContain("a8 8", "a relative arc with an implicit center makes the center marker look non-concentric");
    }

    [Fact]
    public void ThreePointArcIconPlacesPickedPointsOnTheArc()
    {
        var block = GetIconCaseBlock("CadIconName.Arc");
        var markers = MatchCircles(block, fillRequired: true).ToArray();

        block.Should().Contain("A6.5 6.5", "the three-point arc icon should draw the same circular arc implied by its picked points");
        markers.Should().HaveCount(3);
        foreach (var marker in markers)
        {
            Distance(12, 16.5, marker.Cx, marker.Cy).Should().BeApproximately(
                6.5,
                0.05,
                "the start, through, and end markers should all sit on the displayed arc");
        }
    }

    [Fact]
    public void InscribedPolygonIconPlacesVerticesOnGuideCircle()
    {
        var block = GetIconCaseBlock("CadIconName.InscribedPolygon");
        var circle = MatchCircle(block, fillRequired: false);
        circle.Should().NotBeNull();
        var vertices = MatchPathVertices(block).ToArray();

        vertices.Should().HaveCount(5);
        foreach (var vertex in vertices)
        {
            Distance(circle!.Cx, circle.Cy, vertex.X, vertex.Y).Should().BeApproximately(
                circle.R,
                0.05,
                "an inscribed polygon's vertices should land on the guide circle");
        }
    }

    [Fact]
    public void CircumscribedPolygonIconPlacesGuideCircleTangentToSides()
    {
        var block = GetIconCaseBlock("CadIconName.CircumscribedPolygon");
        var circle = MatchCircle(block, fillRequired: false);
        circle.Should().NotBeNull();
        var vertices = MatchPathVertices(block).ToArray();

        vertices.Should().HaveCount(5);
        foreach (var side in ConsecutiveSegments(vertices))
        {
            DistancePointToSegment(
                    new MarkupPoint(circle!.Cx, circle.Cy),
                    side.Start,
                    side.End)
                .Should()
                .BeApproximately(
                    circle.R,
                    0.05,
                    "a circumscribed polygon should wrap around and touch the guide circle");
        }
    }

    private static string GetIconCaseBlock(string caseName)
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "CadIcon.razor"));
        var caseStart = source.IndexOf($"case {caseName}:", StringComparison.Ordinal);
        caseStart.Should().BeGreaterThanOrEqualTo(0);

        var breakIndex = source.IndexOf("break;", caseStart, StringComparison.Ordinal);
        breakIndex.Should().BeGreaterThan(caseStart);
        return source[caseStart..breakIndex];
    }

    private static IEnumerable<CircleMarkup> MatchCircles(string block, bool fillRequired)
    {
        const string pattern = "<circle\\s+cx=\"(?<cx>[0-9.]+)\"\\s+cy=\"(?<cy>[0-9.]+)\"\\s+r=\"(?<r>[0-9.]+)\"(?<attrs>[^>]*)/>";
        foreach (Match match in Regex.Matches(block, pattern, RegexOptions.CultureInvariant))
        {
            if (fillRequired && !match.Groups["attrs"].Value.Contains("fill=\"currentColor\"", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new CircleMarkup(
                Parse(match.Groups["cx"].Value),
                Parse(match.Groups["cy"].Value),
                Parse(match.Groups["r"].Value));
        }
    }

    private static CircleMarkup? MatchCircle(string block, bool fillRequired) =>
        MatchCircles(block, fillRequired).FirstOrDefault();

    private static IEnumerable<MarkupPoint> MatchPathVertices(string block)
    {
        var path = Regex.Match(block, "<path\\s+d=\"(?<d>[^\"]+)\"", RegexOptions.CultureInvariant);
        path.Success.Should().BeTrue();
        foreach (Match match in Regex.Matches(path.Groups["d"].Value, "(?<x>[0-9.]+)[, ]+(?<y>[0-9.]+)", RegexOptions.CultureInvariant))
        {
            yield return new MarkupPoint(Parse(match.Groups["x"].Value), Parse(match.Groups["y"].Value));
        }
    }

    private static IEnumerable<MarkupSegment> ConsecutiveSegments(IReadOnlyList<MarkupPoint> vertices)
    {
        for (var index = 0; index < vertices.Count; index++)
        {
            yield return new MarkupSegment(vertices[index], vertices[(index + 1) % vertices.Count]);
        }
    }

    private static double Parse(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    private static double Distance(double firstX, double firstY, double secondX, double secondY)
    {
        var deltaX = secondX - firstX;
        var deltaY = secondY - firstY;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double DistancePointToSegment(MarkupPoint point, MarkupPoint start, MarkupPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(point.X, point.Y, start.X, start.Y);
        }

        var projection = (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared;
        var clamped = Math.Clamp(projection, 0, 1);
        var projected = new MarkupPoint(start.X + (deltaX * clamped), start.Y + (deltaY * clamped));
        return Distance(point.X, point.Y, projected.X, projected.Y);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }

    private sealed record CircleMarkup(double Cx, double Cy, double R);

    private sealed record MarkupPoint(double X, double Y);

    private sealed record MarkupSegment(MarkupPoint Start, MarkupPoint End);
}
