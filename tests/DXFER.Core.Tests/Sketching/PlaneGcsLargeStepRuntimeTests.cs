using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsLargeStepRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(-37)]
    [InlineData(90)]
    public void ShortSlotDirectWidthEditPreservesOriginLengthAndAllTangencies(double angle)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = Slot(angle);
        var snapshot = DxferDocumentSerializer.Write(document);
        var accepted = solver.Solve(SketchSolveRequest.FromDocument(document));
        AssertSolved(accepted);

        var result = solver.Solve(new(accepted.Document, [], [Width(accepted.Document, 10)]));

        AssertSolved(result);
        AssertSlot(result.Document, 10);
        result.Document.Constraints.Select(c => c.Id).Should().Equal(document.Constraints.Select(c => c.Id));
        DxferDocumentSerializer.Write(document).Should().Be(snapshot);
        var reopened = RoundTrip(result.Document);
        AssertSlot(reopened, 10);
        var editedAgain = solver.Solve(new(reopened, [], [Width(reopened, 20)]));
        AssertSolved(editedAgain);
        AssertSlot(editedAgain.Document, 20);
    }

    [Theory]
    [InlineData(80, false)]
    [InlineData(80, true)]
    [InlineData(-80, false)]
    [InlineData(120, false)]
    public void FourBarAngle180FindsValidSectorAndReopens(double angle, bool reverse)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = FourBar(angle);
        var accepted = solver.Solve(SketchSolveRequest.FromDocument(document));
        AssertSolved(accepted);
        var dimension = new SketchDimension("angle", SketchDimensionKind.Angle,
            reverse ? ["right", "ground"] : ["ground", "right"], 180, isDriving: true);

        var result = solver.Solve(new(accepted.Document, [], [dimension]));

        AssertSolved(result);
        result.Document.Entities.Single(e => e.Id.Value == "ground").Should().Be(document.Entities[0]);
        var right = result.Document.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "right");
        (right.End.X - right.Start.X).Should().BeApproximately(-10, 1e-6);
        (right.End.Y - right.Start.Y).Should().BeApproximately(0, 1e-6);
        AssertDimensionsAndConstraints(result.Document);
        var reopened = RoundTrip(result.Document);
        AssertSolved(solver.Solve(SketchSolveRequest.FromDocument(reopened)));
        var next = solver.Solve(new(reopened, [],
            [new SketchDimension("angle", SketchDimensionKind.Angle, dimension.ReferenceKeys, 160, isDriving: true)]));
        AssertSolved(next);
        AssertDimensionsAndConstraints(next.Document);
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("four-bar")]
    public void FixedGeometryStillRefusesAndPreservesOriginalDocument(string kind)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = kind == "slot" ? Slot(37) : FourBar(80);
        var snapshot = DxferDocumentSerializer.Write(document);
        var dimension = kind == "slot" ? Width(document, 10)
            : new SketchDimension("angle", SketchDimensionKind.Angle, ["ground", "right"], 180, isDriving: true);
        var result = solver.Solve(new(document, [], [dimension],
            fixedReferenceKeys: document.Entities.Select(e => e.Id.Value)));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities.Should().Equal(document.Entities);
        DxferDocumentSerializer.Write(document).Should().Be(snapshot);
        AssertSolved(solver.Solve(SketchSolveRequest.FromDocument(document)));
    }

    [Fact]
    public void RetainedFailedAngleCanRecoverAfterNativeReopenWithoutReenteringItsValue()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var baseline = solver.Solve(SketchSolveRequest.FromDocument(FourBar(80)));
        AssertSolved(baseline);
        var failed = solver.Solve(new(baseline.Document, [],
            [new("angle", SketchDimensionKind.Angle, ["ground", "right"], 180, isDriving: true)],
            fixedReferenceKeys: ["right"]));
        failed.Status.Should().Be(SketchSolveStatus.Failed);
        failed.Document.Entities.Should().Equal(baseline.Document.Entities);

        var reopened = RoundTrip(failed.Document);
        var recovered = solver.Solve(SketchSolveRequest.FromDocument(reopened));

        AssertSolved(recovered);
        AssertDimensionsAndConstraints(recovered.Document);
        recovered.Document.Dimensions.Single(d => d.Id == "angle").Value.Should().Be(180);
    }

    [Fact]
    public void ExportDisposableNativeBaselinesWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("DXFER_LARGE_STEP_FIXTURES");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var (name, document) in new[]
        {
            ("slot-sloped-width20", Slot(37)), ("slot-horizontal-width20", Slot(0)),
            ("four-bar-right-leaning", FourBar(80)), ("four-bar-left-leaning", FourBar(120))
        })
            File.WriteAllText(Path.Combine(output, name + ".dxf"),
                DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
    }

    private static DrawingDocument Slot(double angle)
    {
        var radians = angle * Math.PI / 180;
        var source = new LineEntity(EntityId.Create("source"), default, new(Math.Cos(radians), Math.Sin(radians)));
        var constraints = new List<SketchConstraint>
        {
            new("origin", SketchConstraintKind.Coincident, ["source:start", "$origin:center"])
        };
        if (angle == 0)
        {
            constraints.Add(new("horizontal", SketchConstraintKind.Horizontal, ["source"]));
            constraints.Add(new("horizontal-points", SketchConstraintKind.Horizontal, ["source:start", "source:end"]));
        }
        var document = new DrawingDocument([source],
            [new("length", SketchDimensionKind.LinearDistance, ["source:start", "source:end"], 1, isDriving: true)], constraints,
            new DrawingDocumentMetadata { Units = DrawingUnits.Inches });
        var entity = 0;
        var constraint = 0;
        return LineSlotCreation.Create(document, "source", 20, true,
            prefix => EntityId.Create(prefix + ++entity), kind => kind + "-" + ++constraint, () => "width");
    }

    private static DrawingDocument FourBar(double angle)
    {
        const double ground = 14.583333333333334, left = 8, top = 12, right = 10;
        var radians = angle * Math.PI / 180;
        var a = new Point2(0, 0);
        var b = new Point2(ground, 0);
        var d = new Point2(ground + right * Math.Cos(radians), right * Math.Sin(radians));
        var diagonal = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        var projected = (left * left - top * top + diagonal * diagonal) / (2 * diagonal);
        var height = Math.Sqrt(left * left - projected * projected);
        var c = new Point2(projected * d.X / diagonal - height * d.Y / diagonal,
            projected * d.Y / diagonal + height * d.X / diagonal);
        return new([
            new LineEntity(EntityId.Create("ground"), a, b), new LineEntity(EntityId.Create("left"), a, c),
            new LineEntity(EntityId.Create("top"), c, d), new LineEntity(EntityId.Create("right"), b, d)
        ], [
            new("left-length", SketchDimensionKind.LinearDistance, ["left:start", "left:end"], left, isDriving: true),
            new("top-length", SketchDimensionKind.LinearDistance, ["top:start", "top:end"], top, isDriving: true),
            new("right-length", SketchDimensionKind.LinearDistance, ["right:start", "right:end"], right, isDriving: true)
        ], [
            new("fixed-ground", SketchConstraintKind.Fix, ["ground"]),
            new("horizontal-ground", SketchConstraintKind.Horizontal, ["ground"]),
            new("joint0", SketchConstraintKind.Coincident, ["ground:start", "left:start"]),
            new("joint1", SketchConstraintKind.Coincident, ["left:end", "top:start"]),
            new("joint2", SketchConstraintKind.Coincident, ["top:end", "right:end"]),
            new("joint3", SketchConstraintKind.Coincident, ["right:start", "ground:end"])
        ], new DrawingDocumentMetadata { Units = DrawingUnits.Inches });
    }

    private static SketchDimension Width(DrawingDocument document, double value)
    {
        var original = document.Dimensions.Single(d => d.Id == "width");
        return new(original.Id, original.Kind, original.ReferenceKeys, value, original.Anchor, true);
    }

    private static void AssertSlot(DrawingDocument document, double width)
    {
        var source = document.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "source");
        source.Start.X.Should().BeApproximately(0, 1e-6);
        source.Start.Y.Should().BeApproximately(0, 1e-6);
        foreach (var arc in document.Entities.OfType<ArcEntity>()) arc.Radius.Should().BeApproximately(width / 2, 1e-6);
        document.Constraints.Count(c => c.Kind == SketchConstraintKind.Tangent).Should().Be(4);
        AssertDimensionsAndConstraints(document);
    }

    private static void AssertDimensionsAndConstraints(DrawingDocument document)
    {
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
        {
            SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured).Should().BeTrue();
            measured.Should().BeApproximately(dimension.Value, 1e-6);
        }
    }

    private static void AssertSolved(SketchSolveResult result)
    {
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        result.Document.SolveEvidence!.Engine.Should().Be("PlaneGCS");
    }

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var result = DxfDocumentReader.Read(DxfDocumentWriter.Write(document,
            new DxfWriteOptions(IncludeNativeDocument: true)), out var native);
        native.Should().BeTrue();
        return result;
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        const string module = "src/DXFER.Blazor/wwwroot/planeGcsSolver.js";
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"), Path.Combine(root.FullName, module));
    }
}
