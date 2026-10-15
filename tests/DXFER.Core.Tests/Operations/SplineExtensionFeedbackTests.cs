using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class SplineExtensionFeedbackTests
{
    [Theory]
    [InlineData("cubic", true)]
    [InlineData("cubic", false)]
    [InlineData("quadratic", true)]
    [InlineData("quadratic", false)]
    [InlineData("fit", true)]
    [InlineData("fit", false)]
    public void OffEndCurvedSplinePickReportsUnsupportedExtensionWithoutMutation(string kind, bool start)
    {
        var spline = CreateSpline(kind);
        var samples = spline.GetSamplePoints();
        var endpoint = samples[start ? 0 : ^1];
        var adjacent = samples[start ? 1 : ^2];
        var outward = new Point2(endpoint.X - adjacent.X, endpoint.Y - adjacent.Y);
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        var native = DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));
        foreach (var perpendicularOffset in new[] { 0.0, 0.2 })
        {
            // Canvas extension picks preserve the cursor, including its screen-space lateral offset.
            var pick = new Point2(endpoint.X + outward.X * 2 - outward.Y * perpendicularOffset,
                endpoint.Y + outward.Y * 2 + outward.X * perpendicularOffset);
            DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", pick,
                _ => throw new InvalidOperationException("Refusal must not allocate geometry IDs"), out var next, out var status)
                .Should().BeFalse();
            next.Should().BeSameAs(document);
            status.Should().Contain("Spline extension is not supported").And.Contain("Geometry unchanged");
            DxfDocumentWriter.Write(next, new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(native);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DegreeOneExtensionStillSucceeds(bool start)
    {
        var target = new SplineEntity(EntityId.Create("target"), 1, new Point2[] { new(0, 0), new(4, 0) },
            new double[] { 0, 0, 1, 1 });
        var boundary = new LineEntity(EntityId.Create("boundary"), new(start ? -2 : 6, -2), new(start ? -2 : 6, 2));
        var document = new DrawingDocument(new DrawingEntity[] { target, boundary });
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", new(start ? -0.2 : 4.2, 0),
            name => EntityId.Create(name + "-new"), out var next, out var status).Should().BeTrue();
        status.Should().Be("Power trim/extend applied.");
        var result = (SplineEntity)next.Entities[0];
        result.Degree.Should().Be(1);
        result.ControlPoints[start ? 0 : ^1].Should().Be(new Point2(start ? -2 : 6, 0));
        next.Entities[1].Should().BeSameAs(boundary);
    }

    [Fact]
    public void InteriorExactCubicTrimStillSucceedsWithUnchangedRepresentation()
    {
        var target = new SplineEntity(EntityId.Create("target"), 3,
            new Point2[] { new(0, 0), new(0, 4), new(4, 4), new(4, 0) }, new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target, new LineEntity(EntityId.Create("cutter"), new(2, -1), new(2, 5))
        });
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", new(.625, 2.25),
            name => EntityId.Create(name + "-new"), out var next, out var status).Should().BeTrue();
        status.Should().Be("Power trim/extend applied.");
        var result = (SplineEntity)next.Entities[0];
        result.Degree.Should().Be(3);
        result.ControlPoints.Should().Equal(new Point2(2, 3), new(3, 3), new(4, 2), new(4, 0));
        result.Knots.Should().Equal(.5, .5, .5, .5, 1, 1, 1, 1);
    }

    [Fact]
    public void OffCurveInteriorPickDoesNotClaimExtensionWasRequested()
    {
        var document = new DrawingDocument(new DrawingEntity[] { CreateSpline("cubic") });
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", new(2, .8),
            name => EntityId.Create(name + "-new"), out var next, out var status).Should().BeFalse();
        next.Should().BeSameAs(document);
        status.Should().Contain("Geometry unchanged").And.NotContain("Spline extension is not supported");
    }

    public static SplineEntity CreateSpline(string kind) => kind switch
    {
        "fit" => SplineEntity.FromFitPoints(EntityId.Create("target"), new Point2[] { new(0, 0), new(2, 1), new(4, 0) }),
        "quadratic" => new SplineEntity(EntityId.Create("target"), 2,
            new Point2[] { new(0, 0), new(2, 1), new(4, 0) }, new double[] { 0, 0, 0, 1, 1, 1 }),
        _ => new SplineEntity(EntityId.Create("target"), 3,
            new Point2[] { new(0, 0), new(1, 0), new(3, 1), new(4, 1) }, new double[] { 0, 0, 0, 0, 1, 1, 1, 1 })
    };
}
