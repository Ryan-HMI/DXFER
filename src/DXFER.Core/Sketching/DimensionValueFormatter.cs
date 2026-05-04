using System.Globalization;

namespace DXFER.Core.Sketching;

public static class DimensionValueFormatter
{
    public static string Format(double value, int precision = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);

        var rounded = Math.Round(value, precision, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            return "0";
        }

        return rounded
            .ToString($"F{precision}", CultureInfo.InvariantCulture)
            .TrimEnd('0')
            .TrimEnd('.');
    }
}
