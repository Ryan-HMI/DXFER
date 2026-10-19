using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class OffsetDimensionContourTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void MatchingContourEdgesPlaceOneOffsetDimensionAndEditOneToTwo(bool polyline, bool inset, bool keyed)
    {
        var document = CreateContour(polyline, inset, keyed);
        var sourceEntities = document.Entities.Where(e => e.Id.Value != "copy").ToArray();
        var relation = document.Constraints.Single(c => c.Kind == SketchConstraintKind.Offset);
        var previousId = document.Dimensions.SingleOrDefault()?.Id;
        var anchor = new Point2(5, inset ? 0.5 : -0.5);
        var dimension = Build(document, [SourceBottom(polyline), HorizontalEdge(document, "copy", bottom: true)], "placed", anchor);

        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.ReferenceKeys.Should().Equal(relation.ReferenceKeys);
        dimension.Id.Should().Be(previousId ?? "placed");
        dimension.Value.Should().BeApproximately(1, 1e-7);
        dimension.IsDriving.Should().BeTrue();
        dimension.Anchor.Should().Be(anchor);
        var placed = Solve(document, dimension);
        placed.Entities.Should().Equal(document.Entities);
        placed.Dimensions.Should().ContainSingle();

        var edited = Solve(placed, new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 2, dimension.Anchor, true));

        AssertBounds(edited, "copy", inset ? 2 : -2, inset ? 2 : -2, inset ? 8 : 12, inset ? 4 : 8);
        edited.Entities.Where(e => e.Id.Value != "copy").Should().Equal(sourceEntities);
        edited.Dimensions.Should().ContainSingle().Which.Value.Should().BeApproximately(2, 1e-7);
        var repeated = Build(edited, [HorizontalEdge(edited, "copy", true), SourceBottom(polyline)], "duplicate", new(3, -3));
        repeated.Id.Should().Be(dimension.Id);
        repeated.Value.Should().BeApproximately(2, 1e-7);
        var replaced = Solve(edited, repeated);
        replaced.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(3, -3));
        replaced.Entities.Should().Equal(edited.Entities);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ContourPointToMatchingSourceEdgeGapCanBePlacedAndEdited(bool polyline, bool inset)
    {
        var document = CreateContour(polyline, inset, keyed: false);
        var edge = HorizontalEdge(document, "copy", true);
        var dimension = Build(document, [edge + ":start", SourceBottom(polyline)], "point-gap");

        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        var placed = Solve(document, dimension);
        var edited = Solve(placed, new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 2, null, true));

        AssertBounds(edited, "copy", inset ? 2 : -2, inset ? 2 : -2, inset ? 8 : 12, inset ? 4 : 8);
        edited.Dimensions.Should().ContainSingle();
        edited.Entities.Where(e => e.Id.Value != "copy").Should().Equal(document.Entities.Where(e => e.Id.Value != "copy"));
    }

    [Theory]
    [InlineData(false, "opposite-edge", SketchDimensionKind.PointToLineDistance)]
    [InlineData(true, "opposite-edge", SketchDimensionKind.PointToLineDistance)]
    [InlineData(false, "opposite-point", SketchDimensionKind.PointToLineDistance)]
    [InlineData(true, "opposite-point", SketchDimensionKind.PointToLineDistance)]
    [InlineData(false, "diagonal", SketchDimensionKind.LinearDistance)]
    [InlineData(true, "diagonal", SketchDimensionKind.LinearDistance)]
    [InlineData(false, "perpendicular", SketchDimensionKind.Angle)]
    [InlineData(true, "perpendicular", SketchDimensionKind.Angle)]
    public void NonGapSelectionsStayOrdinaryDimensions(bool polyline, string selection, SketchDimensionKind expectedKind)
    {
        var document = CreateContour(polyline, inset: false, keyed: false);
        var source = SourceBottom(polyline);
        var target = HorizontalEdge(document, "copy", bottom: false);
        if (selection == "opposite-point") target += ":start";
        if (selection == "diagonal")
        {
            source += ":start";
            target = HorizontalEdge(document, "copy", bottom: true) + ":start";
        }
        if (selection == "perpendicular") target = VerticalEdge(document, "copy");

        var dimension = Build(document, [source, target], "ordinary");

        dimension.Kind.Should().Be(expectedKind);
        dimension.ReferenceKeys.Should().Equal(source, target);
        dimension.Id.Should().Be("ordinary");
        if (selection is "opposite-edge" or "opposite-point") dimension.Value.Should().BeApproximately(7, 1e-7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedPlacementPreservesExistingDrivenModeAndIdentity(bool polyline)
    {
        var document = CreateContour(polyline, inset: false, keyed: true);
        var original = document.Dimensions.Single();
        var driven = Solve(document, new(original.Id, original.Kind, original.ReferenceKeys, original.Value, original.Anchor, false));
        var anchor = new Point2(7, -0.5);

        for (var i = 0; i < 3; i++)
        {
            var replacement = Build(driven, [SourceBottom(polyline), HorizontalEdge(driven, "copy", true)], $"new-{i}", anchor);
            replacement.Id.Should().Be(original.Id);
            replacement.IsDriving.Should().BeFalse();
            replacement.ReferenceKeys.Should().Equal(original.ReferenceKeys);
            driven = Solve(driven, replacement);
            driven.Dimensions.Should().ContainSingle().Which.IsDriving.Should().BeFalse();
            driven.Entities.Should().Equal(document.Entities);
        }
        driven.Dimensions.Single().Anchor.Should().Be(anchor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousOffsetRelationshipsDoNotMapASelectedGap(bool polyline)
    {
        var document = CreateContour(polyline, inset: false, keyed: false);
        var relation = document.Constraints.Single(c => c.Kind == SketchConstraintKind.Offset);
        document = new(document.Entities, document.Dimensions, document.Constraints.Append(
            new SketchConstraint("ambiguous-offset", SketchConstraintKind.Offset, relation.ReferenceKeys, SketchConstraintState.Satisfied)));

        string[] selectedKeys = [SourceBottom(polyline), HorizontalEdge(document, "copy", true)];
        var dimension = Build(document, selectedKeys, "ordinary-gap");

        dimension.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
        dimension.Id.Should().Be("ordinary-gap");
        dimension.ReferenceKeys.Should().Equal(selectedKeys);
        dimension.Value.Should().BeApproximately(1, 1e-7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingEachLinkOfAnOffsetChainPreservesOtherGapAndDoesNotDuplicateDimensions(bool keyed)
    {
        var document = CreateContour(polyline: true, inset: false, keyed);
        SelectionOffsetService.TryOffset(document, ["copy"], new(5, -2), _ => EntityId.Create("second"),
            out var chained, out _, out var reason, out _, out _, keyed ? 1 : null, retainIntent: true)
            .Should().BeTrue(reason);
        var originalSource = chained.Entities.Single(e => e.Id.Value == "source");
        var first = Build(chained, ["source|segment|0", HorizontalEdge(chained, "copy", true)], "first-gap");
        first.Kind.Should().Be(SketchDimensionKind.Offset);
        var placed = Solve(chained, first);
        var firstEdited = Solve(placed, new(first.Id, first.Kind, first.ReferenceKeys, 2, first.Anchor, true));

        AssertBounds(firstEdited, "copy", -2, -2, 12, 8);
        AssertBounds(firstEdited, "second", -3, -3, 13, 9);
        var second = Build(firstEdited, [HorizontalEdge(firstEdited, "copy", true), HorizontalEdge(firstEdited, "second", true)], "second-gap");
        second.Kind.Should().Be(SketchDimensionKind.Offset);
        second.ReferenceKeys.Should().Equal("copy", "second");
        second.Id.Should().NotBe(first.Id);
        second.Value.Should().BeApproximately(1, 1e-7);
        var secondPlaced = Solve(firstEdited, second);
        var secondEdited = Solve(secondPlaced, new(second.Id, second.Kind, second.ReferenceKeys, 2, second.Anchor, true));

        AssertBounds(secondEdited, "copy", -2, -2, 12, 8);
        AssertBounds(secondEdited, "second", -4, -4, 14, 10);
        secondEdited.Entities.Single(e => e.Id.Value == "source").Should().Be(originalSource);
        secondEdited.Dimensions.Should().HaveCount(2).And.OnlyContain(d => d.Kind == SketchDimensionKind.Offset && d.Value == 2);
        var repeated = Build(secondEdited, [HorizontalEdge(secondEdited, "second", true), HorizontalEdge(secondEdited, "copy", true)], "duplicate-gap");
        repeated.Id.Should().Be(second.Id);
        Solve(secondEdited, repeated).Dimensions.Should().HaveCount(2);
    }

    private static DrawingDocument CreateContour(bool polyline, bool inset, bool keyed)
    {
        Point2[] vertices = [new(0, 0), new(10, 0), new(10, 6), new(0, 6), new(0, 0)];
        DrawingEntity[] sources = polyline
            ? [new PolylineEntity(EntityId.Create("source"), vertices)]
            : Enumerable.Range(0, 4).Select(i => (DrawingEntity)new LineEntity(EntityId.Create($"edge-{i}"), vertices[i], vertices[i + 1])).ToArray();
        var constraints = polyline ? Array.Empty<SketchConstraint>() : Enumerable.Range(0, 4).SelectMany(i => new[]
        {
            new SketchConstraint($"corner-{i}", SketchConstraintKind.Coincident, [$"edge-{i}:end", $"edge-{(i + 1) % 4}:start"], SketchConstraintState.Satisfied),
            new SketchConstraint($"axis-{i}", i % 2 == 0 ? SketchConstraintKind.Horizontal : SketchConstraintKind.Vertical, [$"edge-{i}"], SketchConstraintState.Satisfied)
        }).ToArray();
        SelectionOffsetService.TryOffset(new(sources, [], constraints), sources.Select(e => e.Id.Value), new(5, inset ? 1 : -1),
            _ => EntityId.Create("copy"), out var document, out var count, out var reason, out _, out _, keyed ? 1 : null, retainIntent: true)
            .Should().BeTrue(reason);
        count.Should().Be(1);
        document.Entities.Single(e => e.Id.Value == "copy").Should().BeOfType<PolylineEntity>();
        return document;
    }

    private static string SourceBottom(bool polyline) => polyline ? "source|segment|0" : "edge-0";

    private static string HorizontalEdge(DrawingDocument document, string id, bool bottom)
    {
        var entity = (PolylineEntity)document.Entities.Single(e => e.Id.Value == id);
        var y = bottom ? entity.GetBounds().MinY : entity.GetBounds().MaxY;
        var index = Enumerable.Range(0, entity.Vertices.Count - 1).Single(i =>
            Math.Abs(entity.Vertices[i].Y - y) < 1e-7 && Math.Abs(entity.Vertices[i + 1].Y - y) < 1e-7);
        return $"{id}|segment|{index}";
    }

    private static string VerticalEdge(DrawingDocument document, string id)
    {
        var entity = (PolylineEntity)document.Entities.Single(e => e.Id.Value == id);
        var x = entity.GetBounds().MaxX;
        var index = Enumerable.Range(0, entity.Vertices.Count - 1).Single(i =>
            Math.Abs(entity.Vertices[i].X - x) < 1e-7 && Math.Abs(entity.Vertices[i + 1].X - x) < 1e-7);
        return $"{id}|segment|{index}";
    }

    private static SketchDimension Build(DrawingDocument document, string[] keys, string id, Point2? anchor = null)
    {
        SketchCommandFactory.TryBuildDimension(document, keys, id, out var dimension, out var status, anchorOverride: anchor)
            .Should().BeTrue(status);
        return dimension;
    }

    private static DrawingDocument Solve(DrawingDocument document, SketchDimension dimension)
    {
        var result = new LegacySketchSolverAdapter().Solve(new(document, [], [dimension]));
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join("; ", result.Diagnostics));
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        return result.Document;
    }

    private static void AssertBounds(DrawingDocument document, string id, double minX, double minY, double maxX, double maxY)
    {
        var bounds = document.Entities.Single(e => e.Id.Value == id).GetBounds();
        bounds.MinX.Should().BeApproximately(minX, 1e-7);
        bounds.MinY.Should().BeApproximately(minY, 1e-7);
        bounds.MaxX.Should().BeApproximately(maxX, 1e-7);
        bounds.MaxY.Should().BeApproximately(maxY, 1e-7);
    }
}
