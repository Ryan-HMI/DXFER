using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class CanvasDragResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DragResultReturnsAuthoritativeAcceptanceToBrowser(bool accepted)
    {
        var canvas = new DrawingCanvas();
        var calls = 0;
        Func<string, CanvasPointDto, CanvasPointDto, bool, PlaneGcsConstraint?, bool> handler = (key, start, end, constrained, intent) =>
        {
            calls++;
            key.Should().Be("circle");
            start.X.Should().Be(3);
            start.Y.Should().Be(0);
            end.X.Should().Be(5);
            end.Y.Should().Be(0);
            constrained.Should().BeTrue();
            intent.Should().BeNull();
            return accepted;
        };
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.GeometryDragRequested))!.SetValue(canvas, handler);

        (await canvas.OnGeometryDragRequested("circle", 3, 0, 5, 0, true)).Should().Be(accepted);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task MissingDragHandlerRejectsOptimisticBrowserChange()
    {
        (await new DrawingCanvas().OnGeometryDragRequested("circle", 3, 0, 5, 0, false))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DragSnapIntentReachesAuthoritativeHandlerUnchanged()
    {
        var canvas = new DrawingCanvas();
        var snap = new PlaneGcsConstraint("ignored", "Midpoint", ["circle:center", "edge"], "Satisfied");
        Func<string, CanvasPointDto, CanvasPointDto, bool, PlaneGcsConstraint?, bool> handler = (_, _, _, _, intent) =>
        {
            intent.Should().BeSameAs(snap);
            return true;
        };
        typeof(DrawingCanvas).GetProperty(nameof(DrawingCanvas.GeometryDragRequested))!.SetValue(canvas, handler);

        (await canvas.OnGeometryDragRequested("circle|point|center|3|0", 3, 0, 5, 0, false, snap))
            .Should().BeTrue();
    }
}
