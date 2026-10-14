using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class CadIconHandoffTests
{
    [Theory]
    [InlineData("NewFile", "new")]
    [InlineData("FolderOpen", "open")]
    [InlineData("Export", "download")]
    [InlineData("Hotkeys", "keyboard")]
    [InlineData("ChevronDown", "chevron-down")]
    [InlineData("Measure", "measure")]
    [InlineData("Undo", "undo")]
    [InlineData("Redo", "redo")]
    [InlineData("Line", "line")]
    [InlineData("MidpointLine", "midpoint-line")]
    [InlineData("Rectangle", "rectangle")]
    [InlineData("CenterRectangle", "center-rectangle")]
    [InlineData("AlignedRectangle", "aligned-rectangle")]
    [InlineData("Circle", "circle")]
    [InlineData("Ellipse", "ellipse")]
    [InlineData("EllipticalArc", "elliptical-arc")]
    [InlineData("Conic", "conic")]
    [InlineData("Spline", "spline")]
    [InlineData("SplineControlPoint", "spline-point")]
    [InlineData("Point", "point")]
    [InlineData("Delete", "delete")]
    [InlineData("Slot", "slot")]
    [InlineData("PowerTrim", "trim")]
    [InlineData("Offset", "offset")]
    [InlineData("Fillet", "fillet")]
    [InlineData("Chamfer", "chamfer")]
    [InlineData("Dimension", "dimension")]
    [InlineData("Move", "move")]
    [InlineData("Mirror", "mirror")]
    [InlineData("Coincident", "coincident")]
    [InlineData("Concentric", "concentric")]
    [InlineData("Parallel", "parallel")]
    [InlineData("Tangent", "tangent")]
    [InlineData("Horizontal", "horizontal")]
    [InlineData("Vertical", "vertical")]
    [InlineData("Perpendicular", "perpendicular")]
    [InlineData("Equal", "equal")]
    [InlineData("Midpoint", "midpoint")]
    [InlineData("Normal", "normal")]
    [InlineData("Symmetric", "symmetric")]
    [InlineData("Fix", "fix")]
    [InlineData("Fit", "fit")]
    [InlineData("VectorToX", "align-x")]
    [InlineData("VectorToY", "align-y")]
    [InlineData("RemoveDuplicates", "remove-duplicates")]
    public void IntegratedArtworkMatchesReviewedMaster(string name, string asset)
    {
        var source = Read("src/DXFER.Blazor/Components/CadIcon.razor");
        var match = Regex.Match(source, $@"case CadIconName\.{name}:\s*([\s\S]*?)\s*break;");
        match.Success.Should().BeTrue();
        var masters = JsonSerializer.Deserialize<Dictionary<string, string>>(
            Read("docs/design/visual-overhaul-20261014/icons.json"))!;
        var actual = XElement.Parse("<svg>" + match.Groups[1].Value + "</svg>");
        var expected = XElement.Parse("<svg>" + masters[asset] + "</svg>");
        XNode.DeepEquals(actual, expected).Should().BeTrue();
    }

    [Fact]
    public void EveryExistingIconRetainsExactlyOneRenderingAndAccessibleButtonContract()
    {
        var source = Read("src/DXFER.Blazor/Components/CadIcon.razor");
        foreach (var name in Enum.GetNames<CadIconName>())
        {
            Regex.Matches(source, $@"case CadIconName\.{name}:").Count.Should().Be(1);
        }

        source.Should().Contain("aria-hidden=\"true\"").And.Contain("focusable=\"false\"");
        var button = Read("src/DXFER.Blazor/Components/CadIconButton.razor");
        button.Should().Contain("aria-label=\"@Label\"").And.Contain("title=\"@TitleText\"")
            .And.Contain("@onclick=\"OnClick\"").And.Contain("disabled=\"@Disabled\"");
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }
}
