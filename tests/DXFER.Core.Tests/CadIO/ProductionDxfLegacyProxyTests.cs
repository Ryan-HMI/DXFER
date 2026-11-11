using System.Text;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;
using HMISync.Web.Infrastructure;

namespace DXFER.Core.Tests.CadIO;

public sealed class ProductionDxfLegacyProxyTests
{
    private const string Extension = "102\n{ACAD_XDICTIONARY\n360\nB\n102\n}\n";
    private const string BendLayer = "SHEETMETAL_BEND_LINES_UP";
    private const string Bend = "0\nLINE\n5\nA\n" + Extension + "8\n" + BendLayer + "\n10\n0\n20\n0\n11\n6\n21\n6\n";
    private const string Cut = "0\nLINE\n5\nD\n8\nCUT\n10\n0\n20\n0\n11\n8\n21\n4\n";
    private const string Body = "100\nAcDbProxyObject\n90\n499\n91\n500\n93\n16\n310\n00A5\n94\n0\n95\n24\n70\n0\n";
    private static readonly string Classes = Class("XRECORD", "AcDbXrecord")
        + Class("SCALE", "AcDbScale") + Class("VISUALSTYLE", "AcDbVisualStyle")
        + Class("MLEADERSTYLE", "AcDbMLeaderStyle");

    [Theory]
    [InlineData("ACAD_SCALELIST", "Standard", 501)]
    [InlineData("ACAD_VISUALSTYLE", "Standard", 502)]
    [InlineData("ACAD_MLEADERSTYLE", "Standard", 503)]
    [InlineData("ONSHAPE_DATA", "FIRST_ANGLE", 500)]
    public void ApprovedCollectionProxiesPreserveWorkingBytesAndGeometry(string collection, string key, int classId)
    {
        foreach (var version in new[] { "AC1012", "AC1014" })
        {
            var objects = Collection(collection, key, classId);
            Preserved(Dxf(objects, version: version), objects);
        }
    }

    [Theory]
    [InlineData("BTBENDISUP", "SHEETMETAL_BEND_LINES_UP")]
    [InlineData("BTBENDISUP", "SHEETMETAL_BEND_LINES_DOWN")]
    [InlineData("BTISBEND", "SHEETMETAL_BEND_LINES_UP")]
    [InlineData("BTISBEND", "SHEETMETAL_BEND_LINES_DOWN")]
    public void OwnedBooleanProxiesDetachOnlyWithExistingBendCleanup(string key, string layer)
    {
        var source = BendSource(key).Replace(BendLayer, layer);
        var bytes = Encoding.UTF8.GetBytes(source);
        var original = bytes.ToArray();
        var working = ProductionDxfMetadata.PrepareWorkingCopy(bytes, DxfCompatibility.ReadPairs(bytes, default));
        bytes.Should().Equal(original);
        var workingText = Encoding.UTF8.GetString(working);
        workingText.Should().Contain(Bend.Replace(BendLayer, layer).Replace(Extension, "")).And.Contain(Cut);
        workingText.Should().NotContain("ACAD_PROXY_OBJECT").And.NotContain("ACAD_XDICTIONARY").And.NotContain(key);

        var prepared = ProductionDxfPreparation.Prepare(source);
        prepared.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        prepared.Document.GetBounds().Width.Should().Be(8);
        prepared.Document.GetBounds().Height.Should().Be(4);
        prepared.Cleanup.CleanupLayersRemoved.Should().Be(1);
        prepared.PreparedText.Should().NotContain("ACAD_PROXY_OBJECT").And.NotContain(layer);
        prepared.Document.Metadata.Warnings.Should().Contain(w => w.Code == "production-metadata-removed");
        ProductionDxfPreparation.Prepare(prepared.PreparedText).PreparedText.Should().Be(prepared.PreparedText);
    }

    [Theory]
    [InlineData("93\n15\n310\n00A5\n", "")]
    [InlineData("93\n16\n310\n00\n310\nA5\n", "94\n0\n")]
    [InlineData("93\n16\n310\n00a5\n", "94\n0\n95\n4294967295\n70\n1\n")]
    public void EnvelopeAllowsOptionalFieldsAndCountsAllBinaryChunks(string data, string optionalFields)
    {
        var body = "100\nAcDbProxyObject\n90\n499\n91\n501\n" + data + optionalFields;
        var objects = Collection("ACAD_SCALELIST", "Standard", 501, body);
        Preserved(Dxf(objects), objects);
    }

