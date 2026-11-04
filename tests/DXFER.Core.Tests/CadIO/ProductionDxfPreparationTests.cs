using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.CadIO;

public sealed class ProductionDxfPreparationTests
{
    internal static string Dxf(string entities, string header = "") =>
        $"0\nSECTION\n2\nHEADER\n{header}0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
    internal static string Line(string layer, double x, double y, double endX, double endY) =>
        FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x}\n20\n{y}\n11\n{endX}\n21\n{endY}\n");
    private static string Circle(double radius) =>
        FormattableString.Invariant($"0\nCIRCLE\n8\nCUT\n10\n8\n20\n8\n40\n{radius}\n");
    internal static string LouverFixture => Dxf(
        Line("SHEETMETAL_CUT_LINES", -5, -5, 10, -5) +
        Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) +
        Line("SHEETMETAL_FORM_UP", 0, 0, 3, 1) +
        Line("BEND", 0, 2, 3, 2) + Circle(.0249) + Circle(.025));

    [Fact]
    public void SharedPipelineConvertsLouverRemovesBendsAndSmallHolesButKeepsCutoff()
    {
        var prepared = ProductionDxfPreparation.Prepare(LouverFixture);
        prepared.Cleanup.SmallCirclesRemoved.Should().Be(1);
        prepared.Cleanup.TearSlotsCreated.Should().Be(1);
        prepared.Document.Entities.Should().HaveCount(6);
        prepared.Document.Entities.OfType<ArcEntity>().Should().HaveCount(2)
            .And.OnlyContain(arc => arc.Radius == .02);
        prepared.Document.Entities.OfType<CircleEntity>().Single().Radius.Should().Be(.025);
        prepared.Document.Metadata.EntityStyles.Values.Should().OnlyContain(style => style.LayerName == "CUT");
        prepared.Document.Metadata.Units.Should().Be(DrawingUnits.Inches);
        LouverFixture.Should().Contain("SHEETMETAL_FORM_TEAR");
    }

    [Fact]
    public void PreparationAndNormalizedDownloadAreIdempotent()
    {
        var first = ProductionDxfPreparation.Prepare(LouverFixture);
        var second = ProductionDxfPreparation.Prepare(first.PreparedText);
        second.PreparedText.Should().Be(first.PreparedText);
        second.Cleanup.TearSlotsCreated.Should().Be(0);
        second.Cleanup.SmallCirclesRemoved.Should().Be(0);
        var document = DrawingNormalizationService.AutoNormalize(first.Document).NormalizedDocument;
        var download = DxfDocumentWriter.Write(document);
        var reopened = ProductionDxfPreparation.Prepare(download);
        reopened.Document.Entities.Should().HaveCount(document.Entities.Count);
        reopened.Document.GetBounds().Width.Should().BeApproximately(document.GetBounds().Width, 1e-6);
        reopened.Document.GetBounds().Height.Should().BeApproximately(document.GetBounds().Height, 1e-6);
        reopened.Document.Entities.OfType<ArcEntity>().Should().OnlyContain(arc => arc.Radius == .02);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InchAndLegacyUnspecifiedSourcesUseEstablishedThreshold(int units) =>
        ProductionDxfPreparation.Prepare(Dxf(Line("0", 0, 0, 2, 2) + Circle(.01), $"9\n$INSUNITS\n70\n{units}\n"))
            .Cleanup.SmallCirclesRemoved.Should().Be(1);

    [Theory]
    [InlineData("9\n$INSUNITS\n70\n4\n")]
    [InlineData("9\n$INSUNITS\n70\n2\n")]
    [InlineData("9\n$INSUNITS\n70\n1\n9\n$INSUNITS\n70\n1\n")]
    [InlineData("9\n$INSUNITS\n70\ngarbage\n")]
    public void NonInchOrAmbiguousUnitsCannotBeStampedAsInches(string header)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(Dxf(Line("0", 0, 0, 2, 2), header));
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*units*review*");
    }

    [Theory]
    [InlineData("0\nLWPOLYLINE\n8\nCUT\n90\n2\n10\n0\n20\n0\n42\n1\n10\n1\n20\n1\n")]
    [InlineData("0\nLINE\n8\nCUT\n67\n1\n10\n0\n20\n0\n11\n2\n21\n2\n")]
    [InlineData("0\nLINE\n8\nCUT\n30\n2\n10\n0\n20\n0\n11\n2\n21\n2\n")]
    [InlineData("0\nDIMENSION\n8\nCUT\n")]
    public void UnsafeGeometryRefusesRatherThanChangingShape(string entities)
    {
        Action prepare = () => ProductionDxfPreparation.Prepare(Dxf(entities));
        prepare.Should().Throw<ProductionDxfPreparationException>();
    }

    [Fact]
    public void NativeIntentAndAmbiguousTearsRefuse()
    {
        Action native = () => ProductionDxfPreparation.Prepare(LouverFixture.Replace("2\nHEADER\n", "2\nHEADER\n999\nDXFER_NATIVE_V1\n"));
        native.Should().Throw<ProductionDxfPreparationException>().WithMessage("*constraints*");
        Action touching = () => ProductionDxfPreparation.Prepare(Dxf(Line("CUT", -5, -5, 10, -5)
            + Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) + Line("CUT", 1, -1, 1, 1)));
        touching.Should().Throw<ProductionDxfPreparationException>().WithMessage("*ambiguous louver*");
    }

    [Theory]
    [InlineData("102\n{ACAD_REACTORS\n330\nAB\n102\n}\n")]
    [InlineData("1001\nAPP\n1005\nAB\n")]
    [InlineData("340\nAB\n")]
    public void RetainedReferencesAreNotLostBeforeDuplicateOrSmallHoleCleanup(string reference)
    {
        var circle = Circle(.01).Replace("0\nCIRCLE\n", "0\nCIRCLE\n5\nAB\n");
        var source = Dxf(Line("CUT", 0, 0, 3, 0) + reference + circle);
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }

    [Theory]
    [InlineData("10\nNaN\n20\n0\n")]
    [InlineData("10\n0\n20\n0\n30\n5\n")]
    [InlineData("10\n0\n20\n0\n10\n100\n")]
    public void DeletingSmallGeometryCannotHideInvalidSourceCoordinates(string coordinates)
    {
        var source = Dxf(Line("CUT", 0, 0, 4, 2) + "0\nCIRCLE\n8\nCUT\n" + coordinates + "40\n0.01\n");
        Action prepare = () => ProductionDxfPreparation.Prepare(source);
        prepare.Should().Throw<ProductionDxfPreparationException>().WithMessage("*Original file unchanged*");
    }

    [Fact]
    public void ValidSplineSurvivesLouverPreparationAndExportWithoutResampling()
    {
        var spline = "0\nSPLINE\n8\nCUT\n70\n12\n71\n2\n72\n6\n73\n3\n74\n0\n"
            + "40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n"
            + "41\n1\n41\n0.5\n41\n1\n10\n20\n20\n20\n10\n21\n20\n22\n10\n22\n20\n20\n";
        var prepared = ProductionDxfPreparation.Prepare(Dxf(Line("CUT", -5, -5, 10, -5)
            + Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) + spline));
        prepared.PreparedText.Should().Contain(spline);
        var source = prepared.Document.Entities.OfType<SplineEntity>().Single();
        var reopened = ProductionDxfPreparation.Prepare(DxfDocumentWriter.Write(prepared.Document));
        var curve = reopened.Document.Entities.OfType<SplineEntity>().Single();
        curve.Degree.Should().Be(source.Degree);
        curve.ControlPoints.Should().Equal(source.ControlPoints);
        curve.Knots.Should().Equal(source.Knots);
        curve.Weights.Should().Equal(source.Weights);
        curve.FitPoints.Should().Equal(source.FitPoints);
    }

    [Fact]
    public void PolylineAlongsideTearKeepsExistingCypNestReviewGate()
    {
        var polyline = "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n-5\n20\n-5\n10\n10\n20\n-5\n10\n10\n20\n10\n";
        Action prepare = () => ProductionDxfPreparation.Prepare(Dxf(polyline + Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0)));
        prepare.Should().Throw<ProductionDxfPreparationException>();
        var withoutTear = ProductionDxfPreparation.Prepare(Dxf(polyline));
        var reopened = ProductionDxfPreparation.Prepare(DxfDocumentWriter.Write(withoutTear.Document));
        reopened.Document.Entities.OfType<PolylineEntity>().Single().Vertices.Should()
            .Equal(withoutTear.Document.Entities.OfType<PolylineEntity>().Single().Vertices);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LocalAndSyncImportsUseSameCleanupAndRefusalIsTransactional(bool sync, bool normalize)
    {
        var component = new DrawingWorkbench();
        var method = typeof(DrawingWorkbench).GetMethod("LoadDxfText", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(component, new object[] { "original.dxf", LouverFixture, sync, normalize }).Should().Be(true);
        var documentField = typeof(DrawingWorkbench).GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var original = (DrawingDocument)documentField.GetValue(component)!;
        original.Entities.Should().HaveCount(6);
        var invalid = Dxf(Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) + Line("CUT", 1, -1, 1, 1));
        Action load = () => method.Invoke(component, new object[] { "refused.dxf", invalid, sync, normalize });
        load.Should().Throw<TargetInvocationException>().WithInnerException<ProductionDxfPreparationException>();
        documentField.GetValue(component).Should().BeSameAs(original);
        typeof(DrawingWorkbench).GetField("_fileName", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component).Should().Be("original.dxf");
    }
}
