using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationCircleDiameterTests
{
    [Theory]
    [InlineData("diameter", false)]
    [InlineData("DIAMETER", true)]
    public void DiameterFiveCreatesRadiusTwoPointFiveAndDrivingDiameter(string key, bool isConstruction)
    {
        var document = Create(new Dictionary<string, double> { [key] = 5 }, isConstruction);

        var circle = document.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>().Which;
        circle.Center.Should().Be(new Point2(10, 20));
        circle.Radius.Should().Be(2.5);
        circle.IsConstruction.Should().Be(isConstruction);
        AssertDimension(document, SketchDimensionKind.Diameter, 5, new Point2(12.5, 20));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiameterTakesPrecedenceWithoutDuplicateRadiusIntent(bool reverseOrder)
    {
        var values = reverseOrder
            ? new Dictionary<string, double> { ["radius"] = 8, ["diameter"] = 5 }
            : new Dictionary<string, double> { ["diameter"] = 5, ["radius"] = 8 };
        var document = Create(values);

        document.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(2.5);
        AssertDimension(document, SketchDimensionKind.Diameter, 5, new Point2(12.5, 20));
    }

    [Fact]
    public void LegacyRadiusKeyKeepsRadiusGeometryAndIntent()
    {
        var document = Create(new Dictionary<string, double> { ["radius"] = 5 });

        document.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(5);
        AssertDimension(document, SketchDimensionKind.Radius, 5, new Point2(15, 20));
    }

    [Fact]
    public void UntypedCircleRemainsPointDefinedWithoutDrivingDimension()
    {
        var document = Create(new Dictionary<string, double>());

        document.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(3);
        document.Dimensions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDiameterDoesNotCreateIntentAndAllowsLegacyRadius(double diameter)
    {
        var values = new Dictionary<string, double> { ["diameter"] = diameter };
        var untyped = Create(values);
        untyped.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(3);
        untyped.Dimensions.Should().BeEmpty();

        values["radius"] = 4;
        var legacy = Create(values);
        legacy.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(4);
        AssertDimension(legacy, SketchDimensionKind.Radius, 4, new Point2(14, 20));
    }

    [Theory]
    [InlineData("threepointarc")]
    [InlineData("tangentarc")]
    [InlineData("centerpointarc")]
    public void ArcRadiusIntentIsUnchangedAndDiameterKeyIsIgnored(string tool)
    {
        var arc = new ArcEntity(EntityId.Create("arc"), new Point2(10, 20), 3, 0, 90);
        var values = new Dictionary<string, double> { ["radius"] = 3, ["diameter"] = 5 };
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            tool, new DrawingEntity[] { arc }, values, () => "size");
        var document = new DrawingDocument(new DrawingEntity[] { arc }, dimensions, Array.Empty<SketchConstraint>());

        var dimension = dimensions.Should().ContainSingle().Which;
        dimension.Kind.Should().Be(SketchDimensionKind.Radius);
        dimension.Value.Should().Be(3);
        dimension.IsDriving.Should().BeTrue();
        dimension.ReferenceKeys.Should().Equal("arc");
        dimension.Anchor.Should().Be(new Point2(13, 20));
        SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();

        values.Remove("radius");
        SketchCreationDimensionFactory.CreateDimensionsForTool(
            tool, new DrawingEntity[] { arc }, values, () => "size").Should().BeEmpty();
    }

    [Theory]
    [InlineData("diameter", SketchDimensionKind.Diameter, 2.5, 4)]
    [InlineData("radius", SketchDimensionKind.Radius, 5, 8)]
    public void NativeReopenAndEditPreserveDimensionKindAndOriginalSnapshot(
        string key, SketchDimensionKind kind, double initialRadius, double editedRadius)
    {
        var original = Create(new Dictionary<string, double> { [key] = 5 });
        var snapshot = DxferDocumentSerializer.Write(original);
        var reopened = Reopen(original);
        DxferDocumentSerializer.Write(reopened).Should().Be(snapshot);
        reopened.Entities.Single().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(initialRadius);
        var dimension = reopened.Dimensions.Single();
        var edit = new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 8,
            dimension.Anchor, isDriving: true);

        var edited = SketchDimensionSolverService.ApplyDimension(reopened, edit);
        var editedReopened = Reopen(edited);

        var circle = editedReopened.Entities.Single().Should().BeOfType<CircleEntity>().Which;
        circle.Center.Should().Be(new Point2(10, 20));
        circle.Radius.Should().Be(editedRadius);
        AssertDimension(editedReopened, kind, 8, dimension.Anchor!.Value);
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
        DxferDocumentSerializer.Write(reopened).Should().Be(snapshot);
        DxferDocumentSerializer.Write(editedReopened).Should().Be(DxferDocumentSerializer.Write(edited));
    }

    private static DrawingDocument Create(IReadOnlyDictionary<string, double> values, bool isConstruction = false)
    {
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool("centercircle",
            new[] { new Point2(10, 20), new Point2(13, 20) },
            _ => EntityId.Create("circle"), isConstruction, values);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "centercircle", entities, values, () => "size");
        return new DrawingDocument(entities, dimensions, Array.Empty<SketchConstraint>());
    }

    private static void AssertDimension(DrawingDocument document, SketchDimensionKind kind, double value, Point2 anchor)
    {
        var dimension = document.Dimensions.Should().ContainSingle().Which;
        dimension.Kind.Should().Be(kind);
        dimension.Value.Should().Be(value);
        dimension.IsDriving.Should().BeTrue();
        dimension.ReferenceKeys.Should().Equal("circle");
        dimension.Anchor.Should().Be(anchor);
        SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();
    }

    private static DrawingDocument Reopen(DrawingDocument document) =>
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
}
