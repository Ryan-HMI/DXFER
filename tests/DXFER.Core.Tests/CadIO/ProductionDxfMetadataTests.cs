using DXFER.CadIO;
using DXFER.Core.Documents;
using FluentAssertions;

namespace DXFER.Core.Tests.CadIO;

public sealed class ProductionDxfMetadataTests
{
    private const string Extension = "102\n{ACAD_XDICTIONARY\n360\nB\n102\n}\n";
    private const string Line = "0\nLINE\n5\nA\n" + Extension + "8\nCUT\n10\n0\n20\n0\n11\n6\n21\n6\n";
    private const string Dictionary = "0\nDICTIONARY\n5\nB\n330\nA\n100\nAcDbDictionary\n280\n1\n281\n1\n3\nbtDetId\n360\nC\n";
    private const string Leaf = "0\nXRECORD\n5\nC\n102\n{ACAD_REACTORS\n330\nB\n102\n}\n330\nB\n100\nAcDbXrecord\n280\n1\n1000\npart-detail\n";
    private const string Font = "0\nTABLE\n2\nSTYLE\n5\n10\n70\n1\n0\nSTYLE\n5\n11\n330\n10\n100\nAcDbSymbolTableRecord\n100\nAcDbTextStyleTableRecord\n2\nStandard\n1001\nACAD\n1000\nVerdana\n1071\n34\n0\nENDTAB\n";
    private static string Dxf(string entities = Line, string objects = Dictionary + Leaf, string tables = "") =>
        "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n"
        + (tables.Length == 0 ? "" : "0\nSECTION\n2\nTABLES\n" + tables + "0\nENDSEC\n")
        + "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n"
        + "0\nSECTION\n2\nOBJECTS\n" + objects + "0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void ScalarExtensionOwnershipAndFontMetadataDoNotBlockGeometry()
    {
        var source = Dxf(tables: Font);
        var prepared = ProductionDxfPreparation.Prepare(source);
        prepared.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        prepared.Document.GetBounds().Width.Should().Be(6);
        prepared.Document.GetBounds().Height.Should().Be(6);
        prepared.PreparedText.Should().Contain(Font).And.NotContain("ACAD_XDICTIONARY").And.NotContain("btDetId");
        prepared.Document.Metadata.Warnings.Should().Contain(w => w.Code == "production-metadata-removed");
        source.Should().Be(Dxf(tables: Font));
        ProductionDxfPreparation.Prepare(prepared.PreparedText).PreparedText.Should().Be(prepared.PreparedText);
    }

    [Theory]
    [InlineData("btDetId")]
    [InlineData("btPartId")]
    [InlineData("btUnflattenedPartId")]
    [InlineData("btOwnerFaceId_")]
    public void ScalarIdsAreNotInterpretedAsDxfHandles(string key)
    {
        var source = Dxf(objects: Dictionary.Replace("btDetId", key) + Leaf.Replace("part-detail", "A"));
        ProductionDxfPreparation.Prepare(source).Document.Entities.Should().ContainSingle();
    }

    [Fact]
    public void ArcRadiusMetadataMustAgreeWithGeometry()
    {
        var arc = "0\nARC\n5\nA\n" + Extension + "8\nCUT\n10\n0\n20\n0\n40\n1\n50\n0\n51\n90\n";
        var objects = Dictionary.Replace("btDetId", "btArcOnSurfaceRadius") + Leaf.Replace("1000\npart-detail", "40\n1.0");
        ProductionDxfPreparation.Prepare(Dxf(arc, objects)).Document.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>();
        Refused(Dxf(arc, objects.Replace("40\n1.0", "40\n2.0")));
        Refused(Dxf(Line, objects));
    }

