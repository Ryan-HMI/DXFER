using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.CadIO;
using DXFER.Core.Documents;
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
            Get<DrawingDocument?>(workbench, "_cornerPreview").Should().BeNull();
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();

            Set(workbench, "_activeTool", fillet ? WorkbenchTool.Fillet : WorkbenchTool.Chamfer);
            Invoke(workbench, "RefreshCornerPreview");
            Invoke(workbench, "CommitCornerPreview");
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().HaveCount(1);
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
    private static DrawingDocument Document(string intent) => new(new DrawingEntity[]
    {
        new LineEntity(EntityId.Create("h"), new(0, 0), new(10, 0)),
        new LineEntity(EntityId.Create("v"), new(0, 0), new(0, 10))
    }, new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
        new[] { "h:start", intent == "unresolvable" ? "missing:end" : "h:end" }, 10, new(5, -2), intent == "driving") },
        intent == "fixed"
            ? new[] { new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { "h" }, SketchConstraintState.Satisfied) }
            : Array.Empty<SketchConstraint>());

    private static HeadlessWorkbench Create()
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
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
