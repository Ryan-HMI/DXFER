using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

[Collection("PlaneGCS resource budget")]
public sealed class WorkbenchDrivingDimensionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementRetainsNativeRedundancyBeforeAnyValueEdit(bool circle)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var workbench = Attach(renderer, runtime);
        var original = Document(circle);
        Set(workbench, "_document", original);
        var duplicate = new SketchDimension("duplicate", original.Dimensions[0].Kind,
            original.Dimensions[0].ReferenceKeys, 10, new(4, -3), true);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "PlaceSketchDimension", duplicate, "Placed dimension.");
            var failed = Get<DrawingDocument>(workbench, "_document");
            AssertRedundant(failed);
            failed.Entities.Should().Equal(original.Entities);
            Get<string>(workbench, "_status").Should().Contain("redundant");
            AssertRedundant(Reopen(failed));
            Invoke(workbench, "OnSketchDimensionAnchorChanged", "duplicate", new CanvasPointDto(6, -4));
            AssertRedundant(Get<DrawingDocument>(workbench, "_document"));
            Invoke(workbench, "UndoLastDocumentChange");
            AssertRedundant(Get<DrawingDocument>(workbench, "_document"));
            Invoke(workbench, "UndoLastDocumentChange");
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Invoke(workbench, "RedoLastDocumentChange");
            AssertRedundant(Get<DrawingDocument>(workbench, "_document"));
            Invoke(workbench, "LoadDxfText", "reopened.dxf",
                DxfDocumentWriter.Write(failed, new DxfWriteOptions(IncludeNativeDocument: true)), false, false);
            AssertRedundant(Get<DrawingDocument>(workbench, "_document"));
            Set(workbench, "_contextKey", "dimension:duplicate");
            Set(workbench, "_contextDocument", Get<DrawingDocument>(workbench, "_document"));
            Invoke(workbench, "ContextDimensionMode", false);
            var driven = Get<DrawingDocument>(workbench, "_document");
            driven.Dimensions.Single(d => d.Id == "duplicate").IsDriving.Should().BeFalse();
            PlaneGcsDiagnostics.Read(driven).HasCurrentEvidence.Should().BeTrue();
            PlaneGcsDiagnostics.FromDocument(driven).Should().BeEmpty();
            Invoke(workbench, "OnSketchDimensionValueChanged", "size", 12d);
            var changed = Reopen(Get<DrawingDocument>(workbench, "_document"));
            changed.Dimensions.Should().OnlyContain(d => Math.Abs(d.Value - 12) < 1e-7);
            changed.Dimensions.Single(d => d.Id == "duplicate").IsDriving.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextPromotionUsesNativeRankAndKeepsRedundantMeasurementDriven(bool redundant)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new TestRenderer(services);
        using var runtime = CreateRuntime();
        var workbench = Attach(renderer, runtime);
        var baseline = Document(false);
        var measurement = new SketchDimension("measurement", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], 10, new(4, -3), false);
        var original = new DrawingDocument(baseline.Entities,
            redundant ? baseline.Dimensions.Append(measurement) : [measurement], []);
        Set(workbench, "_document", original);
        Set(workbench, "_contextDocument", original);
        Set(workbench, "_contextKey", "dimension:measurement");
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            Invoke(workbench, "ContextDimensionMode", true);
            var after = Get<DrawingDocument>(workbench, "_document");
            after.Entities.Should().Equal(original.Entities);
            after.Dimensions.Single(d => d.Id == "measurement").IsDriving.Should().Be(!redundant);
            if (redundant)
            {
                after.Should().BeSameAs(original);
                Get<Stack<DrawingDocument>>(workbench, "_undoStack").Should().BeEmpty();
                Get<string>(workbench, "_status").Should().Contain("redundant");
            }
            else
            {
                PlaneGcsDiagnostics.Read(after).HasCurrentEvidence.Should().BeTrue();
                Invoke(workbench, "OnSketchDimensionValueChanged", "measurement", 12d);
                Reopen(Get<DrawingDocument>(workbench, "_document")).Dimensions.Single().Value.Should().Be(12);
            }
        });
    }

    private static DrawingDocument Document(bool circle) => new(
        circle ? [new CircleEntity(EntityId.Create("edge"), new(4, 4), 5)]
            : new DrawingEntity[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)) },
        [new("size", circle ? SketchDimensionKind.Diameter : SketchDimensionKind.LinearDistance,
            circle ? ["edge"] : ["edge:start", "edge:end"], 10, new(4, -1), true)], []);

    private static void AssertRedundant(DrawingDocument document)
    {
        PlaneGcsDiagnostics.Read(document).HasCurrentEvidence.Should().BeTrue();
        PlaneGcsDiagnostics.GetDimensionState(document, document.Dimensions.Single(d => d.Id == "duplicate"))
            .Should().Be(SketchConstraintState.Unsatisfied);
        PlaneGcsDiagnostics.FromDocument(document).Should().Contain(d => d.ItemId == "duplicate"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
    }

    private static DrawingDocument Reopen(DrawingDocument document) =>
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));

    private static DrawingWorkbench Attach(TestRenderer renderer, IPlaneGcsRuntime runtime)
    {
        var workbench = new HeadlessWorkbench();
        typeof(DrawingWorkbench).GetProperty("SketchSolver", Private)!.SetValue(workbench, new PlaneGcsSketchSolverAdapter(runtime));
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
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

    private static void Set(DrawingWorkbench target, string name, object value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);
    private static T Get<T>(DrawingWorkbench target, string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
