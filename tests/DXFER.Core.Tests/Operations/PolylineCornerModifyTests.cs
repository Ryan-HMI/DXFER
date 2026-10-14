using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class PolylineCornerModifyTests
{
    [Theory]
    [InlineData(true, .7)]
    [InlineData(false, .5)]
    public void AdjacentOpenSegmentsKeepBothTrims(bool fillet, double amount)
    {
        var polyline = new PolylineEntity(EntityId.Create("path"),
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) });
        var document = new DrawingDocument(new[] { polyline });

        Modify(document, fillet, amount, new[] { "path|segment|0", "path|segment|1" }, out var next).Should().BeTrue();

        document.Entities.Single().Should().BeSameAs(polyline);
        polyline.Vertices.Should().Equal(new Point2(0, 0), new Point2(10, 0), new Point2(10, 10));
        var paths = next.Entities.OfType<PolylineEntity>().ToArray();
        paths.Should().HaveCount(2);
        AssertPoint(paths[0].Vertices[0], new(0, 0));
        AssertPoint(paths[0].Vertices[^1], new(10 - amount, 0));
        AssertPoint(paths[1].Vertices[0], new(10, amount));
        AssertPoint(paths[1].Vertices[^1], new(10, 10));
        AssertConnector(next, fillet, amount);
        AssertSatisfied(next);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public void ClosedNonRectangularPathPreservesAllOtherSegmentsAndClosure(bool fillet, bool seam, bool reverse)
    {
        var points = seam
            ? new[] { new Point2(10, 0), new Point2(10, 10), new Point2(4, 13), new Point2(-3, 4), new Point2(0, 0), new Point2(10, 0) }
            : new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(4, 13), new Point2(-3, 4), new Point2(0, 0) };
        var source = new PolylineEntity(EntityId.Create("path"), points);
        var first = seam ? 4 : 0;
        var second = seam ? 0 : 1;
        var selection = new[] { $"path|segment|{first}", $"path|segment|{second}" };
        if (reverse) Array.Reverse(selection);
        var dimensions = Enumerable.Range(0, points.Length - 1)
            .Where(i => i != first && i != second)
            .Select(i => new SketchDimension("length-" + i, SketchDimensionKind.LinearDistance,
                new[] { $"path|segment|{i}:start", $"path|segment|{i}:end" }, Length(points[i], points[i + 1]), isDriving: true));
        var document = new DrawingDocument(new[] { source }, dimensions, Array.Empty<SketchConstraint>());

        Modify(document, fillet, fillet ? .7 : .5, selection, out var next).Should().BeTrue();

        var path = next.Entities.OfType<PolylineEntity>().Single();
        path.Id.Should().Be(source.Id);
        path.Vertices.Should().HaveCount(points.Length);
        var remainingSegments = path.Vertices.Zip(path.Vertices.Skip(1)).ToArray();
        for (var index = 0; index < points.Length - 1; index++)
            if (index != first && index != second)
                remainingSegments.Should().Contain((points[index], points[index + 1]));
        AssertPoint(path.Vertices[0], new(10, fillet ? .7 : .5));
        AssertPoint(path.Vertices[^1], new(10 - (fillet ? .7 : .5), 0));
        foreach (var dimension in next.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(next, dimension).Should().BeTrue();
        AssertSatisfied(next);
        source.Vertices.Should().Equal(points);
        ClosedContourService.Calculate(next).ClosedContourCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void OpenInteriorCornerPreservesStylesConstructionAndReferences(bool fillet, bool reverse)
    {
        var points = new[] { new Point2(-5, -2), new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(20, 12) };
        var source = new PolylineEntity(EntityId.Create("path"), points, true);
        var untouched = new ArcEntity(EntityId.Create("arc"), new(40, 40), 2, 0, 90);
        var style = new DxfEntityStyle("BEND", "DASHED", 3);
        var metadata = new DrawingDocumentMetadata
        {
            Units = DrawingUnits.Inches,
            SourceFileName = "original.dxf",
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["path"] = style, ["arc"] = new("CUT", "CONTINUOUS", 7) }
        };
        var document = new DrawingDocument(new DrawingEntity[] { source, untouched }, new[]
        {
            new SketchDimension("driving", SketchDimensionKind.LinearDistance,
                new[] { "path|segment|3:start", "path|segment|3:end" }, Length(points[3], points[4]), isDriving: true),
            new SketchDimension("reference", SketchDimensionKind.LinearDistance,
                new[] { "path|segment|1:start", "path|segment|1:end" }, 10, new Point2(3, -2)),
            new SketchDimension("canvas", SketchDimensionKind.LinearDistance,
                new[] { "path|point|mid-3|15|11", "path|point|vertex-4|20|12" }, Math.Sqrt(26), new Point2(12, 12)),
            new SketchDimension("unrelated", SketchDimensionKind.Radius, new[] { "arc" }, 2)
        }, new[]
        {
            Constraint("h", SketchConstraintKind.Horizontal, "path|segment|1"),
            Constraint("v", SketchConstraintKind.Vertical, "path|segment|2"),
            Constraint("fixed-unaffected", SketchConstraintKind.Fix, "path|segment|0"),
            Constraint("old-corner", SketchConstraintKind.Coincident, "path|segment|1:end", "path|segment|2:start"),
            new SketchConstraint("suppressed", SketchConstraintKind.Horizontal, new[] { "path|segment|2" }, SketchConstraintState.Suppressed)
        }, metadata);
        var selection = new[] { "path|segment|1", "path|segment|2" };
        if (reverse) Array.Reverse(selection);

        Modify(document, fillet, fillet ? .7 : .5, selection, out var next).Should().BeTrue();

        next.Entities.Single(e => e.Id.Value == "arc").Should().BeSameAs(untouched);
        var paths = next.Entities.OfType<PolylineEntity>().ToArray();
        paths.Should().HaveCount(2);
        paths[0].Vertices.Take(2).Should().Equal(points.Take(2));
        paths[1].Vertices.Skip(1).Should().Equal(points.Skip(3));
        next.Entities.Where(e => e.Id.Value != "arc").Should().OnlyContain(e => e.IsConstruction);
        next.Metadata.SourceFileName.Should().Be(metadata.SourceFileName);
        next.Metadata.Units.Should().Be(metadata.Units);
        foreach (var entity in next.Entities.Where(e => e.Id.Value != "arc"))
            next.Metadata.EntityStyles[entity.Id.Value].Should().Be(style);
        metadata.EntityStyles.Should().HaveCount(2);
        next.Constraints.Should().NotContain(c => c.Id == "old-corner");
        next.Constraints.Single(c => c.Id == "v").ReferenceKeys.Should().Equal("polyline-corner-created|segment|0");
        next.Constraints.Single(c => c.Id == "suppressed").State.Should().Be(SketchConstraintState.Suppressed);
        var measured = next.Dimensions.Single(d => d.Id == "reference");
        measured.Value.Should().BeApproximately(10 - (fillet ? .7 : .5), .000001);
        measured.Anchor.Should().Be(new Point2(3, -2));
        var driving = next.Dimensions.Single(d => d.Id == "driving");
        driving.Value.Should().Be(document.Dimensions[0].Value);
        driving.ReferenceKeys.Should().Equal("polyline-corner-created|segment|1:start", "polyline-corner-created|segment|1:end");
        foreach (var dimension in next.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(next, dimension).Should().BeTrue();
        foreach (var constraint in next.Constraints.Where(c => c.State != SketchConstraintState.Suppressed))
            SketchConstraintService.ValidateConstraint(next, constraint).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(true, "driving")]
    [InlineData(false, "driving")]
    [InlineData(true, "fixed")]
    [InlineData(false, "fixed")]
    [InlineData(true, "external-coincident")]
    [InlineData(false, "external-coincident")]
    [InlineData(true, "whole-path")]
    [InlineData(false, "whole-path")]
    [InlineData(true, "ambiguous-vertex")]
    [InlineData(false, "ambiguous-vertex")]
    [InlineData(true, "bad-reference")]
    [InlineData(true, "malformed-reference")]
    public void InvalidatedIntentRejectsAtomicallyInsteadOfDroppingOrRetargetingIt(bool fillet, string intent)
    {
        var dimensions = new List<SketchDimension>();
        var constraints = new List<SketchConstraint>();
        switch (intent)
        {
            case "driving":
                dimensions.Add(new("driving", SketchDimensionKind.LinearDistance,
                new[] { "path|segment|0:start", "path|segment|0:end" }, 10, isDriving: true)); break;
            case "fixed": constraints.Add(Constraint("fixed", SketchConstraintKind.Fix, "path|segment|0")); break;
            case "external-coincident":
                constraints.Add(Constraint("external", SketchConstraintKind.Coincident,
                "path|segment|0:end", "external:center")); break;
            case "whole-path": constraints.Add(Constraint("fixed-path", SketchConstraintKind.Fix, "path")); break;
            case "ambiguous-vertex":
                dimensions.Add(new("ambiguous", SketchDimensionKind.LinearDistance,
                new[] { "path|point|vertex-0|0|0", "path|point|vertex-1|10|0" }, 10)); break;
            case "bad-reference":
                constraints.Add(new("bad", SketchConstraintKind.Horizontal,
                new[] { "path|segment|99" }, SketchConstraintState.Suppressed)); break;
            case "malformed-reference":
                constraints.Add(new("bad", SketchConstraintKind.Horizontal,
                new[] { "path|segment|bad" }, SketchConstraintState.Suppressed)); break;
        }
        var source = OpenPath();
        var document = new DrawingDocument(new DrawingEntity[] { source, new PointEntity(EntityId.Create("external"), new(10, 0)) },
            dimensions, constraints);

        Modify(document, fillet, .7, new[] { "path|segment|0", "path|segment|1" }, out var next).Should().BeFalse();

        next.Should().BeSameAs(document);
        document.Entities[0].Should().BeSameAs(source);
        document.Dimensions.Should().Equal(dimensions);
        document.Constraints.Should().Equal(constraints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(0.0000001)]
    public void InvalidRadiusAndDistanceNeverClampOrMutate(double amount)
    {
        var document = new DrawingDocument(new[] { OpenPath() });
        foreach (var fillet in new[] { false, true })
        {
            Modify(document, fillet, amount, new[] { "path|segment|0", "path|segment|1" }, out var next).Should().BeFalse();
            next.Should().BeSameAs(document);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonAdjacentSegmentsAndDuplicateIdsRejectAtomically(bool fillet)
    {
        var source = new PolylineEntity(EntityId.Create("path"),
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(20, 10), new Point2(10, 10) });
        var document = new DrawingDocument(new[] { source });
        Modify(document, fillet, .7, new[] { "path|segment|0", "path|segment|2" }, out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
        document = new DrawingDocument(new[] { OpenPath() });
        var result = fillet
            ? DrawingModifyService.TryFilletSelectedCorner(document, new[] { "path|segment|0", "path|segment|1" }, .7, _ => source.Id, out next)
            : DrawingModifyService.TryChamferSelectedCorner(document, new[] { "path|segment|0", "path|segment|1" }, .5, _ => source.Id, out next);
        result.Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurvedAdjacentEdgesRejectWithoutFlattening(bool fillet)
    {
        var arc = new ArcEntity(EntityId.Create("curve"), new(10, 2), 2, 270, 360);
        var document = new DrawingDocument(new DrawingEntity[] { OpenPath(), arc });
        Modify(document, fillet, .7, new[] { "path|segment|0", "curve" }, out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
        Modify(document, fillet, .7, new[] { "path|segment|0", "path|segment|1" }, out next).Should().BeFalse();
        next.Should().BeSameAs(document);
        next.Entities[1].Should().BeSameAs(arc);
    }

    [Fact]
    public void ImportedBulgeSurrogatesAreNotTreatedAsStraightEditableCorners()
    {
        const string text = "0\nLWPOLYLINE\n5\nA\n90\n3\n10\n0\n20\n0\n42\n0.5\n10\n10\n20\n0\n10\n10\n20\n10\n0\nEOF\n";
        var document = DxfDocumentReader.Read(text);
        document.Metadata.Warnings.Should().Contain(w => w.Code == CutPathLengthService.UnsafeImportCode);
        foreach (var fillet in new[] { true, false })
        {
            Modify(document, fillet, .7, new[] { "polyline-A|segment|0", "polyline-A|segment|1" }, out var next).Should().BeFalse();
            next.Should().BeSameAs(document);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ImportedStraightLwPolylineCornersAreEditable(bool fillet, bool closed)
    {
        var text = "0\nLWPOLYLINE\n5\nA\n90\n3\n70\n" + (closed ? "1" : "0")
            + "\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n0\nEOF\n";
        var document = DxfDocumentReader.Read(text);

        Modify(document, fillet, fillet ? .7 : .5,
            new[] { "polyline-A|segment|0", "polyline-A|segment|1" }, out var next).Should().BeTrue();

        AssertSatisfied(next);
        if (closed) ClosedContourService.Calculate(next).ClosedContourCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurveAttachedToUnchangedEndpointAndItsConstraintsArePreserved(bool fillet)
    {
        var curve = new ArcEntity(EntityId.Create("curve"), new(0, 2), 2, 180, 270);
        var document = new DrawingDocument(new DrawingEntity[] { OpenPath(), curve }, Array.Empty<SketchDimension>(), new[]
        {
            Constraint("connected", SketchConstraintKind.Coincident, "path|segment|0:start", "curve:end"),
            Constraint("tangent", SketchConstraintKind.Tangent, "path|segment|0", "curve")
        });
        Modify(document, fillet, .7, new[] { "path|segment|0", "path|segment|1" }, out var next).Should().BeTrue();
        next.Entities.Single(e => e.Id == curve.Id).Should().BeSameAs(curve);
        next.Constraints.Should().Contain(c => c.Id == "connected").And.Contain(c => c.Id == "tangent");
        AssertSatisfied(next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASecondCornerEditPreservesTheFirstConnectorAndItsReferences(bool fillet)
    {
        var document = new DrawingDocument(new[] { new PolylineEntity(EntityId.Create("path"),
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10), new Point2(0, 0) }) });
        Modify(document, fillet, .7, new[] { "path|segment|0", "path|segment|1" }, out var first).Should().BeTrue();
        var connector = first.Entities.Single(e => e is not PolylineEntity);
        var success = fillet
            ? DrawingModifyService.TryFilletSelectedCorner(first, new[] { "path|segment|0", "path|segment|1" }, .7,
                prefix => EntityId.Create(prefix + "-second"), out var next)
            : DrawingModifyService.TryChamferSelectedCorner(first, new[] { "path|segment|0", "path|segment|1" }, .5,
                prefix => EntityId.Create(prefix + "-second"), out next);
        success.Should().BeTrue();
        next.Entities.Single(e => e.Id == connector.Id).Should().BeSameAs(connector);
        AssertSatisfied(next);
        ClosedContourService.Calculate(next).ClosedContourCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true, 60)]
    [InlineData(true, 120)]
    [InlineData(true, -90)]
    [InlineData(true, -45)]
    [InlineData(false, 60)]
    [InlineData(false, 120)]
    [InlineData(false, -90)]
    [InlineData(false, -45)]
    public void NonRightAndClockwiseCornersUseActualAngleWithoutChangingRemoteEndpoints(bool fillet, double turnDegrees)
    {
        const double amount = .7;
        var radians = turnDegrees * Math.PI / 180;
        var corner = new Point2(10, 0);
        var end = new Point2(10 + 10 * Math.Cos(radians), 10 * Math.Sin(radians));
        var document = new DrawingDocument(new[] { new PolylineEntity(EntityId.Create("path"),
            new[] { new Point2(0, 0), corner, end }) });
        Modify(document, fillet, amount, new[] { "path|segment|0", "path|segment|1" }, out var next).Should().BeTrue();
        var paths = next.Entities.OfType<PolylineEntity>().ToArray();
        AssertPoint(paths[0].Vertices[0], new(0, 0));
        AssertPoint(paths[1].Vertices[^1], end);
        var interior = Math.PI - Math.Abs(radians);
        var trim = fillet ? amount / Math.Tan(interior / 2) : amount;
        Length(paths[0].Vertices[^1], corner).Should().BeApproximately(trim, .000001);
        Length(paths[1].Vertices[0], corner).Should().BeApproximately(trim, .000001);
        var connectorLength = fillet ? amount * Math.Abs(radians) : 2 * trim * Math.Sin(interior / 2);
        var length = CutPathLengthService.Calculate(next).CutLengthInches;
        length.Should().NotBeNull();
        ((double)length!.Value).Should().BeApproximately(20 - 2 * trim + connectorLength, .000001);
        AssertSatisfied(next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedSegmentEndpointSelectionUsesTheSameTopologyFix(bool fillet)
    {
        var document = new DrawingDocument(new[] { OpenPath() });
        Modify(document, fillet, .7, new[] { "path|segment|0:end" }, out var next).Should().BeTrue();
        next.Entities.OfType<PolylineEntity>().Should().HaveCount(2);
        AssertSatisfied(next);
    }

    private static PolylineEntity OpenPath() => new(EntityId.Create("path"),
        new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) });

    private static SketchConstraint Constraint(string id, SketchConstraintKind kind, params string[] keys) =>
        new(id, kind, keys, SketchConstraintState.Satisfied);

    private static double Length(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static bool Modify(DrawingDocument document, bool fillet, double amount,
        string[] selection, out DrawingDocument next) => fillet
        ? DrawingModifyService.TryFilletSelectedCorner(document, selection, amount,
            prefix => EntityId.Create(prefix + "-created"), out next)
        : DrawingModifyService.TryChamferSelectedCorner(document, selection, amount,
            prefix => EntityId.Create(prefix + "-created"), out next);

    private static void AssertConnector(DrawingDocument document, bool fillet, double amount)
    {
        if (fillet)
        {
            var arc = document.Entities.OfType<ArcEntity>().Single();
            arc.Radius.Should().BeApproximately(amount, 0.000001);
            AssertPoint(arc.Center, new(10 - amount, amount));
            var sweep = (arc.EndAngleDegrees - arc.StartAngleDegrees + 360) % 360;
            sweep.Should().BeApproximately(90, 0.000001);
        }
        else
        {
            var bridge = document.Entities.OfType<LineEntity>().Single();
            AssertPoint(bridge.Start, new(10 - amount, 0));
            AssertPoint(bridge.End, new(10, amount));
        }
    }

    private static void AssertSatisfied(DrawingDocument document) => document.Constraints
        .Select(c => SketchConstraintService.ValidateConstraint(document, c).State)
        .Should().OnlyContain(state => state == SketchConstraintState.Satisfied);

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 0.000001);
        actual.Y.Should().BeApproximately(expected.Y, 0.000001);
    }
}
