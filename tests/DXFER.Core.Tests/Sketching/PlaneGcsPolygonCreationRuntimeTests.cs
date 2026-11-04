using System.Text.Json;
using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsPolygonCreationRuntimeTests
{
    public static IEnumerable<object[]> VertexCases()
    {
        foreach (var circumscribed in new[] { false, true })
        foreach (var vertex in Enumerable.Range(0, 5))
        foreach (var keyed in new[] { false, true })
            yield return [circumscribed, vertex, keyed];
    }

    [Theory]
    [MemberData(nameof(VertexCases))]
    public void EveryVertexCreatesReopensAndFollowsSourceEdits(bool circumscribed, int vertex, bool keyed)
    {
        var source = Source(circumscribed);
        var start = source.GetVertices()[vertex];
        var values = keyed ? new Dictionary<string, double> { ["length"] = 12 } : new Dictionary<string, double>();
        var created = SketchCreationEntityFactory.CreateEntitiesForTool("line", [start, new(start.X + 3, start.Y + 4)],
            _ => EntityId.Create("created"), false, values);
        var key = $"source|point|vertex-{vertex}|999|-999";
        if (keyed)
        {
            SketchReference.TryNormalize(key, out var canonical).Should().BeTrue();
            key = canonical;
        }
        var index = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [source], created,
            _ => $"c{++index}", creationSnaps: [new CanvasCreationSnapDto(start, "Coincident", [key])]);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool("line", created, values, () => "length");
        var document = new DrawingDocument(new DrawingEntity[] { source }.Concat(created), dimensions, [], Metadata(circumscribed));
        var before = DxferDocumentSerializer.Write(document);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var relation = constraints.Should().ContainSingle().Which;
        relation.ReferenceKeys.Should().Equal(key, "created:start");
        var first = solver.Solve(new(document, constraints, [], fixedReferenceKeys: ["source"]));
        AssertSolved(first, observed, vertex);
        AssertCreationUnchanged(first.Document, source, (LineEntity)created.Single());

        // Resolve's optional dragging argument must not receive Array.map's reference index.
        var reversed = new SketchConstraint(relation.Id, relation.Kind, ["created:start", key], relation.State);
        var second = solver.Solve(new(document, [reversed], [], fixedReferenceKeys: ["source"]));
        AssertSolved(second, observed, vertex);
        AssertCreationUnchanged(second.Document, source, (LineEntity)created.Single());
        second.Document.Constraints.Should().ContainSingle().Which.ReferenceKeys.Should().Equal("created:start", key);
        second.Document.Dimensions.Should().HaveCount(keyed ? 1 : 0);
        DxferDocumentSerializer.Write(document).Should().Be(before);

        var reopened = RoundTrip(second.Document);
        reopened.Constraints.Should().BeEquivalentTo(second.Document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(second.Document.Dimensions);
        var reapplied = solver.Solve(SketchSolveRequest.FromDocument(reopened));
        AssertSolved(reapplied, observed, vertex);
        var target = new Point2(source.Center.X + 4, source.Center.Y - 3);
        solver.TryApplyDrag(reapplied.Document, "source:center", source.Center, target, false,
            out var translated, out var status, automaticSnapConstraints: false).Should().BeTrue(status);
        AssertEngine(observed);
        var movedSource = translated.Entities.OfType<PolygonEntity>().Single();
        AssertPoint(movedSource.Center, target);
        movedSource.Radius.Should().BeApproximately(source.Radius, 1e-6);
        movedSource.RotationAngleDegrees.Should().BeApproximately(source.RotationAngleDegrees, 1e-6);
        AssertPoint(translated.Entities.OfType<LineEntity>().Single().Start, new(start.X + 4, start.Y - 3));
        AssertDocument(translated, vertex);
        translated.Constraints.Should().BeEquivalentTo(reopened.Constraints);
        translated.Dimensions.Should().BeEquivalentTo(reopened.Dimensions, options => options.Excluding(d => d.Anchor));

        var radius = new SketchDimension("source-radius", SketchDimensionKind.Radius, ["source"], 15, isDriving: true);
        var resized = solver.Solve(new(RoundTrip(translated), [], [radius], fixedReferenceKeys: ["source:center"]));
        ExportRadiusFailure(observed, circumscribed, vertex, keyed);
        AssertSolved(resized, observed, vertex);
        var resizedSource = resized.Document.Entities.OfType<PolygonEntity>().Single();
        AssertPoint(resizedSource.Center, target);
        resizedSource.Radius.Should().BeApproximately(15, 1e-6);
        resized.Document.Entities.OfType<LineEntity>().Single().Start.Should().NotBe(translated.Entities.OfType<LineEntity>().Single().Start);
        resized.Document.Constraints.Should().BeEquivalentTo(reopened.Constraints);
        var final = solver.Solve(SketchSolveRequest.FromDocument(RoundTrip(resized.Document)));
        AssertSolved(final, observed, vertex);
        final.Document.Dimensions.Should().HaveCount(keyed ? 2 : 1);
        final.Document.Constraints.Should().BeEquivalentTo(reopened.Constraints);
        DxferDocumentSerializer.Write(document).Should().Be(before);
    }

    public static IEnumerable<object[]> InvalidReferences()
    {
        foreach (var key in new[]
        {
            "source|segment|0", "source|segment|4", "source|segment|5",
            "source|segment|0:center", "source|segment|0:mid",
            "source|segment|5:start", "source|segment|5:end", "source|segment|99:end",
            "source|segment|-1:start", "source|point|vertex-5|23|-1", "source|point|vertex-99|23|-1"
        })
        foreach (var fixedReference in new[] { false, true })
            yield return [key, fixedReference];
    }

    [Theory]
    [MemberData(nameof(InvalidReferences))]
    public void PolygonSegmentBodiesAndInvalidIndicesAreRejectedBeforeRuntime(string key, bool fixedReference)
    {
        var source = Source(false);
        var document = new DrawingDocument([source, new PointEntity(EntityId.Create("created"), source.GetVertices()[0])]);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var constraint = new SketchConstraint("snap", SketchConstraintKind.Coincident, ["created", key]);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(fixedReference
            ? new(document, [], [], fixedReferenceKeys: [key]) : new(document, [constraint], []));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response.Should().BeNull();
        result.Diagnostics.Should().Contain(message => message.Contains(fixedReference
            ? "Unsupported or missing fixed reference" : "Unsupported or missing reference in constraint"));
        result.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData("fixed-first")]
    [InlineData("fixed-last")]
    [InlineData("document-fix")]
    [InlineData("request-fix")]
    [InlineData("initial-guess")]
    public void InvalidRawIdentityCannotMergeIntoValidClosingEndpoint(string path)
    {
        const string invalid = "source|point|vertex-5|23|-1";
        const string valid = "source|segment|4:end";
        var source = Source(false);
        var fix = new SketchConstraint("fix", SketchConstraintKind.Fix, [invalid]);
        var document = new DrawingDocument([source], [], path == "document-fix" ? [fix] : []);
        var request = new SketchSolveRequest(document, path == "request-fix" ? [fix] : [], [],
            fixedReferenceKeys: path == "fixed-first" ? [invalid, valid]
                : path == "fixed-last" ? [valid, invalid] : [valid],
            initialGuesses: path == "initial-guess" ? [new SketchSolveInitialGuess(invalid, source.GetVertices()[0])] : []);
        request.FixedReferenceKeys.Should().Equal(valid);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(request);

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response.Should().BeNull();
        result.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData("source|point|vertex-4|999|-999", "source|segment|3:end", 4)]
    [InlineData("source|segment|4:end", "source|segment|4:end", 0)]
    public void ValidAcquisitionIdentitiesStillUseCanonicalFixedAndInitialGuessKeys(string key, string canonical, int vertex)
    {
        var source = Source(false);
        var document = new DrawingDocument([source]);
        var request = new SketchSolveRequest(document, [], [], fixedReferenceKeys: [key, canonical],
            initialGuesses: [new SketchSolveInitialGuess(key, source.GetVertices()[vertex])]);
        request.FixedReferenceKeys.Should().Equal(canonical);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(request);

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        AssertEngine(observed);
        observed.Request!.FixedReferenceKeys.Should().Equal(canonical, SketchReference.OriginKey);
        observed.Request.InitialGuesses.Should().ContainSingle().Which.ReferenceKey.Should().Be(canonical);
        var solved = result.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<PolygonEntity>().Which;
        AssertPoint(solved.GetVertices()[vertex], source.GetVertices()[vertex]);
    }

    [Theory]
    [InlineData(false, "start", 4)]
    [InlineData(false, "end", 0)]
    [InlineData(true, "start", 4)]
    [InlineData(true, "end", 0)]
    public void ClosingSegmentEndpointAliasesResolveWithoutAdmittingSegmentBodies(bool circumscribed, string target, int vertex)
    {
        var source = Source(circumscribed);
        var start = source.GetVertices()[vertex];
        var created = new LineEntity(EntityId.Create("created"), start, new(start.X + 3, start.Y + 4));
        var document = new DrawingDocument([source, created]);
        var constraint = new SketchConstraint("snap", SketchConstraintKind.Coincident, ["created:start", $"source|segment|4:{target}"]);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(document, [constraint], [], fixedReferenceKeys: ["source"]));

        AssertSolved(result, observed, vertex);
        AssertCreationUnchanged(result.Document, source, created);
        AssertDocument(RoundTrip(result.Document), vertex);
    }

    [Fact]
    public void ExportCreationReferenceBrowserFixturesWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("DXFER_CREATION_REFERENCE_FIXTURES");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var circumscribed in new[] { false, true })
        {
            var source = Source(circumscribed);
            var name = circumscribed ? "circumscribed-pentagon" : "inscribed-pentagon";
            var document = new DrawingDocument([source], [], [], Metadata(circumscribed));
            RoundTrip(document).Entities.Should().Equal(document.Entities);
            File.WriteAllText(Path.Combine(output, $"{name}-before.dxf"),
                DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
            var recipe = new
            {
                file = $"{name}-before.dxf", sourceId = source.Id.Value, tool = "line", automaticSnapConstraints = true,
                center = source.Center, radius = source.Radius, source.Circumscribed, source.SideCount,
                centerEditTarget = new Point2(source.Center.X + 4, source.Center.Y - 3), radiusEditTarget = 15,
                vertices = source.GetVertices().Select((point, vertex) => new
                {
                    index = vertex, point,
                    referenceKey = FormattableString.Invariant($"source|point|vertex-{vertex}|{point.X:R}|{point.Y:R}"),
                    canonicalReferenceKey = vertex == 0 ? "source|segment|0:start" : $"source|segment|{vertex - 1}:end",
                    pointerEnd = new Point2(point.X + 3, point.Y + 4), keyedLength = 12,
                    keyedEnd = new Point2(point.X + 7.2, point.Y + 9.6)
                }).ToArray()
            };
            File.WriteAllText(Path.Combine(output, $"{name}-recipe.json"), JsonSerializer.Serialize(recipe,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        }
    }

    private static PolygonEntity Source(bool circumscribed) =>
        (PolygonEntity)SketchCreationEntityFactory.CreateEntitiesForTool(circumscribed ? "circumscribedpolygon" : "inscribedpolygon",
            [new(17, -9), new(23, -1)], _ => EntityId.Create("source"), false,
            new Dictionary<string, double> { ["sides"] = 5 }).Single();

    private static DrawingDocumentMetadata Metadata(bool circumscribed) => new()
    {
        SourceFileName = circumscribed ? "circumscribed-pentagon-before.dxf" : "inscribed-pentagon-before.dxf",
        Units = DrawingUnits.Inches
    };

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(document,
            new DxfWriteOptions(IncludeNativeDocument: true)), out var nativeRestored);
        nativeRestored.Should().BeTrue();
        return reopened;
    }

    private static void AssertSolved(SketchSolveResult result, ObservedRuntime observed, int vertex)
    {
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        AssertEngine(observed);
        AssertDocument(result.Document, vertex);
    }

    private static void AssertEngine(ObservedRuntime observed)
    {
        observed.Response.Should().NotBeNull();
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
    }

    private static void AssertDocument(DrawingDocument document, int vertex)
    {
        document.Entities.Should().HaveCount(2);
        var polygon = document.Entities.OfType<PolygonEntity>().Single();
        var line = document.Entities.OfType<LineEntity>().Single();
        AssertPoint(line.Start, polygon.GetVertices()[vertex]);
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
        {
            SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured).Should().BeTrue();
            measured.Should().BeApproximately(dimension.Value, 1e-6);
        }
    }

    private static void AssertCreationUnchanged(DrawingDocument document, PolygonEntity source, LineEntity created)
    {
        document.Entities.OfType<PolygonEntity>().Single().Should().Be(source);
        var line = document.Entities.OfType<LineEntity>().Single();
        AssertPoint(line.Start, created.Start);
        AssertPoint(line.End, created.End);
    }

    private static void ExportRadiusFailure(ObservedRuntime observed, bool circumscribed, int vertex, bool keyed)
    {
        var output = Environment.GetEnvironmentVariable("DXFER_CREATION_REFERENCE_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(output) || observed.Response is null || observed.Response.Status == "solved"
            || observed.Request is null || !observed.Request.Dimensions.Any(d => d.Id == "source-radius")) return;
        Directory.CreateDirectory(output);
        var name = $"{(circumscribed ? "circumscribed" : "inscribed")}-vertex-{vertex}-{(keyed ? "keyed" : "pointer")}-radius";
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, name + "-request.json"), JsonSerializer.Serialize(observed.Request, options));
        File.WriteAllText(Path.Combine(output, name + "-response.json"), JsonSerializer.Serialize(observed.Response, options));
        File.WriteAllText(Path.Combine(output, name + "-frame.jsonl"),
            JsonSerializer.Serialize(new { id = 1, request = observed.Request }) + Environment.NewLine);
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
        public PlaneGcsRequest? Request { get; private set; }
        public PlaneGcsResponse? Response { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            Response = null;
            return Response = inner.Solve(request);
        }
    }
}
