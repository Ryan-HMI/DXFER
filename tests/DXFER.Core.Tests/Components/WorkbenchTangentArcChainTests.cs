using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

[Collection("PlaneGCS resource budget")]
public sealed class WorkbenchTangentArcChainTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(null, false)]
    [InlineData(WorkbenchTool.Line, true)]
    [InlineData(WorkbenchTool.TangentArc, true)]
    [InlineData(WorkbenchTool.CenterCircle, false)]
    public void SharedLineCommandUsesLineArcIconAndRemainsPressedInArcMode(WorkbenchTool? active, bool pressed)
    {
        var workbench = new DrawingWorkbench();
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetField("_activeTool", Private)!.SetValue(workbench, active);
        var groups = (IReadOnlyList<WorkbenchToolGroup>)typeof(DrawingWorkbench).GetProperty("AllToolGroups", Private)!.GetValue(workbench)!;
        var command = groups.Single(g => g.Name == "Sketch").Commands.Single(c => c.Id == WorkbenchCommandId.Line);
        command.Label.Should().Be("Line");
        command.Tool.Should().Be(WorkbenchTool.Line);
        command.Icon.Should().Be(CadIconName.TangentArc);
        command.Pressed.Should().Be(pressed);
        command.Disabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task LineArcLinePersistsIntentAndToolNotificationDoesNotAddHistory(bool clockwise, bool vertical, bool automatic)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var original = new DrawingDocument(Array.Empty<DrawingEntity>());
        var workbench = Attach(renderer, original, runtime, automatic);
        var canvas = Canvas(workbench, renderer);
        var sign = clockwise ? -1 : 1;
        Point2 P(double x, double y) => vertical ? new(7 - y, 11 + x) : new(7 + x, 11 + y);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await canvas.OnSketchToolCommitted("line", Coordinates(P(0, 0), P(10, 0)), ["length"], [10]);
            var first = Get<DrawingDocument>(workbench, "_document");
            first.Entities.Should().ContainSingle(Get<string>(workbench, "_status"));
            var incoming = first.Entities.OfType<LineEntity>().Single();
            await canvas.OnSketchToolModeChanged("tangentarc");
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().Be(WorkbenchTool.TangentArc);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(first);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1);

            var snap = new CanvasCreationSnapDto(P(15, sign * 5), vertical ? "ArcQuadrantVertical" : "ArcQuadrantHorizontal", []);
            await canvas.OnSketchToolCommitted("tangentarc", Coordinates(P(10, 0), P(11, 0), P(15, sign * 5)),
                ["radius"], [5], creationSnaps: [snap]);
            var second = Get<DrawingDocument>(workbench, "_document");
            second.Entities.Should().HaveCount(2, Get<string>(workbench, "_status"));
            second.Entities.Single(e => e.Id == incoming.Id).Should().Be(incoming);
            var arc = second.Entities.OfType<ArcEntity>().Single();
            arc.Radius.Should().BeApproximately(5, 1e-7);
            var arcStartKey = arc.Id.Value + (clockwise ? ":end" : ":start");
            var arcEndKey = arc.Id.Value + (clockwise ? ":start" : ":end");
            if (automatic)
            {
                AssertRelation(second, SketchConstraintKind.Coincident, incoming.Id.Value + ":end", arcStartKey);
                AssertRelation(second, SketchConstraintKind.Tangent, incoming.Id.Value, arc.Id.Value);
                AssertRelation(second, vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
                    arc.Id.Value + ":center", arcEndKey);
            }
            else second.Constraints.Should().BeEmpty();

            await canvas.OnSketchToolModeChanged("line");
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().Be(WorkbenchTool.Line);
            typeof(DrawingWorkbench).GetProperty("ActiveToolLabel", Private)!.GetValue(workbench).Should().Be("Line");
            Get<string>(workbench, "_status").Should().Contain("Line active");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(second);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(2);
            await canvas.OnSketchToolModeChanged("line");
            await canvas.OnSketchToolModeChanged("unknown");
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().Be(WorkbenchTool.Line);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(2);

            await canvas.OnSketchToolCommitted("line", Coordinates(P(15, sign * 5), P(15, sign * 10)));
            var third = Get<DrawingDocument>(workbench, "_document");
            third.Entities.Should().HaveCount(3, Get<string>(workbench, "_status"));
            var outgoing = third.Entities.OfType<LineEntity>().Single(l => l.Id != incoming.Id);
            if (automatic)
            {
                AssertRelation(third, SketchConstraintKind.Coincident, arcEndKey, outgoing.Id.Value + ":start");
                AssertRelation(third, SketchConstraintKind.Tangent, arc.Id.Value, outgoing.Id.Value);
                third.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
            }
            else third.Constraints.Should().BeEmpty();
            third.Dimensions.Should().BeEquivalentTo(second.Dimensions);
            foreach (var constraint in third.Constraints)
                SketchConstraintService.ValidateConstraint(third, constraint).State.Should().Be(SketchConstraintState.Satisfied);
            foreach (var dimension in third.Dimensions)
            {
                SketchDimensionSolverService.TryGetDimensionMeasurement(third, dimension, out var measured).Should().BeTrue();
                measured.Should().BeApproximately(dimension.Value, 1e-7);
            }
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(3);
            var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(third, new DxfWriteOptions(IncludeNativeDocument: true)));
            DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(third));
            foreach (var previous in new[] { second, first, original })
            {
                Invoke(workbench, "UndoLastDocumentChange");
                Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(previous);
            }
            foreach (var next in new[] { first, second, third })
            {
                Invoke(workbench, "RedoLastDocumentChange");
                Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(next);
            }
        });
    }

    [Fact]
    public async Task InvalidQuadrantRefusalSurvivesSubsequentLineModeNotification()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var original = new DrawingDocument([new LineEntity(EntityId.Create("incoming"), new(0, 0), new(10, 0))]);
        var workbench = Attach(renderer, original, runtime, true);
        var canvas = Canvas(workbench, renderer);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await canvas.OnSketchToolModeChanged("tangentarc");
            await canvas.OnSketchToolCommitted("tangentarc", [10, 0, 11, 0, 15, 5], creationSnaps:
                [new(new(15, 5), "ArcQuadrantVertical", [])]);
            var failure = Get<string>(workbench, "_solveFailureStatus");
            failure.Should().Contain("Geometry unchanged");
            await canvas.OnSketchToolModeChanged("line");
            Get<WorkbenchTool?>(workbench, "_activeTool").Should().Be(WorkbenchTool.Line);
            typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench).Should().Be(failure);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
        });
    }

    private static double[] Coordinates(params Point2[] points) => points.SelectMany(p => new[] { p.X, p.Y }).ToArray();
    private static void AssertRelation(DrawingDocument document, SketchConstraintKind kind, params string[] keys) =>
        document.Constraints.Should().ContainSingle(c => c.Kind == kind && c.ReferenceKeys.SequenceEqual(keys));

    private static DrawingCanvas Canvas(DrawingWorkbench workbench, TestRenderer renderer)
    {
        var canvas = new HeadlessCanvas();
        Action<string, IReadOnlyList<CanvasPointDto>, IReadOnlyDictionary<string, double>, string?, bool, IReadOnlyList<CanvasCreationSnapDto>?> commit =
            (tool, points, values, key, origin, snaps) => Invoke(workbench, "OnToolCommitRequested", tool, points, values, key!, origin, snaps!);
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.ToolCommitRequested))!.SetValue(canvas, commit);
        Action<string> mode = tool => Invoke(workbench, "OnCanvasToolModeChanged", tool);
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.ToolModeChanged))!.SetValue(canvas, mode);
        renderer.Attach(canvas);
        return canvas;
    }

    private static HeadlessWorkbench Attach(TestRenderer renderer, DrawingDocument document, IPlaneGcsRuntime runtime, bool automatic)
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("SketchSolver", Private)!.SetValue(workbench, new PlaneGcsSketchSolverAdapter(runtime));
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench,
            new WorkbenchMenuCommandService { AutomaticSnapConstraints = automatic });
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench, document);
        typeof(DrawingWorkbench).GetField("_activeTool", Private)!.SetValue(workbench, WorkbenchTool.Line);
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

    private static T Get<T>(DrawingWorkbench target, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
    private sealed class HeadlessCanvas : DrawingCanvas
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
