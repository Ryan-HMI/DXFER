using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class DimensionValueFormatterTests
{
    [Theory]
    [InlineData(12, "12")]
    [InlineData(12.3, "12.3")]
    [InlineData(12.3456, "12.346")]
    [InlineData(12.0004, "12")]
    [InlineData(-0.0004, "0")]
    public void FormatTrimsTrailingZerosAtDefaultPrecision(double value, string expected)
    {
        DimensionValueFormatter.Format(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(12.3456, 2, "12.35")]
    [InlineData(12.3001, 2, "12.3")]
    [InlineData(12.5, 0, "13")]
    public void FormatUsesConfiguredPrecision(double value, int precision, string expected)
    {
        DimensionValueFormatter.Format(value, precision).Should().Be(expected);
    }
}
