using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class TrimArcEndpointTests
{
    [Theory]
    [InlineData(0.001, false)]
    [InlineData(0.001, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(1000, false)]
    [InlineData(1000, true)]
    public void TwoArcEndpointsCutOutMiddleAndKeepBothStubs(double scale, bool reverse)
    {
        var document = Create(scale, reverse);
        Trim(document, 5 * scale, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, scale, (0, 3), (7, 10));
        AssertArcUnchanged(document, result);
        document.Entities.OfType<LineEntity>().Single().GetBounds().MaxY.Should().Be(10 * scale);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    public void OutsideIntervalPickRemovesOnlyPickedOuterSpan(double y, bool reverse)
    {
        var document = Create(reverse: reverse);
        Trim(document, y, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, 1, y < 3 ? (3, 10) : (0, 7));
        result.Entities.OfType<LineEntity>().Single().Id.Value.Should().Be("target");
        AssertArcUnchanged(document, result);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(7, false)]
    [InlineData(7, true)]
    public void SingleTangentContactMakesOneCutNotTwoEndpointCuts(double y, bool reverse)
    {
        var original = Create(reverse: reverse);
        var arc = new ArcEntity(EntityId.Create("arc"), new(2, 5), 2, 90, 270);
        var document = new DrawingDocument([original.Entities[0], arc]);
        Trim(document, y, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, 1, y < 5 ? (5, 10) : (0, 5));
        result.Entities.OfType<LineEntity>().Should().ContainSingle();
        AssertArcUnchanged(document, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArcDiameterIntentAloneDoesNotPreventLineMiddleTrim(bool reverse)
    {
        var original = Create(reverse: reverse);
        var diameter = new SketchDimension("diameter", SketchDimensionKind.Diameter, ["arc"], 4, new(4, 5), true);
        var document = new DrawingDocument(original.Entities, [diameter], []);
        Trim(document, 5, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, 1, (0, 3), (7, 10));
        result.Dimensions.Should().Equal(document.Dimensions);
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions.Single()).Should().BeTrue();
        AssertArcUnchanged(document, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RectangleCornerWeldsFollowTheirOriginalEndpointsOntoRetainedStubs(bool reverse, bool canvas)
    {
        var original = Create(reverse: reverse);
        var startPeer = reverse ? "top:start" : "bottom:start";
        var endPeer = reverse ? "bottom:start" : "top:start";
        var document = new DrawingDocument(original.Entities.Concat(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new(0, 0), new(8, 0)),
            new LineEntity(EntityId.Create("right"), new(8, 0), new(8, 10)),
            new LineEntity(EntityId.Create("top"), new(0, 10), new(8, 10))
        }), [],
        [Constraint("start-weld", SketchConstraintKind.Coincident, EndpointKey(reverse, false, canvas), startPeer),
            Constraint("end-weld", SketchConstraintKind.Coincident, EndpointKey(reverse, true, canvas), endPeer),
            Constraint("vertical", SketchConstraintKind.Vertical, "target")]);
        AssertSatisfied(document);
        Trim(document, 5, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, 1, (0, 3), (7, 10));
        result.Constraints.Single(c => c.Id == "start-weld").ReferenceKeys.Select(Normalize).Should().Contain("target:start");
        result.Constraints.Single(c => c.Id == "end-weld").ReferenceKeys.Select(Normalize).Should().Contain("line-split:end");
        result.Constraints.Select(c => c.Id).Should().Contain(document.Constraints.Select(c => c.Id));
        foreach (var peer in document.Entities.Where(e => e.Id.Value is "bottom" or "right" or "top"))
            result.Entities.Single(e => e.Id == peer.Id).Should().Be(peer);
        AssertArcUnchanged(document, result);
        AssertSatisfied(result);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true)));
        AssertIntervals(reopened, 1, (0, 3), (7, 10));
        AssertSatisfied(reopened);
        reopened.Constraints.Should().BeEquivalentTo(result.Constraints);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void FixedOriginalEndpointKeepsPositionAndMapsToCorrectStub(bool reverse, bool fixedEnd, bool canvas)
    {
        var original = Create(reverse: reverse);
        var key = EndpointKey(reverse, fixedEnd, canvas);
        var document = new DrawingDocument(original.Entities, [], [Constraint("fixed", SketchConstraintKind.Fix, key)]);
        SketchReferenceResolver.TryGetPoint(document, key, out var before).Should().BeTrue();
        Trim(document, 5, out var result, out var status).Should().BeTrue(status);
        AssertIntervals(result, 1, (0, 3), (7, 10));
        var retainedKey = result.Constraints.Single(c => c.Id == "fixed").ReferenceKeys.Single();
        Normalize(retainedKey).Should().Be(fixedEnd ? "line-split:end" : "target:start");
        SketchReferenceResolver.TryGetPoint(result, retainedKey, out var after).Should().BeTrue();
        after.Should().Be(before, "validation alone cannot detect a moved Fix anchor");
        AssertArcUnchanged(document, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalEdgeMidpointIntentRefusesWithoutDeletingOrReinterpretingConstraint(bool reverse)
    {
        var original = Create(reverse: reverse);
        var diameter = new SketchDimension("diameter", SketchDimensionKind.Diameter, ["arc"], 4, new(4, 5), true);
        var document = new DrawingDocument(original.Entities, [diameter],
            [Constraint("midpoint", SketchConstraintKind.Midpoint, "target", "arc:center")]);
        AssertSatisfied(document);
        Trim(document, 5, out var result, out var status).Should().BeFalse();
        result.Entities.Should().Equal(document.Entities);
        result.Constraints.Should().Equal(document.Constraints);
        result.Dimensions.Should().Equal(document.Dimensions);
        status.Should().Contain("Midpoint").And.Contain("Geometry unchanged");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void LineSplitPreservesCanonicalAndCanvasEndpointFixOrWeld(bool reverse, bool canvas, bool weld)
    {
        var original = Create(reverse: reverse);
        var line = (LineEntity)original.Entities[0];
        var peer = new LineEntity(EntityId.Create("peer"), line.End, new(8, line.End.Y));
        var endKey = EndpointKey(reverse, true, canvas);
        var endConstraint = weld
            ? Constraint("end", SketchConstraintKind.Coincident, endKey, "peer:start")
            : Constraint("end", SketchConstraintKind.Fix, endKey);
        var document = new DrawingDocument([line, peer], [],
            [Constraint("start", SketchConstraintKind.Fix, EndpointKey(reverse, false, canvas)), endConstraint]);
        AssertSatisfied(document);

        LineSplitService.TrySplitLineAtPoint(document, "target", new(0, 5), EntityId.Create("line-split"),
            out var result).Should().BeTrue();

        AssertIntervals(result, 1, (0, 5), (5, 10));
        var mappedEnd = result.Constraints.Single(c => c.Id == "end").ReferenceKeys[0];
        Normalize(mappedEnd).Should().Be("line-split:end");
        SketchReferenceResolver.TryGetPoint(result, mappedEnd, out var end).Should().BeTrue();
        end.Should().Be(line.End);
        var mappedStart = result.Constraints.Single(c => c.Id == "start").ReferenceKeys.Single();
        Normalize(mappedStart).Should().Be("target:start");
        SketchReferenceResolver.TryGetPoint(result, mappedStart, out var start).Should().BeTrue();
        start.Should().Be(line.Start);
        result.Entities.Single(e => e.Id == peer.Id).Should().Be(peer);
        AssertSatisfied(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrimAndLineSplitCopySourceLayerAndStyleToNewFragment(bool split)
    {
        var original = Create();
        var style = new DxfEntityStyle("BEND", "DASHED", 3);
        var arcStyle = new DxfEntityStyle("CUT", "CONTINUOUS", 7);
        var document = new DrawingDocument(original.Entities, [], [], original.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["target"] = style, ["arc"] = arcStyle }
        });
        DrawingDocument result;
        if (split)
            LineSplitService.TrySplitLineAtPoint(document, "target", new(0, 5), EntityId.Create("line-split"),
                out result).Should().BeTrue();
        else
            Trim(document, 5, out result, out var status).Should().BeTrue(status);

        result.Metadata.EntityStyles["target"].Should().Be(style);
        result.Metadata.EntityStyles["line-split"].Should().Be(style);
        result.Metadata.EntityStyles["arc"].Should().Be(arcStyle);
        document.Metadata.EntityStyles.Should().HaveCount(2).And.NotContainKey("line-split");
        AssertArcUnchanged(document, result);
    }

    [Fact]
    public void WholeFixedTargetRefusesRatherThanSplittingProtectedGeometry()
    {
        var original = Create();
        var document = new DrawingDocument(original.Entities, [], [Constraint("fixed", SketchConstraintKind.Fix, "target")]);
        Trim(document, 5, out var result, out var status).Should().BeFalse();
        result.Entities.Should().Equal(document.Entities);
        result.Constraints.Should().Equal(document.Constraints);
        status.Should().Contain("constraints");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingOriginalSpanDoesNotBecomeDimensionBetweenDisconnectedStubs(bool reverse)
    {
        var original = Create(reverse: reverse);
        var span = new SketchDimension("span", SketchDimensionKind.LinearDistance, ["target:start", "target:end"], 10, new(2, 5), true);
        var document = new DrawingDocument(original.Entities, [span], []);
        Trim(document, 5, out var result, out var status).Should().BeFalse();
        result.Entities.Should().Equal(document.Entities);
        result.Dimensions.Should().Equal(document.Dimensions);
        result.Dimensions.Single().ReferenceKeys.Should().Equal("target:start", "target:end");
        status.Should().Contain("dimensions");
    }

    private static DrawingDocument Create(double scale = 1, bool reverse = false)
    {
        var low = new Point2(0, 0);
        var high = new Point2(0, 10 * scale);
        return new([new LineEntity(EntityId.Create("target"), reverse ? high : low, reverse ? low : high),
            new ArcEntity(EntityId.Create("arc"), new(0, 5 * scale), 2 * scale, 270, 90)]);
    }
    private static SketchConstraint Constraint(string id, SketchConstraintKind kind, params string[] keys) =>
        new(id, kind, keys, SketchConstraintState.Satisfied);
    private static string EndpointKey(bool reverse, bool end, bool canvas)
    {
        var label = end ? "end" : "start";
        var y = reverse == end ? 0 : 10;
        return canvas ? $"target|point|{label}|0|{y}" : "target:" + label;
    }
    private static string Normalize(string key)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        return normalized;
    }
    private static bool Trim(DrawingDocument document, double y, out DrawingDocument result, out string status) =>
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", new(0, y), _ => EntityId.Create("line-split"), out result, out status);
    private static void AssertIntervals(DrawingDocument document, double scale, params (double Low, double High)[] expected)
    {
        var lines = document.Entities.OfType<LineEntity>().Where(l => l.Id.Value is "target" or "line-split")
            .OrderBy(l => Math.Min(l.Start.Y, l.End.Y)).ToArray();
        lines.Should().HaveCount(expected.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var tolerance = Math.Max(1e-10, scale * 1e-9);
            lines[index].Start.X.Should().BeApproximately(0, tolerance);
            lines[index].End.X.Should().BeApproximately(0, tolerance);
            Math.Min(lines[index].Start.Y, lines[index].End.Y).Should().BeApproximately(expected[index].Low * scale, tolerance);
            Math.Max(lines[index].Start.Y, lines[index].End.Y).Should().BeApproximately(expected[index].High * scale, tolerance);
        }
    }
    private static void AssertArcUnchanged(DrawingDocument before, DrawingDocument after) =>
        after.Entities.OfType<ArcEntity>().Single().Should().Be(before.Entities.OfType<ArcEntity>().Single());
    private static void AssertSatisfied(DrawingDocument document) =>
        document.Constraints.Select(c => SketchConstraintService.ValidateConstraint(document, c).State)
            .Should().OnlyContain(state => state == SketchConstraintState.Satisfied);
}
