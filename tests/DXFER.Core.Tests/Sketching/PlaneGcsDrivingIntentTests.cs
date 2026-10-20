using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsDrivingIntentTests
{
    [Fact]
    public void NativeRedundancyForAttemptedHypotenuseRejectsEvenWhenResidualMatches()
    {
        var original = Document(
            Dimension("width", SketchDimensionKind.HorizontalDistance, 3),
            Dimension("height", SketchDimensionKind.VerticalDistance, 4));
        var attempted = Dimension("diagonal", SketchDimensionKind.LinearDistance, 5);
        var runtime = new ReportingRuntime(["diagonal"]);

        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original, [], [attempted]));

        runtime.Request!.AttemptedDrivingDimensionIds.Should().Equal("diagonal");
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities[0].Should().BeSameAs(original.Entities[0]);
        result.Document.Dimensions.Should().Contain(attempted);
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "diagonal" && d.ItemKind == "dimension");
        original.Dimensions.Should().HaveCount(2);
    }

    [Fact]
    public void RedundancyForExistingIntentDoesNotRejectUnrelatedNewDrivingIntent()
    {
        var original = Document(Dimension("width", SketchDimensionKind.HorizontalDistance, 3));
        var runtime = new ReportingRuntime(["width", "internal-equation"]);
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original, [],
            [Dimension("height", SketchDimensionKind.VerticalDistance, 4)]));

        runtime.Request!.AttemptedDrivingDimensionIds.Should().Equal("height");
        result.Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedRetryAndLabelEditAreNotNewDrivingIntent(bool moveLabel)
    {
        var dimension = Dimension("diagonal", SketchDimensionKind.LinearDistance, 5);
        var original = Document(dimension);
        var update = moveLabel
            ? new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, dimension.Value, new(8, 9), true)
            : dimension;
        var runtime = new ReportingRuntime(["diagonal"]);

        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original, [], [update]));

        runtime.Request!.AttemptedDrivingDimensionIds.Should().BeEmpty();
        result.Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedValueOrReferenceToDrivingConversionIsAttemptedIntent(bool wasDriving)
    {
        var existing = new SketchDimension("diagonal", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], wasDriving ? 6 : 5, isDriving: wasDriving);
        var runtime = new ReportingRuntime(["diagonal"]);
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(Document(existing), [],
            [Dimension("diagonal", SketchDimensionKind.LinearDistance, 5)]));

        runtime.Request!.AttemptedDrivingDimensionIds.Should().Equal("diagonal");
        result.Status.Should().Be(SketchSolveStatus.Failed);
    }

    [Fact]
    public void MissingStructuredReportCannotSilentlyAcceptAttemptedDrivingIntent()
    {
        var original = Document();
        var result = new PlaneGcsSketchSolverAdapter(new ReportingRuntime(null)).Solve(new(original, [],
            [Dimension("diagonal", SketchDimensionKind.LinearDistance, 5)]));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities[0].Should().BeSameAs(original.Entities[0]);
        result.Diagnostics.Should().Contain(message => message.Contains("redundancy by intent ID"));
    }

    [Fact]
    public void EmptyStructuredReportAcceptsIndependentDrivingIntent()
    {
        var result = new PlaneGcsSketchSolverAdapter(new ReportingRuntime([])).Solve(new(Document(), [],
            [Dimension("diagonal", SketchDimensionKind.LinearDistance, 5)]));

        result.Status.Should().Be(SketchSolveStatus.Solved);
    }

    private static SketchDimension Dimension(string id, SketchDimensionKind kind, double value) =>
        new(id, kind, ["edge:start", "edge:end"], value, isDriving: true);

    private static DrawingDocument Document(params SketchDimension[] dimensions) =>
        new([new LineEntity(EntityId.Create("edge"), new(0, 0), new(3, 4))], dimensions, []);

    // This isolates the adapter acceptance contract; real WASM attribution belongs
    // in mapper/runtime integration tests.
    private sealed class ReportingRuntime(IReadOnlyList<string>? redundantIds) : IPlaneGcsRuntime
    {
        public PlaneGcsRequest? Request { get; private set; }

        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            return new("solved", request.Document.Entities, ["Redundant intent: pre-existing diagnostic."],
                "PlaneGCS", redundantIds);
        }
    }
}
