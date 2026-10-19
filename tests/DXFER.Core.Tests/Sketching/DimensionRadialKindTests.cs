using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class DimensionRadialKindTests
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
    public void ConversionPreservesGeometryAndAllOtherIntent(bool arc, bool diameter, bool driving)
    {
        var original = Fixture(arc, diameter, driving);
        var snapshot = DxferDocumentSerializer.Write(original);
        var kind = diameter ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter;

        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out var reason).Should().BeTrue();

        AssertConversion(original, result, kind, diameter ? 3 : 6);
        reason.Should().NotBeEmpty();
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions[0]).Should().BeTrue();
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
        SketchDimensionModeService.TrySetRadialKind(result, "size", original.Dimensions[0].Kind, out var restored, out _).Should().BeTrue();
        DxferDocumentSerializer.Write(restored).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedDrivingIntentScalesWithoutSolvingEvenOnFixedGeometry(bool arc, bool diameter)
    {
        var original = Fixture(arc, diameter, driving: true, value: diameter ? 10 : 5);
        original = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fixed-shape", SketchConstraintKind.Fix,
                ["shape"], SketchConstraintState.Unknown)), original.Metadata);
        SketchDimensionSolverService.IsDimensionSatisfied(original, original.Dimensions[0]).Should().BeFalse();
        var kind = diameter ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter;

        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out _).Should().BeTrue();

        AssertConversion(original, result, kind, diameter ? 5 : 10);
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions[0]).Should().BeFalse();
        result.Constraints.Last().State.Should().Be(SketchConstraintState.Unknown);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DrivenConversionMeasuresGeometryInsteadOfScalingStaleValue(bool arc, bool diameter)
    {
        var original = Fixture(arc, diameter, driving: false, value: 99);
        var kind = diameter ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter;

        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out _).Should().BeTrue();

        AssertConversion(original, result, kind, diameter ? 3 : 6);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PolygonControlRadiusConversionPreservesDefinitionAndCount(bool diameter, bool driving)
    {
        var original = Fixture(diameter: diameter, driving: driving, value: diameter ? 10 : 5);
        var polygon = new PolygonEntity(EntityId.Create("shape"), new(2, 4), 3, 30, 6, true);
        var count = new SketchDimension("count", SketchDimensionKind.Count, ["shape"], 6, new(2, 0), true);
        original = new DrawingDocument([polygon, original.Entities[1]], original.Dimensions.Append(count),
            original.Constraints, original.Metadata);
        var kind = diameter ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter;

        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out _).Should().BeTrue();

        AssertConversion(original, result, kind, driving ? (diameter ? 5 : 10) : (diameter ? 3 : 6));
        result.Entities[0].Should().BeSameAs(polygon);
        result.Dimensions[2].Should().BeSameAs(count);
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions[0]).Should().Be(!driving);
        SketchDimensionSolverService.IsDimensionSatisfied(result, count).Should().BeTrue();
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true)));
        DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(result));
    }

    [Theory]
    [InlineData("reference", false)]
    [InlineData("reference", true)]
    [InlineData("locked", false)]
    [InlineData("locked", true)]
    [InlineData("hidden", false)]
    [InlineData("hidden", true)]
    public void ProtectedGeometryRefusesIncludingNoOp(string protection, bool noOp)
    {
        var original = Fixture();
        var metadata = original.Metadata with
        {
            Mode = protection == "reference" ? DrawingDocumentMode.ReferenceOnly : DrawingDocumentMode.Editable,
            Layers = new Dictionary<string, DxfEntityStyle>
            {
                ["parts"] = new("parts", IsLocked: protection == "locked", IsHidden: protection == "hidden")
            }
        };
        original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, metadata);

        AssertRefusal(original, noOp ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("shape:bogus")]
    [InlineData("shape:start")]
    [InlineData("shape:end")]
    [InlineData("shape:center")]
    [InlineData("shape|point|center|2|4")]
    [InlineData("shape|segment|0")]
    [InlineData("shape|segment|-1")]
    [InlineData("shape|segment|bad")]
    [InlineData("$origin:center")]
    [InlineData("dimension:size")]
    [InlineData("constraint:fixed-other")]
    public void MalformedMissingOrNonEntityReferencesRefuse(string key)
    {
        AssertRefusal(WithReferences(Fixture(), [key]));
    }

    [Fact]
    public void EmptyOrMultipleReferencesAndDuplicateDimensionIdsRefuse()
    {
        var original = Fixture();
        AssertRefusal(WithReferences(original, []));
        AssertRefusal(WithReferences(original, ["shape", "other"]));
        AssertRefusal(new DrawingDocument(original.Entities,
            original.Dimensions.Append(original.Dimensions[0]), original.Constraints, original.Metadata));
    }

    [Theory]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("ellipse")]
    [InlineData("spline")]
    [InlineData("point")]
    public void OtherEntityTypesRefuse(string type)
    {
        var original = Fixture();
        var id = EntityId.Create("shape");
        DrawingEntity shape = type switch
        {
            "line" => new LineEntity(id, new(0, 0), new(6, 0)),
            "polyline" => new PolylineEntity(id, [new(0, 0), new(6, 0), new(6, 4)]),
            "ellipse" => new EllipseEntity(id, new(0, 0), new(3, 0), 0.5),
            "spline" => SplineEntity.FromFitPoints(id, [new(0, 0), new(3, 2), new(6, 0)]),
            _ => new PointEntity(id, new(0, 0))
        };
        AssertRefusal(new DrawingDocument([shape, original.Entities[1]], original.Dimensions,
            original.Constraints, original.Metadata));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidDimensionValuesRefuseForEitherMode(bool driving)
    {
        foreach (var value in new[] { 0, -3, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            AssertRefusal(Fixture(driving: driving, value: value));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidGeometryRadiusRefuses(bool arc)
    {
        foreach (var radius in new[] { 0, -3, double.NaN, double.PositiveInfinity })
        {
            var original = Fixture(arc);
            DrawingEntity invalid = arc
                ? ((ArcEntity)original.Entities[0]) with { Radius = radius }
                : ((CircleEntity)original.Entities[0]) with { Radius = radius };
            AssertRefusal(new DrawingDocument([invalid, original.Entities[1]], original.Dimensions,
                original.Constraints, original.Metadata));
        }
    }

    [Fact]
    public void OverflowAndUnderflowRefuseWithoutChangingIntent()
    {
        AssertRefusal(Fixture(value: double.MaxValue));
        AssertRefusal(Fixture(diameter: true, value: double.Epsilon), SketchDimensionKind.Radius);
        var driven = Fixture(driving: false);
        AssertRefusal(new DrawingDocument(
            [((CircleEntity)driven.Entities[0]) with { Radius = double.MaxValue }, driven.Entities[1]],
            driven.Dimensions, driven.Constraints, driven.Metadata));
    }

    [Fact]
    public void UnsupportedKindsOrMissingIdRefuseAndValidNoOpRetainsSnapshot()
    {
        var original = Fixture(driving: false, value: 99);
        AssertRefusal(original, SketchDimensionKind.LinearDistance);
        AssertRefusal(original, (SketchDimensionKind)999);
        SketchDimensionModeService.TrySetRadialKind(original, "unrelated", SketchDimensionKind.Diameter,
            out var unsupported, out _).Should().BeFalse();
        unsupported.Should().BeSameAs(original);
        SketchDimensionModeService.TrySetRadialKind(original, "missing", SketchDimensionKind.Diameter,
            out var missing, out _).Should().BeFalse();
        missing.Should().BeSameAs(original);
        SketchDimensionModeService.TrySetRadialKind(original, "size", SketchDimensionKind.Radius,
            out var unchanged, out var reason).Should().BeTrue();
        unchanged.Should().BeSameAs(original);
        reason.Should().Be("Radial dimension kind unchanged.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeReopenAndSubsequentEditRetainConvertedKind(bool arc, bool diameter)
    {
        var original = Fixture(arc, diameter);
        var before = DxferDocumentSerializer.Write(original);
        var kind = diameter ? SketchDimensionKind.Radius : SketchDimensionKind.Diameter;
        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out _).Should().BeTrue();
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true)));
        DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(result));
        var dimension = reopened.Dimensions[0];
        var edited = SketchDimensionSolverService.ApplyDimension(reopened,
            new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 8, dimension.Anchor, true));

        edited.Dimensions[0].Kind.Should().Be(kind);
        edited.Dimensions[0].Value.Should().Be(8);
        var radius = arc ? ((ArcEntity)edited.Entities[0]).Radius : ((CircleEntity)edited.Entities[0]).Radius;
        radius.Should().Be(kind == SketchDimensionKind.Diameter ? 4 : 8);
        SketchDimensionSolverService.IsDimensionSatisfied(edited, edited.Dimensions[0]).Should().BeTrue();
        DxferDocumentSerializer.Write(original).Should().Be(before);
        DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(result));
    }

    private static DrawingDocument Fixture(bool arc = false, bool diameter = false, bool driving = true, double? value = null)
    {
        DrawingEntity shape = arc
            ? new ArcEntity(EntityId.Create("shape"), new(2, 4), 3, 20, 160, true)
            : new CircleEntity(EntityId.Create("shape"), new(2, 4), 3, true);
        return new DrawingDocument([shape, new LineEntity(EntityId.Create("other"), new(0, 0), new(10, 0))],
            [new("size", diameter ? SketchDimensionKind.Diameter : SketchDimensionKind.Radius,
                ["shape"], value ?? (diameter ? 6 : 3), new(7, 5), driving),
             new("unrelated", SketchDimensionKind.LinearDistance, ["other:start", "other:end"], 10, new(5, 2), true)],
            [new("fixed-other", SketchConstraintKind.Fix, ["other"], SketchConstraintState.Satisfied)],
            DrawingDocumentMetadata.Empty with
            {
                Units = DrawingUnits.Inches,
                SourceFileName = "source.dxf",
                EntityStyles = new Dictionary<string, DxfEntityStyle> { ["shape"] = new("parts", "CONTINUOUS", 3) }
            });
    }

    private static DrawingDocument WithReferences(DrawingDocument document, string[] references) => new(
        document.Entities,
        [new("size", SketchDimensionKind.Radius, references, 3, new(7, 5), true), document.Dimensions[1]],
        document.Constraints, document.Metadata);

    private static void AssertConversion(DrawingDocument original, DrawingDocument result, SketchDimensionKind kind, double value)
    {
        var before = original.Dimensions[0];
        var after = result.Dimensions[0];
        after.Id.Should().Be(before.Id);
        after.Kind.Should().Be(kind);
        after.Value.Should().Be(value);
        after.ReferenceKeys.Should().Equal(before.ReferenceKeys);
        after.Anchor.Should().Be(before.Anchor);
        after.IsDriving.Should().Be(before.IsDriving);
        result.Dimensions.Count.Should().Be(original.Dimensions.Count);
        result.Dimensions[1].Should().BeSameAs(original.Dimensions[1]);
        result.Metadata.Should().BeSameAs(original.Metadata);
        result.Entities.Count.Should().Be(original.Entities.Count);
        for (var index = 0; index < original.Entities.Count; index++)
            result.Entities[index].Should().BeSameAs(original.Entities[index]);
        result.Constraints.Count.Should().Be(original.Constraints.Count);
        for (var index = 0; index < original.Constraints.Count; index++)
            result.Constraints[index].Should().BeSameAs(original.Constraints[index]);
    }

    private static void AssertRefusal(DrawingDocument original, SketchDimensionKind kind = SketchDimensionKind.Diameter)
    {
        SketchDimensionModeService.TrySetRadialKind(original, "size", kind, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(original);
        reason.Should().NotBeEmpty();
    }
}
