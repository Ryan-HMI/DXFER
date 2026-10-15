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
        var arc = MatchCircularArc(block);
        var center = MatchCircles(block, fillRequired: true).Single();

        // This small positive-sweep quarter arc must be centered at the pick,
        // not at the other possible SVG endpoint-arc center.
        arc.Start.X.Should().BeApproximately(center.Cx - arc.Radius, 0.05);
        arc.Start.Y.Should().BeApproximately(center.Cy, 0.05);
        arc.End.X.Should().BeApproximately(center.Cx, 0.05);
        arc.End.Y.Should().BeApproximately(center.Cy - arc.Radius, 0.05);
        Distance(center.Cx, center.Cy, arc.Start.X, arc.Start.Y).Should().BeApproximately(arc.Radius, 0.05);
        Distance(center.Cx, center.Cy, arc.End.X, arc.End.Y).Should().BeApproximately(arc.Radius, 0.05);
    }

    [Fact]
    public void ThreePointArcIconPlacesPickedPointsOnTheArc()
    {
        var block = GetIconCaseBlock("CadIconName.Arc");
        var arc = MatchCircularArc(block);
        var markers = MatchCircles(block, fillRequired: true).ToArray();

        arc.End.Y.Should().BeApproximately(arc.Start.Y, 0.05);
        (arc.End.X - arc.Start.X).Should().BeApproximately(2 * arc.Radius, 0.05);
        var center = new MarkupPoint((arc.Start.X + arc.End.X) / 2, arc.Start.Y);
        var expectedMarkers = new[] { arc.Start, new MarkupPoint(center.X, center.Y - arc.Radius), arc.End };
        markers.Should().HaveCount(3);
        for (var index = 0; index < markers.Length; index++)
        {
            var marker = markers[index];
            Distance(center.X, center.Y, marker.Cx, marker.Cy).Should().BeApproximately(
                arc.Radius,
                0.05,
                "the start, through, and end markers should all sit on the displayed arc");
            Distance(expectedMarkers[index].X, expectedMarkers[index].Y, marker.Cx, marker.Cy)
                .Should().BeApproximately(0, 0.05, "the through pick must be on the drawn half, not the opposite semicircle");
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
        return ParsePolygonVertices(path.Groups["d"].Value);
    }

    // Deliberately limited to the line-only polygon grammar used by these icons.
    // Reject unsupported commands instead of interpreting their parameters as vertices.
    private static IReadOnlyList<MarkupPoint> ParsePolygonVertices(string path)
    {
        const string number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)";
        var tokens = Regex.Matches(path, $@"[A-Za-z]|{number}").Select(match => match.Value).ToArray();
        Regex.Replace(path, $@"[A-Za-z]|{number}|[\s,]", "").Should().BeEmpty();
        var vertices = new List<MarkupPoint>();
        var point = new MarkupPoint(0, 0);
        var command = ' ';
        var index = 0;
        while (index < tokens.Length)
        {
            if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
            if (command is 'Z' or 'z')
            {
                index.Should().Be(tokens.Length);
                return vertices;
            }

            "MmLlHhVv".Should().Contain(command.ToString());
            var relative = char.IsLower(command);
            var first = Parse(tokens[index++]);
            point = char.ToUpperInvariant(command) switch
            {
                'M' or 'L' => new MarkupPoint(first + (relative ? point.X : 0), Parse(tokens[index++]) + (relative ? point.Y : 0)),
                'H' => new MarkupPoint(first + (relative ? point.X : 0), point.Y),
                'V' => new MarkupPoint(point.X, first + (relative ? point.Y : 0)),
                _ => throw new InvalidOperationException("Unsupported polygon command.")
            };
            vertices.Add(point);
            command = command switch { 'M' => 'L', 'm' => 'l', _ => command };
        }

        throw new InvalidOperationException("Icon polygon must be closed.");
    }

    [Fact]
    public void PolygonVertexReaderResolvesRelativeMovesAndHorizontalEdges()
    {
        ParsePolygonVertices("m12 3 8.6 6.2-3.3 10.1H6.7L3.4 9.2Z").Should().BeEquivalentTo(
            new[] { new MarkupPoint(12, 3), new MarkupPoint(20.6, 9.2), new MarkupPoint(17.3, 19.3),
                new MarkupPoint(6.7, 19.3), new MarkupPoint(3.4, 9.2) },
            options => options.WithStrictOrdering().Using<double>(context => context.Subject.Should().BeApproximately(context.Expectation, 1e-10)).WhenTypeIs<double>());
    }

    private static MarkupArc MatchCircularArc(string block)
    {
        const string number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)";
        var match = Regex.Match(block,
            $@"<path\s+d=""M(?<x>{number})[\s,]+(?<y>{number})\s*(?<command>[Aa])(?<rx>{number})[\s,]+(?<ry>{number})[\s,]+0[\s,]+0[\s,]+1[\s,]+(?<ex>{number})[\s,]*(?<ey>{number})",
            RegexOptions.CultureInvariant);
        match.Success.Should().BeTrue("these circular icons use unrotated, small, positive-sweep arcs");
        var start = new MarkupPoint(Parse(match.Groups["x"].Value), Parse(match.Groups["y"].Value));
        var radius = Parse(match.Groups["rx"].Value);
        radius.Should().BeGreaterThan(0);
        Parse(match.Groups["ry"].Value).Should().Be(radius);
        var relative = match.Groups["command"].Value == "a";
        var end = new MarkupPoint(Parse(match.Groups["ex"].Value) + (relative ? start.X : 0),
            Parse(match.Groups["ey"].Value) + (relative ? start.Y : 0));
        return new MarkupArc(start, end, radius);
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

    private sealed record MarkupArc(MarkupPoint Start, MarkupPoint End, double Radius);

    private sealed record MarkupSegment(MarkupPoint Start, MarkupPoint End);
}
