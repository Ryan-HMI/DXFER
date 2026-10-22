using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using FluentAssertions;
using Microsoft.JSInterop;

namespace DXFER.Core.Tests.Components;

public sealed class PatternCenterPickTransitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyEndingAnUnchangedDraftResetsCanvasModeBeforePicking(bool endPatternEditing)
    {
        var canvas = new DrawingCanvas();
        var bridge = new Bridge();
        typeof(DrawingCanvas).GetField("_canvasInstance", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(canvas, bridge);
        await canvas.PickPatternCenterAsync(new DrawingDocument([]), "center", true, endPatternEditing);
        bridge.Calls.Should().Equal(endPatternEditing
            ? ["setDocument", "setCornerPreview", "setActiveTool", "pickPatternCenter"]
            : ["setDocument", "pickPatternCenter"]);
    }

    private sealed class Bridge : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "setActiveTool") args.Should().Equal("select");
            if (identifier == "setCornerPreview") args![3].Should().Be(false);
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
