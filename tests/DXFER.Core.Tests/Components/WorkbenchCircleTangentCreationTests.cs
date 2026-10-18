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

public sealed class WorkbenchCircleTangentCreationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSnapCreatesOneHistoryTransactionAndKeepsPeersAndExistingFailedIntent(bool failedIntent)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var original = Fixture(failedIntent);
        var workbench = Attach(renderer, original);
        var originalBytes = Native(original);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Canvas(workbench).OnSketchToolCommitted("centercircle", new double[] { 0, 3, 0, 0 },
                new[] { "radius" }, new double[] { 3 }, "edge");
            var created = Get<DrawingDocument>(workbench, "_document");
            created.Entities.Take(original.Entities.Count).Should().Equal(original.Entities);
            created.Entities.OfType<CircleEntity>().Should().ContainSingle().Which.Center.Should().Be(new Point2(0, 3));
            created.Entities.OfType<CircleEntity>().Single().Radius.Should().Be(3);
            created.Dimensions.Take(original.Dimensions.Count).Should().Equal(original.Dimensions);
            created.Constraints.Take(original.Constraints.Count).Should().Equal(original.Constraints);
            created.Constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Tangent)
                .Which.State.Should().Be(SketchConstraintState.Satisfied);
            var radial = created.Dimensions.Single(d => d.Kind == SketchDimensionKind.Radius);
            radial.Value.Should().Be(3);
            radial.IsDriving.Should().BeTrue();
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle().Which.Should().BeSameAs(original);
            Native(original).Should().Be(originalBytes);
            if (failedIntent)
            {
                created.Constraints.Single(c => c.Id == "failed-relation").State.Should().Be(SketchConstraintState.Unsatisfied);
                SketchDimensionSolverService.IsDimensionSatisfied(created, created.Dimensions.Single(d => d.Id == "failed-distance")).Should().BeFalse();
            }
            var reopened = DxfDocumentReader.Read(Native(created));
            DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(created));
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(created);
        });
    }

    [Theory]
    [InlineData("missing", 3)]
    [InlineData("edge", 4)]
    [InlineData("edge:start", 3)]
    public async Task StaleOrIncompatibleSnapRefusesWithoutHistoryAndExplainsWhy(string key, double radius)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var original = Fixture(false);
        var workbench = Attach(renderer, original);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Canvas(workbench).OnSketchToolCommitted("centercircle", new double[] { 0, 3, 0, 0 },
                new[] { "radius" }, new[] { radius }, key);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
            var status = Get<string>(workbench, "_status");
            status.Should().Contain("tangent snap").And.Contain("Geometry unchanged");
            typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench).Should().Be(status);
        });
    }

    [Fact]
    public async Task NullSnapStillCreatesOrdinaryCircleWithoutPersistentTangency()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        var workbench = Attach(renderer, Fixture(false));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await Canvas(workbench).OnSketchToolCommitted("centercircle", new double[] { 0, 3, 0, 0 }, null, null, null);
            var document = Get<DrawingDocument>(workbench, "_document");
            document.Entities.OfType<CircleEntity>().Should().ContainSingle();
            document.Constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Tangent);
            Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().ContainSingle();
        });
    }

    private static DrawingDocument Fixture(bool failedIntent)
    {
        var entities = new List<DrawingEntity> { new LineEntity(EntityId.Create("edge"), new(-4, 0), new(4, 0)) };
        var dimensions = new List<SketchDimension> { new("width", SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" }, 8, isDriving: true) };
        var constraints = new List<SketchConstraint>();
        if (failedIntent)
        {
            entities.Add(new LineEntity(EntityId.Create("unrelated"), new(20, 20), new(23, 24)));
            dimensions.Add(new("failed-distance", SketchDimensionKind.LinearDistance,
                new[] { "unrelated:start", "unrelated:end" }, 10, isDriving: true));
            constraints.Add(new("failed-relation", SketchConstraintKind.Horizontal, new[] { "unrelated" }, SketchConstraintState.Unsatisfied));
        }
        return new DrawingDocument(entities, dimensions, constraints);
    }

    private static DrawingCanvas Canvas(DrawingWorkbench workbench)
    {
        var canvas = new DrawingCanvas();
        Action<string, IReadOnlyList<CanvasPointDto>, IReadOnlyDictionary<string, double>, string?, bool> handler =
            (tool, points, locks, key, origin) => Invoke(workbench, "OnToolCommitRequested", tool, points, locks, key!, origin);
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.ToolCommitRequested))!.SetValue(canvas, handler);
        return canvas;
    }

    private static HeadlessWorkbench Attach(TestRenderer renderer, DrawingDocument document)
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench, document);
        typeof(DrawingWorkbench).GetField("_activeTool", Private)!.SetValue(workbench, WorkbenchTool.CenterCircle);
        renderer.Attach(workbench);
        return workbench;
    }

    private static string Native(DrawingDocument document) => DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));
    private static T Get<T>(DrawingWorkbench workbench, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(workbench)!;
    private static void Invoke(DrawingWorkbench workbench, string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(workbench, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Exercise real event/handler/history without unrelated browser modules.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
