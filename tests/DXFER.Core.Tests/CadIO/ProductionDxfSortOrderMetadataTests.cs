using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.CadIO;

public sealed class ProductionDxfSortOrderMetadataTests
{
    private const string Entries = "331\nA\n5\nD\n331\nD\n5\nA\n";
    private const string SortHeader = "0\nSORTENTSTABLE\n5\n21\n102\n{ACAD_REACTORS\n330\n20\n102\n}\n330\n20\n100\nAcDbSortentsTable\n330\n1F\n";
    private const string Line = "0\nLINE\n5\nA\n330\n1F\n8\nCUT\n10\n0\n20\n0\n11\n6\n21\n6\n";
    private static string Dxf(string entries = Entries, string radius = "1", string extraEntities = "") =>
        "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n"
        + "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nBLOCK_RECORD\n5\n1\n70\n1\n"
        + "0\nBLOCK_RECORD\n5\n1F\n102\n{ACAD_XDICTIONARY\n360\n20\n102\n}\n330\n1\n100\nAcDbSymbolTableRecord\n100\nAcDbBlockTableRecord\n2\n*Model_Space\n"
        + "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + Line
        + $"0\nCIRCLE\n5\nD\n330\n1F\n8\nCUT\n10\n3\n20\n3\n40\n{radius}\n" + extraEntities
        + "0\nENDSEC\n0\nSECTION\n2\nOBJECTS\n0\nDICTIONARY\n5\n20\n330\n1F\n100\nAcDbDictionary\n3\nACAD_SORTENTS\n360\n21\n"
        + SortHeader + entries + "0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void PopulatedSortTableUsesHeaderIdentityAndOnlyDropsVerifiedOrderingEntries()
    {
        var source = Dxf();
        var raw = DxfDocumentReader.Read(source);
        var prepared = ProductionDxfPreparation.Prepare(source);
        prepared.Document.Entities.Should().Equal(raw.Entities);
        prepared.Document.GetBounds().Should().Be(raw.GetBounds());
        CutPathLengthService.Calculate(prepared.Document).Should().Be(CutPathLengthService.Calculate(raw));
        ClosedContourService.Calculate(prepared.Document).Should().Be(ClosedContourService.Calculate(raw));
        prepared.PreparedText.Should().Contain(SortHeader).And.NotContain(Entries);
        source.Should().Be(Dxf());
        ProductionDxfPreparation.Prepare(prepared.PreparedText).PreparedText.Should().Be(prepared.PreparedText);
    }

    [Fact]
    public void DrawOrderHandlesAreOrderingKeysNotObjectReferences()
    {
        var prepared = ProductionDxfPreparation.Prepare(Dxf("331\nA\n5\nCAFE\n331\nD\n5\nBEEF\n"));
        prepared.Document.Entities.Should().HaveCount(2);
    }

    [Fact]
    public void DrawOrderDoesNotProtectSmallHolesOrDuplicatesFromCleanup()
    {
        var small = ProductionDxfPreparation.Prepare(Dxf(radius: "0.01"));
        small.Cleanup.SmallCirclesRemoved.Should().Be(1);
        small.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        small.PreparedText.Should().NotContain(Entries);

        var duplicate = Line.Replace("5\nA", "5\nE");
        var cleaned = ProductionDxfPreparation.Prepare(Dxf(Entries + "331\nE\n5\nEE\n", extraEntities: duplicate));
        cleaned.Cleanup.ExactDuplicatesRemoved.Should().Be(1);
        ProductionDxfPreparation.Prepare(DxfDocumentWriter.Write(cleaned.Document)).Document.Entities.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("331\nMISSING\n5\nA\n")]
    [InlineData("331\n20\n5\nA\n")]
    [InlineData("331\nA\n")]
    [InlineData("5\nA\n331\nD\n")]
    [InlineData("331\nA\n105\nD\n")]
    [InlineData("340\nA\n5\nD\n")]
    [InlineData("331\nA\n5\nNOTHEX\n")]
    [InlineData("331\nA\n5\n0\n")]
    [InlineData("331\nA\n5\nA\n331\nA\n5\nD\n")]
    [InlineData("331\nA\n5\nA\n331\nD\n5\n0A\n")]
    [InlineData("331\nA\n5\nD\n1005\nA\n1000\nmetadata\n")]
    public void InvalidOrAmbiguousOrderingEntriesRemainBlocked(string entries) => Refused(Dxf(entries));

    [Theory]
    [InlineData("duplicate-header-id")]
    [InlineData("duplicate-entity-id")]
    [InlineData("duplicate-object-id")]
    [InlineData("missing-object-id")]
    [InlineData("wrong-subclass")]
    [InlineData("wrong-block")]
    [InlineData("wrong-entity-owner")]
    [InlineData("extra-body")]
    public void SortOrderSupportDoesNotRelaxIdentityOwnershipOrAssociationValidation(string failure)
    {
        var source = failure switch
        {
            "duplicate-header-id" => Dxf().Replace("SORTENTSTABLE\n5\n21", "SORTENTSTABLE\n5\n21\n5\n22"),
            "duplicate-entity-id" => Dxf().Replace("CIRCLE\n5\nD", "CIRCLE\n5\nD\n5\nE"),
            "duplicate-object-id" => Dxf().Replace("SORTENTSTABLE\n5\n21", "SORTENTSTABLE\n5\nA"),
            "missing-object-id" => Dxf().Replace("SORTENTSTABLE\n5\n21\n", "SORTENTSTABLE\n"),
            "wrong-subclass" => Dxf().Replace("100\nAcDbSortentsTable", "100\nAcDbAssocDependency"),
            "wrong-block" => Dxf().Replace("AcDbSortentsTable\n330\n1F", "AcDbSortentsTable\n330\n1"),
            "wrong-entity-owner" => Dxf().Replace("CIRCLE\n5\nD\n330\n1F", "CIRCLE\n5\nD\n330\n1"),
            _ => Dxf(Entries + "1000\nUNKNOWN\n")
        };
        Refused(source);
    }

    [Fact]
    public void GenuineEntityReferencesOutsideSortOrderStillRefuse()
    {
        Refused(Dxf().Replace("11\n6\n21\n6\n", "11\n6\n21\n6\n331\nD\n"));
        Refused(Dxf().Replace("CIRCLE\n5\nD\n", "CIRCLE\n5\nD\n102\n{ACAD_REACTORS\n330\nA\n102\n}\n"));
    }

    private static void Refused(string source)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }
}
