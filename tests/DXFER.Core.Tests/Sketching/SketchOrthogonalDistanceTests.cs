using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchOrthogonalDistanceTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void DistanceBetweenPointsWithDrivingProjectionsFailsAtomically(double value)
    {
        var document = PointDocument();
        var distance = Driving("diagonal", SketchDimensionKind.LinearDistance, value, "a", "b:center");

        var result = Solve(document, distance);

        AssertRejected(document, distance, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistanceAcrossOrthogonalPathIsRedundantWithoutRectangleNames(bool polyline)
    {
        var (document, first, second) = OrthogonalPath(polyline, constrainAxes: true);
        var distance = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        AssertRejected(document, distance, Solve(document, distance));
    }

    [Fact]
    public void MatchingLengthsInCoincidentHingeDoNotFixItsOrthogonalComponents()
    {
        var (document, first, second) = OrthogonalPath(polyline: false, constrainAxes: false);
        var distance = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        var result = Solve(document, distance);

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.AffectedDiagnostics.Should().BeEmpty();
        result.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrReferenceOnlyComponentDoesNotMakeDistanceRedundant(bool referenceOnly)
    {
        var original = PointDocument();
        var dimensions = original.Dimensions.Take(1).ToList();
        if (referenceOnly)
        {
            dimensions.Add(new SketchDimension("dy", SketchDimensionKind.VerticalDistance,
                new[] { "a", "b" }, 4, isDriving: false));
        }

        var document = new DrawingDocument(original.Entities, dimensions, Array.Empty<SketchConstraint>());
        var distance = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", "b");

        Solve(document, distance).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void NonDrivingDiagonalRemainsAValidMeasurement()
    {
        var document = PointDocument();
        var distance = new SketchDimension("diagonal", SketchDimensionKind.LinearDistance,
            new[] { "a", "b" }, 5, isDriving: false);

        Solve(document, distance).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void RectangleFactoryDiagonalFailurePreservesOtherDimensionsAndConstraints(double value)
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool("twopointrectangle",
            new[] { new Point2(0, 0), new Point2(3, 4) },
            _ => EntityId.Create($"edge-{++sequence}"), isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool("twopointrectangle",
            entities, _ => $"relation-{++sequence}");
        var document = new DrawingDocument(entities, new[]
        {
            Driving("width", SketchDimensionKind.LinearDistance, 3, "edge-3:start", "edge-3:end"),
            Driving("height", SketchDimensionKind.LinearDistance, 4, "edge-4:start", "edge-4:end")
        }, constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, value, "edge-3:end", "edge-2:start");

        AssertRejected(document, diagonal, Solve(document, diagonal));
    }

    [Theory]
    [InlineData("line", "feature|point|end|999|999")]
    [InlineData("polyline", "feature|segment|0:end")]
    [InlineData("circle", "feature:center")]
    [InlineData("arc", "feature:center")]
    [InlineData("arc-endpoint", "feature:start")]
    [InlineData("polygon", "feature:center")]
    public void DrivingProjectionsUseLivePointReferencesAcrossEntityTypes(string kind, string reference)
    {
        var id = EntityId.Create("feature");
        var location = new Point2(3, 4);
        DrawingEntity feature = kind switch
        {
            "line" => new LineEntity(id, new Point2(1, 2), location),
            "polyline" => new PolylineEntity(id, new[] { new Point2(1, 2), location }),
            "circle" => new CircleEntity(id, location, 2),
            "arc" => new ArcEntity(id, location, 2, 0, 90),
            "arc-endpoint" => new ArcEntity(id, new Point2(1, 4), 2, 0, 90),
            "polygon" => new PolygonEntity(id, location, 2, 0, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("a"), new Point2(0, 0)), feature
        }, new[]
        {
            Driving("dx", SketchDimensionKind.HorizontalDistance, 3, reference, "a"),
            Driving("dy", SketchDimensionKind.VerticalDistance, 4, "a:center", reference)
        }, Array.Empty<SketchConstraint>());
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", reference);

        AssertRejected(document, diagonal, Solve(document, diagonal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuppressedAxisOrMissingWeldDoesNotProveRedundancy(bool suppressAxis)
    {
        var (original, first, second) = OrthogonalPath(polyline: false, constrainAxes: true);
        var constraints = original.Constraints
            .Where(constraint => suppressAxis || constraint.Kind != SketchConstraintKind.Coincident)
            .Select(constraint => suppressAxis && constraint.Kind == SketchConstraintKind.Vertical
                ? new SketchConstraint(constraint.Id, constraint.Kind, constraint.ReferenceKeys, SketchConstraintState.Suppressed)
                : constraint).ToArray();
        var document = new DrawingDocument(original.Entities, original.Dimensions, constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        Solve(document, diagonal).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void UnsatisfiedProjectionIsNotDependencyEvidence()
    {
        var original = PointDocument();
        var document = new DrawingDocument(original.Entities, new[]
        {
            original.Dimensions[0],
            Driving("dy", SketchDimensionKind.VerticalDistance, 9, "a", "b")
        }, original.Constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", "b");

        SketchDimensionSolverService.IsDimensionSatisfied(document, diagonal).Should().BeTrue();
    }

    [Fact]
    public void MeasurementMatchingButRedundantLengthIntentIsNotDependencyEvidence()
    {
        var (original, first, second) = OrthogonalPath(polyline: true, constrainAxes: true);
        var length = original.Dimensions[0];
        var duplicate = new SketchDimension("duplicate", length.Kind, length.ReferenceKeys, length.Value, isDriving: true);
        var document = new DrawingDocument(original.Entities, original.Dimensions.Append(duplicate), original.Constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        SketchDimensionSolverService.IsDimensionSatisfied(document, length).Should().BeFalse();
        SketchDimensionSolverService.IsDimensionSatisfied(document, duplicate).Should().BeFalse();
        SketchDimensionSolverService.IsDimensionSatisfied(document, diagonal).Should().BeTrue();
    }

    [Fact]
    public void ExistingDistanceWithSameIdDoesNotProveItsOwnRedundancy()
    {
        var original = PointDocument();
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", "b");
        var document = new DrawingDocument(original.Entities,
            new[] { original.Dimensions[0], diagonal }, original.Constraints);

        Solve(document, diagonal).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void ComponentsCanBeEstablishedThroughMultiplePointProjections()
    {
        var original = PointDocument();
        var document = new DrawingDocument(original.Entities.Append(
            new PointEntity(EntityId.Create("via"), new Point2(1, 7))), new[]
        {
            Driving("dx1", SketchDimensionKind.HorizontalDistance, 1, "a", "via"),
            Driving("dx2", SketchDimensionKind.HorizontalDistance, 2, "via", "b"),
            original.Dimensions[1]
        }, original.Constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "b", "a");

        AssertRejected(document, diagonal, Solve(document, diagonal));
    }

    [Fact]
    public void FixedReferencesEstablishBothComponents()
    {
        var original = PointDocument();
        var document = new DrawingDocument(original.Entities, Array.Empty<SketchDimension>(), new[]
        {
            new SketchConstraint("fix-a", SketchConstraintKind.Fix, new[] { "a" }),
            new SketchConstraint("fix-b", SketchConstraintKind.Fix, new[] { "b:center" })
        });
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a:center", "b");

        AssertRejected(document, diagonal, Solve(document, diagonal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PerpendicularTransfersAnEstablishedAxisBetweenPolylineSegments(bool suppressed)
    {
        var (original, first, second) = OrthogonalPath(polyline: true, constrainAxes: true);
        var constraints = original.Constraints.Where(constraint => constraint.Kind != SketchConstraintKind.Vertical)
            .Append(new SketchConstraint("right-angle", SketchConstraintKind.Perpendicular,
                new[] { "path|segment|1", "path|segment|0" },
                suppressed ? SketchConstraintState.Suppressed : SketchConstraintState.Unknown));
        var document = new DrawingDocument(original.Entities, original.Dimensions, constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        SketchDimensionSolverService.IsDimensionSatisfied(document, diagonal).Should().Be(suppressed);
    }

    [Fact]
    public void InvalidCoincidentConstraintCannotSupplyDependencyDespiteStoredSatisfiedState()
    {
        var (original, first, second) = OrthogonalPath(polyline: false, constrainAxes: true);
        var constraints = original.Constraints.Where(constraint => constraint.Kind != SketchConstraintKind.Coincident)
            .Append(new SketchConstraint("wrong-joint", SketchConstraintKind.Coincident,
                new[] { "alpha:start", "beta:start" }, SketchConstraintState.Satisfied));
        var document = new DrawingDocument(original.Entities, original.Dimensions, constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, first, second);

        SketchDimensionSolverService.IsDimensionSatisfied(document, diagonal).Should().BeTrue();
    }

    [Fact]
    public void EqualCoordinatesOnSeparatePointsDoNotCreateAnImplicitWeld()
    {
        var original = PointDocument();
        var document = new DrawingDocument(original.Entities.Append(
            new PointEntity(EntityId.Create("other"), new Point2(3, 4))), original.Dimensions, original.Constraints);
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", "other");

        Solve(document, diagonal).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void ConvertingFailedDiagonalToReferenceClearsOnlyItsFailure()
    {
        var original = PointDocument();
        var diagonal = Driving("diagonal", SketchDimensionKind.LinearDistance, 5, "a", "b");
        var failed = Solve(original, diagonal);
        AssertRejected(original, diagonal, failed);
        var reference = new SketchDimension(diagonal.Id, diagonal.Kind, diagonal.ReferenceKeys,
            diagonal.Value, isDriving: false);

        var recovered = Solve(failed.Document, reference);

        recovered.Status.Should().Be(SketchSolveStatus.Solved);
        recovered.AffectedDiagnostics.Should().BeEmpty();
        recovered.Document.Entities.Should().Equal(original.Entities);
        recovered.Document.Dimensions.Take(original.Dimensions.Count).Should().Equal(original.Dimensions);
    }

    [Fact]
    public void FixedOrthogonalComponentsIncludeAZeroProjection()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("a"), new Point2(0, 0)),
            new PointEntity(EntityId.Create("b"), new Point2(3, 0))
        }, new[]
        {
            Driving("dx", SketchDimensionKind.HorizontalDistance, 3, "a", "b"),
            Driving("dy", SketchDimensionKind.VerticalDistance, 0, "a", "b")
        }, Array.Empty<SketchConstraint>());
        var distance = Driving("distance", SketchDimensionKind.LinearDistance, 3, "a", "b");

        AssertRejected(document, distance, Solve(document, distance));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotOnlyCurveSamplesAreNotTreatedAsLiveConstraintReferences(bool spline)
    {
        // Unidentified spline samples cannot validate driving geometry from saved coordinates.
        // Ellipse snapshot behavior is outside this spline-reference change.
        var id = EntityId.Create("curve");
        DrawingEntity curve = spline
            ? SplineEntity.FromFitPoints(id, new[] { new Point2(0, 0), new Point2(3, 4) })
            : new EllipseEntity(id, new Point2(0, 0), new Point2(3, 4), 0.5);
        const string first = "curve|point|sample-a|0|0";
        const string second = "curve|point|sample-b|3|4";
        var document = new DrawingDocument(new[] { curve }, new[]
        {
            Driving("dx", SketchDimensionKind.HorizontalDistance, 3, first, second),
            Driving("dy", SketchDimensionKind.VerticalDistance, 4, first, second)
        }, Array.Empty<SketchConstraint>());
        var distance = Driving("distance", SketchDimensionKind.LinearDistance, 5, first, second);

        SketchDimensionSolverService.IsDimensionSatisfied(document, distance).Should().Be(!spline);
        document.Entities.Should().ContainSingle().Which.Should().BeSameAs(curve);
    }

    private static DrawingDocument PointDocument() => new(
        new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("a"), new Point2(0, 0)),
            new PointEntity(EntityId.Create("b"), new Point2(3, 4))
        },
        new[]
        {
            Driving("dx", SketchDimensionKind.HorizontalDistance, 3, "b", "a:center"),
            Driving("dy", SketchDimensionKind.VerticalDistance, 4, "a", "b")
        },
        Array.Empty<SketchConstraint>(),
        new DrawingDocumentMetadata { SourceFileName = "offset-points.dxf", Units = DrawingUnits.Inches });

    private static (DrawingDocument Document, string First, string Second) OrthogonalPath(
        bool polyline, bool constrainAxes)
    {
        DrawingEntity[] entities = polyline
            ? new DrawingEntity[]
            {
                new PolylineEntity(EntityId.Create("path"), new[]
                {
                    new Point2(0, 0), new Point2(3, 0), new Point2(3, 4)
                })
            }
            : new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("alpha"), new Point2(0, 0), new Point2(3, 0)),
                new LineEntity(EntityId.Create("beta"), new Point2(3, 0), new Point2(3, 4))
            };
        var horizontal = polyline ? "path|segment|0" : "alpha";
        var vertical = polyline ? "path|segment|1" : "beta";
        var constraints = new List<SketchConstraint>();
        if (!polyline)
        {
            constraints.Add(new SketchConstraint("joint", SketchConstraintKind.Coincident,
                new[] { horizontal + ":end", vertical + ":start" }));
        }

        if (constrainAxes)
        {
            constraints.Add(new SketchConstraint("h", SketchConstraintKind.Horizontal, new[] { horizontal }));
            constraints.Add(new SketchConstraint("v", SketchConstraintKind.Vertical, new[] { vertical }));
        }

        var document = new DrawingDocument(entities, new[]
        {
            Driving("length-a", SketchDimensionKind.LinearDistance, 3, horizontal + ":start", horizontal + ":end"),
            Driving("length-b", SketchDimensionKind.LinearDistance, 4, vertical + ":start", vertical + ":end")
        }, constraints);
        return (document, horizontal + ":start", vertical + ":end");
    }

    private static SketchDimension Driving(string id, SketchDimensionKind kind, double value,
        string first, string second) => new(id, kind, new[] { first, second }, value, isDriving: true);

    private static SketchSolveResult Solve(DrawingDocument document, SketchDimension distance) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,
            Array.Empty<SketchConstraint>(), new[] { distance }));

    private static void AssertRejected(DrawingDocument original, SketchDimension distance, SketchSolveResult result)
    {
        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.Document.Dimensions.Select(dimension => dimension.Id)
            .Should().Equal(original.Dimensions.Select(dimension => dimension.Id).Append(distance.Id));
        result.Document.Dimensions.Last().Should().BeEquivalentTo(distance);
        foreach (var dimension in original.Dimensions)
        {
            SketchDimensionSolverService.IsDimensionSatisfied(result.Document, dimension).Should().BeTrue();
        }

        result.Document.Constraints.Select(constraint => constraint.Id)
            .Should().Equal(original.Constraints.Select(constraint => constraint.Id));
        result.Document.Constraints.Should().NotContain(constraint => constraint.State != SketchConstraintState.Satisfied);
        result.AffectedDiagnostics.Should().ContainSingle().Which.ItemId.Should().Be(distance.Id);
    }
}
