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
public sealed class WorkbenchPatternCenterSnapTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("Coincident", "target")]
    [InlineData("Midpoint", "edge")]
    [InlineData("PointOnLine", "edge")]
    [InlineData("Horizontal", "target")]
    [InlineData("Vertical", "target")]
    public async Task SnapIsStagedSolvedAndCommittedOnceWithSourceIntentUnchanged(string kind, string key)
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3, [new(new(2, 3), kind, [key])]);
            fixture.Unchanged();
            fixture.Property("CanConfirmPatternPreview").Should().Be(false);
            fixture.Move("end", 2, 3, [new(new(2, 3), kind, [key])]);
            fixture.Property("CanConfirmPatternPreview").Should().Be(true, fixture.Error);
            var preview = fixture.Preview!;
            var centerId = preview.Metadata.Patterns.Single().CenterPointId!;
            centerId.Should().NotBeNullOrEmpty();
            fixture.Original.Metadata.Patterns.Single().CenterPointId.Should().BeNull();
            AssertSnap(preview, centerId, kind, key);
            fixture.Unchanged();
            fixture.Invoke("CommitPatternPreview");
            var committed = fixture.Document;
            AssertSnap(committed, centerId, kind, key);
            committed.Entities.Single(e => e.Id.Value == "seed").Should().Be(fixture.Original.Entities.Single(e => e.Id.Value == "seed"));
            committed.Entities.Single(e => e.Id.Value == "target").Should().Be(fixture.Original.Entities.Single(e => e.Id.Value == "target"));
            foreach (var constraint in fixture.Original.Constraints)
                committed.Constraints.Single(c => c.Id == constraint.Id).Should().BeEquivalentTo(constraint);
            committed.Metadata.Patterns.Single().Center.Should().Be(new Point2(2, 3));
            fixture.Undo.Should().ContainSingle().Which.Should().BeSameAs(fixture.Original);
            var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(committed));
            AssertSnap(reopened, centerId, kind, key);
            fixture.Invoke("UndoLastDocumentChange");
            fixture.Document.Should().BeSameAs(fixture.Original);
            fixture.Invoke("RedoLastDocumentChange");
            fixture.Document.Should().BeSameAs(committed);
        });
    }

    [Fact]
    public async Task AutoOffKeepsExactCoordinatesWithoutExposingOrInferringIntent()
    {
        await using var fixture = new Fixture(automatic: false);
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("end", 2, 3, []);
            fixture.Property("CanConfirmPatternPreview").Should().Be(true, fixture.Error);
            fixture.Preview!.Constraints.Should().Equal(fixture.Original.Constraints);
            fixture.Preview.Metadata.Patterns.Single().CenterPointId.Should().BeNull();
            fixture.Invoke("CommitPatternPreview");
            fixture.Document.Metadata.Patterns.Single().Center.Should().Be(new Point2(2, 3));
            fixture.Document.Constraints.Should().BeEquivalentTo(fixture.Original.Constraints);
            fixture.Undo.Should().ContainSingle();
        });
    }

    [Fact]
    public async Task CancelDiscardsStagedPointAndIntentWithoutHistory()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3, [new(new(2, 3), "Coincident", ["target"])]);
            fixture.Preview!.Metadata.Patterns.Single().CenterPointId.Should().NotBeNull();
            fixture.Move("cancel", 0, 0);
            fixture.Preview!.Metadata.Patterns.Single().CenterPointId.Should().BeNull();
            fixture.Preview.Constraints.Should().Equal(fixture.Original.Constraints);
            fixture.Unchanged();
            fixture.Invoke("ExitInspectorTool");
            fixture.Move("end", 2, 3, [new(new(2, 3), "Coincident", ["target"])]);
            fixture.Preview.Should().BeNull();
            fixture.Unchanged();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountEditRetainsSnapButExplicitCenterEditClearsIt(bool editCenter)
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("end", 2, 3, [new(new(2, 3), "Coincident", ["target"])]);
            fixture.Draft.Count = "5";
            if (editCenter) fixture.Draft.CenterX = "4";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Property("CanConfirmPatternPreview").Should().Be(true, fixture.Error);
            fixture.Invoke("CommitPatternPreview");
            var pattern = fixture.Document.Metadata.Patterns.Single();
            pattern.Count.Should().Be(5);
            pattern.Center.Should().Be(new Point2(editCenter ? 4 : 2, 3));
            if (editCenter) fixture.Document.Constraints.Should().BeEquivalentTo(fixture.Original.Constraints);
            else AssertSnap(fixture.Document, pattern.CenterPointId!, "Coincident", "target");
            fixture.Undo.Should().ContainSingle();
        });
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("locked-member")]
    [InlineData("hidden")]
    [InlineData("member")]
    [InlineData("missing")]
    [InlineData("stale")]
    public async Task InvalidOrFixedSnapRefusesAtomically(string failure)
    {
        await using var fixture = new Fixture(protection: failure);
        await fixture.Run(() =>
        {
            var target = failure == "member" ? fixture.Original.Metadata.Patterns.Single().Members[0].EntityId + ":center"
                : failure == "missing" ? "missing" : "target";
            fixture.Move("begin", 0, 0);
            fixture.Move("end", 2, 3, [new(new(failure == "stale" ? 8 : 2, 3), "Coincident", [target])]);
            fixture.Preview.Should().BeNull();
            if (failure != "locked-member") fixture.Error.Should().NotBeNullOrWhiteSpace();
            else fixture.Property("PatternEditing").Should().Be(false, "locked geometry disables starting the gesture");
            fixture.Property("CanConfirmPatternPreview").Should().Be(false);
            fixture.Invoke("CommitPatternPreview");
            fixture.Unchanged();
        });
    }

    private static void AssertSnap(DrawingDocument document, string centerId, string kind, string key)
    {
        var constraint = document.Constraints.Should().ContainSingle(c => c.Kind.ToString() == kind
            && c.ReferenceKeys.SequenceEqual(new[] { key, centerId })).Which;
        SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        document.Entities.OfType<PointEntity>().Single(p => p.Id.Value == centerId).Location.Should().Be(new Point2(2, 3));
        DrawingPatternService.TryValidate(document, out var reason).Should().BeTrue(reason);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly TestRenderer _renderer;
        private readonly HeadlessWorkbench _workbench = new();
        private readonly PlaneGcsNodeRuntime _runtime = CreateRuntime();
        public DrawingDocument Original { get; }
        public DrawingDocument Document => Get<DrawingDocument>("_document");
        public DrawingDocument? Preview => (DrawingDocument?)Property("PatternPreview");
        public PatternDraft Draft => Get<PatternDraft>("_patternDraft");
        public string Error => Get<string?>("_patternError") ?? "";
        public Stack<DrawingDocument> Undo => Get<Stack<DrawingDocument>>("_undoStack");

        public Fixture(bool automatic = true, string? protection = null)
        {
            var source = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(10, 0), 1),
                new PointEntity(EntityId.Create("target"), new(2, 3)), new LineEntity(EntityId.Create("edge"), new(0, 3), new(4, 3))]);
            var pattern = new PatternDefinition { Id = "pattern", SourceEntityIds = ["seed"], Kind = PatternKind.Radial, Count = 4, FullCircle = true };
            DrawingPatternService.TryCreate(source, pattern, out var document, out var reason).Should().BeTrue(reason);
            var constraints = new List<SketchConstraint> { new("fix-seed", SketchConstraintKind.Fix, ["seed"], SketchConstraintState.Satisfied) };
            if (protection == "fixed")
            {
                DrawingPatternService.TryExposeCenter(document, pattern.Id, out document, out reason).Should().BeTrue(reason);
                constraints.Add(new("fix-center", SketchConstraintKind.Fix, [document.Metadata.Patterns.Single().CenterPointId!], SketchConstraintState.Satisfied));
            }
            if (protection == "hidden") document = DrawingLayerService.WithMetadata(document, document.Metadata with
            {
                EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles) { ["target"] = new("hidden") },
                Layers = new Dictionary<string, DxfEntityStyle> { ["hidden"] = new("hidden", IsHidden: true) }
            });
            if (protection == "locked-member") document = DrawingLayerService.WithMetadata(document, document.Metadata with
            {
                EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles)
                {
                    [document.Metadata.Patterns.Single().Members[0].EntityId] = new("locked")
                },
                Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) }
            });
            Original = new DrawingDocument(document.Entities, document.Dimensions, constraints, document.Metadata);
            typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(_workbench, Original);
            typeof(DrawingWorkbench).GetField("_shownPatternControlsId", Private)!.SetValue(_workbench, pattern.Id);
            typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(_workbench, new WorkbenchMenuCommandService { AutomaticSnapConstraints = automatic });
            typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(_workbench, new ToolHotkeyService());
            typeof(DrawingWorkbench).GetProperty("SketchSolver", Private)!.SetValue(_workbench, new PlaneGcsSketchSolverAdapter(_runtime));
            _renderer = new TestRenderer(_services);
            _renderer.Attach(_workbench);
        }

        public Task Run(Action action) => _renderer.Dispatcher.InvokeAsync(action);
        public void Move(string phase, double x, double y, CanvasCreationSnapDto[]? snaps = null) => Invoke("MovePatternCenter", "pattern", x, y, phase, snaps!);
        public void Invoke(string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(_workbench, args);
        public object? Property(string name) => typeof(DrawingWorkbench).GetProperty(name, Private)!.GetValue(_workbench);
        private T Get<T>(string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(_workbench)!;
        public void Unchanged()
        {
            Document.Should().BeSameAs(Original);
            Undo.Should().BeEmpty();
            Get<Stack<DrawingDocument>>("_redoStack").Should().BeEmpty();
        }
        public async ValueTask DisposeAsync()
        {
#pragma warning disable BL0006
            await _renderer.DisposeAsync();
#pragma warning restore BL0006
            _runtime.Dispose();
            await _services.DisposeAsync();
        }
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
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"), Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }
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
