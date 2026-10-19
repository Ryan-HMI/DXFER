using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class AxisCollapseFallbackTests
{
    private static readonly DrawingDocumentMetadata Metadata = DrawingDocumentMetadata.Empty with
    {
        SourceFileName = "foreign.dxf", SourceSha256 = "source-hash", Units = DrawingUnits.Millimeters,
        EntityStyles = new Dictionary<string, DxfEntityStyle> { ["line"] = new("cuts", TrueColor: 0x123456) }
    };

    private static LineEntity Perpendicular(SketchConstraintKind kind, double length = 10) =>
        new(EntityId.Create("line"), new Point2(0, 0), kind == SketchConstraintKind.Horizontal ? new Point2(0, length) : new Point2(length, 0), true);

    private static DrawingDocument Document(LineEntity line, IEnumerable<SketchConstraint>? constraints = null,
        IEnumerable<SketchDimension>? dimensions = null) =>
        new(new DrawingEntity[] { line }, dimensions ?? Array.Empty<SketchDimension>(), constraints ?? Array.Empty<SketchConstraint>(), Metadata);

    private static SketchConstraint Axis(SketchConstraintKind kind, string reference = "line", SketchConstraintState state = SketchConstraintState.Unknown) =>
        new("axis", kind, new[] { reference }, state);

    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraintKind kind, string reference = "line") =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { Axis(kind, reference) }, Array.Empty<SketchDimension>()));

    private static Point2 Delta(SketchConstraintKind kind, double length) =>
        kind == SketchConstraintKind.Horizontal ? new Point2(length, 0) : new Point2(0, length);

    private static void AssertStableRefusal(DrawingDocument document, SketchSolveResult result)
    {
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().BeEquivalentTo(document.Dimensions, options => options.WithStrictOrdering());
        result.Document.Metadata.Should().BeSameAs(document.Metadata);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void ExactTiesPreferPositiveAxisForEitherEndpointOrderAndAnchor(SketchConstraintKind kind, bool reverse)
    {
        var line = Perpendicular(kind);
        if (reverse) line = line with { Start = line.End, End = line.Start };
        foreach (var anchor in new[] { "none", "start", "end" })
        {
            var document = Document(line, anchor == "none" ? null : new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "line:" + anchor }) });
            var result = Solve(document, kind);
            result.Status.Should().Be(SketchSolveStatus.Solved);
            var aligned = result.Document.Entities.Single().Should().BeOfType<LineEntity>().Subject;
            var delta = Delta(kind, 10);
            aligned.Start.Should().Be(anchor == "end" ? new Point2(line.End.X - delta.X, line.End.Y - delta.Y) : line.Start);
            aligned.End.Should().Be(anchor == "end" ? line.End : new Point2(line.Start.X + delta.X, line.Start.Y + delta.Y));
            aligned.Id.Should().Be(line.Id);
            aligned.IsConstruction.Should().BeTrue();
            result.Document.Metadata.Should().BeSameAs(Metadata);
            result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
            document.Entities.Single().Should().BeSameAs(line);
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void NearPerpendicularUsesClosestDirectionOnlyAtCollapseThreshold(SketchConstraintKind kind)
    {
        foreach (var projected in new[] { -2e-6, -1e-6, -0.5e-6, 0, 0.5e-6, 1e-6, 2e-6 })
        {
            var line = Perpendicular(kind) with { End = kind == SketchConstraintKind.Horizontal ? new Point2(projected, 10) : new Point2(10, projected) };
            var result = Solve(Document(line), kind);
            result.Status.Should().Be(SketchSolveStatus.Solved);
            var aligned = (LineEntity)result.Document.Entities.Single();
            var expected = Math.Abs(projected) <= 1e-6 ? Math.CopySign(Math.Sqrt(100 + projected * projected), projected == 0 ? 1 : projected) : projected;
            var delta = Delta(kind, expected);
            aligned.End.X.Should().BeApproximately(delta.X, 1e-12);
            aligned.End.Y.Should().BeApproximately(delta.Y, 1e-12);
            aligned.Start.Should().Be(line.Start);
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void OrdinaryProjectionKeepsItsOriginalCoordinateContract(SketchConstraintKind kind, bool reverse)
    {
        var line = new LineEntity(EntityId.Create("line"), new Point2(2, 3), new Point2(5, 7), true);
        if (reverse) line = line with { Start = line.End, End = line.Start };
        foreach (var fixEnd in new[] { false, true })
        {
            var document = Document(line, fixEnd ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "line:end" }) } : null);
            var result = Solve(document, kind);
            result.Status.Should().Be(SketchSolveStatus.Solved);
            var aligned = (LineEntity)result.Document.Entities.Single();
            var expectedStart = !fixEnd ? line.Start : kind == SketchConstraintKind.Horizontal
                ? new Point2(line.Start.X, line.End.Y) : new Point2(line.End.X, line.Start.Y);
            var expectedEnd = fixEnd ? line.End : kind == SketchConstraintKind.Horizontal
                ? new Point2(line.End.X, line.Start.Y) : new Point2(line.Start.X, line.End.Y);
            aligned.Start.Should().Be(expectedStart);
            aligned.End.Should().Be(expectedEnd);
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void ZeroAndSubToleranceSegmentsStayUnchangedWhileValidTinySegmentsRotate(SketchConstraintKind kind)
    {
        foreach (var length in new[] { 0, 0.5e-6, 1e-6, 1.01e-6, 2e-6 })
        {
            var line = Perpendicular(kind, length);
            var document = Document(line);
            var result = Solve(document, kind);
            if (length <= 1e-6)
            {
                AssertStableRefusal(document, result);
                SketchConstraintService.ApplyConstraint(document, Axis(kind)).Entities.Single().Should().BeSameAs(line);
            }
            else
            {
                result.Status.Should().Be(SketchSolveStatus.Solved);
                ((LineEntity)result.Document.Entities.Single()).End.Should().Be(Delta(kind, length));
            }
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void WholeAndBothEndpointFixesCannotBeBypassed(SketchConstraintKind kind)
    {
        foreach (var keys in new[] { new[] { "line" }, new[] { "line:start", "line:end" } })
        {
            var document = Document(Perpendicular(kind), new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, keys) });
            AssertStableRefusal(document, Solve(document, kind));
            SketchConstraintService.ApplyConstraint(document, Axis(kind)).Entities.Should().Equal(document.Entities);
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void DrivingLengthIsPreservedWithEitherAnchor(SketchConstraintKind kind, bool fixEnd)
    {
        var dimension = new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "line:start", "line:end" }, 10, new Point2(12, 12), true);
        var document = Document(Perpendicular(kind), new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { fixEnd ? "line:end" : "line:start" }) }, new[] { dimension });
        var result = Solve(document, kind);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Dimensions.Single().Should().BeEquivalentTo(dimension);
        SketchDimensionSolverService.IsDimensionSatisfied(result.Document, result.Document.Dimensions.Single()).Should().BeTrue();
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void ConflictingDrivingComponentStillRefusesWithEitherDimensionOrder(SketchConstraintKind kind)
    {
        var keys = new[] { "line:start", "line:end" };
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance, keys, 10, new Point2(12, 12), true);
        var component = new SketchDimension("component", kind == SketchConstraintKind.Horizontal ? SketchDimensionKind.VerticalDistance : SketchDimensionKind.HorizontalDistance,
            keys, 10, new Point2(15, 12), true);
        foreach (var dimensions in new[] { new[] { length, component }, new[] { component, length } })
        {
            var document = Document(Perpendicular(kind), dimensions: dimensions);
            AssertStableRefusal(document, Solve(document, kind));
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void OppositeAxisConflictsUnlessSuppressed(SketchConstraintKind kind, bool suppressed)
    {
        var opposite = new SketchConstraint("opposite", kind == SketchConstraintKind.Horizontal ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
            new[] { "line" }, suppressed ? SketchConstraintState.Suppressed : SketchConstraintState.Satisfied);
        var document = Document(Perpendicular(kind), new[] { opposite });
        var result = Solve(document, kind);
        if (!suppressed) AssertStableRefusal(document, result);
        else
        {
            result.Status.Should().Be(SketchSolveStatus.Solved);
            result.Document.Constraints.Single(c => c.Id == "opposite").State.Should().Be(SketchConstraintState.Suppressed);
        }
        var suppressedAxis = SketchConstraintService.ApplyConstraint(document, Axis(kind, state: SketchConstraintState.Suppressed));
        suppressedAxis.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, false)]
    [InlineData(SketchConstraintKind.Horizontal, true)]
    [InlineData(SketchConstraintKind.Vertical, false)]
    [InlineData(SketchConstraintKind.Vertical, true)]
    public void PolylineFallbackPreservesClosingAliasesAndNonTargetVertices(SketchConstraintKind kind, bool fixClosure)
    {
        var original = new[] { new Point2(0, 0), new(8, -3), new(8, 9), new(0, 6), new(0, 0) };
        if (kind == SketchConstraintKind.Vertical) original = original.Select(p => new Point2(p.Y, p.X)).ToArray();
        foreach (var alias in new[] { "line|segment|0:start", "line|segment|3:end" })
        {
            var polyline = new PolylineEntity(EntityId.Create("line"), original, true);
            var document = new DrawingDocument(new DrawingEntity[] { polyline }, Array.Empty<SketchDimension>(),
                fixClosure ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { alias }) } : Array.Empty<SketchConstraint>(), Metadata);
            var result = Solve(document, kind, "line|segment|3");
            result.Status.Should().Be(SketchSolveStatus.Solved);
            var aligned = result.Document.Entities.Single().Should().BeOfType<PolylineEntity>().Subject;
            var expected = original.ToArray();
            var delta = Delta(kind, 6);
            if (fixClosure) expected[3] = new Point2(-delta.X, -delta.Y);
            else expected[0] = expected[^1] = new Point2(original[3].X + delta.X, original[3].Y + delta.Y);
            aligned.Vertices.Should().Equal(expected);
            aligned.Id.Should().Be(polyline.Id);
            aligned.IsConstruction.Should().BeTrue();
            result.Document.Metadata.Should().BeSameAs(Metadata);
            polyline.Vertices.Should().Equal(original);
        }
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void DependentOffsetAndFixedSourceRefuseRotationWithoutDroppingIntent(SketchConstraintKind kind)
    {
        var source = Perpendicular(kind) with { Id = EntityId.Create("source") };
        var dependent = (LineEntity)source.Transform(kind == SketchConstraintKind.Horizontal ? Transform2.Translation(2, 0) : Transform2.Translation(0, 2)) with { Id = EntityId.Create("line") };
        var relation = new SketchConstraint("offset", SketchConstraintKind.Offset, new[] { "source", "line" }, SketchConstraintState.Satisfied);
        var fix = new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "source" });
        var gap = new SketchDimension("gap", SketchDimensionKind.Offset, relation.ReferenceKeys, 2, new Point2(5, 5), true);
        foreach (var constraints in new[] { new[] { relation, fix }, new[] { fix, relation } })
        {
            var document = new DrawingDocument(new DrawingEntity[] { source, dependent }, new[] { gap }, constraints, Metadata);
            SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);
            var result = Solve(document, kind);
            AssertStableRefusal(document, result);
            result.Document.Constraints.Single(c => c.Id == "offset").ReferenceKeys.Should().Equal(relation.ReferenceKeys);
            result.Document.Constraints.Single(c => c.Id == "offset").State.Should().Be(SketchConstraintState.Satisfied);
        }
    }
}
