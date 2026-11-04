using DXFER.Blazor.Sketching;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;
using System.Text.Json;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsEllipseCreationRuntimeTests
{
    [Theory]
    [InlineData(90, false, 0)]
    [InlineData(90, true, 0)]
    [InlineData(90, false, 37)]
    [InlineData(90, true, 37)]
    [InlineData(0, false, 0)]
    [InlineData(0, true, 37)]
    public void UnconstrainedEllipseQuadrantDimensionCreatesEditsAndReopens(int quadrant, bool reverse, double angle)
    {
        var radians = angle * Math.PI / 180;
        var ellipse = new EllipseEntity(EntityId.Create("shape"), new(7, -2),
            new(6 * Math.Cos(radians), 6 * Math.Sin(radians)), 0.5);
        var original = new DrawingDocument([ellipse]);
        var keys = new[] { quadrant, quadrant + 180 }.Select(degrees =>
        {
            var point = ellipse.PointAtParameterDegrees(degrees);
            return FormattableString.Invariant($"shape|point|quadrant-{degrees}|{point.X:R}|{point.Y:R}");
        }).ToArray();
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(original, keys, "picked-axis", out var dimension, out var status,
            anchorOverride: new(16, -2)).Should().BeTrue(status);
        dimension.ReferenceKeys.Should().Equal(keys);
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.IsDriving.Should().BeTrue();
        dimension.Value.Should().BeApproximately(quadrant == 0 ? 12 : 6, 1e-6);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);

        var created = solver.Solve(new(original, [], [dimension]));

        AssertSolved(created, observed, 12, 6);
        var placed = (EllipseEntity)created.Document.Entities.Single();
        placed.Center.X.Should().BeApproximately(ellipse.Center.X, 1e-12);
        placed.Center.Y.Should().BeApproximately(ellipse.Center.Y, 1e-12);
        placed.MajorAxisEndPoint.X.Should().BeApproximately(ellipse.MajorAxisEndPoint.X, 1e-12);
        placed.MajorAxisEndPoint.Y.Should().BeApproximately(ellipse.MajorAxisEndPoint.Y, 1e-12);
        created.Document.Constraints.Should().BeEmpty();
        foreach (var value in quadrant == 0 ? new[] { 14d, 16d } : new[] { 8d, 10d })
        {
            var reopened = RoundTrip(created.Document);
            reopened.Dimensions.Single().ReferenceKeys.Should().Equal(keys);
            var edit = WithValue(reopened.Dimensions.Single(), value);
            created = solver.Solve(new(reopened, [], [edit]));
            created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
            observed.Response!.Engine.Should().Be("PlaneGCS");
            created.Document.Dimensions.Single().IsDriving.Should().BeTrue();
            created.Document.Dimensions.Single().ReferenceKeys.Should().Equal(keys);
            SketchDimensionSolverService.TryGetDimensionMeasurement(created.Document, edit, out var measured).Should().BeTrue();
            measured.Should().BeApproximately(value, 1e-6);
            var changed = (EllipseEntity)created.Document.Entities.Single();
            var radius = Math.Sqrt(changed.MajorAxisEndPoint.X * changed.MajorAxisEndPoint.X
                + changed.MajorAxisEndPoint.Y * changed.MajorAxisEndPoint.Y);
            (2 * radius * (quadrant == 0 ? 1 : changed.MinorRadiusRatio)).Should().BeApproximately(value, 1e-6);
        }
        original.Dimensions.Should().BeEmpty();
        original.Entities.Should().Equal(ellipse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuadrantFixedConflictReachesEngineAndRefusesWithoutMovingGeometry(bool fixPoints)
    {
        var keys = new[] { "shape|point|quadrant-90|7|1", "shape|point|quadrant-270|7|-5" };
        var document = new DrawingDocument([new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(6, 0), 0.5)]);
        var dimension = new SketchDimension("minor", SketchDimensionKind.LinearDistance, keys, 8, isDriving: true);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(
            new(document, [], [dimension], fixedReferenceKeys: fixPoints ? keys : ["shape"]));

        observed.Response.Should().NotBeNull();
        observed.Response!.Engine.Should().Be("PlaneGCS");
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Single().Value.Should().Be(8);
    }

    [Fact]
    public void ExportCreationReferenceBrowserFixturesWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("DXFER_CREATION_REFERENCE_FIXTURES");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var source = (EllipseEntity)SketchCreationEntityFactory.CreateEntitiesForTool("ellipticalarc",
            [new(17, -9), new(23, -1), new(13, -6), new(11, -13)], _ => EntityId.Create("source"), false).Single();
        var document = new DrawingDocument([source], [], [], new DrawingDocumentMetadata
        {
            SourceFileName = "ellipticalarc-before.dxf", Units = DrawingUnits.Inches
        });
        RoundTrip(document).Entities.Should().Equal(document.Entities);
        File.WriteAllText(Path.Combine(output, "ellipticalarc-before.dxf"),
            DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
        var recipe = new
        {
            file = "ellipticalarc-before.dxf", sourceId = source.Id.Value, tool = "line", automaticSnapConstraints = true,
            center = source.Center, source.MajorAxisEndPoint, source.MinorRadiusRatio,
            source.StartParameterDegrees, source.EndParameterDegrees,
            endpoints = new[] { "start", "end" }.Select(label =>
            {
                var point = source.PointAtParameterDegrees(label == "start" ? source.StartParameterDegrees : source.EndParameterDegrees);
                return new
                {
                    label, point, referenceKey = FormattableString.Invariant($"source|point|{label}|{point.X:R}|{point.Y:R}"),
                    canonicalReferenceKey = $"source:{label}", pointerEnd = new Point2(point.X + 3, point.Y + 4),
                    keyedLength = 12, keyedEnd = new Point2(point.X + 7.2, point.Y + 9.6)
                };
            }).ToArray()
        };
        File.WriteAllText(Path.Combine(output, "ellipticalarc-recipe.json"), JsonSerializer.Serialize(recipe,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    }

    [Theory]
    [InlineData("start", false)]
    [InlineData("end", false)]
    [InlineData("start", true)]
    [InlineData("end", true)]
    public void LineCreationAtEllipticalArcEndpointReachesRealEngineAndReopens(string label, bool keyed)
    {
        var source = (EllipseEntity)SketchCreationEntityFactory.CreateEntitiesForTool("ellipticalarc",
            [new(17, -9), new(23, -1), new(13, -6), new(11, -13)], _ => EntityId.Create("source"), false).Single();
        var start = source.PointAtParameterDegrees(label == "start" ? source.StartParameterDegrees : source.EndParameterDegrees);
        var values = keyed ? new Dictionary<string, double> { ["length"] = 12 } : new Dictionary<string, double>();
        var created = SketchCreationEntityFactory.CreateEntitiesForTool("line", [start, new(start.X + 3, start.Y + 4)],
            _ => EntityId.Create("created"), false, values);
        var index = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [source], created,
            _ => $"c{++index}", creationSnaps: [new CanvasCreationSnapDto(start, "Coincident", [$"source|point|{label}|999|-999"])]);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool("line", created, values, () => "length");
        var document = new DrawingDocument(new DrawingEntity[] { source }.Concat(created), dimensions, []);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(document, constraints, [], fixedReferenceKeys: ["source"]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        result.Document.Entities.Should().BeEquivalentTo(document.Entities);
        result.Document.Constraints.Should().ContainSingle();
        var reopened = RoundTrip(result.Document);
        reopened.Constraints.Should().BeEquivalentTo(result.Document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(dimensions);
        foreach (var constraint in reopened.Constraints)
            SketchConstraintService.ValidateConstraint(reopened, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        foreach (var dimension in reopened.Dimensions)
        {
            SketchDimensionSolverService.TryGetDimensionMeasurement(reopened, dimension, out var measured).Should().BeTrue();
            measured.Should().BeApproximately(12, 1e-6);
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 37)]
    [InlineData(7, -2, 0)]
    [InlineData(7, -2, 37)]
    [InlineData(-4, 9, 90)]
    [InlineData(-4, 9, 217)]
    public void KeyedMajor12Minor6CreationReachesRealEngine(double x, double y, double angle)
    {
        var generated = Create(x, y, angle);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = new DrawingDocument(generated.Entities, [], [], generated.Metadata);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(
            new(original, generated.Constraints, generated.Dimensions));

        AssertSolved(result, observed, 12, 6);
        result.Document.Entities.Should().BeEquivalentTo(generated.Entities);
        result.Document.Dimensions.Should().BeEquivalentTo(generated.Dimensions);
        result.Document.Constraints.Should().BeEquivalentTo(generated.Constraints);
        original.Dimensions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 37, 1)]
    [InlineData(7, -2, 37, 0)]
    [InlineData(7, -2, 37, 1)]
    [InlineData(-4, 9, 90, 0)]
    [InlineData(-4, 9, 217, 1)]
    public void CreatedAxisCanBeEditedThenReopenedAndEditedAgain(double x, double y, double angle, int axis)
    {
        var generated = Create(x, y, angle);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var created = solver.Solve(new(new DrawingDocument(generated.Entities), generated.Constraints, generated.Dimensions));
        AssertSolved(created, observed, 12, 6);
        var editedDimension = WithValue(created.Document.Dimensions[axis], axis == 0 ? 14 : 8);

        var edited = solver.Solve(new(created.Document, [], [editedDimension], fixedReferenceKeys: ["shape:center"]));

        AssertSolved(edited, observed, axis == 0 ? 14 : 12, axis == 0 ? 6 : 8);
        AssertPoint(((EllipseEntity)edited.Document.Entities.Single()).Center, new(x, y));
        var reopened = RoundTrip(edited.Document);
        reopened.Dimensions.Should().BeEquivalentTo(edited.Document.Dimensions);
        reopened.Constraints.Should().BeEquivalentTo(edited.Document.Constraints);
        var reapplied = solver.Solve(SketchSolveRequest.FromDocument(reopened));
        AssertSolved(reapplied, observed, axis == 0 ? 14 : 12, axis == 0 ? 6 : 8);
        var nextDimension = WithValue(reapplied.Document.Dimensions[1 - axis], axis == 0 ? 8 : 14);
        var next = solver.Solve(new(reapplied.Document, [], [nextDimension], fixedReferenceKeys: ["shape:center"]));
        AssertSolved(next, observed, 14, 8);
        AssertPoint(((EllipseEntity)next.Document.Entities.Single()).Center, new(x, y));
        next.Document.Dimensions.SelectMany(d => d.ReferenceKeys).Should()
            .Equal(generated.Dimensions.SelectMany(d => d.ReferenceKeys));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void StaleUppercaseAxisCoordinatesAreIdentityNotGeometry(int axis)
    {
        var generated = Create(7, -2, 37);
        var name = axis == 0 ? "MAJOR" : "MINOR";
        var stale = new SketchDimension("stale", SketchDimensionKind.LinearDistance,
            [$"shape|point|{name}-END|999|999", $"shape|point|{name}-START|-999|-999"],
            axis == 0 ? 12 : 6, isDriving: true);
        var document = new DrawingDocument(generated.Entities, [stale], []);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(SketchSolveRequest.FromDocument(document));

        AssertSolved(result, observed, 12, 6);
        result.Document.Entities.Should().BeEquivalentTo(document.Entities);
        var ellipse = (EllipseEntity)result.Document.Entities.Single();
        SketchReferenceResolver.TryGetPoint(result.Document, stale.ReferenceKeys[0], out var point).Should().BeTrue();
        AssertPoint(point, ellipse.PointAtParameterDegrees(axis == 0 ? 0 : 90));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void FixedEntityOrOppositeAxisPointsRefuseIncompatibleDiameter(bool axisPoints, int axis)
    {
        var document = Create(7, -2, 37);
        var keys = axisPoints ? document.Dimensions[axis].ReferenceKeys.ToArray() : ["shape"];
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var edit = WithValue(document.Dimensions[axis], axis == 0 ? 14 : 8);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(document, [], [edit], fixedReferenceKeys: keys));

        observed.Response.Should().NotBeNull("fixed axes must reach the actual constrained engine");
        observed.Response!.Engine.Should().Be("PlaneGCS");
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions[axis].Value.Should().Be(edit.Value);
    }

    [Theory]
    [InlineData("major-start", 0)]
    [InlineData("major-end", 0)]
    [InlineData("minor-start", 1)]
    [InlineData("minor-end", 1)]
    [InlineData("quadrant-180", 0)]
    [InlineData("quadrant-0", 0)]
    [InlineData("quadrant-270", 1)]
    [InlineData("quadrant-90", 1)]
    public void FixedNamedPointAllowsOtherAxisEditAndIsCheckedAgainstLiveResult(string label, int fixedAxis)
    {
        var document = Create(7, -2, 37);
        var fixedKey = $"shape|point|{label}|999|999";
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var edit = WithValue(document.Dimensions[1 - fixedAxis], fixedAxis == 0 ? 8 : 14);

        var result = solver.Solve(new(document, [], [edit], fixedReferenceKeys: [fixedKey]));

        AssertSolved(result, observed, fixedAxis == 0 ? 12 : 14, fixedAxis == 0 ? 8 : 6);
        SketchReferenceResolver.TryGetPoint(document, fixedKey, out var before).Should().BeTrue();
        SketchReferenceResolver.TryGetPoint(result.Document, fixedKey, out var after).Should().BeTrue();
        AssertPoint(after, before);

        // Tamper only after a real solve to exercise the adapter's independent fixed-point guard.
        var tampered = new TranslatedResponseRuntime(runtime);
        var rejected = new PlaneGcsSketchSolverAdapter(tampered).Solve(
            new(document, [], [], fixedReferenceKeys: [fixedKey]));
        tampered.Response!.Engine.Should().Be("PlaneGCS");
        tampered.Response.Status.Should().Be("solved");
        rejected.Status.Should().Be(SketchSolveStatus.Failed);
        rejected.Diagnostics.Should().Contain(message => message.Contains("would move fixed point"));
        rejected.Document.Entities.Should().Equal(document.Entities);
    }

    public static IEnumerable<object[]> InvalidAxisKeys() => SketchEllipseAxisReferenceTests.InvalidAxisKeys();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AxisNamesCannotBeUsedOnAnotherEntityType(bool circle)
    {
        DrawingEntity entity = circle
            ? new CircleEntity(EntityId.Create("shape"), new(7, -2), 6)
            : new LineEntity(EntityId.Create("shape"), new(1, -2), new(13, -2));
        var document = new DrawingDocument([entity]);
        var dimension = new SketchDimension("wrong-type", SketchDimensionKind.LinearDistance,
            ["shape|point|major-start|1|-2", "shape|point|major-end|13|-2"], 12, isDriving: true);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(document, [], [dimension]));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response.Should().BeNull();
        result.Diagnostics.Should().Contain(message => message.Contains("Invalid dimension 'wrong-type'"));
        result.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [MemberData(nameof(InvalidAxisKeys))]
    public void InvalidAxisReferenceIsRejectedBeforeCallingRuntime(string key)
    {
        var document = Create(7, -2, 37);
        var bad = new SketchDimension("bad-axis", SketchDimensionKind.LinearDistance,
            [key, document.Dimensions[0].ReferenceKeys[1]], 12, isDriving: true);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(document, [], [bad]));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response.Should().BeNull();
        result.Diagnostics.Should().Contain(message => message.Contains("Invalid dimension 'bad-axis'"));
        result.Document.Entities.Should().Equal(document.Entities);
    }

    private static DrawingDocument Create(double x, double y, double angle)
    {
        var center = new Point2(x, y);
        var radians = angle * Math.PI / 180;
        var values = new Dictionary<string, double> { ["major"] = 12, ["minor"] = 6 };
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool("ellipse",
            [center, new(x + 10 * Math.Cos(radians), y + 10 * Math.Sin(radians)),
                new(x - 10 * Math.Sin(radians), y + 10 * Math.Cos(radians))],
            _ => EntityId.Create("shape"), false, values);
        var dimensionIndex = 0;
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool("ellipse", entities, values,
            () => $"diameter-{++dimensionIndex}");
        var constraintIndex = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("ellipse", [], entities,
            _ => $"constraint-{++constraintIndex}", originSnapUsed: x == 0 && y == 0);
        return new(entities, dimensions, constraints,
            new DrawingDocumentMetadata { SourceFileName = "keyed-ellipse.dxf", Units = DrawingUnits.Inches });
    }

    private static SketchDimension WithValue(SketchDimension dimension, double value) =>
        new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, value, dimension.Anchor, dimension.IsDriving);

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var restored = DxfDocumentReader.Read(DxfDocumentWriter.Write(document,
            new DxfWriteOptions(IncludeNativeDocument: true)), out var nativeRestored);
        nativeRestored.Should().BeTrue();
        return restored;
    }

    private static void AssertSolved(SketchSolveResult result, ObservedRuntime observed, double major, double minor)
    {
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response.Should().NotBeNull();
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        var ellipse = result.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>().Which;
        var radius = Math.Sqrt(ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X
            + ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y);
        (2 * radius).Should().BeApproximately(major, 1e-6);
        (2 * radius * ellipse.MinorRadiusRatio).Should().BeApproximately(minor, 1e-6);
        foreach (var dimension in result.Document.Dimensions)
        {
            SketchDimensionSolverService.TryGetDimensionMeasurement(result.Document, dimension, out var measured).Should().BeTrue();
            measured.Should().BeApproximately(dimension.Value, 1e-6);
        }
    }

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-6);
        actual.Y.Should().BeApproximately(expected.Y, 1e-6);
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists);
        if (node is null) throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        var module = Environment.GetEnvironmentVariable("DXFER_CREATION_REFERENCE_SOLVER_MODULE");
        module = string.IsNullOrWhiteSpace(module) ? Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js")
            : Path.GetFullPath(module);
        if (!File.Exists(module)) throw new FileNotFoundException("The requested PlaneGCS module is unavailable.", module);
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"), module);
    }

    private sealed class ObservedRuntime(IPlaneGcsRuntime inner) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse? Response { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request) => Response = inner.Solve(request);
    }

    private sealed class TranslatedResponseRuntime(IPlaneGcsRuntime inner) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse? Response { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Response = inner.Solve(request);
            return Response with
            {
                Entities = Response.Entities?.Select(e => e with { Center = e.Center! with { X = e.Center.X + 1 } }).ToArray()
            };
        }
    }
}
