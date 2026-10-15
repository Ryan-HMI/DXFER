using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Tests.Operations;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchSplineExtensionFeedbackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("cubic", true)]
    [InlineData("cubic", false)]
    [InlineData("quadratic", true)]
    [InlineData("quadratic", false)]
    [InlineData("fit", true)]
    [InlineData("fit", false)]
    public async Task OffEndPickShowsSpecificRefusalAndLeavesNativeDocumentAndHistoryUnchanged(string kind, bool start)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        renderer.Attach(workbench);
        var target = SplineExtensionFeedbackTests.CreateSpline(kind);
        var document = new DrawingDocument(new DrawingEntity[] { target });
        var samples = target.GetSamplePoints();
        var endpoint = samples[start ? 0 : ^1];
        var adjacent = samples[start ? 1 : ^2];
        var pick = new CanvasPointDto(endpoint.X + (endpoint.X - adjacent.X) * 2,
            endpoint.Y + (endpoint.Y - adjacent.Y) * 2);
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench, document);
        typeof(DrawingWorkbench).GetField("_activeTool", Private)!.SetValue(workbench, WorkbenchTool.PowerTrim);
        var native = DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "OnPowerTrimRequested", "target", pick);
            var status = Get<string>(workbench, "_status");
            status.Should().Contain("Spline extension is not supported").And.Contain("Geometry unchanged");
            typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench).Should().Be(status);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(document);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            Get<Stack<DrawingDocument>>(workbench, "_redoStack").Should().BeEmpty();
            Invoke(workbench, "UndoLastDocumentChange");
            Invoke(workbench, "RedoLastDocumentChange");
            DxfDocumentWriter.Write(Get<DrawingDocument>(workbench, "_document"),
                new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(native);
        });
    }

    private static T Get<T>(DrawingWorkbench target, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static void Invoke(DrawingWorkbench target, string name, params object[] args) =>
        typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Run the actual command handler and prompt without unrelated browser modules.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
