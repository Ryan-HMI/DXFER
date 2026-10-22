using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchSymmetricNormalValidationTests
{
    [Fact]
    public void NormalIsAppendedWithoutChangingPersistedEnumValues()
    {
        ((int)SketchConstraintKind.Symmetric).Should().Be(9);
        ((int)SketchConstraintKind.PointOnCircle).Should().Be(14);
        ((int)SketchConstraintKind.Normal).Should().Be(15);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymmetricPointsReflectAcrossOffsetDiagonalAxisInEitherDirection(bool reversed)
    {
        var entities = new DrawingEntity[]
        {
            new PointEntity(Id("a"), new(4, 4)), new PointEntity(Id("b"), new(3, 5)),
            new LineEntity(Id("axis"), reversed ? new(11, 12) : new(1, 2), reversed ? new(1, 2) : new(11, 12))
        };
        AssertState(entities, SketchConstraintKind.Symmetric, true, "a", "b", "axis");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.0000005, true)]
    [InlineData(0.00001, false)]
    public void SymmetricPointResidualUsesDrawingUnitTolerance(double error, bool satisfied) =>
        AssertState([new PointEntity(Id("a"), new(-3, 2)), new PointEntity(Id("b"), new(3 + error, 2)), Axis()],
            SketchConstraintKind.Symmetric, satisfied, "a", "b", "axis");

    [Fact]
    public void OriginAndStableCircleCenterCanBePointPair() =>
        AssertState([new CircleEntity(Id("circle"), new(4, 0), 2), new LineEntity(Id("axis"), new(2, -1), new(2, 1))],
            SketchConstraintKind.Symmetric, true, SketchReference.OriginKey, "circle:center", "axis");

    [Fact]
    public void LineVertexPairsUseStoredInterleavedCorrespondence()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(Id("a"), new(-3, 1), new(-4, 5)),
            new LineEntity(Id("b"), new(3, 1), new(4, 5)), Axis()
        };
        AssertState(entities, SketchConstraintKind.Symmetric, true, "a:start", "b:start", "a:end", "b:end", "axis");
        AssertState(entities, SketchConstraintKind.Symmetric, false, "a:start", "b:end", "a:end", "b:start", "axis");
    }

    [Fact]
    public void PolylineVerticesAndAxisSegmentUseIdentityInsteadOfStaleCanvasCoordinates()
    {
        var entities = new DrawingEntity[]
        {
            new PolylineEntity(Id("a"), [new(-3, 1), new(-4, 5), new(-2, 7)]),
            new PolylineEntity(Id("b"), [new(3, 1), new(4, 5), new(2, 7)]),
            new PolylineEntity(Id("axis"), [new(20, 20), new(0, -10), new(0, 10)])
        };
        AssertState(entities, SketchConstraintKind.Symmetric, true,
            "a|point|vertex-0|999|999", "b|segment|0:start",
            "a|segment|0:end", "b|point|vertex-1|-999|-999",
            "a|segment|1:end", "b|segment|1:end", "axis|segment|1");
    }

    [Theory]
    [InlineData("a", "a", "axis")]
    [InlineData("a", "missing", "axis")]
    [InlineData("a", "b", "axis:start")]
    [InlineData("a", "b", "axis|segment|0")]
    [InlineData("a", "b", "axis|segment|-1")]
    [InlineData("a|segment|0:center", "b", "axis")]
    [InlineData("a|point|quadrant-right|3|2", "b", "axis")]
    public void InvalidSymmetricReferencesDoNotFallBackToEntityOrRawCoordinates(string a, string b, string axis) =>
        AssertState([new PointEntity(Id("a"), new(-3, 2)), new PointEntity(Id("b"), new(3, 2)), Axis()],
            SketchConstraintKind.Symmetric, false, a, b, axis);

    [Fact]
    public void SymmetricRejectsAxisSelfReferencesAndNonVertexMultiPairs()
    {
        AssertState([new LineEntity(Id("axis"), new(-3, 0), new(3, 0))],
            SketchConstraintKind.Symmetric, false, "axis:start", "axis:end", "axis");
        AssertState([new PointEntity(Id("a"), new(-3, 2)), new PointEntity(Id("b"), new(3, 2)), Axis()],
            SketchConstraintKind.Symmetric, false, "a", "b", "a", "b", "axis");
        AssertState([Axis()], SketchConstraintKind.Symmetric, false, "axis");
        AssertState([Axis()], SketchConstraintKind.Symmetric, false, "axis", "axis");
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(2.001, 3, false)]
    [InlineData(2, 3.001, false)]
    [InlineData(0, 3, false)]
    [InlineData(double.NaN, 3, false)]
    [InlineData(double.PositiveInfinity, 3, false)]
    public void SymmetricCirclesRequireMirroredCentersAndEqualValidRadii(double radius, double x, bool satisfied) =>
        AssertState([new CircleEntity(Id("a"), new(-3, 2), 2), new CircleEntity(Id("b"), new(x, 2), radius), Axis()],
            SketchConstraintKind.Symmetric, satisfied, "a", "b", "axis");

    [Theory]
    [InlineData(90, 180, true)]
    [InlineData(450, 540, true)]
    [InlineData(0, 90, false)]
    [InlineData(180, 450, false)]
    [InlineData(90, 90, false)]
    [InlineData(double.NaN, 180, false)]
    public void SymmetricArcsReflectEndpointsInReverseOrder(double start, double end, bool satisfied) =>
        AssertState([new ArcEntity(Id("a"), new(-3, 2), 2, 0, 90),
            new ArcEntity(Id("b"), new(3, 2), 2, start, end), Axis()],
            SketchConstraintKind.Symmetric, satisfied, "a", "b", "axis");

    [Fact]
    public void SymmetricWrappedArcsAreValidButMixedCurveTypesAreNot()
    {
        AssertState([new ArcEntity(Id("a"), new(-3, 2), 2, 350, 10),
            new ArcEntity(Id("b"), new(3, 2), 2, 170, 190), Axis()],
            SketchConstraintKind.Symmetric, true, "a", "b", "axis");
        AssertState([new CircleEntity(Id("a"), new(-3, 2), 2),
            new ArcEntity(Id("b"), new(3, 2), 2, 0, 360), Axis()],
            SketchConstraintKind.Symmetric, false, "a", "b", "axis");
    }

    [Theory]
    [InlineData("line:end", -2, 0, 2, 0, true)]
    [InlineData("line:start", 2, 0, 5, 0, true)]
    [InlineData("line:end", 2, -3, 2, 0, false)]
    [InlineData("line:end", -2, 0, 1, 0, false)]
    [InlineData("line:end", 2, 0, 2, 0, false)]
    [InlineData("line:mid", -2, 0, 6, 0, false)]
    [InlineData("line", -2, 0, 2, 0, false)]
    public void NormalRequiresEndpointContactAndSupportingLineThroughCenter(string key, double x1, double y1, double x2, double y2, bool satisfied) =>
        AssertState([new LineEntity(Id("line"), new(x1, y1), new(x2, y2)), new CircleEntity(Id("curve"), new(0, 0), 2)],
            SketchConstraintKind.Normal, satisfied, key, "curve");

    [Fact]
    public void NormalPolylineSegmentEndpointUsesCurrentVertex()
    {
        var entities = new DrawingEntity[]
        {
            new PolylineEntity(Id("line"), [new(8, 8), new(5, 0), new(2, 0)]),
            new CircleEntity(Id("curve"), new(0, 0), 2)
        };
        AssertState(entities, SketchConstraintKind.Normal, true, "line|segment|1:end", "curve");
        AssertState(entities, SketchConstraintKind.Normal, true, "line|point|vertex-2|999|999", "curve");
        AssertState(entities, SketchConstraintKind.Normal, false, "line|segment|99:end", "curve");
        AssertState(entities, SketchConstraintKind.Normal, false, "line|segment|-1:end", "curve");
    }

    [Theory]
    [InlineData(350, 10, true)]
    [InlineData(0, 90, true)]
    [InlineData(270, 360, true)]
    [InlineData(90, 270, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 360, true)]
    [InlineData(0, 720, false)]
    [InlineData(0, double.PositiveInfinity, false)]
    public void NormalArcContactMustLieOnValidFiniteSweep(double start, double end, bool satisfied) =>
        AssertState([new LineEntity(Id("line"), new(5, 0), new(2, 0)),
            new ArcEntity(Id("curve"), new(0, 0), 2, start, end)],
            SketchConstraintKind.Normal, satisfied, "line:end", "curve");

    [Theory]
    [InlineData("line|segment|0:end", "curve")]
    [InlineData("line:end", "curve:center")]
    [InlineData("line:end", "curve|segment|0")]
    [InlineData("line:end", "line")]
    [InlineData("curve", "line:end")]
    [InlineData("line:end", "missing")]
    public void NormalRejectsInvalidReferenceShapes(string endpoint, string curve) =>
        AssertState([new LineEntity(Id("line"), new(5, 0), new(2, 0)), new CircleEntity(Id("curve"), new(0, 0), 2)],
            SketchConstraintKind.Normal, false, endpoint, curve);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NormalRejectsInvalidRadius(double radius) =>
        AssertState([new LineEntity(Id("line"), new(5, 0), new(2, 0)), new CircleEntity(Id("curve"), new(0, 0), radius)],
            SketchConstraintKind.Normal, false, "line:end", "curve");

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SymmetricRejectsDegenerateOrNonfiniteAxis(double endY) =>
        AssertState([new PointEntity(Id("a"), new(-3, 2)), new PointEntity(Id("b"), new(3, 2)),
            new LineEntity(Id("axis"), new(0, 0), new(0, endY))],
            SketchConstraintKind.Symmetric, false, "a", "b", "axis");

    [Theory]
    [InlineData(SketchConstraintKind.Normal, true)]
    [InlineData(SketchConstraintKind.Normal, false)]
    [InlineData(SketchConstraintKind.Symmetric, true)]
    [InlineData(SketchConstraintKind.Symmetric, false)]
    public void NativeRoundtripPreservesIntentAndValidationNeverMutatesGeometry(SketchConstraintKind kind, bool satisfied)
    {
        var entities = kind == SketchConstraintKind.Normal
            ? new DrawingEntity[] { new LineEntity(Id("a"), new(5, 0), new(satisfied ? 2 : 1, 0)), new CircleEntity(Id("b"), new(0, 0), 2) }
            : [new PointEntity(Id("a"), new(-3, 2)), new PointEntity(Id("b"), new(satisfied ? 3 : 4, 2)), Axis()];
        var keys = kind == SketchConstraintKind.Normal ? new[] { "a:end", "b" } : ["a", "b", "axis"];
        var intent = new SketchConstraint("intent", kind, keys, SketchConstraintState.Unknown);
        var document = new DrawingDocument(entities, [], [intent]);
        var before = DxferDocumentSerializer.Write(document);
        var reopened = DxferDocumentSerializer.Read(before);
        SketchConstraintService.ValidateConstraint(reopened, reopened.Constraints.Single()).State.Should().Be(
            satisfied ? SketchConstraintState.Satisfied : SketchConstraintState.Unsatisfied);
        DxferDocumentSerializer.Write(reopened).Should().Be(before);
        SketchConstraintService.ApplyConstraint(document, intent).Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Normal)]
    [InlineData(SketchConstraintKind.Symmetric)]
    public void SuppressedIntentStaysSuppressedEvenWithMissingReferences(SketchConstraintKind kind)
    {
        var intent = new SketchConstraint("suppressed", kind, ["missing"], SketchConstraintState.Suppressed);
        SketchConstraintService.ValidateConstraint(new DrawingDocument([]), intent).Should().BeSameAs(intent);
    }

    private static EntityId Id(string id) => EntityId.Create(id);
    private static LineEntity Axis() => new(Id("axis"), new(0, -10), new(0, 10));
    private static void AssertState(DrawingEntity[] entities, SketchConstraintKind kind, bool satisfied, params string[] keys)
    {
        var document = new DrawingDocument(entities);
        var intent = new SketchConstraint("intent", kind, keys);
        var result = SketchConstraintService.ValidateConstraint(document, intent);
        result.State.Should().Be(satisfied ? SketchConstraintState.Satisfied : SketchConstraintState.Unsatisfied);
        result.Id.Should().Be(intent.Id);
        result.ReferenceKeys.Should().Equal(keys);
        document.Entities.Should().Equal(entities);
    }
}
