using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
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

public sealed class WorkbenchPatternCenterControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AcceptAndCancelDismissControlsUntilExplicitShow(bool linear, bool accept)
    {
        await using var fixture = new Fixture(linear ? "rectangular" : null);
        await fixture.Run(() =>
        {
            fixture.Invoke("EditViewportPattern", fixture.Pattern);
            if (linear) fixture.Draft!.Columns = "3";
            else fixture.Draft!.Count = "5";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Property("CanConfirmPatternPreview").Should().Be(true);
            fixture.Invoke(accept ? "CommitPatternPreview" : "ExitInspectorTool");
            fixture.Draft.Should().BeNull();
            fixture.Property("ViewportPattern").Should().BeNull();
            fixture.Get<string?>("_shownPatternControlsId").Should().BeNull();
            fixture.Set("_selectedEntityIds", new HashSet<string>(StringComparer.Ordinal) { "seed" });
            fixture.Property("ViewportPattern").Should().BeNull("selection must not resurrect a dismissed card");
            if (accept) fixture.Undo.Should().ContainSingle();
            else fixture.Unchanged();
            var current = fixture.Document.Metadata.Patterns.Single();
            fixture.Invoke("TogglePatternControls", current);
            fixture.Property("ViewportPattern").Should().NotBeNull();
            fixture.Invoke("EditViewportPattern", current);
            fixture.Draft.Should().NotBeNull();
            fixture.Property("ViewportPattern").Should().NotBeNull();
        });
    }

    [Fact]
    public async Task ExplicitCancelClosesDirectionPickerAndWholePatternEditor()
    {
        await using var fixture = new Fixture("rectangular");
        await fixture.Run(() =>
        {
            fixture.Invoke("EditViewportPattern", fixture.Pattern);
            fixture.Invoke("BeginPatternDirectionPick", "U");
            fixture.Get<string?>("_patternDirectionPicking").Should().Be("U");
            fixture.Invoke("ExitInspectorTool");
            fixture.Draft.Should().BeNull();
            fixture.Property("ViewportPattern").Should().BeNull();
            fixture.Get<string?>("_patternDirectionPicking").Should().BeNull();
            fixture.Invoke("PickPatternDirection", "pattern", "U", "seed");
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task InvalidAcceptKeepsEditorAndControlsOpen()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Invoke("EditViewportPattern", fixture.Pattern);
            fixture.Draft!.Count = "0";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Invoke("CommitPatternPreview");
            fixture.Draft.Should().NotBeNull();
            fixture.Property("ViewportPattern").Should().NotBeNull();
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task HeldCenterMoveOnlyPreviews_EndUsesLatestPosition_ConfirmCreatesOneHistoryEntry()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3);
            fixture.Preview!.Metadata.Patterns.Single().Center.Should().Be(new Point2(2, 3));
            fixture.Unchanged();
            fixture.Property("CanConfirmPatternPreview").Should().Be(false);
            fixture.Invoke("TogglePatternControls", fixture.Pattern);
            fixture.Property("ViewportPattern").Should().NotBeNull("a captured drag cannot hide its own release target");
            fixture.Invoke("CommitPatternPreview");
            fixture.Unchanged();
            fixture.Move("end", 4, 5);
            fixture.Preview!.Metadata.Patterns.Single().Center.Should().Be(new Point2(4, 5));
            fixture.Property("CanConfirmPatternPreview").Should().Be(true);
            fixture.Unchanged();
            fixture.Invoke("CommitPatternPreview");
            fixture.Document.Metadata.Patterns.Single().Center.Should().Be(new Point2(4, 5));
            fixture.Document.Entities.Single(e => e.Id.Value == "seed").Should().Be(fixture.Original.Entities.Single(e => e.Id.Value == "seed"));
            fixture.Undo.Should().ContainSingle().Which.Should().BeSameAs(fixture.Original);
            fixture.Invoke("UndoLastDocumentChange");
            fixture.Document.Should().BeSameAs(fixture.Original);
            fixture.Invoke("RedoLastDocumentChange");
            fixture.Document.Metadata.Patterns.Single().Center.Should().Be(new Point2(4, 5));
        });
    }

    [Fact]
    public async Task CancelRestoresExactDraftTextAndIgnoresLateUpdates_ThenAnotherDragRecovers()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Invoke("EditViewportPattern", fixture.Pattern);
            fixture.Draft!.CenterX = "1.2500";
            fixture.Draft.CenterY = "-2.500";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Move("begin", 1.25, -2.5);
            fixture.Move("update", 5, 6);
            fixture.Move("cancel", double.NaN, double.NaN);
            fixture.Draft.CenterX.Should().Be("1.2500");
            fixture.Draft.CenterY.Should().Be("-2.500");
            fixture.Move("update", 9, 9);
            fixture.Move("end", 9, 9);
            fixture.Preview!.Metadata.Patterns.Single().Center.Should().Be(new Point2(1.25, -2.5));
            fixture.Unchanged();
            fixture.Move("begin", 1.25, -2.5);
            fixture.Move("end", 2, 4);
            fixture.Preview!.Metadata.Patterns.Single().Center.Should().Be(new Point2(2, 4));
            fixture.Invoke("ExitInspectorTool");
            fixture.Move("end", 7, 8);
            fixture.Move("update", 7, 8);
            fixture.Draft.Should().BeNull();
            fixture.Preview.Should().BeNull();
            fixture.Unchanged();
        });
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 0)]
    public async Task NonFiniteCoordinatesCannotOpenOrCorruptDraft(double x, double y)
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", x, y);
            fixture.Draft.Should().BeNull();
            fixture.Move("begin", 0, 0);
            fixture.Move("update", x, y);
            fixture.Move("end", x, y);
            fixture.Draft!.CenterX.Should().Be("0");
            fixture.Draft.CenterY.Should().Be("0");
            fixture.Property("CanConfirmPatternPreview").Should().Be(false);
            fixture.Get<bool>("_patternCenterDragging").Should().BeFalse();
            fixture.Move("begin", 0, 0);
            fixture.Move("end", 0, 0);
            fixture.Property("CanConfirmPatternPreview").Should().Be(true);
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task WrongIdDuplicateBeginAndUnsolicitedPhasesCannotReplaceDraft()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("update", 8, 8);
            fixture.Move("end", 8, 8);
            fixture.Move("cancel", 8, 8);
            fixture.Move("unknown", 8, 8);
            fixture.Move("begin", 8, 8, "other");
            fixture.Draft.Should().BeNull();
            fixture.Move("begin", 0, 0);
            var draft = fixture.Draft;
            fixture.Move("update", 2, 3);
            fixture.Move("begin", 2, 3);
            fixture.Move("end", 9, 9, "other");
            fixture.Move("cancel", 9, 9, "other");
            fixture.Draft.Should().BeSameAs(draft);
            fixture.Move("cancel", 0, 0);
            fixture.Draft!.CenterX.Should().Be("0", "a duplicate begin must not overwrite the drag rollback point");
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task ReplacedDocumentRejectsStaleUpdatesAndCommitWithoutHistory()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3);
            var replacement = new DrawingDocument(fixture.Original.Entities, fixture.Original.Dimensions,
                fixture.Original.Constraints, fixture.Original.Metadata);
            fixture.Set("_document", replacement);
            fixture.Move("update", 8, 9);
            fixture.Move("end", 8, 9);
            fixture.Move("begin", 8, 9);
            fixture.Invoke("CommitPatternPreview");
            fixture.Draft!.CenterX.Should().Be("0");
            fixture.Get<bool>("_patternCenterDragging").Should().BeFalse();
            fixture.Preview.Should().BeNull();
            fixture.Document.Should().BeSameAs(replacement);
            fixture.Undo.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("locked-source")]
    [InlineData("locked-member")]
    [InlineData("hidden-source")]
    [InlineData("reference-only")]
    [InlineData("rectangular")]
    public async Task ProtectedOrNonRadialPatternsCannotBeginCenterDrag(string protection)
    {
        await using var fixture = new Fixture(protection);
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3);
            fixture.Move("end", 2, 3);
            fixture.Draft.Should().BeNull();
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task FixedMemberRefusesPreview_CancelRecoversThroughExistingValidation()
    {
        await using var fixture = new Fixture("fixed-member");
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3);
            fixture.Preview.Should().BeNull();
            fixture.Get<string>("_patternError").Should().Contain("Fixed");
            fixture.Invoke("CommitPatternPreview");
            fixture.Move("cancel", 0, 0);
            fixture.Preview.Should().NotBeNull();
            fixture.Get<string?>("_patternError").Should().BeNull();
            fixture.Unchanged();
        });
    }

    [Fact]
    public async Task InvalidDraftIsNotCommittedByDraggingAndCanRecoverWithoutLosingOtherInputs()
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Invoke("EditViewportPattern", fixture.Pattern);
            fixture.Draft!.Count = "1";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Move("begin", 0, 0);
            fixture.Move("end", 2, 3);
            fixture.Preview.Should().BeNull();
            fixture.Invoke("CommitPatternPreview");
            fixture.Unchanged();
            fixture.Draft.Count = "8";
            fixture.Invoke("RefreshPatternPreview");
            fixture.Preview!.Metadata.Patterns.Single().Count.Should().Be(8);
            fixture.Preview.Metadata.Patterns.Single().Center.Should().Be(new Point2(2, 3));
            fixture.Unchanged();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidModeOrProtectionAtReleaseClearsDragAndPreservesHistory(bool protect)
    {
        await using var fixture = new Fixture();
        await fixture.Run(() =>
        {
            fixture.Move("begin", 0, 0);
            fixture.Move("update", 2, 3);
            if (protect)
                fixture.Set("_document", DrawingLayerService.WithMetadata(fixture.Original,
                    fixture.Original.Metadata with { Mode = DrawingDocumentMode.ReferenceOnly }));
            else fixture.Draft!.Mode = "Rectangular";
            var document = fixture.Document;
            fixture.Move("end", 4, 5);
            fixture.Get<bool>("_patternCenterDragging").Should().BeFalse();
            fixture.Draft!.CenterX.Should().Be("0");
            fixture.Draft.CenterY.Should().Be("0");
            fixture.Get<string?>("_patternError").Should().NotBeNull();
            fixture.Invoke("CommitPatternPreview");
            fixture.Document.Should().BeSameAs(document);
            fixture.Undo.Should().BeEmpty();
        });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly TestRenderer _renderer;
        private readonly HeadlessWorkbench _workbench = new();
        public DrawingDocument Original { get; }
        public PatternDefinition Pattern => Original.Metadata.Patterns.Single();
        public DrawingDocument Document => Get<DrawingDocument>("_document");
        public DrawingDocument? Preview => (DrawingDocument?)Property("PatternPreview");
        public PatternDraft? Draft => Get<PatternDraft?>("_patternDraft");
        public Stack<DrawingDocument> Undo => Get<Stack<DrawingDocument>>("_undoStack");

        public Fixture(string? protection = null)
        {
            var source = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(10, 0), 1)]);
            var pattern = new PatternDefinition
            {
                Id = "pattern", SourceEntityIds = ["seed"], Count = 4, FullCircle = true,
                Kind = protection == "rectangular" ? PatternKind.Rectangular : PatternKind.Radial,
                Columns = 2, Rows = 2, XPitch = 3, YPitch = 3
            };
            DrawingPatternService.TryCreate(source, pattern, out var document, out var reason).Should().BeTrue(reason);
            var memberId = document.Metadata.Patterns.Single().Members.First().EntityId;
            if (protection is "locked-source" or "locked-member" or "hidden-source")
            {
                var id = protection == "locked-member" ? memberId : "seed";
                document = DrawingLayerService.WithMetadata(document, document.Metadata with
                {
                    EntityStyles = new Dictionary<string, DxfEntityStyle> { [id] = new("protected") },
                    Layers = new Dictionary<string, DxfEntityStyle> { ["protected"] = new("protected", IsLocked: protection != "hidden-source", IsHidden: protection == "hidden-source") }
                });
            }
            if (protection == "reference-only")
                document = DrawingLayerService.WithMetadata(document, document.Metadata with { Mode = DrawingDocumentMode.ReferenceOnly });
            if (protection == "fixed-member")
                document = new DrawingDocument(document.Entities, document.Dimensions,
                    [new SketchConstraint("fixed", SketchConstraintKind.Fix, [memberId])], document.Metadata);
            Original = document;
            Set("_document", document);
            Set("_shownPatternControlsId", pattern.Id);
            typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(_workbench, new WorkbenchMenuCommandService());
            typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(_workbench, new ToolHotkeyService());
            _renderer = new TestRenderer(_services);
            _renderer.Attach(_workbench);
        }

        public Task Run(Action action) => _renderer.Dispatcher.InvokeAsync(action);
        public void Move(string phase, double x, double y, string id = "pattern") => Invoke("MovePatternCenter", id, x, y, phase, null!);
        public void Invoke(string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(_workbench, args);
        public object? Property(string name) => typeof(DrawingWorkbench).GetProperty(name, Private)!.GetValue(_workbench);
        public T Get<T>(string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(_workbench)!;
        public void Set(string name, object value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(_workbench, value);
        public void Unchanged()
        {
            Document.Should().BeSameAs(Original);
            Undo.Should().BeEmpty();
            Get<Stack<DrawingDocument>>("_redoStack").Should().BeEmpty();
        }
        public async ValueTask DisposeAsync()
        {
#pragma warning disable BL0006 // Dispose the attached test renderer.
            await _renderer.DisposeAsync();
#pragma warning restore BL0006
            await _services.DisposeAsync();
        }
    }

    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Attach the actual callback without unrelated browser modules.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}
