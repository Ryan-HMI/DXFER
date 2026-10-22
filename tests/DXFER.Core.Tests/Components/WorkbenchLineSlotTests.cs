using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

[Collection("PlaneGCS resource budget")]
public sealed class WorkbenchLineSlotTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanvasCommitCreatesClosedTangentSlotPreservesSourceIntentAndUsesOneUndo(bool keyedWidth)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var original = Fixture();
        var originalJson = DxferDocumentSerializer.Write(original);
        var workbench = Attach(renderer, original, runtime);
        Get<HashSet<string>>(workbench, "_selectedEntityIds").Add("source");
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Canvas(workbench).CommitLineSlot("source", 4, keyedWidth);
            var created = Get<DrawingDocument>(workbench, "_document");
            created.Should().NotBeSameAs(original, Get<string>(workbench, "_status"));
            created.Entities.Should().HaveCount(original.Entities.Count + 4);
            created.Entities.Single(e => e.Id.Value == "source").Should().Be(
                ((LineEntity)original.Entities.Single(e => e.Id.Value == "source")) with { IsConstruction = true });
            created.Entities.Single(e => e.Id.Value == "peer").Should().Be(original.Entities.Single(e => e.Id.Value == "peer"));
            created.Metadata.EntityStyles["source"].Should().Be(original.Metadata.EntityStyles["source"]);
            created.Metadata.Units.Should().Be(original.Metadata.Units);
            foreach (var dimension in original.Dimensions)
                created.Dimensions.Single(d => d.Id == dimension.Id).Should().BeEquivalentTo(dimension);
            foreach (var constraint in original.Constraints)
                created.Constraints.Single(c => c.Id == constraint.Id).Should().BeEquivalentTo(constraint);

            var added = created.Entities.Where(e => !original.Entities.Any(o => o.Id == e.Id)).ToArray();
            added.Should().OnlyContain(e => !e.IsConstruction);
            added.OfType<LineEntity>().Should().HaveCount(2);
            added.OfType<ArcEntity>().Should().HaveCount(2);
            ClosedContourService.Calculate(new DrawingDocument(added)).ClosedContourCount.Should().Be(1);
            foreach (var arc in added.OfType<ArcEntity>())
            {
                arc.Radius.Should().BeApproximately(2, 1e-7);
                var ends = arc.GetSamplePoints(1);
                foreach (var end in ends)
                {
                    var line = added.OfType<LineEntity>().Single(l => Near(l.Start, end) || Near(l.End, end));
                    var dot = (line.End.X - line.Start.X) * (end.X - arc.Center.X)
                        + (line.End.Y - line.Start.Y) * (end.Y - arc.Center.Y);
                    dot.Should().BeApproximately(0, 1e-7);
                }
            }
            created.Constraints.Count(c => c.Kind == SketchConstraintKind.Tangent).Should().Be(4);
            var widthDimensions = created.Dimensions.Where(d => !original.Dimensions.Any(o => o.Id == d.Id)).ToArray();
            if (keyedWidth)
            {
                var dimension = widthDimensions.Should().ContainSingle().Which;
                dimension.IsDriving.Should().BeTrue();
                dimension.Value.Should().Be(4);
                SketchDimensionSolverService.TryGetDimensionMeasurement(created, dimension, out var width).Should().BeTrue();
                width.Should().BeApproximately(4, 1e-7);
            }
            else widthDimensions.Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle().Which.Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().BeNull();
            Get<HashSet<string>>(workbench, "_selectedEntityIds").Should().BeEmpty();
            Get<bool>(workbench, "_focusCanvasAfterRender").Should().BeTrue();
            DxferDocumentSerializer.Write(original).Should().Be(originalJson);
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(created);
        });
    }

    [Theory]
    [InlineData("missing", 4)]
    [InlineData("source:start", 4)]
    [InlineData("peer", 4)]
    [InlineData("source", 0)]
    [InlineData("source", -1)]
    [InlineData("source", double.NaN)]
    [InlineData("source", double.PositiveInfinity)]
    public Task InvalidInputRefusesWithoutChangingDocumentOrHistory(string key, double width) =>
        AssertRefused(Fixture(), key, width);

    [Theory]
    [InlineData("locked")]
    [InlineData("hidden")]
    [InlineData("degenerate")]
    [InlineData("generated")]
    public Task ProtectedOrDegenerateSourceRefusesWithoutChangingDocumentOrHistory(string condition)
    {
        var original = Fixture();
        var key = "source";
        if (condition == "generated")
        {
            var pattern = new PatternDefinition { Id = "pattern", SourceEntityIds = [key], Columns = 2, XPitch = 20 };
            DrawingPatternService.TryCreate(original, pattern, out var patterned, out var reason).Should().BeTrue(reason);
            original = patterned;
            key = original.Metadata.Patterns.Single().Members.Single().EntityId;
        }
        else if (condition == "degenerate")
        {
            original = new DrawingDocument([new LineEntity(EntityId.Create(key), new(0, 0), new(0, 0))]);
        }
        else
        {
            var layers = new Dictionary<string, DxfEntityStyle>(original.Metadata.Layers)
            {
                ["centerlines"] = new("centerlines", IsLocked: condition == "locked", IsHidden: condition == "hidden")
            };
            original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints,
                original.Metadata with { Layers = layers });
            DrawingLayerService.CanEdit(original, key).Should().BeFalse();
        }
        return AssertRefused(original, key, 4);
    }

    [Fact]
    public async Task SelectionFirstSlotCommandPreservesSelectionWithoutEditingOrHistory()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var original = Fixture();
        var workbench = Attach(renderer, original, runtime);
        Set(workbench, "_activeTool", null);
        Get<HashSet<string>>(workbench, "_selectedEntityIds").Add("source");
        var resetToken = Get<int>(workbench, "_selectionResetToken");
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await (Task)Invoke(workbench, "InvokeWorkbenchCommand", WorkbenchCommandId.Slot)!;
            Get<WorkbenchTool>(workbench, "_activeTool").Should().Be(WorkbenchTool.Slot);
            Get<HashSet<string>>(workbench, "_selectedEntityIds").Should().Equal("source");
            Get<int>(workbench, "_selectionResetToken").Should().Be(resetToken);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task DeprecatedThreePointSlotCallbackRefusesWithoutHistory()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var original = Fixture();
        var workbench = Attach(renderer, original, runtime);
        var canvas = new DrawingCanvas();
        Action<string, IReadOnlyList<CanvasPointDto>, IReadOnlyDictionary<string, double>, string?, bool, IReadOnlyList<CanvasCreationSnapDto>?> handler =
            (tool, points, values, key, origin, snaps) => Invoke(workbench, "OnToolCommitRequested", tool, points, values, key!, origin, snaps!);
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.ToolCommitRequested))!.SetValue(canvas, handler);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await canvas.OnSketchToolCommitted("slot", new double[] { 0, 0, 10, 0, 10, 2 }, null, null, null);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
            Get<string>(workbench, "_status").Should().Contain("existing line").And.Contain("Geometry unchanged");
        });
    }

    private static async Task AssertRefused(DrawingDocument original, string key, double width)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var workbench = Attach(renderer, original, runtime);
        var json = DxferDocumentSerializer.Write(original);
        var redo = new DrawingDocument(Array.Empty<DrawingEntity>());
        Get<Stack<DrawingDocument>>(workbench, "_redoStack").Push(redo);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Canvas(workbench).CommitLineSlot(key, width, true);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            DxferDocumentSerializer.Write(original).Should().Be(json);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().ContainSingle().Which.Should().BeSameAs(redo);
            Get<string>(workbench, "_status").Should().Contain("Geometry unchanged");
        });
    }

    private static DrawingDocument Fixture() => new(
        [new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0)),
         new PointEntity(EntityId.Create("peer"), new(30, 40))],
        [new SketchDimension("length", SketchDimensionKind.LinearDistance, ["source:start", "source:end"],
            10, new Point2(5, -3), isDriving: true)],
        [new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, ["source"], SketchConstraintState.Satisfied)],
        DrawingDocumentMetadata.Empty with
        {
            Units = DrawingUnits.Inches,
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["source"] = new("centerlines", ColorNumber: 3) }
        });

    private static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) < 1e-7 && Math.Abs(a.Y - b.Y) < 1e-7;

    private static DrawingCanvas Canvas(DrawingWorkbench workbench)
    {
        var canvas = new DrawingCanvas();
        Action<string, double, bool> handler = (key, width, keyed) => Invoke(workbench, "OnLineSlotCommitRequested", key, width, keyed);
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.SlotCommitRequested))!.SetValue(canvas, handler);
        return canvas;
    }

    private static HeadlessWorkbench Attach(TestRenderer renderer, DrawingDocument document, IPlaneGcsRuntime runtime)
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("SketchSolver", Private)!.SetValue(workbench, new PlaneGcsSketchSolverAdapter(runtime));
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        Set(workbench, "_document", document);
        Set(workbench, "_activeTool", WorkbenchTool.Slot);
        renderer.Attach(workbench);
        return workbench;
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }

    private static void Set(DrawingWorkbench target, string name, object? value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);
    private static T Get<T>(DrawingWorkbench target, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Exercise real Canvas callbacks and Workbench history without browser rendering.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
