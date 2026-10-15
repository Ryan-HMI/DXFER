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

public sealed class WorkbenchPowerTrimIntentTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("trim", true, 6)]
    [InlineData("trim", false, 6)]
    [InlineData("split", true, 4)]
    [InlineData("split", false, 4)]
    [InlineData("extend", true, 14)]
    [InlineData("extend", false, 14)]
    public async Task HandlerPreservesIntentAndHistoryAcrossRefusalOrReferenceEdit(string operation, bool driving, double expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        renderer.Attach(workbench);
        var entities = new List<DrawingEntity>
        {
            new LineEntity(EntityId.Create("target"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("boundary"), new(operation == "extend" ? 14 : 4, -3), new(operation == "extend" ? 14 : 4, 3))
        };
        if (operation == "split") entities.Add(new LineEntity(EntityId.Create("right"), new(7, -3), new(7, 3)));
        var original = new DrawingDocument(entities, new[]
        {
            new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "target:start", "target:end" },
                10, new Point2(5, -2), driving)
        }, Array.Empty<SketchConstraint>());
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench, original);
        var native = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "OnPowerTrimRequested", "target", new CanvasPointDto(operation == "extend" ? 11 : operation == "split" ? 5 : 2, 0));
            var after = Get<DrawingDocument>(workbench, "_document");
            if (driving)
            {
                after.Should().BeSameAs(original);
                Get<string>(workbench, "_status").Should().Contain("Geometry unchanged");
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
                DxfDocumentWriter.Write(after, new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(native);
            }
            else
            {
                after.Dimensions.Single().Value.Should().BeApproximately(expected, 1e-9);
                after.Dimensions.Single().IsDriving.Should().BeFalse();
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1);
                var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(after, new DxfWriteOptions(IncludeNativeDocument: true)));
                reopened.Entities.Should().BeEquivalentTo(after.Entities);
                reopened.Dimensions.Should().BeEquivalentTo(after.Dimensions);
            }
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(after);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SplitRefusalIsVisibleAndNotOverwrittenByActivation(bool preselected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        renderer.Attach(workbench);
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new(0, 0), new(10, 0)),
            new PointEntity(EntityId.Create("pick"), new(2, 0))
        }, new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "target:start", "target:end" }, 10, isDriving: true) }, Array.Empty<SketchConstraint>());
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench, original);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (preselected)
            {
                Get<HashSet<string>>(workbench, "_selectedEntityIds").UnionWith(new[] { "target", "pick" });
                await (Task)Invoke(workbench, "InvokeWorkbenchCommand", WorkbenchCommandId.SplitAtPoint)!;
            }
            else
            {
                typeof(DrawingWorkbench).GetField("_activeTool", Private)!.SetValue(workbench, WorkbenchTool.SplitAtPoint);
                Invoke(workbench, "OnSplitAtPointRequested", "target", new CanvasPointDto(2, 0));
            }
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            var status = Get<string>(workbench, "_status");
            status.Should().Contain("Geometry unchanged").And.Contain("dimensions and constraints");
            typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench).Should().Be(status);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
        });
    }

    private static T Get<T>(DrawingWorkbench target, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) =>
        typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Exercise the real command handler without loading unrelated browser UI.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
