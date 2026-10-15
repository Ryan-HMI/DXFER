using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfEllipseExportTests
{
    [Theory]
    [InlineData(true, 0, 360)]
    [InlineData(false, 0, 360)]
    [InlineData(true, 25, 220)]
    [InlineData(false, 25, 220)]
    [InlineData(true, 300, 40)]
    [InlineData(false, 300, 40)]
    [InlineData(true, -90, 90)]
    [InlineData(false, -90, 90)]
    [InlineData(true, 30, 30)]
    [InlineData(false, 30, 30)]
    public void AxisCrossingExportsCanonicalCurveWithoutChangingNativeIntent(bool editMajor, double start, double end)
    {
        var angle = 37 * Math.PI / 180;
        var original = new EllipseEntity(EntityId.Create("ellipse"), new(7, -2),
            new(2.5 * Math.Cos(angle), 2.5 * Math.Sin(angle)), 0.6, start, end);
        var document = new DrawingDocument(new[] { original },
            new[] { Dimension("major", 5), Dimension("minor", 3) }, Array.Empty<SketchConstraint>());
        var solved = new LegacySketchSolverAdapter().Solve(new(document, Array.Empty<SketchConstraint>(),
            new[] { editMajor ? Dimension("major", 2) : Dimension("minor", 6) }));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        var ellipse = (EllipseEntity)solved.Document.Entities.Single();
        ellipse.MinorRadiusRatio.Should().BeGreaterThan(1);

        var plain = DxfDocumentReader.Read(DxfDocumentWriter.Write(solved.Document), out var plainRestored);

        plainRestored.Should().BeFalse();
        plain.Dimensions.Should().BeEmpty();
        var exported = plain.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>().Subject;
        exported.MinorRadiusRatio.Should().BeInRange(0.000001, 1);
        exported.MinorRadiusRatio.Should().BeApproximately(1 / ellipse.MinorRadiusRatio, 1e-9);
        exported.Center.Should().Be(ellipse.Center);
        exported.MajorAxisEndPoint.X.Should().BeApproximately(-ellipse.MajorAxisEndPoint.Y * ellipse.MinorRadiusRatio, 1e-9);
        exported.MajorAxisEndPoint.Y.Should().BeApproximately(ellipse.MajorAxisEndPoint.X * ellipse.MinorRadiusRatio, 1e-9);
        exported.StartParameterDegrees.Should().BeInRange(0, 360);
        exported.EndParameterDegrees.Should().BeInRange(0, 360.00000001);
        for (var parameter = -90; parameter <= 450; parameter += 15)
        {
            AssertPoint(exported.PointAtParameterDegrees(parameter - 90), ellipse.PointAtParameterDegrees(parameter));
        }

        if (start == end || end - start == 360)
        {
            (exported.EndParameterDegrees - exported.StartParameterDegrees).Should().BeApproximately(360, 1e-8);
        }
        else
        {
            foreach (var pair in exported.GetSamplePoints(32).Zip(ellipse.GetSamplePoints(32)))
            {
                AssertPoint(pair.First, pair.Second);
            }
        }

        var native = DxfDocumentReader.Read(DxfDocumentWriter.Write(solved.Document,
            new DxfWriteOptions(IncludeNativeDocument: true)), out var nativeRestored);
        nativeRestored.Should().BeTrue();
        native.Should().BeEquivalentTo(solved.Document, options => options.WithStrictOrdering());
        new LegacySketchSolverAdapter().Solve(SketchSolveRequest.FromDocument(native))
            .Status.Should().Be(SketchSolveStatus.Solved);
        solved.Document.Entities.Single().Should().BeSameAs(ellipse);
        document.Entities.Single().Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(1)]
    public void AlreadyCanonicalEllipseKeepsItsAxisAndParameters(double ratio)
    {
        var ellipse = new EllipseEntity(EntityId.Create("ellipse"), new(7, -2), new(2, 1), ratio, 25, 220);

        var read = DxfDocumentReader.Read(DxfDocumentWriter.Write(new DrawingDocument(new[] { ellipse })));

        var exported = (EllipseEntity)read.Entities.Single();
        exported.MajorAxisEndPoint.Should().Be(ellipse.MajorAxisEndPoint);
        exported.MinorRadiusRatio.Should().Be(ratio);
        exported.StartParameterDegrees.Should().BeApproximately(25, 1e-8);
        exported.EndParameterDegrees.Should().BeApproximately(220, 1e-8);
    }

    private static SketchDimension Dimension(string axis, double value) => new(axis, SketchDimensionKind.LinearDistance,
        new[] { $"ellipse|point|{axis}-start|0|0", $"ellipse|point|{axis}-end|0|0" }, value, new Point2(0, 0), true);

    [Theory]
    [InlineData(false, 1e11, 2)]
    [InlineData(true, 1e11, 2)]
    [InlineData(false, 2, double.MaxValue)]
    [InlineData(true, 2, double.MaxValue)]
    public void UnrepresentableCrossingFailsExportWithoutClampingOrMutating(bool native, double ratio, double axisLength)
    {
        var ellipse = new EllipseEntity(EntityId.Create("ellipse"), new(0, 0), new(axisLength, 0), ratio);
        var document = new DrawingDocument(new[] { ellipse });

        var write = () => DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: native));

        write.Should().Throw<FormatException>().WithMessage("*valid DXF axis ratio*");
        document.Entities.Single().Should().BeSameAs(ellipse);
    }

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-8);
        actual.Y.Should().BeApproximately(expected.Y, 1e-8);
    }
}
