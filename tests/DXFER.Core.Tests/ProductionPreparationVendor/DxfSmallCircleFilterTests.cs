using System.Text;
using FluentAssertions;
using HMISync.Web.Infrastructure;

namespace HMISync.Tests.Engineering;

public sealed class DxfSmallCircleFilterTests
{
    private static string Circle(string radius, string handle = "A") => $"0\nCIRCLE\n5\n{handle}\n8\nCUT\n10\n1\n20\n1\n40\n{radius}\n";
    private static byte[] Dxf(string entities) => Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n");
    private const string Line = "0\nLINE\n5\nB\n8\nCUT\n10\n0\n20\n0\n11\n0.001\n21\n0\n";

    [Theory]
    [InlineData("0.024999999999999", 1)]
    [InlineData(".025", 0)]
    [InlineData("2.5e-2", 0)]
    [InlineData("0.025000000000001", 0)]
    [InlineData("0.000001", 1)]
    [InlineData("0", 0)]
    [InlineData("-0.01", 0)]
    [InlineData("NaN", 0)]
    public void StrictDiameterBoundary(string radius, int removed)
    {
        var source = Dxf(Circle(radius) + Line);
        var result = DxfSmallCircleFilter.Apply(source);
        result.RemovedCount.Should().Be(removed);
        result.Bytes.Should().Equal(removed == 1 ? Dxf(Line) : source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsOtherEntitiesDuplicatesLayersAndRawFormatting(bool crlf)
    {
        var others = Line + Line + Circle(".001", "C").Replace("CIRCLE", "ARC") +
            "0\nSPLINE\n5\nD\n8\nOTHER\n40\n.001\n0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n.001\n20\n0\n0\nELLIPSE\n40\n.001\n";
        var source = Dxf(Circle(".01") + others);
        var expected = Dxf(others);
        if (crlf)
        {
            source = Encoding.UTF8.GetBytes("\uFEFF" + Encoding.UTF8.GetString(source).Replace("\n", "\r\n"));
            expected = Encoding.UTF8.GetBytes("\uFEFF" + Encoding.UTF8.GetString(expected).Replace("\n", "\r\n"));
        }
        var result = DxfSmallCircleFilter.Apply(source);
        result.RemovedCount.Should().Be(1);
        result.Bytes.Should().Equal(expected);
        DxfSmallCircleFilter.Apply(result.Bytes).RemovedCount.Should().Be(0);
    }

    [Fact]
    public void DoesNotBreakReferencedCirclesNativeSnapshotsOrBlockAndPaperGeometry()
    {
        var reference = Dxf(Circle(".01") + Line + "340\nA\n");
        var native = Dxf(Circle(".01") + "999\nDXFER_NATIVE_V1\n");
        foreach (var source in new[] { reference, native })
        {
            var result = DxfSmallCircleFilter.Apply(source);
            result.ProtectedCount.Should().Be(1);
            result.Bytes.Should().Equal(source);
        }
        var paper = Dxf(Circle(".01") + "67\n1\n");
        DxfSmallCircleFilter.Apply(paper).Bytes.Should().Equal(paper);
        var block = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Dxf(Circle(".01"))).Replace("ENTITIES", "BLOCKS"));
        DxfSmallCircleFilter.Apply(block).Bytes.Should().Equal(block);
    }

    [Fact]
    public void IncompleteOrBinaryInputCannotBePartiallyCleaned()
    {
        var incomplete = Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n" + Circle(".01"));
        Action act = () => DxfSmallCircleFilter.Apply(incomplete);
        act.Should().Throw<InvalidDataException>();
        act = () => DxfSmallCircleFilter.Apply("AutoCAD Binary DXF\0"u8.ToArray());
        act.Should().Throw<InvalidDataException>();
    }
}
