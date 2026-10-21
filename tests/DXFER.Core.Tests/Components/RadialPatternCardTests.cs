using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

public sealed class RadialPatternCardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RadialCardKeepsCenterHandleAndShowsAngleOnlyForPartialSpacing(bool fullCircle)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<PatternViewportControls>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PatternViewportControls.Pattern)] = new PatternDefinition { Id = "radial", Kind = PatternKind.Radial },
                [nameof(PatternViewportControls.SourceBounds)] = new Bounds2(9, 9, 11, 11),
                [nameof(PatternViewportControls.Draft)] = new PatternDraft { Mode = "Radial", FullCircle = fullCircle, Count = "6", StepAngle = "45" },
                [nameof(PatternViewportControls.CanConfirm)] = true,
                [nameof(PatternViewportControls.CanEdit)] = true
            }));
            return component.ToHtmlString();
        });
        html.Should().Contain("pattern-radial-card").And.Contain("data-pattern-center-handle");
        html.Should().Contain("data-source-min-x=\"9\"").And.Contain("data-source-max-y=\"11\"");
        html.Should().Contain("aria-label=\"Confirm\"").And.Contain("aria-label=\"Cancel pattern changes\"");
        html.Should().Contain("type=\"checkbox\" aria-label=\"Full Circle\"");
        html.Should().NotContain("Viewport pattern center X").And.NotContain("Viewport pattern center Y");
        html.Should().NotContain("data-pattern-anchor=\"actions\"").And.NotContain("data-pattern-anchor=\"angle\"");
        if (fullCircle) html.Should().NotContain("Viewport pattern step angle");
        else html.Should().Contain("Viewport pattern step angle").And.Contain("value=\"45\"");
    }
}
