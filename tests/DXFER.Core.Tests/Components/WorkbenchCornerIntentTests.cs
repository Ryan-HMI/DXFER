using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchCornerIntentTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaneGcsPolygonCountStagesDiscreteGeometryAndPreservesOtherIntent(bool unavailable)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new FeaturePlaneGcsRuntime();
        var offline = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(unavailable ? offline : runtime));
        renderer.Attach(workbench);
        var original = PolygonDocument();
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "OnSketchDimensionValueChanged", "count", 6.0);
            var request = unavailable ? offline.LastRequest : runtime.LastRequest;
            request.Should().NotBeNull();
            request!.Document.Entities.Single(e => e.Id == "polygon").SideCount.Should().Be(6);
            request.Document.Dimensions.Single(d => d.Id == "count").Value.Should().Be(6);
            var accepted = Get<DrawingDocument>(workbench, "_document");
            accepted.Entities.OfType<PolygonEntity>().Single().SideCount.Should().Be(unavailable ? 5 : 6);
            accepted.Entities.Single(e => e.Id.Value == "peer").Should().Be(original.Entities[1]);
            accepted.Dimensions.Single(d => d.Id == "radius").Should().Be(original.Dimensions[1]);
            accepted.Dimensions.Single(d => d.Id == "count").Value.Should().Be(6);
            accepted.Constraints.Should().BeEquivalentTo(original.Constraints);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            if (unavailable) Get<string>(workbench, "_solveFailureStatus").Should().Contain("unavailable");
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        });
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("locked")]
    [InlineData("reference")]
    [InlineData("vertex")]
    [InlineData("dimension-vertex")]
    [InlineData("pattern")]
    public async Task PlaneGcsPolygonCountRefusesProtectedOrAmbiguousTopologyBeforeSolving(string guard)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = PolygonDocument();
        if (guard is "fixed" or "vertex")
            original = new DrawingDocument(original.Entities, original.Dimensions,
                original.Constraints.Append(new SketchConstraint("guard", SketchConstraintKind.Fix,
                    [guard == "fixed" ? "polygon" : "polygon|segment|0:start"], SketchConstraintState.Satisfied)), original.Metadata);
        if (guard == "dimension-vertex")
            original = new DrawingDocument(original.Entities, original.Dimensions.Append(
                new SketchDimension("vertex-label", SketchDimensionKind.LinearDistance,
                    ["polygon|segment|0:start", "polygon|segment|0:end"], 1, new(0, 0), false)),
                original.Constraints, original.Metadata);
        if (guard == "reference")
            original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints,
                original.Metadata with { Mode = DrawingDocumentMode.ReferenceOnly });
        if (guard == "locked")
            original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints,
                original.Metadata with
                {
                    Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) },
                    EntityStyles = new Dictionary<string, DxfEntityStyle> { ["polygon"] = new("locked") }
                });
        if (guard == "pattern")
        {
            DrawingPatternService.TryCreate(original, new PatternDefinition
            {
                Id = "polygon-pattern", Kind = PatternKind.Rectangular, SourceEntityIds = ["polygon"],
                Columns = 2, Rows = 1, XPitch = 30
            }, out var patterned, out var reason).Should().BeTrue(reason);
            original = patterned;
        }
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "OnSketchDimensionValueChanged", "count", 6.0);
            runtime.CallCount.Should().Be(0);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<string>(workbench, "_solveFailureStatus").Should().Contain("Geometry unchanged");
        });
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("pattern")]
    public async Task PlaneGcsGeneratedFeatureCommitsReachAdapter(string feature)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new FeaturePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = FeatureSeed();
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            if (feature == "offset")
                Invoke(workbench, "TryApplyModifyTool", "offset", new[] { "h" },
                    new[] { new Point2(5, 2) }, new Dictionary<string, double> { ["offset"] = 2 })
                    .Should().Be(true);
            else
                Invoke(workbench, "ApplyDocumentChangeCore", WithPattern(original), "Pattern saved.", true)
                    .Should().Be(true);
            runtime.Calls.Should().Be(1);
            var accepted = Get<DrawingDocument>(workbench, "_document");
            accepted.Entities.Should().HaveCount(2);
            if (feature == "offset")
                accepted.Constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Offset);
            else
                accepted.Metadata.Patterns.Should().ContainSingle();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaneGcsPatternSourceDragRequiresSolvedMembersNotLegacyRepair(bool followsMembers)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new FeaturePlaneGcsRuntime(followsMembers);
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = WithPattern(FeatureSeed());
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "OnGeometryDragRequested", "h", new CanvasPointDto(0, 0),
                new CanvasPointDto(3, 4), false, null!).Should().Be(followsMembers);
            runtime.Calls.Should().Be(1);
            runtime.LastRequest!.FixedReferenceKeys.Should().NotContain(
                original.Metadata.Patterns.Single().Members.Single().EntityId);
            if (followsMembers)
            {
                var accepted = Get<DrawingDocument>(workbench, "_document");
                DrawingPatternService.TryValidate(accepted, out var reason).Should().BeTrue(reason);
                ((LineEntity)accepted.Entities[0]).Start.Should().Be(new Point2(3, 4));
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            }
            else
            {
                Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
                Get<string>(workbench, "_solveFailureStatus").Should().Contain("pattern");
            }
        });
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("pattern")]
    public void PlaneGcsCanvasFeaturePreviewsUseFullDocument(string feature)
    {
        var runtime = new FeaturePlaneGcsRuntime();
        var canvas = new DrawingCanvas();
        var original = feature == "pattern" ? WithPattern(FeatureSeed()) : FeatureSeed();
        typeof(DrawingCanvas).GetProperty("SketchSolver", Private)!.SetValue(canvas, new PlaneGcsSketchSolverAdapter(runtime));
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.Document))!.SetValue(canvas, new DrawingDocument([]));
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.FullDocument))!.SetValue(canvas, original);
        var result = feature == "offset"
            ? canvas.GetOffsetPreview(["h"], new(5, 2), 2)
            : canvas.GetPlaneGcsDragPreview("h", new(0, 0), new(3, 4), false);
        var preview = result.GetType().GetProperty("document")!.GetValue(result) as CanvasDocumentDto;
        preview.Should().NotBeNull();
        preview!.Entities.Should().HaveCount(feature == "offset" ? 1 : 2);
        runtime.Calls.Should().Be(1);
        runtime.LastRequest!.Document.Entities.Should().HaveCount(2);
        original.Entities[0].Should().Be(new LineEntity(EntityId.Create("h"), new(0, 0), new(10, 0)));
    }

    [Theory]
    [InlineData("rotate", "fixed")]
    [InlineData("split", "fixed")]
    [InlineData("split", "driving")]
    [InlineData("cleanup", "fixed")]
    public async Task PlaneGcsDirectGeometryEditsCannotBypassActiveIntent(string operation, string intent)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = operation == "cleanup"
            ? DrawingPrepService.Transform(Document(intent), Transform2.RotationDegreesAbout(30, new(0, 0)))
            : Document(intent);
        Set(workbench, "_document", original);
        Get<Stack<DrawingDocument>>(workbench, "_redoStack").Push(original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            switch (operation)
            {
                case "rotate":
                    Invoke(workbench, "RotateDocumentAboutBoundsCenter", 90.0, "Rotated.");
                    break;
                case "split":
                    Invoke(workbench, "SplitLineAtPoint", "h", new DXFER.Core.Geometry.Point2(5, 0))
                        .Should().Be(false);
                    break;
                case "cleanup":
                    Invoke(workbench, "ApplyAutoCleanup");
                    break;
            }
            runtime.CallCount.Should().Be(0, "invalid direct edits must be refused without solving");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().ContainSingle()
                .Which.Should().BeSameAs(original);
            Get<string>(workbench, "_status").Should().Contain("Geometry unchanged");
        });
    }

    [Theory]
    [InlineData("translate-unrelated")]
    [InlineData("split-unrelated")]
    [InlineData("rotate-driving")]
    [InlineData("cleanup-driving")]
    public async Task PlaneGcsValidDirectEditsUseOneHistoryEntryWithoutSolving(string operation)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = Document(operation.EndsWith("unrelated", StringComparison.Ordinal) ? "fixed" : "driving");
        if (operation == "cleanup-driving")
            original = DrawingPrepService.Transform(original, Transform2.RotationDegreesAbout(30, new(0, 0)));
        Set(workbench, "_document", original);
        Get<Stack<DrawingDocument>>(workbench, "_redoStack").Push(original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            switch (operation)
            {
                case "translate-unrelated":
                    Invoke(workbench, "TryApplyModifyTool", "translate", new[] { "v" },
                        new[] { new Point2(0, 0), new Point2(3, 4) }, new Dictionary<string, double>())
                        .Should().Be(true);
                    break;
                case "split-unrelated":
                    Invoke(workbench, "SplitLineAtPoint", "v", new Point2(0, 5)).Should().Be(true);
                    break;
                case "rotate-driving":
                    Invoke(workbench, "RotateDocumentAboutBoundsCenter", 90.0, "Rotated.");
                    break;
                case "cleanup-driving":
                    Invoke(workbench, "ApplyAutoCleanup");
                    break;
            }
            runtime.CallCount.Should().Be(0);
            var accepted = Get<DrawingDocument>(workbench, "_document");
            accepted.Should().NotBeSameAs(original);
            accepted.Constraints.Should().BeEquivalentTo(original.Constraints);
            accepted.Dimensions.Single().Value.Should().BeApproximately(10, 1e-8);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(accepted);
        });
    }

    [Fact]
    public async Task PlaneGcsUnrelatedDirectEditKeepsFailedIntentAndItsDiagnostic()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = new DrawingDocument(Document("driving").Entities, Document("driving").Dimensions,
            [new SketchConstraint("failed", SketchConstraintKind.Vertical, ["h"], SketchConstraintState.Unsatisfied)]);
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "TryApplyModifyTool", "translate", new[] { "v" },
                new[] { new Point2(0, 0), new Point2(3, 4) }, new Dictionary<string, double>())
                .Should().Be(true);
            runtime.CallCount.Should().Be(0);
            var accepted = Get<DrawingDocument>(workbench, "_document");
            accepted.Constraints.Single().Should().BeSameAs(original.Constraints.Single());
            accepted.Dimensions.Single().Should().BeSameAs(original.Dimensions.Single());
            Get<string?>(workbench, "_solveFailureStatus").Should().NotBeNullOrWhiteSpace();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
        });
    }

    [Fact]
    public async Task PlaneGcsDirectTransformStillAllowsGeometryWithoutSketchIntent()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = new DrawingDocument(Document("reference").Entities);
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "RotateDocumentAboutBoundsCenter", 90.0, "Rotated.");
            runtime.CallCount.Should().Be(0);
            Get<DrawingDocument>(workbench, "_document").Should().NotBeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            Get<string?>(workbench, "_solveFailureStatus").Should().BeNull();
            Get<string>(workbench, "_status").Should().Be("Rotated.");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailablePlaneGcsRetainsExplicitConstraintAndDimensionEdits(bool dimensionEdit)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = Document("driving");
        Set(workbench, "_document", original);

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            if (dimensionEdit)
                Invoke(workbench, "OnSketchDimensionValueChanged", "length", 99.0);
            else
            {
                Get<HashSet<string>>(workbench, "_selectedEntityIds").Add("h");
                Invoke(workbench, "TryApplySketchConstraint", SketchConstraintKind.Vertical, true);
            }

            runtime.CallCount.Should().Be(1);
            var retained = Get<DrawingDocument>(workbench, "_document");
            retained.Entities.Should().BeEquivalentTo(original.Entities);
            if (dimensionEdit)
            {
                retained.Dimensions.Single(d => d.Id == "length").Value.Should().Be(99);
                SketchDimensionSolverService.GetDimensionState(retained, retained.Dimensions.Single())
                    .Should().Be(SketchConstraintState.Unsatisfied);
            }
            else
                retained.Constraints.Should().ContainSingle(c =>
                    c.Kind == SketchConstraintKind.Vertical && c.State == SketchConstraintState.Unsatisfied);
            Get<string>(workbench, "_status").Should().Contain("PlaneGCS runtime is unavailable")
                .And.Contain("Geometry unchanged");
            Get<string>(workbench, "_solveFailureStatus").Should().Be(Get<string>(workbench, "_status"));
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaneGcsFailedIntentRemainsEditableWithoutAcceptingFailedGeometry(bool dimensionEdit)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create(new PlaneGcsSketchSolverAdapter(new UnavailablePlaneGcsRuntime()));
        renderer.Attach(workbench);
        var original = Document("reference");
        Set(workbench, "_document", original);
        var dimensions = dimensionEdit
            ? new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
                new[] { "h:start", "h:end" }, 99, new(5, -2), isDriving: true) }
            : original.Dimensions;
        var constraints = dimensionEdit
            ? original.Constraints
            : new[] { new SketchConstraint("failed-vertical", SketchConstraintKind.Vertical,
                new[] { "h" }, SketchConstraintState.Unsatisfied) };
        var failedDocument = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("h"), new(0, 0), new(99, 0)),
            original.Entities.Single(entity => entity.Id.Value == "v")
        }, dimensions, constraints, original.Metadata);
        var result = new SketchSolveResult(SketchSolveStatus.Failed, failedDocument,
            new[] { "PlaneGCS rejected the requested intent." });

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "ApplySketchSolveResult", result, "Should not report success.");
            var retained = Get<DrawingDocument>(workbench, "_document");
            retained.Entities.Should().BeEquivalentTo(original.Entities);
            retained.Metadata.Should().BeSameAs(original.Metadata);
            retained.Dimensions.Should().BeEquivalentTo(dimensions);
            retained.Constraints.Should().BeEquivalentTo(constraints);
            Get<string>(workbench, "_status").Should().Contain("PlaneGCS rejected")
                .And.Contain("Geometry unchanged").And.NotContain("Should not report success");
            Get<string>(workbench, "_solveFailureStatus").Should().Be(Get<string>(workbench, "_status"));
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PlaneGcsCornerSizeBindingAndDefaultPreviewCannotAcceptFailedSolve(bool fillet, bool explicitSize)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = WithHiddenPeer(Document("reference"));
        Set(workbench, "_document", original);
        Set(workbench, "_activeTool", fillet ? WorkbenchTool.Fillet : WorkbenchTool.Chamfer);
        Get<HashSet<string>>(workbench, "_selectedEntityIds").UnionWith(["h", "v"]);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            if (explicitSize)
                typeof(DrawingWorkbench).GetProperty("CornerSizeDraft", Private)!.SetValue(workbench, "2");
            else
                Invoke(workbench, "RefreshCornerPreview");

            runtime.CallCount.Should().Be(1);
            runtime.LastRequest!.Document.Entities.Should().ContainSingle(e => e.Id == "hidden-peer" && e.IsHidden);
            runtime.LastRequest.Document.Dimensions.Should().Contain(d => d.Id != "length" && d.IsDriving == explicitSize);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<string>(workbench, "_solveFailureStatus").Should().Contain("runtime is unavailable");
            Invoke(workbench, "CommitCornerPreview");
            runtime.CallCount.Should().Be(1, "a rejected preview must not become a committable corner");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        });
    }

    [Theory]
    [InlineData("line")]
    [InlineData("chamfer")]
    [InlineData("fillet")]
    public async Task UnavailablePlaneGcsDoesNotCommitCreationOrCorner(string operation)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var runtime = new UnavailablePlaneGcsRuntime();
        var workbench = Create(new PlaneGcsSketchSolverAdapter(runtime));
        renderer.Attach(workbench);
        var original = WithHiddenPeer(Document("reference"));
        Set(workbench, "_document", original);
        Get<Stack<DrawingDocument>>(workbench, "_redoStack").Push(original);
        var native = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            if (operation == "line")
            {
                Invoke(workbench, "OnToolCommitRequested", "line",
                    new[] { new CanvasPointDto(20, 20), new CanvasPointDto(30, 25) },
                    new Dictionary<string, double>(), null!, false, null!);
            }
            else
            {
                SelectCorner(workbench);
                Invoke(workbench, "TryApplyCornerModifyTool", Command(operation == "fillet"), true)
                    .Should().Be(false);
            }

            runtime.CallCount.Should().Be(1, "the opt-in adapter must reach the runtime without a legacy fallback");
            runtime.LastRequest!.Document.Entities.Should().ContainSingle(e => e.Id == "hidden-peer" && e.IsHidden);
            runtime.LastRequest.FixedReferenceKeys.Should().Contain("hidden-peer");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().ContainSingle()
                .Which.Should().BeSameAs(original);
            Get<string>(workbench, "_status").Should().Contain("PlaneGCS runtime is unavailable")
                .And.Contain(UnavailablePlaneGcsRuntime.Reason).And.Contain("Geometry unchanged");
            Get<string>(workbench, "_solveFailureStatus").Should().Be(Get<string>(workbench, "_status"));
            DxfDocumentWriter.Write(Get<DrawingDocument>(workbench, "_document"),
                new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(native);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InteractivePreviewRecomputesWithoutHistoryAndCancelDiscards(bool fillet)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create();
        renderer.Attach(workbench);
        var original = Document("reference");
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Set(workbench, "_activeTool", fillet ? WorkbenchTool.Fillet : WorkbenchTool.Chamfer);
            SelectCorner(workbench);
            var first = Get<DrawingDocument>(workbench, "_cornerPreview");
            first.Dimensions.Single(d => d.Id == "length").Value.Should().BeApproximately(8, 1e-10);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            foreach (var invalid in new[] { "0", "-1", "", "NaN", "100" })
            {
                Invoke(workbench, "OnCornerModifyDistanceChanged", new ChangeEventArgs { Value = invalid });
                Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
                Invoke(workbench, "CommitCornerPreview");
                Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            }
            Invoke(workbench, "OnCornerSizeDragged", 3.0);
            Get<DrawingDocument>(workbench, "_cornerPreview").Dimensions.Single(d => d.Id == "length").Value.Should().BeApproximately(7, 1e-10);
            Invoke(workbench, "OnToolCanceled");
            Get<HashSet<string>>(workbench, "_selectedEntityIds").Should().BeEmpty();
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().BeNull();
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();

            Set(workbench, "_activeTool", fillet ? WorkbenchTool.Fillet : WorkbenchTool.Chamfer);
            Get<HashSet<string>>(workbench, "_selectedEntityIds").UnionWith(new[] { "h", "v" });
            Invoke(workbench, "RefreshCornerPreview");
            Invoke(workbench, "CommitCornerPreview");
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1);
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().BeNull("Confirm finishes the corner session");
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Invoke(workbench, "CommitCornerPreview");
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1, "repeated confirmation must not commit again");
            var committed = Get<DrawingDocument>(workbench, "_document");
            committed.Dimensions.Single(d => d.Id == "length").Value.Should().BeApproximately(7, 1e-10);
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(committed);
            var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(committed, new DxfWriteOptions(IncludeNativeDocument: true)));
            reopened.Entities.Should().BeEquivalentTo(committed.Entities);
            reopened.Dimensions.Should().BeEquivalentTo(committed.Dimensions);
        });
    }

    [Theory]
    [InlineData(WorkbenchTool.Chamfer)]
    [InlineData(WorkbenchTool.Fillet)]
    public async Task ActivatingCornerShowsRequiredControls(WorkbenchTool tool)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create();
        renderer.Attach(workbench);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(DrawingWorkbench).GetProperty(nameof(DrawingWorkbench.EnableFullEditing))!.SetValue(workbench, true);
            Invoke(workbench, "ActivateTool", tool, "Corner active.");
            typeof(DrawingWorkbench).GetProperty("IsOperationInspectorVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(workbench).Should().Be(true);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InteractiveConflictingPreviewCannotCommit(bool fillet)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create();
        renderer.Attach(workbench);
        var original = Document("driving");
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Set(workbench, "_activeTool", fillet ? WorkbenchTool.Fillet : WorkbenchTool.Chamfer);
            SelectCorner(workbench);
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Invoke(workbench, "CommitCornerPreview");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false, "driving")]
    [InlineData(true, "driving")]
    [InlineData(false, "fixed")]
    [InlineData(true, "fixed")]
    [InlineData(false, "unresolvable")]
    [InlineData(true, "unresolvable")]
    public async Task RefusedCornerLeavesDocumentNativeExportAndHistoryUnchanged(bool fillet, string intent)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create();
        renderer.Attach(workbench);
        var original = Document(intent);
        Set(workbench, "_document", original);
        var native = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            SelectCorner(workbench);
            Invoke(workbench, "TryApplyCornerModifyTool", Command(fillet), true).Should().Be(false);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<string>(workbench, "_status").Should().Contain("Geometry unchanged");
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
            Invoke(workbench, "UndoLastDocumentChange");
            Invoke(workbench, "RedoLastDocumentChange");
            DxfDocumentWriter.Write(Get<DrawingDocument>(workbench, "_document"),
                new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(native);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReferenceCornerUndoRedoAndNativeReopenPreserveIntent(bool legacy, bool fillet)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Create();
        renderer.Attach(workbench);
        var original = Document("reference");
        Set(workbench, "_document", original);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            if (legacy)
            {
                DrawingDocument changed;
                (fillet
                    ? DrawingModifyService.TryFilletSelectedLines(original, new[] { "h", "v" }, 2, out changed)
                    : DrawingModifyService.TryChamferSelectedLines(original, new[] { "h", "v" }, 2, out changed)).Should().BeTrue();
                Invoke(workbench, "ApplyDocumentChange", changed, "Legacy corner applied.");
            }
            else
            {
                SelectCorner(workbench);
                Invoke(workbench, "TryApplyCornerModifyTool", Command(fillet), true).Should().Be(true);
            }
            var accepted = Get<DrawingDocument>(workbench, "_document");
            var dimension = accepted.Dimensions.Single(d => d.Id == "length");
            dimension.IsDriving.Should().BeFalse();
            dimension.Value.Should().BeApproximately(8, 1e-10);
            dimension.Anchor.Should().Be(original.Dimensions.Single().Anchor);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1);
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(accepted);
            var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(accepted,
                new DxfWriteOptions(IncludeNativeDocument: true)));
            reopened.Dimensions.Should().BeEquivalentTo(accepted.Dimensions);
            reopened.Constraints.Should().BeEquivalentTo(accepted.Constraints);
            reopened.Entities.Should().BeEquivalentTo(accepted.Entities);
        });
    }

    private static WorkbenchCommandId Command(bool fillet) => fillet ? WorkbenchCommandId.Fillet : WorkbenchCommandId.Chamfer;
    private static DrawingDocument FeatureSeed() => new([new LineEntity(EntityId.Create("h"), new(0, 0), new(10, 0))]);
    private static DrawingDocument PolygonDocument() => new(
        [new PolygonEntity(EntityId.Create("polygon"), new(0, 0), 10, 0, 5),
            new LineEntity(EntityId.Create("peer"), new(50, 0), new(60, 0))],
        [new SketchDimension("count", SketchDimensionKind.Count, ["polygon"], 5, new(0, 12), true),
            new SketchDimension("radius", SketchDimensionKind.Radius, ["polygon"], 10, new(0, -12), true)],
        [new SketchConstraint("peer-fixed", SketchConstraintKind.Fix, ["peer"], SketchConstraintState.Satisfied),
            new SketchConstraint("center-fixed", SketchConstraintKind.Fix, ["polygon:center"], SketchConstraintState.Satisfied)]);
    private static DrawingDocument WithHiddenPeer(DrawingDocument document) => new(
        document.Entities.Append(new LineEntity(EntityId.Create("hidden-peer"), new(50, 50), new(60, 50))),
        document.Dimensions, document.Constraints, document.Metadata with
        {
            Layers = new Dictionary<string, DxfEntityStyle> { ["hidden"] = new("hidden", IsHidden: true) },
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["hidden-peer"] = new("hidden") }
        });
    private static DrawingDocument WithPattern(DrawingDocument original)
    {
        DrawingPatternService.TryCreate(original, new PatternDefinition
        {
            Id = "pattern", Kind = PatternKind.Rectangular, SourceEntityIds = ["h"],
            Columns = 2, Rows = 1, XPitch = 20
        }, out var candidate, out var reason).Should().BeTrue(reason);
        return candidate;
    }
    private static DrawingDocument Document(string intent) => new(new DrawingEntity[]
    {
        new LineEntity(EntityId.Create("h"), new(0, 0), new(10, 0)),
        new LineEntity(EntityId.Create("v"), new(0, 0), new(0, 10))
    }, new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
        new[] { "h:start", intent == "unresolvable" ? "missing:end" : "h:end" }, 10, new(5, -2), intent == "driving") },
        intent == "fixed"
            ? new[] { new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { "h" }, SketchConstraintState.Satisfied) }
            : Array.Empty<SketchConstraint>());

    private static HeadlessWorkbench Create(ISketchSolver? solver = null)
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        typeof(DrawingWorkbench).GetProperty("SketchSolver", Private)!.SetValue(workbench, solver ?? new LegacySketchSolverAdapter());
        return workbench;
    }
    private static void SelectCorner(DrawingWorkbench workbench)
    {
        Get<HashSet<string>>(workbench, "_selectedEntityIds").UnionWith(new[] { "h", "v" });
        Invoke(workbench, "OnCornerModifyDistanceChanged", new ChangeEventArgs { Value = "2" });
    }
    private static void Set(DrawingWorkbench target, string name, object value) =>
        typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);
    private static T Get<T>(DrawingWorkbench target, string name) =>
        (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) =>
        typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);

    private sealed class UnavailablePlaneGcsRuntime : IPlaneGcsRuntime
    {
        public const string Reason = "Test runtime is offline.";
        public int CallCount { get; private set; }
        public PlaneGcsRequest? LastRequest { get; private set; }

        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            CallCount++;
            LastRequest = request;
            throw new InvalidOperationException(Reason);
        }
    }

    private sealed class FeaturePlaneGcsRuntime(bool followsMembers = true) : IPlaneGcsRuntime
    {
        public int Calls { get; private set; }
        public PlaneGcsRequest? LastRequest { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Calls++;
            LastRequest = request;
            var entities = request.Document.Entities.Select(entity =>
                request.Drag is { } drag && (!entity.IsGenerated || followsMembers)
                    ? entity with { Points = entity.Points.Select(point => new PlaneGcsPoint(
                        point.X + drag.End.X - drag.Start.X, point.Y + drag.End.Y - drag.Start.Y)).ToArray() }
                    : entity).ToArray();
            return new PlaneGcsResponse("solved", entities, [], "PlaneGCS", []);
        }
    }

    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }

#pragma warning disable BL0006 // Attach the real command handler without rendering unrelated UI or loading JS.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
