using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

public sealed class FloatingPatternCardTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task OnlyRadialCardFloatsAndLinearDirectionWidgetsKeepTheirAnchors(bool radial, bool editing)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<PatternViewportControls>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PatternViewportControls.Pattern)] = new PatternDefinition
                {
                    Id = "pattern", Kind = radial ? PatternKind.Radial : PatternKind.Rectangular,
                    CenterPointId = "center", Members = [new PatternMember("seed", 0, 1, "copy")]
                },
                [nameof(PatternViewportControls.Draft)] = editing ? new PatternDraft
                {
                    Mode = radial ? "Radial" : "Linear", Count = "6", PitchX = "1.25", PitchY = "2.75", FullCircle = true
                } : null,
                [nameof(PatternViewportControls.CanEdit)] = true,
                [nameof(PatternViewportControls.CanConfirm)] = true
            }));
            return result.ToHtmlString();
        });
        html.Should().Contain("data-center-point-id=\"center\"").And.Contain("data-member-ids=\"[&quot;copy&quot;]\"");
        if (!editing)
        {
            html.Should().NotContain("data-pattern-card").And.NotContain("<input").And.NotContain("Edit pattern");
            if (radial) html.Should().Contain("data-pattern-center-handle");
            return;
        }
        if (radial)
        {
            html.Should().Contain("data-pattern-card").And.Contain("data-pattern-card-drag")
                .And.Contain("aria-label=\"Move pattern card\"").And.Contain("pattern-card-content");
            var headerStart = html.IndexOf("data-pattern-card-drag", StringComparison.Ordinal);
            var headerEnd = html.IndexOf("</button>", headerStart, StringComparison.Ordinal);
            html[headerStart..headerEnd].Should().NotContain("<input").And.NotContain("Confirm").And.NotContain("Cancel");
            html.Should().NotContain("data-pattern-anchor=\"source\"").And.NotContain("data-pattern-anchor=\"x\"")
                .And.NotContain("data-pattern-anchor=\"y\"").And.NotContain("data-pattern-anchor=\"actions\"");
            html.Should().Contain("pattern-radial-card").And.Contain("Viewport pattern instances").And.Contain("Full Circle");
            html.IndexOf("data-pattern-center-handle", StringComparison.Ordinal).Should().BeLessThan(headerStart);
            html.Should().NotContain("Viewport pattern center X").And.NotContain("Viewport pattern center Y");
            if (editing) html.Should().Contain("aria-label=\"Confirm\"");
        }
        else
        {
            html.Should().NotContain("data-pattern-card");
            html.Should().Contain("data-pattern-anchor=\"x\"").And.Contain("data-pattern-anchor=\"y\"")
                .And.Contain("data-pattern-anchor=\"stagger\"").And.Contain("data-pattern-anchor=\"actions\"");
            html.Should().Contain("Viewport pattern U count").And.Contain("Viewport pattern V pitch")
                .And.Contain("Flip U direction").And.Contain("Pick V direction").And.Contain("Viewport stagger rows");
            html.Should().NotContain("data-pattern-center-handle");
            if (editing) html.Should().Contain("value=\"1.25\"").And.Contain("value=\"2.75\"");
        }
        if (editing) html.Should().Contain("Confirm").And.Contain("aria-label=\"Cancel pattern changes\"");
    }
}
