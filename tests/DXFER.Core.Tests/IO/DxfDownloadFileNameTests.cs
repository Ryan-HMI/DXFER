using DXFER.Blazor.IO;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDownloadFileNameTests
{
    [Theory]
    [InlineData("flat-pattern.dxf", "flat-pattern.dxf")]
    [InlineData("customer-artifact.dwg", "customer-artifact.dxf")]
    [InlineData("part name", "part name.dxf")]
    [InlineData("", "drawing.dxf")]
    [InlineData("C:\\temp\\unsafe:name.dxf", "unsafe-name.dxf")]
    public void CreatesSafeDxfDownloadName(string input, string expected)
    {
        DxfDownloadFileName.FromSourceName(input).Should().Be(expected);
    }
}