    [Theory]
    [InlineData("ACAD_VISUALSTYLE", 502, "1001\nACAD\n1000\nAcDbSavedByObjectVersion\n1070\n1\n")]
    [InlineData("ACAD_MLEADERSTYLE", 503, "1001\nACAD_MLEADERVER\n1070\n1\n")]
    public void OnlyTheResolvedClassStandardXdataIsPreserved(string collection, int classId, string xdata)
    {
        var objects = Collection(collection, "Standard", classId, ProxyBody(classId) + xdata);
        Preserved(Dxf(objects), objects);
        Refused(Dxf(objects.Replace("1001\nACAD", "1001\nUNKNOWN")));
        Refused(Dxf(objects.Replace("1070\n1", "1005\nD")));
        Refused(Dxf(objects.Replace("1070\n1", "1070\n40000")));
    }

    [Theory]
    [InlineData("AC1015")]
    [InlineData("AC1032")]
    [InlineData(null)]
    public void ProxyCompatibilityRequiresAnExplicitSupportedLegacyVersion(string? version)
    {
        Refused(Dxf(Collection("ACAD_SCALELIST", "Standard", 501), version: version));
        Refused(BendSource(version: version));
    }

    [Theory]
    [InlineData("missing-classes")]
    [InlineData("wrong-dxf-name")]
    [InlineData("wrong-cpp-name")]
    [InlineData("entity-class")]
    [InlineData("missing-entity-flag")]
    [InlineData("duplicate-entity-flag")]
    [InlineData("duplicate-name")]
    [InlineData("wrong-index")]
    [InlineData("class-outside-classes")]
    public void ClassIdentityMustComeFromAnUnambiguousObjectClassAtTheDeclaredIndex(string failure)
    {
        var classes = failure switch
        {
            "missing-classes" => "",
            "wrong-dxf-name" => Classes.Replace("1\nSCALE\n", "1\nUNKNOWN\n"),
            "wrong-cpp-name" => Classes.Replace("2\nAcDbScale\n", "2\nAcDbAssocNetwork\n"),
            "entity-class" => Classes.Replace("281\n0\n", "281\n1\n"),
            "missing-entity-flag" => Classes.Replace("281\n0\n", ""),
            "duplicate-entity-flag" => Classes.Replace("281\n0\n", "281\n0\n281\n0\n"),
            "duplicate-name" => Classes.Replace("1\nSCALE\n", "1\nSCALE\n1\nSCALE\n"),
            "wrong-index" => Class("SCALE", "AcDbScale") + Class("XRECORD", "AcDbXrecord"),
            _ => Classes
        };
        var source = Dxf(Collection("ACAD_SCALELIST", "Standard", 501), classes: classes);
        if (failure == "class-outside-classes") source = source.Replace("2\nCLASSES\n", "2\nUNKNOWN\n");
        Refused(source);
    }

    [Theory]
    [InlineData("DWGPROPS", "", 500)]
    [InlineData("FIRST_ANGLE", "", 500)]
    [InlineData("ONSHAPE_DATA", "DIM_UNITS", 500)]
    [InlineData("ONSHAPE_DATA", "DWGGEN_VERSION", 500)]
    [InlineData("ONSHAPE_DATA", "UNKNOWN", 500)]
    [InlineData("ONSHAPE_DATA", "FIRST_ANGLE", 501)]
    [InlineData("ACAD_SCALELIST", "Standard", 500)]
    [InlineData("ACAD_SCALELIST", "Standard", 502)]
    [InlineData("ACAD_VISUALSTYLE", "Standard", 503)]
    [InlineData("ACAD_ASSOCNETWORK", "Standard", 501)]
    public void ARecognizedClassDoesNotAuthorizeOtherDictionaryRoles(string collection, string key, int classId)
    {
        var objects = key.Length == 0
            ? Dictionary("10", "0", collection, "12") + Proxy("12", "10", classId)
            : Collection(collection, key, classId);
        Refused(Dxf(objects));
    }

