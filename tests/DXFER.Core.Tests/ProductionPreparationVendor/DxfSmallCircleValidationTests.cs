using System.Text;
using FluentAssertions;
using HMISync.Web.Infrastructure;

namespace HMISync.Tests.Engineering;

public sealed class DxfSmallCircleValidationTests
{
    private const string Circle = "0\nCIRCLE\n5\nA\n8\nCUT\n10\n1\n20\n2\n40\n.01\n";
    private const string Line = "0\nLINE\n5\nB\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n";
    private static byte[] Dxf(string entities, string? units = null) => Encoding.ASCII.GetBytes(
        (units is null ? "" : "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n" + units + "0\nENDSEC\n") +
        "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n");

    [Theory]
    [InlineData(10, "NaN")]
    [InlineData(20, "NaN")]
    [InlineData(10, "Infinity")]
    [InlineData(20, "-Infinity")]
    [InlineData(10, "1e999")]
    [InlineData(20, "invalid")]
    [InlineData(30, "5")]
    [InlineData(30, "NaN")]
    [InlineData(39, ".001")]
    [InlineData(39, "Infinity")]
    [InlineData(210, "1")]
    [InlineData(220, "1")]
    [InlineData(230, "-1")]
    [InlineData(230, "NaN")]
    public void InvalidCandidateCannotBeHiddenByRemoval(int code, string value)
    {
        var circle = code is 10 or 20
            ? Circle.Replace($"{code}\n{(code == 10 ? 1 : 2)}\n", $"{code}\n{value}\n")
            : Circle + $"{code}\n{value}\n";
        RefusesWithoutChangingSource(Dxf(Circle.Replace("5\nA", "5\nC") + circle + Line));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void MissingCenterCoordinateIsRefused(int code) =>
        RefusesWithoutChangingSource(Dxf(Circle.Replace($"{code}\n{(code == 10 ? 1 : 2)}\n", "") + Line));

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(39)]
    [InlineData(210)]
    [InlineData(220)]
    [InlineData(230)]
    public void DuplicateCandidateCoordinatesAreRefusedEvenWhenEqual(int code)
    {
        var value = code is 10 or 230 ? 1 : code == 20 ? 2 : 0;
        var fields = $"{code}\n{value}\n";
        RefusesWithoutChangingSource(Dxf(Circle + (code is 10 or 20 ? fields : fields + fields) + Line));
    }

    [Theory]
    [InlineData("70\n4\n")]
    [InlineData("70\n2\n")]
    [InlineData("70\nNaN\n")]
    [InlineData("70\n1.0\n")]
    [InlineData("1\n1\n")]
    [InlineData("")]
    [InlineData("70\n1\n70\n1\n")]
    [InlineData("70\n1\n9\n$INSUNITS\n70\n1\n")]
    public void UnsupportedOrAmbiguousUnitsAreRefusedBeforeFiltering(string units)
    {
        RefusesWithoutChangingSource(Dxf(Circle + Line, units));
        RefusesWithoutChangingSource(Dxf(Line, units));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("70\n0\n")]
    [InlineData("70\n1\n")]
    public void InchAndUnitlessPlanarCandidatesStillCleanLosslessly(string? units)
    {
        var source = Dxf(Circle + "30\n0\n39\n0\n210\n0\n220\n0\n230\n1\n" + Line, units);
        var original = source.ToArray();
        var result = DxfSmallCircleFilter.Apply(source);
        result.RemovedCount.Should().Be(1);
        result.Bytes.Should().Equal(Dxf(Line, units));
        source.Should().Equal(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedInvalidCirclesStayVisibleToDownstreamValidation(bool native)
    {
        var circle = Circle.Replace("10\n1", "10\nNaN") + "30\n5\n";
        var source = Dxf(circle + Line + (native ? "999\nDXFER_NATIVE_V1\n" : "340\nA\n"));
        var result = DxfSmallCircleFilter.Apply(source);
        result.RemovedCount.Should().Be(0);
        result.ProtectedCount.Should().Be(1);
        result.Bytes.Should().Equal(source);
    }

    private static void RefusesWithoutChangingSource(byte[] source)
    {
        var original = source.ToArray();
        Action apply = () => DxfSmallCircleFilter.Apply(source);
        apply.Should().Throw<InvalidDataException>();
        source.Should().Equal(original);
    }
}
