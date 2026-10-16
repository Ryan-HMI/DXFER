using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using DXFER.Blazor.Components;

namespace DXFER.Core.Tests.Components;

public sealed class CadIconJoinTrimTests
{
    [Fact]
    public void CoincidentHasOrthogonalCrosshairsAndAHeavyFilledCenter()
    {
        var icon = ConstraintIconGeometry.All["coincident"];
        icon.Outline.Should().Be("M3 12h18M12 3v18");
        icon.Fill.Should().Be("M14.8 12a2.8 2.8 0 1 0-5.6 0a2.8 2.8 0 1 0 5.6 0Z");
    }

    [Fact]
    public void MidpointIsOneEquilateralTriangleWithoutAnExtraLineOrDots()
    {
        var icon = ConstraintIconGeometry.All["midpoint"];
        icon.Fill.Should().BeNull();
        icon.Outline.Should().Be("M12 4.205771366 21 19.794228634H3Z");
        var height = 19.794228634 - 4.205771366;
        Math.Sqrt(9 * 9 + height * height).Should().BeApproximately(18, 1e-8);
        ConstraintIconGeometry.All.Keys.Should().BeEquivalentTo(new[]
        {
            "coincident", "concentric", "parallel", "tangent", "horizontal", "vertical",
            "perpendicular", "equal", "midpoint", "fix"
        });
    }

    [Fact]
    public void PowerTrimShowsRetainedEndsAtTwoCrossingsAndDeletionInsideTheGap()
    {
        var icon = Artwork("PowerTrim");
        var lines = icon.Elements("line").ToArray();
        var boundaries = lines.Where(l => Number(l, "x1") == Number(l, "x2")).OrderBy(l => Number(l, "x1")).ToArray();
        var retained = lines.Where(l => Number(l, "y1") == Number(l, "y2")).OrderBy(l => Number(l, "x1")).ToArray();
        boundaries.Should().HaveCount(2);
        retained.Should().HaveCount(2);
        var left = Number(boundaries[0], "x1");
        var right = Number(boundaries[1], "x1");
        var y = Number(retained[0], "y1");
        Number(retained[0], "x2").Should().Be(left);
        Number(retained[1], "x1").Should().Be(right);
        Number(retained[1], "y1").Should().Be(y);
        foreach (var boundary in boundaries)
        {
            Number(boundary, "y1").Should().BeLessThan(y);
            Number(boundary, "y2").Should().BeGreaterThan(y);
        }
        var deletion = lines.Except(boundaries).Except(retained).ToArray();
        deletion.Should().HaveCount(2);
        foreach (var line in deletion)
        {
            Number(line, "x1").Should().BeInRange(left + 2, right - 2);
            Number(line, "x2").Should().BeInRange(left + 2, right - 2);
            ((Number(line, "y1") + Number(line, "y2")) / 2).Should().Be(y);
        }
        Number(deletion[0], "x1").Should().Be(Number(deletion[1], "x2"));
        Number(deletion[0], "x2").Should().Be(Number(deletion[1], "x1"));
        icon.Descendants("circle").Should().BeEmpty("no scissors handles");
    }

    [Theory]
    [InlineData("PowerTrim", "trim")]
    public void RuntimeStandaloneAndSpriteMatchMasterAndFitTheGlyph(string name, string asset)
    {
        const string root = "docs/design/visual-overhaul-20261014/";
        var masters = JsonSerializer.Deserialize<Dictionary<string, string>>(Read(root + "icons.json"))!;
        var master = XElement.Parse("<svg>" + masters[asset] + "</svg>");
        XNode.DeepEquals(Artwork(name), master).Should().BeTrue();
        var standalone = XElement.Parse(Read(root + "icons/" + asset + ".svg"));
        var sprite = XElement.Parse(Read(root + "dxfer-icons.svg")).Elements()
            .Single(e => (string?)e.Attribute("id") == "dxfer-" + asset);
        foreach (var representation in new[] { standalone, sprite })
        {
            foreach (var node in representation.Descendants()) node.Name = node.Name.LocalName;
            XNode.DeepEquals(new XElement("svg", representation.Elements()), master).Should().BeTrue();
            ((string?)representation.Attribute("viewBox")).Should().Be("0 0 24 24");
        }
        foreach (var line in master.Elements("line"))
            foreach (var coordinate in new[] { "x1", "x2", "y1", "y2" })
                Number(line, coordinate).Should().BeInRange(2, 22);
        foreach (var circle in master.Elements("circle"))
            foreach (var axis in new[] { "cx", "cy" })
            {
                (Number(circle, axis) - Number(circle, "r")).Should().BeGreaterThanOrEqualTo(2);
                (Number(circle, axis) + Number(circle, "r")).Should().BeLessThanOrEqualTo(22);
            }
    }

    [Fact]
    public void CompactSizesAndExistingAccessibleCommandNamesAreUnchanged()
    {
        var icon = Read("src/DXFER.Blazor/Components/CadIcon.razor");
        icon.Should().Contain("width=\"20\"").And.Contain("height=\"20\"")
            .And.Contain("stroke-width=\"1.6\"").And.Contain("aria-hidden=\"true\"");
        var palette = Read("src/DXFER.Blazor/Components/WorkbenchToolPalette.razor.css");
        Regex.Match(palette, @"\.dxfer-tool-group ::deep \.dxfer-icon-button\s*\{[^}]*\}").Value
            .Should().Contain("width: 2rem;").And.Contain("height: 2rem;");
        Regex.Match(palette, @"\.dxfer-command-strip ::deep \.dxfer-icon\s*\{[^}]*\}").Value
            .Should().Contain("width: 1.25rem;").And.Contain("height: 1.25rem;");
        var commands = Read("src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs");
        commands.Should().Contain("Command(WorkbenchCommandId.Coincident, WorkbenchTool.Coincident, CadIconName.Coincident, \"Coincident\"")
            .And.Contain("Command(WorkbenchCommandId.PowerTrim, WorkbenchTool.PowerTrim, CadIconName.PowerTrim, \"Power trim/extend\"");
    }

    private static XElement Artwork(string name)
    {
        var match = Regex.Match(Read("src/DXFER.Blazor/Components/CadIcon.razor"),
            $@"case CadIconName\.{name}:\s*([\s\S]*?)\s*break;");
        match.Success.Should().BeTrue();
        return XElement.Parse("<svg>" + match.Groups[1].Value + "</svg>");
    }

    private static double Number(XElement element, string attribute) =>
        double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);

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