    [Theory]
    [InlineData("BTDETID")]
    [InlineData("BTPARTID")]
    [InlineData("BTBENDANGLE")]
    [InlineData("BTBENDRADIUS")]
    [InlineData("FIRST_ANGLE")]
    [InlineData("UnknownConstraint")]
    public void OpaqueXrecordProxiesAreNotGeneralScalarMetadata(string key) => Refused(BendSource(key));

    [Theory]
    [InlineData("cut-layer")]
    [InlineData("near-match-layer")]
    [InlineData("duplicate-layer")]
    [InlineData("not-a-line")]
    [InlineData("wrong-class")]
    [InlineData("proxy-entity")]
    public void BooleanProxyExceptionDoesNotExtendToRetainedGeometryOrOtherTypes(string failure)
    {
        var source = failure switch
        {
            "cut-layer" => BendSource().Replace(BendLayer, "CUT"),
            "near-match-layer" => BendSource().Replace(BendLayer, BendLayer + "_OTHER"),
            "duplicate-layer" => BendSource().Replace("8\n" + BendLayer, "8\nCUT\n8\n" + BendLayer),
            "not-a-line" => BendSource().Replace(Bend, "0\nCIRCLE\n5\nA\n" + Extension + "8\n" + BendLayer + "\n10\n0\n20\n0\n40\n1\n"),
            "wrong-class" => BendSource(body: ProxyBody(501)),
            _ => BendSource().Replace("ACAD_PROXY_OBJECT", "ACAD_PROXY_ENTITY")
                .Replace("AcDbProxyObject", "AcDbProxyEntity").Replace("90\n499", "90\n498")
        };
        Refused(source);
    }