    [Fact]
    public void MetadataDoesNotProtectSmallHolesOrLouverTearsFromCleanup()
    {
        var retained = "0\nLINE\n5\nD\n8\nCUT\n10\n-5\n20\n-5\n11\n10\n21\n-5\n";
        var circle = "0\nCIRCLE\n5\nA\n" + Extension + "8\nCUT\n10\n8\n20\n8\n40\n0.01\n";
        var small = ProductionDxfPreparation.Prepare(Dxf(retained + circle));
        small.Cleanup.SmallCirclesRemoved.Should().Be(1);
        small.Document.Entities.Should().ContainSingle();
        small.PreparedText.Should().NotContain("btDetId");

        var tear = Line.Replace("8\nCUT", "8\nSHEETMETAL_FORM_TEAR").Replace("11\n6\n21\n6", "11\n3\n21\n0");
        var louver = ProductionDxfPreparation.Prepare(Dxf(retained + tear));
        louver.Cleanup.TearSlotsCreated.Should().Be(1);
        louver.Document.Entities.OfType<ArcEntity>().Should().HaveCount(2);
    }

    [Fact]
    public void SourceGeometryAndNativeIntentAreValidatedBeforeMetadataCanBeDiscarded()
    {
        Refused(Dxf(Line.Replace("20\n0", "20\nNaN")));
        foreach (var source in new[] { Dxf(objects: Dictionary + Leaf.Replace("part-detail", "DXFER_NATIVE_V1")),
            Dxf(tables: Font.Replace("Verdana", "DXFER_NATIVE_V1")) })
        {
            Action prepare = () => ProductionDxfPreparation.Prepare(source);
            prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*native DXFER constraints*");
        }
    }

    [Theory]
    [InlineData("1005\nA\n")]
    [InlineData("340\nA\n")]
    [InlineData("330\nA\n")]
    [InlineData("10\n1\n20\n2\n")]
    [InlineData("310\nABCD\n")]
    public void TypedReferencesAndCoordinateOrOpaquePayloadsAreNotScalarMetadata(string payload) =>
        Refused(Dxf(objects: Dictionary + Leaf.Replace("1000\npart-detail\n", payload)));

    [Theory]
    [InlineData("missing")]
    [InlineData("owner")]
    [InlineData("reactor")]
    [InlineData("duplicate-handle")]
    [InlineData("duplicate-key")]
    [InlineData("cycle")]
    [InlineData("unknown-key")]
    [InlineData("extra-payload")]
    [InlineData("dangling-external")]
    [InlineData("external-leaf-reference")]
    [InlineData("hidden-header-reference")]
    [InlineData("shared-leaf")]
    [InlineData("missing-close")]
    public void AmbiguousOrUnknownExtensionGraphsFailClosed(string failure)
    {
        var source = failure switch
        {
            "missing" => Dxf(objects: Dictionary),
            "owner" => Dxf(objects: Dictionary.Replace("330\nA", "330\nD") + Leaf),
            "reactor" => Dxf(objects: Dictionary + Leaf.Replace("{ACAD_REACTORS\n330\nB", "{ACAD_REACTORS\n330\nA")),
            "duplicate-handle" => Dxf(objects: Dictionary + Leaf + Leaf),
            "duplicate-key" => Dxf(objects: Dictionary + "3\nbtDetId\n360\nC\n" + Leaf),
            "cycle" => Dxf(objects: Dictionary.Replace("360\nC", "360\nB") + Leaf),
            "unknown-key" => Dxf(objects: Dictionary.Replace("btDetId", "ConstraintExpression") + Leaf),
            "extra-payload" => Dxf(objects: Dictionary + Leaf + "1000\nanother\n"),
            "dangling-external" => Dxf(Line + "340\nDEAD\n"),
            "hidden-header-reference" => Dxf(objects: Dictionary + Leaf.Replace("100\nAcDbXrecord", "340\nA\n100\nAcDbXrecord")),
            "shared-leaf" => Dxf(objects: Dictionary + "3\nbtPartId\n360\nC\n" + Leaf),
            "missing-close" => Dxf(Line.Replace("360\nB\n102\n}\n", "360\nB\n")),
            _ => Dxf(Line + "340\nC\n")
        };
        Refused(source);
    }

