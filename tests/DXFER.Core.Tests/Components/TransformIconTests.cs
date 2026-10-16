using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class TransformIconTests
{
    [Fact]
    public void RotateHasOneLargeCircularArcAndOnePronouncedFilledArrowhead()
    {
        var paths = Artwork("Rotate").Descendants("path").ToArray();
        paths.Should().HaveCount(2);
        paths[0].Attribute("d")!.Value.Should().Be("M18.92820323 8A8 8 0 1 1 12 4");
        paths[0].Attribute("stroke-width")!.Value.Should().Be("2");
        paths[1].Attribute("fill")!.Value.Should().Be("currentColor");
        paths[1].Attribute("d")!.Value.Should().Be("M16.5 4 11 1 11 7Z");
    }

    [Fact]
    public void ScaleSquaresShareALowerLeftCornerAndOnlyTheSmallerIsFilled()
    {
        var icon = Artwork("Scale");
        var squares = icon.Elements("rect").ToArray();
        squares.Should().HaveCount(2);
        icon.Elements("path").Should().BeEmpty();
        var small = squares.Single(s => (string?)s.Attribute("fill") == "currentColor");
        var large = squares.Single(s => s.Attribute("fill") is null);
        Number(small, "x").Should().Be(Number(large, "x"));
        (Number(small, "y") + Number(small, "height")).Should().Be(Number(large, "y") + Number(large, "height"));
        Number(small, "width").Should().BeLessThan(Number(large, "width"));
        foreach (var square in squares) Number(square, "width").Should().Be(Number(square, "height"));
    }

    private static double Number(XElement element, string attribute) => double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);

    private static XElement Artwork(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src/DXFER.Blazor/Components/CadIcon.razor");
            if (!File.Exists(path)) continue;
            var match = Regex.Match(File.ReadAllText(path), $@"case CadIconName\.{name}:\s*([\s\S]*?)\s*break;");
            match.Success.Should().BeTrue();
            return XElement.Parse("<svg>" + match.Groups[1].Value + "</svg>");
        }
        throw new FileNotFoundException("CadIcon.razor");
    }
}