    [Theory]
    [InlineData("missing-subclass")]
    [InlineData("wrong-subclass")]
    [InlineData("duplicate-subclass")]
    [InlineData("wrong-proxy-id")]
    [InlineData("duplicate-proxy-id")]
    [InlineData("missing-class-id")]
    [InlineData("unknown-class-id")]
    [InlineData("duplicate-class-id")]
    [InlineData("missing-length")]
    [InlineData("duplicate-length")]
    [InlineData("negative-length")]
    [InlineData("length-mismatch")]
    [InlineData("insufficient-bytes")]
    [InlineData("empty-data")]
    [InlineData("odd-hex")]
    [InlineData("invalid-hex")]
    [InlineData("missing-data")]
    [InlineData("extra-data")]
    [InlineData("wrong-binary-code")]
    [InlineData("invalid-end-marker")]
    [InlineData("negative-drawing-format")]
    [InlineData("overflow-drawing-format")]
    [InlineData("invalid-data-format")]
    [InlineData("unexpected-graphics")]
    [InlineData("unexpected-coordinate")]
    [InlineData("unexpected-xdata")]
    public void MalformedOrUnrecognizedEnvelopeFieldsFailClosedForPreservedAndDetachedProxies(string failure)
    {
        var body = failure switch
        {
            "missing-subclass" => Body.Replace("100\nAcDbProxyObject\n", ""),
            "wrong-subclass" => Body.Replace("AcDbProxyObject", "AcDbXrecord"),
            "duplicate-subclass" => Body + "100\nAcDbProxyObject\n",
            "wrong-proxy-id" => Body.Replace("90\n499", "90\n498"),
            "duplicate-proxy-id" => Body.Replace("90\n499", "90\n499\n90\n499"),
            "missing-class-id" => Body.Replace("91\n500\n", ""),
            "unknown-class-id" => Body.Replace("91\n500", "91\n999"),
            "duplicate-class-id" => Body.Replace("91\n500", "91\n500\n91\n500"),
            "missing-length" => Body.Replace("93\n16\n", ""),
            "duplicate-length" => Body.Replace("93\n16", "93\n16\n93\n16"),
            "negative-length" => Body.Replace("93\n16", "93\n-8"),
            "length-mismatch" => Body.Replace("93\n16", "93\n8"),
            "insufficient-bytes" => Body.Replace("93\n16", "93\n17"),
            "empty-data" => Body.Replace("93\n16\n310\n00A5\n", "93\n0\n"),
            "odd-hex" => Body.Replace("00A5", "00A"),
            "invalid-hex" => Body.Replace("00A5", "00G5"),
            "missing-data" => Body.Replace("310\n00A5\n", ""),
            "extra-data" => Body.Replace("310\n00A5\n", "310\n00A5\n310\nFF\n"),
            "wrong-binary-code" => Body.Replace("310\n", "311\n"),
            "invalid-end-marker" => Body.Replace("94\n0", "94\n1"),
            "negative-drawing-format" => Body.Replace("95\n24", "95\n-1"),
            "overflow-drawing-format" => Body.Replace("95\n24", "95\n4294967296"),
            "invalid-data-format" => Body.Replace("70\n0", "70\n2"),
            "unexpected-graphics" => Body + "92\n0\n",
            "unexpected-coordinate" => Body + "10\n1\n20\n2\n",
            _ => Body + "1001\nUNKNOWN\n1070\n1\n"
        };
        Refused(BendSource(body: body));
        Refused(Dxf(Collection("ONSHAPE_DATA", "FIRST_ANGLE", 500, body)));
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-dictionary-owner")]
    [InlineData("wrong-reactor")]
    [InlineData("missing-proxy")]
    [InlineData("duplicate-handle")]
    [InlineData("shared-leaf")]
    [InlineData("inbound-leaf")]
    [InlineData("inbound-dictionary")]
    public void DetachmentStillRequiresExclusiveAndConsistentOwnership(string failure)
    {
        var source = failure switch
        {
            "wrong-owner" => BendSource().Replace("}\n330\nB\n100\nAcDbProxyObject", "}\n330\nD\n100\nAcDbProxyObject"),
            "wrong-dictionary-owner" => BendSource().Replace("5\nB\n330\nA", "5\nB\n330\nD"),
            "wrong-reactor" => BendSource().Replace("{ACAD_REACTORS\n330\nB", "{ACAD_REACTORS\n330\nD"),
            "missing-proxy" => BendSource().Replace(Proxy("C", "B", 500), ""),
            "duplicate-handle" => BendSource().Replace(Proxy("C", "B", 500), Proxy("C", "B", 500) + Proxy("C", "B", 500)),
            "shared-leaf" => BendSource().Replace("3\nBTISBEND\n360\nC\n", "3\nBTISBEND\n360\nC\n3\nBTBENDISUP\n360\nC\n"),
            "inbound-leaf" => BendSource().Replace("5\nD\n", "5\nD\n330\nC\n"),
            _ => BendSource().Replace("5\nD\n", "5\nD\n330\nB\n")
        };
        Refused(source);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-reactor")]
    [InlineData("shared-leaf")]
    [InlineData("orphan")]
    public void PreservedCollectionProxiesStillRequireValidOwnership(string failure)
    {
        var objects = Collection("ACAD_SCALELIST", "Standard", 501);
        objects = failure switch
        {
            "wrong-owner" => objects.Replace("}\n330\n11\n100\nAcDbProxyObject", "}\n330\n10\n100\nAcDbProxyObject"),
            "wrong-reactor" => objects.Replace("{ACAD_REACTORS\n330\n11", "{ACAD_REACTORS\n330\n10"),
            "shared-leaf" => objects.Replace("3\nStandard\n360\n12\n", "3\nStandard\n360\n12\n3\nAnother\n360\n12\n"),
            _ => objects + Proxy("13", "11", 501)
        };
        Refused(Dxf(objects));
    }

    [Theory]
    [InlineData("330\nD\n")]
    [InlineData("340\nD\n")]
    [InlineData("350\nD\n")]
    [InlineData("360\nD\n")]
    [InlineData("340\nDEAD\n")]
    public void ActualProxyReferencesCannotBeHiddenByPreservationOrRemoval(string reference)
    {
        var body = Body.Replace("94\n0\n", reference + "94\n0\n");
        Refused(BendSource(body: body));
        Refused(Dxf(Collection("ONSHAPE_DATA", "FIRST_ANGLE", 500, body)));
    }

    [Fact]
    public void RetainedMetadataCannotReferenceADetachedBooleanProxy()
    {
        var inbound = Dictionary("E", "0", "DWGPROPS", "F")
            + "0\nXRECORD\n5\nF\n330\nE\n100\nAcDbXrecord\n280\n1\n340\nC\n";
        Refused(Dxf(Dictionary("B", "A", "BTISBEND", "C") + Proxy("C", "B", 500) + inbound, Bend + Cut));
    }

    [Fact]
    public void ProxyCompatibilityDoesNotSuppressBranchedContourReview()
    {
        var branches = "0\nLINE\n5\nE\n8\nCUT\n10\n0\n20\n0\n11\n4\n21\n8\n"
            + "0\nLINE\n5\nF\n8\nCUT\n10\n0\n20\n0\n11\n8\n21\n0\n";
        var baseline = DxfDocumentReader.Read(Dxf("", Cut + branches));
        var expected = ClosedContourService.Calculate(baseline);
        expected.ClosedContourCount.Should().BeNull();
        expected.ContourReviewReason.Should().Contain("Branched");
        var objects = Dictionary("B", "A", "BTISBEND", "C") + Proxy("C", "B", 500)
            + Collection("ACAD_SCALELIST", "Standard", 501);

        var prepared = ProductionDxfPreparation.Prepare(Dxf(objects, Bend + Cut + branches));

        prepared.Document.Entities.Should().HaveCount(3);
        prepared.Document.GetBounds().Should().Be(baseline.GetBounds());
        CutPathLengthService.Calculate(prepared.Document).Should().Be(CutPathLengthService.Calculate(baseline));
        ClosedContourService.Calculate(prepared.Document).Should().Be(expected);
    }

    private static string Class(string dxfName, string cppName) =>
        $"0\nCLASS\n1\n{dxfName}\n2\n{cppName}\n3\nObjectDBX Classes\n90\n0\n280\n1\n281\n0\n";

    private static string Dictionary(string handle, string owner, string key, string child) =>
        $"0\nDICTIONARY\n5\n{handle}\n330\n{owner}\n100\nAcDbDictionary\n280\n1\n281\n1\n3\n{key}\n360\n{child}\n";

    private static string ProxyBody(int classId) => Body.Replace("91\n500\n", $"91\n{classId}\n");

    private static string Proxy(string handle, string owner, int classId, string? body = null) =>
        $"0\nACAD_PROXY_OBJECT\n5\n{handle}\n102\n{{ACAD_REACTORS\n330\n{owner}\n102\n}}\n330\n{owner}\n"
        + (body ?? ProxyBody(classId));

    private static string Collection(string collection, string key, int classId, string? body = null) =>
        Dictionary("10", "0", collection, "11") + Dictionary("11", "10", key, "12") + Proxy("12", "11", classId, body);

    private static string BendSource(string key = "BTISBEND", string? body = null, string? version = "AC1014") =>
        Dxf(Dictionary("B", "A", key, "C") + Proxy("C", "B", 500, body), Bend + Cut, version: version);

    private static string Dxf(string objects, string entities = Cut, string? classes = null, string? version = "AC1014") =>
        "0\nSECTION\n2\nHEADER\n" + (version is null ? "" : $"9\n$ACADVER\n1\n{version}\n")
        + "9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nCLASSES\n" + (classes ?? Classes)
        + "0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + entities
        + "0\nENDSEC\n0\nSECTION\n2\nOBJECTS\n" + objects + "0\nENDSEC\n0\nEOF\n";

    private static void Preserved(string source, string objects)
    {
        var bytes = Encoding.UTF8.GetBytes(source.Replace("\n", "\r\n"));
        var original = bytes.ToArray();
        var working = ProductionDxfMetadata.PrepareWorkingCopy(bytes, DxfCompatibility.ReadPairs(bytes, default));
        bytes.Should().Equal(original);
        working.Should().Equal(original);
        var prepared = ProductionDxfPreparation.Prepare(source);
        prepared.Document.Entities.Should().Equal(DxfDocumentReader.Read(source).Entities);
        prepared.PreparedText.Should().Contain(objects);
        ProductionDxfPreparation.Prepare(prepared.PreparedText).PreparedText.Should().Be(prepared.PreparedText);
    }

    private static void Refused(string source)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }
}
