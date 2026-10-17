using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchOriginReferenceTests
{
    private const string Origin = "$origin:center";

    [Fact]
    public void PublicOriginIdentityIsStableAndReservedAgainstGeometryCollisions()
    {
        SketchReference.OriginEntityId.Should().Be("$origin");
        SketchReference.OriginKey.Should().Be(Origin);
        SketchReference.Origin.ToString().Should().Be(Origin);
        SketchReference.Origin.IsOrigin.Should().BeTrue();
        SketchReference.Origin.Target.Should().Be(SketchReferenceTarget.Center);
        var create = () => new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("$origin"), new(50, 50))
        });
        create.Should().Throw<ArgumentException>().WithMessage("*reserved*");
    }

    [Fact]
    public void OriginResolvesWithoutGeometryAndIgnoresStaleCanvasCoordinates()
    {
        var document = new DrawingDocument(Array.Empty<DrawingEntity>());
        foreach (var key in new[] { Origin, "$origin|point|center|25|30" })
        {
            SketchReferenceResolver.TryGetPoint(document, key, out var point).Should().BeTrue();
            point.Should().Be(new Point2(0, 0));
            SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
            normalized.Should().Be(Origin);
        }
        SketchReferenceResolver.TryGetEntity(document, "$origin", out _).Should().BeFalse();
        SketchReferenceResolver.TryGetLine(document, Origin, out _).Should().BeFalse();
        document.Entities.Should().BeEmpty();
    }

    [Theory]
    [InlineData("$origin")]
    [InlineData("$origin:start")]
    [InlineData("$origin:end")]
    [InlineData("$origin|segment|0:center")]
    [InlineData("$origin|point|unknown|25|30")]
    public void NoncanonicalOriginTargetsDoNotResolve(string key)
    {
        SketchReferenceResolver.TryGetPoint(new DrawingDocument(Array.Empty<DrawingEntity>()), key, out _).Should().BeFalse();
    }

    public static IEnumerable<object[]> PointRelations()
    {
        foreach (var kind in new[] { SketchConstraintKind.Coincident, SketchConstraintKind.Horizontal, SketchConstraintKind.Vertical })
        foreach (var reverse in new[] { false, true })
        foreach (var entityKind in new[] { "point", "line", "polyline", "circle", "arc", "ellipse", "polygon" })
            yield return new object[] { kind, reverse, entityKind };
    }

    [Theory]
    [MemberData(nameof(PointRelations))]
    public void PointRelationsMoveOnlyRealGeometryInEitherOrder(SketchConstraintKind kind, bool reverse, string entityKind)
    {
        var (entity, key) = CreateGeometry(entityKind);
        var document = new DrawingDocument(new[] { entity });
        var constraint = new SketchConstraint("relation", kind, Keys(key, reverse));
        var solved = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { constraint }, Array.Empty<SketchDimension>()));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        solved.Document.Entities.Should().ContainSingle().Which.Id.Should().Be(entity.Id);
        SketchReferenceResolver.TryGetPoint(solved.Document, key, out var point).Should().BeTrue();
        point.Should().Be(kind switch
        {
            SketchConstraintKind.Horizontal => new Point2(3, 0),
            SketchConstraintKind.Vertical => new Point2(0, 4),
            _ => new Point2(0, 0)
        });
        SketchReferenceResolver.TryGetPoint(solved.Document, Origin, out var origin).Should().BeTrue();
        origin.Should().Be(new Point2(0, 0));
        solved.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(SketchDimensionKind.LinearDistance, false, 6, 8)]
    [InlineData(SketchDimensionKind.LinearDistance, true, 6, 8)]
    [InlineData(SketchDimensionKind.HorizontalDistance, false, 10, 4)]
    [InlineData(SketchDimensionKind.HorizontalDistance, true, 10, 4)]
    [InlineData(SketchDimensionKind.VerticalDistance, false, 3, 10)]
    [InlineData(SketchDimensionKind.VerticalDistance, true, 3, 10)]
    public void DrivingDistanceUsesOriginAsImmutableAnchor(SketchDimensionKind kind, bool reverse, double x, double y)
    {
        var document = new DrawingDocument(new DrawingEntity[] { new PointEntity(EntityId.Create("point"), new(3, 4)) });
        var dimension = new SketchDimension("distance", kind, Keys("point:center", reverse), 10, isDriving: true);
        var solved = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { dimension }));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        ((PointEntity)solved.Document.Entities.Single()).Location.Should().Be(new Point2(x, y));
        SketchDimensionSolverService.IsDimensionSatisfied(solved.Document, dimension).Should().BeTrue();
        document.Entities.Single().Should().Be(new PointEntity(EntityId.Create("point"), new(3, 4)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedPeerConstraintAndDimensionConflictsRetainGeometryAndReportFailure(bool reverse)
    {
        var (point, key) = CreateGeometry("point");
        var fix = new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { key }, SketchConstraintState.Satisfied);
        var document = new DrawingDocument(new[] { point }, Array.Empty<SketchDimension>(), new[] { fix });
        foreach (var kind in new[] { SketchConstraintKind.Coincident, SketchConstraintKind.Horizontal, SketchConstraintKind.Vertical })
        {
            var relation = new SketchConstraint("relation", kind, Keys(key, reverse));
            var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
            result.Status.Should().Be(SketchSolveStatus.OverConstrained);
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Constraints.Single(c => c.Id == relation.Id).State.Should().Be(SketchConstraintState.Unsatisfied);
        }
        foreach (var kind in new[] { SketchDimensionKind.LinearDistance, SketchDimensionKind.HorizontalDistance, SketchDimensionKind.VerticalDistance })
        {
            var dimension = new SketchDimension("distance", kind, Keys(key, reverse), 10, isDriving: true);
            var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { dimension }));
            result.Status.Should().Be(SketchSolveStatus.OverConstrained);
            result.Document.Entities.Should().Equal(document.Entities);
            result.Document.Dimensions.Single().Value.Should().Be(10);
            result.AffectedDiagnostics.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDrivingDistanceCannotBeSilentlyBrokenByOriginCoincidence(bool reverse)
    {
        var (point, key) = CreateGeometry("point");
        var dimension = new SketchDimension("distance", SketchDimensionKind.LinearDistance, Keys(key, reverse), 5, isDriving: true);
        var document = new DrawingDocument(new[] { point }, new[] { dimension }, Array.Empty<SketchConstraint>());
        var relation = new SketchConstraint("coincident", SketchConstraintKind.Coincident, Keys(key, reverse));
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Single().Value.Should().Be(5);
        result.Document.Constraints.Single().State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginMidpointMovesFreeLineButCannotMoveFixedLine(bool reverse)
    {
        var line = new LineEntity(EntityId.Create("line"), new(1, 4), new(5, 4));
        var relation = new SketchConstraint("midpoint", SketchConstraintKind.Midpoint, Keys("line", reverse));
        var document = new DrawingDocument(new DrawingEntity[] { line });
        var solved = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        solved.Document.Entities.Single().Should().Be(line with { Start = new(-2, 0), End = new(2, 0) });

        var fixedDocument = new DrawingDocument(document.Entities, Array.Empty<SketchDimension>(),
            new[] { new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { "line" }, SketchConstraintState.Satisfied) });
        var rejected = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(fixedDocument, new[] { relation }, Array.Empty<SketchDimension>()));
        rejected.Status.Should().Be(SketchSolveStatus.OverConstrained);
        rejected.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginParticipatesInOrthogonalDrivingDependencyChecks(bool reverse)
    {
        var (point, key) = CreateGeometry("point");
        var refs = Keys(key, reverse);
        var document = new DrawingDocument(new[] { point }, new[]
        {
            new SketchDimension("x", SketchDimensionKind.HorizontalDistance, refs, 3, isDriving: true),
            new SketchDimension("y", SketchDimensionKind.VerticalDistance, refs, 4, isDriving: true)
        }, Array.Empty<SketchConstraint>());
        var redundant = new SketchDimension("diagonal", SketchDimensionKind.LinearDistance, refs, 5, isDriving: true);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { redundant }));
        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(document.Entities);
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "diagonal"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);

        var reference = new SketchDimension("diagonal", SketchDimensionKind.LinearDistance, refs, 5, isDriving: false);
        var accepted = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { reference }));
        accepted.Status.Should().Be(SketchSolveStatus.Solved);
        accepted.Document.Entities.Should().Equal(document.Entities);
    }

    [Fact]
    public void OriginFixedStateCannotBeSuppressedOrOmittedFromExternalAdapterRequest()
    {
        var (point, key) = CreateGeometry("point");
        var suppressedFix = new SketchConstraint("unfix-origin", SketchConstraintKind.Fix, new[] { Origin }, SketchConstraintState.Suppressed);
        var dimension = new SketchDimension("distance", SketchDimensionKind.LinearDistance, new[] { key, Origin }, 10, isDriving: true);
        var document = new DrawingDocument(new[] { point }, Array.Empty<SketchDimension>(), new[] { suppressedFix });
        var request = new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { dimension }, fixedReferenceKeys: Array.Empty<string>());
        request.FixedReferenceKeys.Should().Equal(Origin);
        var solved = new LegacySketchSolverAdapter().Solve(request);
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        ((PointEntity)solved.Document.Entities.Single()).Location.Should().Be(new Point2(6, 8));
        SketchReferenceResolver.TryGetPoint(solved.Document, Origin, out var origin).Should().BeTrue();
        origin.Should().Be(new Point2(0, 0));
    }

    [Theory]
    [InlineData("$origin:center")]
    [InlineData("$origin|point|center|0|0")]
    public void DirectOriginDragCannotCreateOrMoveGeometry(string key)
    {
        var (point, _) = CreateGeometry("point");
        var document = new DrawingDocument(new[] { point });
        SketchGeometryDragService.TryApplyDrag(document, key, new(0, 0), new(8, 9), false, out var next, out _).Should().BeFalse();
        next.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragCannotBreakCoincidenceWithIntrinsicOrigin(bool reverse)
    {
        var point = new PointEntity(EntityId.Create("point"), new(0, 0));
        var relation = new SketchConstraint("origin-lock", SketchConstraintKind.Coincident,
            Keys("point:center", reverse), SketchConstraintState.Satisfied);
        var document = new DrawingDocument(new DrawingEntity[] { point }, Array.Empty<SketchDimension>(), new[] { relation });
        SketchGeometryDragService.TryApplyDrag(document, "point|point|center|0|0", new(0, 0), new(8, 9), false,
            out var next, out _).Should().BeFalse();
        next.Entities.Should().Equal(document.Entities);
        next.Constraints.Should().Equal(document.Constraints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceDimensionToOriginMeasuresWithoutChangingGeometry(bool reverse)
    {
        var (point, key) = CreateGeometry("point");
        var document = new DrawingDocument(new[] { point });
        var dimension = new SketchDimension("reference", SketchDimensionKind.LinearDistance, Keys(key, reverse), 5, isDriving: false);
        var result = SketchDimensionSolverService.ApplyDimension(document, dimension);
        result.Entities.Should().Equal(document.Entities);
        SketchDimensionSolverService.TryGetDimensionMeasurement(result, dimension, out var value).Should().BeTrue();
        value.Should().Be(5);
        result.Dimensions.Single().IsDriving.Should().BeFalse();
    }

    [Fact]
    public void UnsupportedSplinePointRelationRefusesWithoutApproximatingSpline()
    {
        var spline = new SplineEntity(EntityId.Create("spline"), 3,
            new Point2[] { new(1, 1), new(2, 1), new(3, 2), new(4, 2) }, new double[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        var constraint = new SketchConstraint("coincident", SketchConstraintKind.Coincident, new[] { Origin, "spline:start" });
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { constraint }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Single().Should().BeSameAs(spline);
        result.Document.Constraints.Single().State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void OriginReferencesPersistWithoutAnExportablePoint()
    {
        var line = new LineEntity(EntityId.Create("line"), new(0, 0), new(3, 4));
        var dimension = new SketchDimension("distance", SketchDimensionKind.LinearDistance, new[] { Origin, "line:end" }, 5, isDriving: false);
        var constraint = new SketchConstraint("coincident", SketchConstraintKind.Coincident, new[] { "line:start", Origin }, SketchConstraintState.Satisfied);
        var document = new DrawingDocument(new DrawingEntity[] { line }, new[] { dimension }, new[] { constraint });
        foreach (var restored in new[]
        {
            DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(document)),
            DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)))
        })
        {
            restored.Entities.Should().Equal(document.Entities);
            restored.Dimensions.Single().ReferenceKeys.Should().Equal(dimension.ReferenceKeys);
            restored.Constraints.Single().ReferenceKeys.Should().Equal(constraint.ReferenceKeys);
            SketchDimensionSolverService.TryGetDimensionMeasurement(restored, restored.Dimensions.Single(), out var value).Should().BeTrue();
            value.Should().Be(5);
            SketchConstraintService.ValidateConstraint(restored, restored.Constraints.Single()).State.Should().Be(SketchConstraintState.Satisfied);
        }
        var plain = DxfDocumentReader.Read(DxfDocumentWriter.Write(document));
        plain.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
    }

    private static string[] Keys(string key, bool reverse) => reverse ? new[] { key, Origin } : new[] { Origin, key };

    private static (DrawingEntity Entity, string Key) CreateGeometry(string kind) => kind switch
    {
        "line" => (new LineEntity(EntityId.Create("real"), new(3, 4), new(9, 8)), "real:start"),
        "polyline" => (new PolylineEntity(EntityId.Create("real"), new Point2[] { new(3, 4), new(9, 8) }), "real|segment|0:start"),
        "circle" => (new CircleEntity(EntityId.Create("real"), new(3, 4), 2), "real:center"),
        "arc" => (new ArcEntity(EntityId.Create("real"), new(3, 4), 2, 0, 90), "real:center"),
        "ellipse" => (new EllipseEntity(EntityId.Create("real"), new(3, 4), new(2, 0), .5), "real:center"),
        "polygon" => (new PolygonEntity(EntityId.Create("real"), new(3, 4), 2, 0, 5), "real:center"),
        _ => (new PointEntity(EntityId.Create("real"), new(3, 4)), "real:center")
    };
}
