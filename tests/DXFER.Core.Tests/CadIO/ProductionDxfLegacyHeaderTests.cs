using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.CadIO;

public sealed class ProductionDxfLegacyHeaderTests
{
    private const string Version = "9\n$ACADVER\n1\nAC1014\n";
    private static string Dxf(string key, string payload) =>
        "0\nSECTION\n2\nHEADER\n" + Version + "0\nENDSEC\n"
        + "0\nSECTION\n2\nENTITIES\n0\nLINE\n5\nA\n8\nCUT\n10\n0\n20\n0\n11\n8\n21\n4\n0\nENDSEC\n"
        + "0\nSECTION\n2\nOBJECTS\n0\nDICTIONARY\n5\nB\n330\n0\n100\nAcDbDictionary\n3\nAcDbHeaderRoundTripXrec\n350\nC\n"
        + "0\nDICTIONARY\n5\nC\n330\nB\n100\nAcDbDictionary\n3\n" + key + "\n350\nD\n"
        + "0\nXRECORD\n5\nD\n330\nC\n100\nAcDbXrecord\n" + payload + "0\nENDSEC\n0\nEOF\n";

    [Theory]
    [InlineData("CEPSNID", "340\n0\n")]
    [InlineData("CEPSNTYPE", "90\n0\n")]
    [InlineData("FINGERPRINTGUID", "1\nfixture-id\n")]
    [InlineData("VERSIONGUID", "1\nfixture-version\n")]
    [InlineData("HYPERLINKBASE", "1\n\n")]
    [InlineData("STYLESHEET", "1\n\n")]
    [InlineData("INSUNITS", "90\n1\n")]
    [InlineData("INSUNITS", "90\n0\n")]
    [InlineData("LAYOUTDICT", "340\n0\n")]
    [InlineData("PLOTSETDICT", "340\n0\n")]
    [InlineData("LWETCUNION", "90\n10781\n")]
    [InlineData("PSVPSCALE", "40\n0\n")]
    [InlineData("TSTACKALIGN", "90\n1\n")]
    [InlineData("TSTACKSIZE", "90\n70\n")]
    [InlineData("UCSBASE", "340\n0\n")]
    [InlineData("PUCSBASE", "340\n0\n")]
    [InlineData("UCSORTHOREF", "340\n0\n")]
    [InlineData("PUCSORTHOREF", "340\n0\n")]
    [InlineData("UCSORTHOVIEW", "70\n0\n")]
    [InlineData("PUCSORTHOVIEW", "70\n0\n")]
    [InlineData("UCSORGBACK", "10\n1\n20\n2\n30\n3\n")]
    [InlineData("PUCSORGTOP", "10\n0\n20\n0\n30\n0\n")]
    [InlineData("DIMSTYLEDATA", "1000\nDSTYLE\n1002\n{\n1070\n340\n340\n0\n1002\n}\n")]
    public void KnownLegacyHeaderMetadataIsPreservedWithoutChangingGeometry(string key, string payload)
    {
        var source = Dxf(key, payload);
        var result = ProductionDxfPreparation.Prepare(source);
        result.Document.Entities.Should().ContainSingle();
        result.Document.GetBounds().Width.Should().Be(8);
        result.Document.GetBounds().Height.Should().Be(4);
        result.PreparedText.Should().Contain("3\n" + key + "\n").And.Contain(payload);
        source.Should().Be(Dxf(key, payload));
        ProductionDxfPreparation.Prepare(source.Replace(key, key.ToLowerInvariant())).Document.Entities.Should().ContainSingle();
    }

    [Theory]
    [InlineData("INSUNITS", "90\n4\n")]
    [InlineData("INSUNITS", "90\n2\n")]
    [InlineData("INSUNITS", "90\ninvalid\n")]
    [InlineData("INSUNITS", "90\n1\n90\n4\n")]
    [InlineData("INSUNITS", "70\n1\n")]
    [InlineData("UNKNOWN_CONSTRAINT", "90\n1\n")]
    [InlineData("CEPSNID", "340\nA\n")]
    [InlineData("LAYOUTDICT", "340\nDEAD\n")]
    [InlineData("FINGERPRINTGUID", "1005\nA\n")]
    [InlineData("HYPERLINKBASE", "310\nABCD\n")]
    [InlineData("PSVPSCALE", "40\nNaN\n")]
    [InlineData("UCSORGBACK", "10\n0\n20\nNaN\n30\n0\n")]
    [InlineData("UCSORGBACK", "10\n0\n20\n0\n")]
    [InlineData("DIMSTYLEDATA", "1000\nDSTYLE\n1002\n{\n1070\n340\n340\nA\n1002\n}\n")]
    [InlineData("DIMSTYLEDATA", "1000\nDSTYLE\n1002\n{\n1070\n340\n340\n0\n1002\n}\n1005\nA\n")]
    public void UnknownUnsafeOrNonInchHeaderMetadataIsNotDiscarded(string key, string payload) => Refused(Dxf(key, payload));

    [Fact]
    public void LegacyVersionMustBeUniqueAndInHeader()
    {
        var source = Dxf("INSUNITS", "90\n1\n");
        Refused(source.Replace(Version, ""));
        Refused(source.Replace(Version, Version + Version));
        Refused(source.Replace("1\nAC1014", "70\nAC1014"));
        Refused(source.Replace(Version, "").Replace("8\nCUT\n", "8\nCUT\n" + Version));
        Refused(source.Replace(Version, "").Replace("3\nINSUNITS\n", "3\nINSUNITS\n" + Version));
        Refused(source.Replace("AC1014", "AC1015"));
        Refused(source.Replace("0\nSECTION\n2\nENTITIES", "0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nSECTION\n2\nENTITIES"));
    }

    private static void Refused(string source)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }
}