    [Theory]
    [InlineData("102\n{ACAD_REACTORS\n330\nA\n102\n}\n")]
    [InlineData("1001\nAPP\n1005\nA\n")]
    [InlineData("340\nA\n")]
    [InlineData("330\nA\n")]
    public void RetainedEntityAssociationsStillBlockImport(string association) =>
        Refused(Dxf(Line + "0\nLINE\n5\nD\n" + association + "10\n0\n20\n1\n11\n6\n21\n1\n"));

    [Fact]
    public void UnrecognizedObjectsAndFontApplicationDataRemainBlocked()
    {
        Refused(Dxf(objects: Dictionary + Leaf + "0\nACDBASSOCNETWORK\n5\nD\n330\n0\n"));
        Refused(Dxf(tables: Font.Replace("1001\nACAD", "1001\nUNKNOWN")));
        Refused(Dxf(tables: Font.Replace("1071\n34", "1005\nA")));
    }

    [Fact]
    public void PlainLayoutStyleAndOwnedRoundtripDataArePreservedInWorkingCopy()
    {
        var tables = "0\nTABLE\n2\nVPORT\n5\n20\n102\n{ACAD_XDICTIONARY\n360\n21\n102\n}\n330\n0\n100\nAcDbSymbolTable\n70\n0\n0\nENDTAB\n";
        var background = "0\nDICTIONARY\n5\n21\n330\n20\n100\nAcDbDictionary\n3\nACAD_XREC_ROUNDTRIP\n360\n22\n"
            + "0\nXRECORD\n5\n22\n102\n{ACAD_REACTORS\n330\n21\n102\n}\n330\n21\n100\nAcDbXrecord\n280\n1\n102\nVTR_DEFAULTLIGHTING\n70\n1\n340\n0\n"
            + "0\nDICTIONARY\n5\n30\n330\n0\n100\nAcDbDictionary\n3\nACAD_LAYOUT\n350\n31\n"
            + "0\nDICTIONARY\n5\n31\n102\n{ACAD_REACTORS\n330\n30\n102\n}\n330\n30\n100\nAcDbDictionary\n3\nModel\n350\n32\n"
            + "0\nLAYOUT\n5\n32\n330\n31\n100\nAcDbPlotSettings\n1\n\n100\nAcDbLayout\n1\nModel\n";
        var prepared = ProductionDxfPreparation.Prepare(Dxf(objects: Dictionary + Leaf + background, tables: Font + tables));
        prepared.Document.Entities.Should().ContainSingle();
        prepared.PreparedText.Should().Contain(background).And.Contain(Font);
        Refused(Dxf(objects: Dictionary + Leaf + background.Replace("340\n0", "340\nA"), tables: Font + tables));
        Refused(Dxf(objects: Dictionary + Leaf + background.Replace("ACAD_XREC_ROUNDTRIP", "UNKNOWN_CONSTRAINT"), tables: Font + tables));
        Refused(Dxf(objects: Dictionary + Leaf + background.Replace("ACAD_LAYOUT", "ACAD_ASSOCNETWORK"), tables: Font + tables));
    }

    [Fact]
    public void GraphRemovalPreservesUntouchedBytesAndInputBuffer()
    {
        var source = System.Text.Encoding.UTF8.GetBytes(Dxf(tables: Font).Replace("\n", "\r\n"));
        var original = source.ToArray();
        var pairs = HMISync.Web.Infrastructure.DxfCompatibility.ReadPairs(source, default);
        var result = ProductionDxfMetadata.PrepareWorkingCopy(source, pairs);
        source.Should().Equal(original);
        System.Text.Encoding.UTF8.GetString(result).Should().Contain(Font.Replace("\n", "\r\n"));
        System.Text.Encoding.UTF8.GetString(result).Should().Contain("10\r\n0\r\n20\r\n0\r\n11\r\n6\r\n21\r\n6\r\n");
    }

    private static void Refused(string source)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }
}
