using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
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
public sealed class WorkbenchSymmetryNormalTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(WorkbenchCommandId.Normal, WorkbenchTool.Normal, CadIconName.Normal, "endpoint")]
    [InlineData(WorkbenchCommandId.Symmetric, WorkbenchTool.Symmetric, CadIconName.Symmetric, "third pick")]
    public void PaletteEnablesExistingSlotsWithSharedGlyphsAndConfigurableHotkeys(WorkbenchCommandId id, WorkbenchTool tool,
        CadIconName icon, string tooltip)
    {
        var workbench = new DrawingWorkbench();
        Property(workbench, "ToolHotkeys", new ToolHotkeyService());
        Property(workbench, "MenuCommandService", new WorkbenchMenuCommandService());
        Set(workbench, "_activeTool", tool);
        var groups = (IReadOnlyList<WorkbenchToolGroup>)typeof(DrawingWorkbench).GetProperty("AllToolGroups", Private)!.GetValue(workbench)!;
        var command = groups.SelectMany(g => g.Commands).Single(c => c.Id == id);
        command.Disabled.Should().BeFalse();
        command.IsFuture.Should().BeFalse();
        command.Pressed.Should().BeTrue();
        command.Icon.Should().Be(icon);
        command.TooltipText.Should().Contain(tooltip);
        ConstraintIconGeometry.All.Should().ContainKey(id.ToString().ToLowerInvariant());
        ToolHotkeyResolver.TryResolve([new ToolHotkeyBinding(id, "F8")], new ToolHotkeyPress("F8"), out var resolved).Should().BeTrue();
        resolved.Should().Be(id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreselectedAndToolFirstUseRealSolverKeepCanonicalIntentAndOneUndo(bool symmetric, bool toolFirst)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = Runtime();
        DrawingEntity[] entities = symmetric
            ? [new LineEntity(EntityId.Create("a"), new(-2, 0), new(-4, 3)), new LineEntity(EntityId.Create("b"), new(5, 3), new(3, 0)),
                new LineEntity(EntityId.Create("axis"), new(0, -5), new(0, 5))]
            : [new LineEntity(EntityId.Create("line"), new(3.1, 0.2), new(8, 1)), new CircleEntity(EntityId.Create("circle"), new(0, 0), 3)];
        var original = new DrawingDocument(entities);
        var workbench = new HeadlessWorkbench();
        Property(workbench, "SketchSolver", new PlaneGcsSketchSolverAdapter(runtime));
        Property(workbench, "MenuCommandService", new WorkbenchMenuCommandService());
        Property(workbench, "ToolHotkeys", new ToolHotkeyService());
        Set(workbench, "_document", original);
        renderer.Attach(workbench);
        var canvas = new HeadlessCanvas();
        renderer.Attach(canvas);
        typeof(DrawingCanvas).GetProperty("SelectionChanged")!.SetValue(canvas,
            (Action<IReadOnlySet<string>>)(keys => Invoke(workbench, "OnSelectionChanged", keys)));
        typeof(DrawingCanvas).GetProperty("ActiveSelectionChanged")!.SetValue(canvas,
            (Action<string?>)(key => Invoke(workbench, "OnActiveSelectionChanged", key!)));
        var command = symmetric ? WorkbenchCommandId.Symmetric : WorkbenchCommandId.Normal;
        var keys = symmetric ? new[] { "a", "b", "axis" } : new[] { "line|point|start|3.1|0.2", "circle|point|quadrant-0|3|0" };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (toolFirst) await (Task)Invoke(workbench, "InvokeWorkbenchCommand", command)!;
            for (var count = 1; count <= keys.Length; count++)
            {
                await canvas.OnSelectionChangedFromCanvas(keys.Take(count).ToArray(), keys[count - 1]);
                if (count < keys.Length || !toolFirst)
                    Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            }
            if (!toolFirst) await (Task)Invoke(workbench, "InvokeWorkbenchCommand", command)!;
            var changed = Get<DrawingDocument>(workbench, "_document");
            changed.Should().NotBeSameAs(original, Get<string>(workbench, "_status"));
            var relation = changed.Constraints.Should().ContainSingle().Which;
            relation.Kind.Should().Be(symmetric ? SketchConstraintKind.Symmetric : SketchConstraintKind.Normal);
            relation.ReferenceKeys.Should().Equal(symmetric
                ? ["a:start", "b:end", "a:end", "b:start", "axis"] : ["line:start", "circle"]);
            SketchConstraintService.ValidateConstraint(changed, relation).State.Should().Be(SketchConstraintState.Satisfied);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
            var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(changed));
            reopened.Constraints.Single().ReferenceKeys.Should().Equal(relation.ReferenceKeys);
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(changed);
        });
    }

    private static PlaneGcsNodeRuntime Runtime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p, executable)).First(File.Exists);
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }

    private static void Property(DrawingWorkbench target, string name, object value) => typeof(DrawingWorkbench).GetProperty(name, Private)!.SetValue(target, value);
    private static void Set(DrawingWorkbench target, string name, object? value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);
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
#pragma warning disable BL0006 // Exercise real selection callbacks and history without browser rendering.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
