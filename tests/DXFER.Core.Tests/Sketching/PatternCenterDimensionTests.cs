using System.Globalization;
using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PatternCenterDimensionTests
{
    public static IEnumerable<object[]> CenterPairs()
    {
        foreach (var memberPair in new[] { false, true })
        foreach (var reverse in new[] { false, true })
        foreach (var canvas in new[] { false, true })
            yield return [memberPair, reverse, canvas];
    }

    [Theory]
    [MemberData(nameof(CenterPairs))]
    public void MeasuredCenterDistancePlacesWithoutChangingPattern(bool memberPair, bool reverse, bool canvas)
    {
        var document = Create();
        var dimension = Build(document, memberPair, reverse, canvas);
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.IsDriving.Should().BeTrue();
        dimension.Value.Should().BeApproximately(4 * Math.Sqrt(2), 1e-10);
        var expected = Pair(document, memberPair).Select(id => id + ":center");
        dimension.ReferenceKeys.Select(Normalize).Should().BeEquivalentTo(expected);
        var result = Solve(document, dimension);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertGeometryUnchanged(document, result.Document);
        result.Document.Dimensions.Should().ContainSingle().Which.Id.Should().Be("distance");
        AssertValid(result.Document);
        var reopened = Reopen(result.Document);
        reopened.Dimensions.Should().BeEquivalentTo(result.Document.Dimensions);
        AssertGeometryUnchanged(result.Document, reopened);
        AssertValid(reopened);
    }

    [Theory]
    [MemberData(nameof(CenterPairs))]
    public void ChangedDrivingDistanceRefusesWithoutInventingInversePatternSemantics(bool memberPair, bool reverse, bool canvas)
    {
        var document = Create();
        var dimension = Build(document, memberPair, reverse, canvas);
        var placed = Solve(document, dimension);
        placed.Status.Should().Be(SketchSolveStatus.Solved);
        var changed = new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 8, dimension.Anchor, true);
        var result = Solve(placed.Document, changed);
        result.Status.Should().Be(SketchSolveStatus.Failed,
            "a generic point distance cannot independently move members or select a radial pattern parameter to invert");
        result.Document.Entities.Should().Equal(placed.Document.Entities);
        result.Document.Metadata.Patterns.Should().BeEquivalentTo(placed.Document.Metadata.Patterns);
        result.Document.Dimensions.Should().Equal(placed.Document.Dimensions);
        result.Diagnostics.Should().Contain(message => message.Contains("conflicts with driving dimension 'distance'")
            && message.Contains("Generated members cannot be edited independently"));
        placed.Document.Dimensions.Single().Value.Should().BeApproximately(4 * Math.Sqrt(2), 1e-10);
    }

    [Theory]
    [MemberData(nameof(CenterPairs))]
    public void ReferenceDistanceMeasuresRegeneratedCentersAfterReopenAndSourceEdit(bool memberPair, bool reverse, bool canvas)
    {
        var document = Create();
        var measured = Build(document, memberPair, reverse, canvas);
        var reference = new SketchDimension(measured.Id, measured.Kind, measured.ReferenceKeys, measured.Value, measured.Anchor, false);
        var placed = Solve(document, reference);
        placed.Status.Should().Be(SketchSolveStatus.Solved);
        AssertGeometryUnchanged(document, placed.Document);
        var reopened = Reopen(placed.Document);
        reopened.Dimensions.Single().IsDriving.Should().BeFalse();
        var snapshot = reopened.Entities.ToArray();

        SketchGeometryDragService.TryApplyDrag(reopened, "seed|point|center|4|0", new(4, 0), new(5, 1), false,
            out var moved, out var status, false).Should().BeTrue(status);
        moved.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "seed").Center.Should().Be(new Point2(5, 1));
        var dimension = moved.Dimensions.Single();
        dimension.Id.Should().Be(reference.Id);
        dimension.IsDriving.Should().BeFalse();
        dimension.ReferenceKeys.Should().Equal(reference.ReferenceKeys);
        dimension.Value.Should().BeApproximately(Math.Sqrt(52), 1e-9);
        SketchDimensionSolverService.TryGetDimensionMeasurement(moved, dimension, out var actual).Should().BeTrue();
        dimension.Value.Should().BeApproximately(actual, 1e-10);
        moved.Entities.OfType<CircleEntity>().Should().OnlyContain(c => c.Radius == 1);
        moved.Metadata.Patterns.Should().BeEquivalentTo(reopened.Metadata.Patterns);
        reopened.Entities.Should().Equal(snapshot, "the native/history snapshot remains immutable");
        AssertValid(moved);
        var final = Reopen(moved);
        final.Dimensions.Should().BeEquivalentTo(moved.Dimensions);
        AssertValid(final);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternCircleBodyPairDoesNotBecomeCenterOrOffsetDistance(bool reverse)
    {
        var document = Create();
        var keys = Pair(document, memberPair: true);
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(document, keys, "body", out _, out var status).Should().BeFalse();
        status.Should().Contain("No single editable Offset relationship");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedConcentricCircleBodyPairStillCreatesOffsetDimension(bool reverse)
    {
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("source"), new(0, 0), 3),
                new CircleEntity(EntityId.Create("dependent"), new(0, 0), 4)], [],
            [new SketchConstraint("offset", SketchConstraintKind.Offset, ["source", "dependent"], SketchConstraintState.Satisfied)]);
        var keys = new[] { "source", "dependent" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(document, keys, "gap", out var dimension, out _).Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.ReferenceKeys.Should().Equal("source", "dependent");
        dimension.Value.Should().Be(1);
    }

    private static DrawingDocument Create()
    {
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(4, 0), 1)]);
        DrawingPatternService.TryCreate(document, new PatternDefinition
        {
            Id = "radial", Kind = PatternKind.Radial, SourceEntityIds = ["seed"], Count = 4,
            Center = new(0, 0), FullCircle = true
        }, out var result, out var reason).Should().BeTrue(reason);
        return result;
    }

    private static string[] Pair(DrawingDocument document, bool memberPair)
    {
        var members = document.Metadata.Patterns.Single().Members;
        var first = members.Single(m => m.Column == 1).EntityId;
        return memberPair ? [first, members.Single(m => m.Column == 2).EntityId] : ["seed", first];
    }

    private static SketchDimension Build(DrawingDocument document, bool memberPair, bool reverse, bool canvas)
    {
        var ids = Pair(document, memberPair);
        if (reverse) Array.Reverse(ids);
        var keys = ids.Select(id =>
        {
            var center = document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == id).Center;
            return canvas ? $"{id}|point|center|{center.X.ToString("R", CultureInfo.InvariantCulture)}|{center.Y.ToString("R", CultureInfo.InvariantCulture)}" : id + ":center";
        }).ToArray();
        SketchCommandFactory.TryBuildDimension(document, keys, "distance", out var dimension, out var status,
            anchorOverride: new(6, 6)).Should().BeTrue(status);
        return dimension;
    }
    private static SketchSolveResult Solve(DrawingDocument document, SketchDimension dimension) =>
        new LegacySketchSolverAdapter().Solve(new(document, [], [dimension]));
    private static DrawingDocument Reopen(DrawingDocument document) => DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
    private static string Normalize(string key)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        return normalized;
    }
    private static void AssertGeometryUnchanged(DrawingDocument before, DrawingDocument after)
    {
        after.Entities.Select(e => e.Id).Should().Equal(before.Entities.Select(e => e.Id));
        foreach (var original in before.Entities.OfType<CircleEntity>())
        {
            var actual = after.Entities.OfType<CircleEntity>().Single(c => c.Id == original.Id);
            actual.Center.X.Should().BeApproximately(original.Center.X, 1e-10);
            actual.Center.Y.Should().BeApproximately(original.Center.Y, 1e-10);
            actual.Radius.Should().Be(original.Radius);
        }
        after.Metadata.Patterns.Should().BeEquivalentTo(before.Metadata.Patterns);
    }
    private static void AssertValid(DrawingDocument document)
    {
        DrawingPatternService.TryValidate(document, out var reason).Should().BeTrue(reason);
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(document, d));
    }
}
