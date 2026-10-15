using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchEllipseAxisDimensionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 37)]
    [InlineData(0, 90)]
    [InlineData(1, 0)]
    [InlineData(1, 37)]
    [InlineData(1, 90)]
    public void EditingKeyedDiameterPreservesOtherAxisCenterOrientationAndIntent(int axis, double angle)
    {
        var document = Create(angle);
        var original = (EllipseEntity)document.Entities[0];
        var edit = WithValue(document.Dimensions[axis], axis == 0 ? 6 : 4);

        var result = Solve(document, edit);

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.AffectedDiagnostics.Should().BeEmpty();
        AssertEllipse(result.Document, original, axis == 0 ? 6 : 5, axis == 0 ? 3 : 4);
        result.Document.Dimensions[axis].Should().BeEquivalentTo(edit);
        result.Document.Dimensions[1 - axis].Should().BeSameAs(document.Dimensions[1 - axis]);
        result.Document.Metadata.Should().BeSameAs(document.Metadata);
        result.Document.Dimensions.Should().OnlyContain(dimension =>
            SketchDimensionSolverService.IsDimensionSatisfied(result.Document, dimension));
        document.Entities[0].Should().BeSameAs(original);
        original.MinorRadiusRatio.Should().BeApproximately(0.6, 1e-12);
    }

    [Fact]
    public void ReapplyingBothDiametersDoesNotRescaleTheOtherAxis()
    {
        var document = Create(37);
        var original = (EllipseEntity)document.Entities[0];
        var edited = Solve(document, WithValue(document.Dimensions[0], 6));
        edited.Status.Should().Be(SketchSolveStatus.Solved);
        var minorEdit = Solve(edited.Document, WithValue(document.Dimensions[1], 4));
        minorEdit.Status.Should().Be(SketchSolveStatus.Solved);

        var reapplied = new LegacySketchSolverAdapter().Solve(SketchSolveRequest.FromDocument(minorEdit.Document));

        reapplied.Status.Should().Be(SketchSolveStatus.Solved);
        AssertEllipse(reapplied.Document, original, 6, 4);
        var direct = SketchDimensionSolverService.ApplyDimensions(document,
            new[] { WithValue(document.Dimensions[0], 6), WithValue(document.Dimensions[1], 4) });
        AssertEllipse(direct, original, 6, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InvalidDiameterRollsBackGeometryAndKeepsFailedIntent(int axis)
    {
        var document = Create(37);
        foreach (var value in new[] { 0, -document.Dimensions[axis].Value, 1e-9, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var edit = WithValue(document.Dimensions[axis], value);

            var result = Solve(document, edit);

            result.Status.Should().Be(SketchSolveStatus.OverConstrained);
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Metadata.Should().BeSameAs(document.Metadata);
            result.Document.Dimensions[axis].Should().BeSameAs(edit);
            result.Document.Dimensions[1 - axis].Should().BeSameAs(document.Dimensions[1 - axis]);
            result.AffectedDiagnostics.Should().ContainSingle().Which.ItemId.Should().Be(edit.Id);
            SketchDimensionSolverService.IsDimensionSatisfied(result.Document, edit).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(0, "shape", false, false)]
    [InlineData(1, "shape", false, false)]
    [InlineData(0, "shape:center", false, true)]
    [InlineData(1, "shape:center", false, true)]
    [InlineData(0, "shape", true, true)]
    [InlineData(1, "shape", true, true)]
    public void RespectsWholeEntityFixButAllowsFixedCenterAndSuppressedFix(int axis, string reference, bool suppressed, bool succeeds)
    {
        var source = Create(37);
        var constraint = new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { reference },
            suppressed ? SketchConstraintState.Suppressed : SketchConstraintState.Satisfied);
        var document = new DrawingDocument(source.Entities, source.Dimensions, new[] { constraint }, source.Metadata);
        var edit = WithValue(document.Dimensions[axis], axis == 0 ? 6 : 4);

        var result = Solve(document, edit);

        result.Status.Should().Be(succeeds ? SketchSolveStatus.Solved : SketchSolveStatus.OverConstrained);
        result.Document.Constraints.Should().ContainSingle().Which.Should().BeEquivalentTo(constraint);
        if (succeeds)
        {
            AssertEllipse(result.Document, (EllipseEntity)source.Entities[0], axis == 0 ? 6 : 5, axis == 0 ? 3 : 4);
        }
        else
        {
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Metadata.Should().BeSameAs(document.Metadata);
            result.AffectedDiagnostics.Should().ContainSingle().Which.ItemId.Should().Be(edit.Id);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConflictingAxisDimensionPreservesGeometryAndOtherValidDimensions(int axis)
    {
        var document = Create(37);
        var dimension = document.Dimensions[axis];
        var conflict = new SketchDimension("conflict", dimension.Kind, dimension.ReferenceKeys, 8, dimension.Anchor, true);

        var result = Solve(document, conflict);

        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Metadata.Should().BeSameAs(document.Metadata);
        result.Document.Dimensions.Take(2).Should().Equal(document.Dimensions);
        result.Document.Dimensions.Last().Should().BeSameAs(conflict);
        result.AffectedDiagnostics.Should().ContainSingle().Which.ItemId.Should().Be(conflict.Id);
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(result.Document, d));
        RoundTrip(result.Document).Should().BeEquivalentTo(result.Document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NamedAxisUsesLiveGeometryNotStoredCoordinatesAndPreservesUndimensionedAxis(int axis)
    {
        var source = Create(37);
        var ellipse = (EllipseEntity)source.Entities[0] with
        {
            StartParameterDegrees = 25,
            EndParameterDegrees = 220,
            IsConstruction = true
        };
        var document = new DrawingDocument(new[] { ellipse }, Array.Empty<SketchDimension>(), Array.Empty<SketchConstraint>(), source.Metadata);
        var label = axis == 0 ? "MAJOR" : "MINOR";
        var edit = new SketchDimension("axis", SketchDimensionKind.LinearDistance,
            new[] { $"shape|point|{label}-END|999|999", $"shape|point|{label}-START|-999|-999" },
            axis == 0 ? 6 : 4, new Point2(0, 0), true);

        var result = Solve(document, edit);

        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertEllipse(result.Document, ellipse, axis == 0 ? 6 : 5, axis == 0 ? 3 : 4);
        result.Document.Dimensions.Single().ReferenceKeys.Should().Equal(edit.ReferenceKeys);
    }

    [Fact]
    public void ReferenceDimensionDoesNotResizeEllipse()
    {
        var document = Create();
        var axis = document.Dimensions[0];
        var reference = new SketchDimension("reference", axis.Kind, axis.ReferenceKeys, 6, axis.Anchor, false);

        var result = SketchDimensionSolverService.ApplyDimension(document, reference);

        result.Entities.Should().Equal(document.Entities);
        result.Dimensions.Last().Should().BeSameAs(reference);
    }

    [Fact]
    public void ReopenedFailedIntentCanSolveAndSubsequentEditsRemainStable()
    {
        var source = Create(37);
        var failedIntent = new DrawingDocument(source.Entities,
            new[] { WithValue(source.Dimensions[0], 6), source.Dimensions[1] }, source.Constraints, source.Metadata);
        var reopened = RoundTrip(failedIntent);
        SketchDimensionSolverService.IsDimensionSatisfied(reopened, reopened.Dimensions[0]).Should().BeFalse();

        var recovered = new LegacySketchSolverAdapter().Solve(SketchSolveRequest.FromDocument(reopened));

        recovered.Status.Should().Be(SketchSolveStatus.Solved);
        AssertEllipse(recovered.Document, (EllipseEntity)source.Entities[0], 6, 3);
        var saved = RoundTrip(recovered.Document);
        saved.Should().BeEquivalentTo(recovered.Document);
        var minorEdit = Solve(saved, WithValue(saved.Dimensions[1], 4));
        minorEdit.Status.Should().Be(SketchSolveStatus.Solved);
        AssertEllipse(minorEdit.Document, (EllipseEntity)source.Entities[0], 6, 4);
    }

    [Fact]
    public void AxisEditsRemainIndependentOfApplicationOrderWithoutRelabelingAxes()
    {
        var source = Create(37);
        var edits = new[] { WithValue(source.Dimensions[0], 2), WithValue(source.Dimensions[1], 4) };
        foreach (var dimensions in new[] { edits, edits.Reverse().ToArray() })
        {
            var result = Solve(source, dimensions);
            result.Status.Should().Be(SketchSolveStatus.Solved);
            for (var i = 0; i < 5; i++)
            {
                result = new LegacySketchSolverAdapter().Solve(SketchSolveRequest.FromDocument(result.Document));
                result.Status.Should().Be(SketchSolveStatus.Solved);
                AssertEllipse(result.Document, (EllipseEntity)source.Entities[0], 2, 4);
            }
        }
    }

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var text = DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));
        var restored = DxfDocumentReader.Read(text, out var nativeRestored);
        nativeRestored.Should().BeTrue();
        return restored;
    }

    private static DrawingDocument Create(double angle = 0)
    {
        var radians = angle * Math.PI / 180;
        var center = new Point2(7, -2);
        var values = new Dictionary<string, double> { ["major"] = 5, ["minor"] = 3 };
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool("ellipse", new[]
        {
            center,
            new Point2(center.X + 10 * Math.Cos(radians), center.Y + 10 * Math.Sin(radians)),
            new Point2(center.X - 10 * Math.Sin(radians), center.Y + 10 * Math.Cos(radians))
        }, _ => EntityId.Create("shape"), isConstruction: false, values);
        var sequence = 0;
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool("ellipse", entities,
            values, () => $"diameter-{++sequence}");
        return new DrawingDocument(entities, dimensions, Array.Empty<SketchConstraint>(),
            new DrawingDocumentMetadata { SourceFileName = "ellipse.dxf", Units = DrawingUnits.Inches });
    }

    private static SketchDimension WithValue(SketchDimension dimension, double value) =>
        new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, value, dimension.Anchor, dimension.IsDriving);

    private static SketchSolveResult Solve(DrawingDocument document, params SketchDimension[] dimensions) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,
            Array.Empty<SketchConstraint>(), dimensions));

    private static void AssertEllipse(DrawingDocument document, EllipseEntity original, double major, double minor)
    {
        var ellipse = document.Entities[0].Should().BeOfType<EllipseEntity>().Subject;
        var originalLength = Length(original.MajorAxisEndPoint);
        ellipse.Id.Should().Be(original.Id);
        ellipse.Center.Should().Be(original.Center);
        ellipse.MajorAxisEndPoint.X.Should().BeApproximately(original.MajorAxisEndPoint.X / originalLength * major / 2, 1e-10);
        ellipse.MajorAxisEndPoint.Y.Should().BeApproximately(original.MajorAxisEndPoint.Y / originalLength * major / 2, 1e-10);
        (2 * Length(ellipse.MajorAxisEndPoint)).Should().BeApproximately(major, 1e-10);
        (2 * Length(ellipse.MajorAxisEndPoint) * ellipse.MinorRadiusRatio).Should().BeApproximately(minor, 1e-10);
        ellipse.StartParameterDegrees.Should().Be(original.StartParameterDegrees);
        ellipse.EndParameterDegrees.Should().Be(original.EndParameterDegrees);
        ellipse.IsConstruction.Should().Be(original.IsConstruction);
    }

    private static double Length(Point2 vector) => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
}
