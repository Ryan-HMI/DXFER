using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PolylineAxisConstraintTests
{
    private static DrawingDocument Fixture(bool closed, IEnumerable<SketchConstraint>? constraints = null,
        IEnumerable<SketchDimension>? dimensions = null)
    {
        var points = new[] { new Point2(0, 0), new(8, 0), new(7, 5), new(1, 6) };
        return new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("poly"), closed ? points.Append(points[0]) : points, true),
            new CircleEntity(EntityId.Create("untouched"), new Point2(20, 20), 2)
        }, dimensions ?? Array.Empty<SketchDimension>(), constraints ?? Array.Empty<SketchConstraint>(),
            DrawingDocumentMetadata.Empty with
            {
                SourceFileName = "foreign-contour.dxf", SourceSha256 = "foreign-source-hash",
                NormalizedFileName = "normalized.dxf", NormalizedSha256 = "normalized-hash",
                Units = DrawingUnits.Millimeters, TrustedSource = true,
                Warnings = new[] { new DrawingDocumentWarning("fixture-warning", DrawingDocumentWarningSeverity.Warning, "Preserve source provenance.") },
                EntityStyles = new Dictionary<string, DxfEntityStyle> { ["poly"] = new("cuts", TrueColor: 0x123456) }
            });
    }

    private static SketchConstraint Axis(SketchConstraintKind kind, int segment = 2) =>
        new("axis", kind, new[] { $"poly|segment|{segment}" });

    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraint constraint) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { constraint }, Array.Empty<SketchDimension>()));

    private static void AssertPolyline(DrawingDocument before, DrawingDocument after, IReadOnlyList<Point2> expected)
    {
        after.Entities.Should().HaveCount(before.Entities.Count);
        var polyline = after.Entities[0].Should().BeOfType<PolylineEntity>().Subject;
        polyline.Id.Should().Be(before.Entities[0].Id);
        polyline.IsConstruction.Should().BeTrue();
        polyline.Vertices.Should().Equal(expected);
        after.Entities[1].Should().BeSameAs(before.Entities[1]);
        after.Metadata.Should().BeSameAs(before.Metadata);
        after.Metadata.EntityStyles["poly"].Should().Be(before.Metadata.EntityStyles["poly"]);
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void DirectAxisCandidatePreservesPolylineAndOnlyChangesTargetEndpoint(bool closed, SketchConstraintKind kind)
    {
        var document = Fixture(closed);
        var original = ((PolylineEntity)document.Entities[0]).Vertices.ToArray();
        var expected = original.ToArray();
        expected[3] = kind == SketchConstraintKind.Horizontal ? new Point2(1, 5) : new Point2(7, 6);
        var candidate = SketchConstraintService.ApplyConstraint(document, Axis(kind));
        AssertPolyline(document, candidate, expected);
        candidate.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
        ((PolylineEntity)document.Entities[0]).Vertices.Should().Equal(original);
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void SolverAcceptsFeasiblePolylineAxisWithoutReplacingParent(bool closed, SketchConstraintKind kind)
    {
        var document = Fixture(closed);
        var expected = ((PolylineEntity)document.Entities[0]).Vertices.ToArray();
        expected[3] = kind == SketchConstraintKind.Horizontal ? new Point2(1, 5) : new Point2(7, 6);
        var result = Solve(document, Axis(kind));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPolyline(document, result.Document, expected);
        result.Document.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void FixedTargetEndMovesOnlyStartAndPreservesMetadata(bool closed, SketchConstraintKind kind)
    {
        var document = Fixture(closed, new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "poly|segment|2:end" }) });
        var expected = ((PolylineEntity)document.Entities[0]).Vertices.ToArray();
        expected[2] = kind == SketchConstraintKind.Horizontal ? new Point2(7, 6) : new Point2(1, 5);
        var result = Solve(document, Axis(kind));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPolyline(document, result.Document, expected);
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void ClosingEndpointAliasesStayTogetherAndFixedAliasAnchorsEitherReference(bool fixedAlias, SketchConstraintKind kind)
    {
        foreach (var alias in new[] { "poly|segment|0:start", "poly|segment|3:end" })
        {
            var document = Fixture(true, fixedAlias ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { alias }) } : null);
            var expected = ((PolylineEntity)document.Entities[0]).Vertices.ToArray();
            if (fixedAlias) expected[3] = kind == SketchConstraintKind.Horizontal ? new Point2(1, 0) : new Point2(0, 6);
            else expected[0] = expected[^1] = kind == SketchConstraintKind.Horizontal ? new Point2(0, 6) : new Point2(1, 0);
            var result = Solve(document, Axis(kind, 3));
            result.Status.Should().Be(SketchSolveStatus.Solved);
            AssertPolyline(document, result.Document, expected);
        }
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void WholeSegmentAndBothEndpointFixesRefuseWithoutChangingGeometry(bool closed, SketchConstraintKind kind)
    {
        foreach (var keys in new[] { new[] { "poly" }, new[] { "poly|segment|2" }, new[] { "poly|segment|2:start", "poly|segment|2:end" } })
        {
            var document = Fixture(closed, new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, keys) });
            var direct = SketchConstraintService.ApplyConstraint(document, Axis(kind));
            direct.Entities.Should().Equal(document.Entities);
            var result = Solve(document, Axis(kind));
            result.Status.Should().NotBe(SketchSolveStatus.Solved);
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Metadata.Should().BeSameAs(document.Metadata);
            result.Document.Constraints.Single(c => c.Id == "axis").State.Should().Be(SketchConstraintState.Unsatisfied);
        }
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void DrivingLengthRemainsExactWhenAxisAlignmentIsFeasible(bool fixEnd, SketchConstraintKind kind)
    {
        var length = Math.Sqrt(37);
        var dimension = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "poly|segment|2:start", "poly|segment|2:end" }, length, new Point2(4, 8), true);
        var document = Fixture(true, fixEnd ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "poly|segment|2:end" }) } : null,
            new[] { dimension });
        var result = Solve(document, Axis(kind));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        var vertices = result.Document.Entities[0].Should().BeOfType<PolylineEntity>().Subject.Vertices;
        vertices[0].Should().Be(new Point2(0, 0));
        vertices[1].Should().Be(new Point2(8, 0));
        vertices[^1].Should().Be(vertices[0]);
        if (fixEnd) vertices[3].Should().Be(new Point2(1, 6));
        else vertices[2].Should().Be(new Point2(7, 5));
        result.Document.Dimensions.Single().Should().BeEquivalentTo(dimension);
        SketchDimensionSolverService.IsDimensionSatisfied(result.Document, result.Document.Dimensions.Single()).Should().BeTrue();
        SketchConstraintService.ValidateConstraint(result.Document, Axis(kind)).State.Should().Be(SketchConstraintState.Satisfied);
        result.Document.Metadata.Should().BeSameAs(document.Metadata);
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void IncompatibleDrivingComponentAndLengthRefuseStable(bool closed, SketchConstraintKind kind)
    {
        var keys = new[] { "poly|segment|2:start", "poly|segment|2:end" };
        var component = new SketchDimension("component", kind == SketchConstraintKind.Horizontal
            ? SketchDimensionKind.VerticalDistance : SketchDimensionKind.HorizontalDistance, keys,
            kind == SketchConstraintKind.Horizontal ? 1 : 6, new Point2(10, 8), true);
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance, keys, Math.Sqrt(37), new Point2(4, 8), true);
        foreach (var dimensions in new[] { new[] { component, length }, new[] { length, component } })
        {
            var document = Fixture(closed, dimensions: dimensions);
            var result = Solve(document, Axis(kind));
            result.Status.Should().NotBe(SketchSolveStatus.Solved);
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Dimensions.Should().BeEquivalentTo(document.Dimensions, options => options.WithStrictOrdering());
            result.Document.Metadata.Should().BeSameAs(document.Metadata);
        }
    }

    [Theory]
    [InlineData(false, SketchConstraintKind.Horizontal)]
    [InlineData(true, SketchConstraintKind.Horizontal)]
    [InlineData(false, SketchConstraintKind.Vertical)]
    [InlineData(true, SketchConstraintKind.Vertical)]
    public void AlreadyAlignedEdgeSavesSatisfiedWithoutChangingGeometry(bool closed, SketchConstraintKind kind)
    {
        var seed = Fixture(closed);
        var polyline = (PolylineEntity)seed.Entities[0];
        polyline = polyline.WithVertex(3, kind == SketchConstraintKind.Horizontal ? new Point2(1, 5) : new Point2(7, 6));
        var document = new DrawingDocument(new[] { polyline, seed.Entities[1] }, seed.Dimensions, seed.Constraints, seed.Metadata);
        var result = Solve(document, Axis(kind));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPolyline(document, result.Document, polyline.Vertices);
        result.Document.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void PolygonEdgeAlignmentIsUnsupportedWithoutConvertingThePolygon(SketchConstraintKind kind)
    {
        var polygon = new PolygonEntity(EntityId.Create("poly"), new Point2(0, 0), 5, 30, 6);
        var document = new DrawingDocument(new DrawingEntity[] { polygon });
        var direct = SketchConstraintService.ApplyConstraint(document, Axis(kind, 0));
        direct.Entities.Single().Should().BeSameAs(polygon);
        direct.Constraints.Single().State.Should().Be(SketchConstraintState.Unsatisfied);
        var result = Solve(document, Axis(kind, 0));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Single().Should().BeSameAs(polygon);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void PerpendicularBareLineProjectionStillRefusesRatherThanCommittingZeroLength(SketchConstraintKind kind, bool fixEnd)
    {
        // A length-preserving quarter-turn is a separate orientation contract, not part of the proxy-write fix.
        var line = new LineEntity(EntityId.Create("line"), new Point2(0, 0),
            kind == SketchConstraintKind.Horizontal ? new Point2(0, 10) : new Point2(10, 0));
        var document = new DrawingDocument(new DrawingEntity[] { line }, Array.Empty<SketchDimension>(),
            fixEnd ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "line:end" }) } : Array.Empty<SketchConstraint>());
        var result = Solve(document, new SketchConstraint("axis", kind, new[] { "line" }));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Single().Should().BeSameAs(line);
        result.Document.Constraints.Single(c => c.Id == "axis").State.Should().Be(SketchConstraintState.Unsatisfied);
    }
}
