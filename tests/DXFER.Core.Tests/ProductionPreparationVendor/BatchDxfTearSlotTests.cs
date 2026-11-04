using System.Globalization;
using System.Text;
using FluentAssertions;
using HMISync.Web.Production;

namespace HMISync.Tests.Production;

public sealed class BatchDxfTearSlotTests
{
    private const string Tear = "SHEETMETAL_FORM_TEAR";
    internal static string Line(string layer, double x, double y, double xx, double yy) =>
        FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x}\n20\n{y}\n11\n{xx}\n21\n{yy}\n");
    internal static string Arc(string layer, double x, double y, double start, double end) =>
        FormattableString.Invariant($"0\nARC\n8\n{layer}\n10\n{x}\n20\n{y}\n40\n0.02\n50\n{start}\n51\n{end}\n");
    private static string Outline => Line("SHEETMETAL_CUT_LINES", -5, -5, 10, -5);
    private static byte[] Dxf(string entities) => BatchDxfLayerTests.Dxf(Outline + entities);

    [Fact]
    public void GeneratedEdgesHaveUniqueHandlesAboveExistingRecordsAndInheritOwner()
    {
        var tear = Line(Tear, 0, 0, 3, 0).Replace("0\nLINE\n", "0\nLINE\n5\nFFFF\n330\nAB\n");
        var original = Dxf(tear + Line(Tear, 0, 3, 3, 3));
        var result = BatchDxfLayers.Prepare(original, []);
        var edges = Entities(result.Bytes).Skip(1).ToArray();
        edges.Should().HaveCount(8);
        edges.Select(e => e[5]).Should().OnlyHaveUniqueItems();
        edges.Select(e => ulong.Parse(e[5], NumberStyles.HexNumber)).Should().OnlyContain(h => h > 0xFFFF);
        edges.Take(4).Should().OnlyContain(e => e[330] == "AB");
        BatchDxfLayers.Prepare(original, []).Bytes.Should().Equal(result.Bytes);
    }

    [Fact]
    public void ExhaustedHandlesHoldInsteadOfColliding()
    {
        var tear = Line(Tear, 0, 0, 3, 0).Replace("0\nLINE\n", "0\nLINE\n5\nFFFFFFFFFFFFFFFF\n");
        Action prepare = () => BatchDxfLayers.Prepare(Dxf(tear), []);
        prepare.Should().Throw<InvalidDataException>().WithMessage("*handle range*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(235)]
    public void CenteredSlotsAreClosedRoundEndedAndExactlyFortyThouWide(double degrees)
    {
        var angle = degrees * Math.PI / 180;
        var dx = Math.Cos(angle); var dy = Math.Sin(angle);
        var original = Dxf(Line(Tear, 0, 0, dx * 3.942, dy * 3.942) + Line("SHEETMETAL_FORM_UP", 0, 0, 1, 1) + Line("SHEETMETAL_FORM_CENTERMARK", 0, 0, 1, 1));
        var result = BatchDxfLayers.Prepare(original, []);
        result.TearSlotsCreated.Should().Be(1);
        result.ExistingTearSlots.Should().Be(0);
        var entities = Entities(result.Bytes);
        entities.Should().HaveCount(5);
        entities.Should().OnlyContain(e => e[8] == "SHEETMETAL_CUT_LINES");
        var lines = entities.Skip(1).Where(e => e[0] == "LINE").ToArray();
        lines.Should().HaveCount(2);
        foreach (var line in lines)
        {
            var x = N(line, 10); var y = N(line, 20);
            Math.Abs(-dy * x + dx * y).Should().BeApproximately(.02, 1e-10);
            Math.Sqrt(Math.Pow(N(line, 11) - x, 2) + Math.Pow(N(line, 21) - y, 2)).Should().BeApproximately(3.942, 1e-10);
        }
        var points = new List<(double x, double y)>();
        foreach (var e in entities.Skip(1))
        {
            if (e[0] == "LINE") { points.Add((N(e, 10), N(e, 20))); points.Add((N(e, 11), N(e, 21))); }
            else
            {
                N(e, 40).Should().Be(.02);
                ((N(e, 51) - N(e, 50) + 360) % 360).Should().BeApproximately(180, 1e-10);
                foreach (var code in new[] { 50, 51 })
                    points.Add((N(e, 10) + .02 * Math.Cos(N(e, code) * Math.PI / 180), N(e, 20) + .02 * Math.Sin(N(e, code) * Math.PI / 180)));
            }
        }
        foreach (var p in points) points.Count(q => Math.Abs(q.x - p.x) < 1e-10 && Math.Abs(q.y - p.y) < 1e-10).Should().Be(2);
        BatchDxfLayers.Prepare(result.Bytes, []).Bytes.Should().Equal(result.Bytes);
        Encoding.ASCII.GetString(original).Should().Contain(Tear);
    }

    [Fact]
    public void ExistingOneSidedManualSlotIsUnchanged_AndItsTearEdgeCannotBeStripped()
    {
        var original = Dxf(Line(Tear, 0, 0, 3, 0) + Line("0", 0, -.04, 3, -.04) + Arc("0", 0, -.02, 90, 270) + Arc("0", 3, -.02, 270, 90));
        var result = BatchDxfLayers.Prepare(original, []);
        result.Bytes.Should().Equal(original);
        result.ExistingTearSlots.Should().Be(1);
        result.TearSlotsCreated.Should().Be(0);
        result.Layers.Single(l => l.Name == Tear).RequiresReview.Should().BeFalse();
        Action strip = () => BatchDxfLayers.Prepare(original, [Tear]);
        strip.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("arc")]
    [InlineData("joined")]
    [InlineData("duplicate")]
    [InlineData("partial")]
    [InlineData("wrong-cap")]
    [InlineData("crossing")]
    [InlineData("zero")]
    [InlineData("nonplanar")]
    [InlineData("nan")]
    [InlineData("polyline")]
    public void AmbiguousOrUnsafeTearsHoldThePart(string scenario)
    {
        var tear = Line(Tear, 0, 0, 3, 0);
        var entities = scenario switch
        {
            "arc" => Arc(Tear, 0, 0, 0, 180),
            "joined" => tear + Line(Tear, 3, 0, 3, 2),
            "duplicate" => tear + Line(Tear, 3, 0, 0, 0),
            "partial" => tear + Line("0", 0, -.04, 3, -.04),
            "wrong-cap" => tear + Line("0", 0, -.04, 3, -.04) + Arc("0", 0, -.02, 270, 90) + Arc("0", 3, -.02, 270, 90),
            "crossing" => tear + Line("0", 1, -1, 1, 1),
            "zero" => Line(Tear, 0, 0, 0, 0),
            "nonplanar" => tear + "30\n1\n",
            "nan" => tear.Replace("10\n0", "10\nNaN"),
            _ => tear + "0\nLWPOLYLINE\n8\n0\n90\n0\n"
        };
        Action prepare = () => BatchDxfLayers.Prepare(Dxf(entities), []);
        prepare.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExplicitMillimetersAreRejected_UnitlessAndInchesAccepted()
    {
        foreach (var unit in new[] { 0, 1, 4 })
        {
            var bytes = Encoding.ASCII.GetBytes($"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n{unit}\n0\nENDSEC\n" + Encoding.ASCII.GetString(Dxf(Line(Tear, 0, 0, 3, 0))));
            if (unit == 4) { Action action = () => BatchDxfLayers.Prepare(bytes, []); action.Should().Throw<InvalidDataException>(); }
            else BatchDxfLayers.Prepare(bytes, []).TearSlotsCreated.Should().Be(1);
        }
    }

    [Theory]
    [InlineData("SHEETMETAL_FORM_UP")]
    [InlineData("SHEETMETAL_FORM_DOWN")]
    [InlineData("SHEETMETAL_FORM_CENTERMARK")]
    public void FormLayersAreAutomaticallyRemovedEvenWithoutTears(string layer)
    {
        var original = Dxf(Line(layer, 0, 0, 1, 1));
        var result = BatchDxfLayers.Prepare(original, []);
        result.Bytes.Should().Equal(Dxf(""));
        result.TearSlotsCreated.Should().Be(0);
        result.Layers.Single(l => l.Name == layer).Should().Be(new BatchDxfLayer(layer, 1, true, false, true));
        Encoding.ASCII.GetString(original).Should().Contain(layer);
    }

    [Fact]
    public void WrongSideParallelEdgeFromManuallyPreparedFlatIsHeld_NotDoubledOrRepaired()
    {
        var original = Dxf(Line(Tear, 5.759696105, 18.954, 5.759696105, 22.896) +
            Line("0", 5.799696105, 18.954, 5.799696105, 22.896) +
            Arc("0", 5.739696105, 18.954, 180, 360) + Arc("0", 5.739696105, 22.896, 360, 180));
        Action action = () => BatchDxfLayers.Prepare(original, []);
        action.Should().Throw<InvalidDataException>().WithMessage("*partially prepared*");
    }

    [Fact]
    public void ExistingSlotEdgesOnAnOptionalLayerCannotBeRemovedLeavingAnOpenCut()
    {
        var source = Dxf(Line(Tear, 0, 0, 3, 0) + Line("COUNTERSINK", 0, -.04, 3, -.04) + Arc("0", 0, -.02, 90, 270) + Arc("0", 3, -.02, 270, 90));
        Action action = () => BatchDxfLayers.Prepare(source, ["COUNTERSINK"]);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AllRecognizedBendAliasesAreRemovedBeforeSlotProximityChecks()
    {
        var source = Dxf(Line(Tear, 0, 0, 3, 0) + Line("BEND_DOWN", 0, 0, 3, 0));
        var result = BatchDxfLayers.Prepare(source, []);
        result.TearSlotsCreated.Should().Be(1);
        Encoding.ASCII.GetString(result.Bytes).Should().NotContain("BEND_DOWN");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SplineEnvelopeMustBeProvablyDisjoint_AndBytesArePreserved(bool nearby, bool negativeWeight)
    {
        var x = nearby ? 1 : 10;
        var spline = $"0\nSPLINE\n8\nSHEETMETAL_CUT_LINES\n71\n2\n72\n6\n73\n3\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n10\n{x}\n20\n-1\n10\n{x + 1}\n20\n0\n10\n{x + 2}\n20\n1\n" +
            (negativeWeight ? "41\n-1\n41\n1\n41\n1\n" : "");
        var source = Dxf(Line(Tear, 0, 0, 3, 0) + spline);
        if (nearby || negativeWeight) { Action action = () => BatchDxfLayers.Prepare(source, []); action.Should().Throw<InvalidDataException>(); }
        else
        {
            var result = BatchDxfLayers.Prepare(source, []);
            result.TearSlotsCreated.Should().Be(1);
            Encoding.ASCII.GetString(result.Bytes).Should().Contain(spline);
        }
    }

    private static double N(Dictionary<int, string> entity, int code) => double.Parse(entity[code], CultureInfo.InvariantCulture);
    private static List<Dictionary<int, string>> Entities(byte[] bytes)
    {
        var records = Encoding.ASCII.GetString(bytes).Split('\n');
        var result = new List<Dictionary<int, string>>();
        Dictionary<int, string>? current = null;
        for (var i = 0; i + 1 < records.Length; i += 2)
        {
            var code = int.Parse(records[i], CultureInfo.InvariantCulture); var value = records[i + 1];
            if (code == 0)
            {
                current = value is "LINE" or "ARC" ? new() : null;
                if (current != null) result.Add(current);
            }
            if (code != 100) current?.Add(code, value);
        }
        return result;
    }
}
