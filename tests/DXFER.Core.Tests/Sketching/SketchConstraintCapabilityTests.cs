using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchConstraintCapabilityTests
{
    private static readonly string[] FeatureScriptConstraintNames =
    {
        "NONE",
        "COINCIDENT",
        "PARALLEL",
        "VERTICAL",
        "HORIZONTAL",
        "PERPENDICULAR",
        "CONCENTRIC",
        "MIRROR",
        "MIDPOINT",
        "TANGENT",
        "EQUAL",
        "LENGTH",
        "DISTANCE",
        "ANGLE",
        "RADIUS",
        "NORMAL",
        "FIX",
        "PROJECTED",
        "OFFSET",
        "CIRCULAR_PATTERN",
        "PIERCE",
        "LINEAR_PATTERN",
        "MAJOR_DIAMETER",
        "MINOR_DIAMETER",
        "QUADRANT",
        "DIAMETER",
        "SILHOUETTED",
        "CENTERLINE_DIMENSION",
        "INTERSECTED",
        "RHO",
        "EQUAL_CURVATURE",
        "BEZIER_DEGREE",
        "FREEZE"
    };

    [Fact]
    public void CapabilityTableCoversEveryFeatureScriptConstraintTypeName()
    {
        Enum.GetNames<FeatureScriptConstraintType>().Should().Equal(FeatureScriptConstraintNames);

        FeatureScriptConstraintCapabilities.All
            .Select(capability => capability.FeatureScriptName)
            .Should()
            .Equal(FeatureScriptConstraintNames);

        FeatureScriptConstraintCapabilities.All
            .Select(capability => capability.ConstraintType)
            .Should()
            .OnlyHaveUniqueItems()
            .And.HaveCount(FeatureScriptConstraintNames.Length);
    }

    [Fact]
    public void CapabilityTableDoesNotExposeMutableBackingArray()
    {
        FeatureScriptConstraintCapabilities.All.GetType().IsArray.Should().BeFalse();
    }

    [Theory]
    [InlineData(FeatureScriptConstraintType.COINCIDENT, SketchConstraintKind.Coincident)]
    [InlineData(FeatureScriptConstraintType.PARALLEL, SketchConstraintKind.Parallel)]
    [InlineData(FeatureScriptConstraintType.VERTICAL, SketchConstraintKind.Vertical)]
    [InlineData(FeatureScriptConstraintType.HORIZONTAL, SketchConstraintKind.Horizontal)]
    [InlineData(FeatureScriptConstraintType.PERPENDICULAR, SketchConstraintKind.Perpendicular)]
    [InlineData(FeatureScriptConstraintType.CONCENTRIC, SketchConstraintKind.Concentric)]
    [InlineData(FeatureScriptConstraintType.MIDPOINT, SketchConstraintKind.Midpoint)]
    [InlineData(FeatureScriptConstraintType.EQUAL, SketchConstraintKind.Equal)]
    [InlineData(FeatureScriptConstraintType.FIX, SketchConstraintKind.Fix)]
    public void SupportedConstraintsMapToExistingDxferConstraintKinds(
        FeatureScriptConstraintType constraintType,
        SketchConstraintKind sketchConstraintKind)
    {
        var capability = FeatureScriptConstraintCapabilities.Get(constraintType);

        capability.Support.Should().Be(FeatureScriptConstraintSupport.Supported);
        capability.SketchConstraintKind.Should().Be(sketchConstraintKind);
        capability.SketchDimensionKind.Should().BeNull();
    }

    [Theory]
    [InlineData(FeatureScriptConstraintType.LENGTH, SketchDimensionKind.LinearDistance)]
    [InlineData(FeatureScriptConstraintType.DISTANCE, SketchDimensionKind.LinearDistance)]
    [InlineData(FeatureScriptConstraintType.ANGLE, SketchDimensionKind.Angle)]
    [InlineData(FeatureScriptConstraintType.RADIUS, SketchDimensionKind.Radius)]
    [InlineData(FeatureScriptConstraintType.DIAMETER, SketchDimensionKind.Diameter)]
    public void DimensionOnlyConstraintsMapToExistingDxferDimensionKinds(
        FeatureScriptConstraintType constraintType,
        SketchDimensionKind sketchDimensionKind)
    {
        var capability = FeatureScriptConstraintCapabilities.Get(constraintType);

        capability.Support.Should().Be(FeatureScriptConstraintSupport.DimensionOnly);
        capability.SketchDimensionKind.Should().Be(sketchDimensionKind);
        capability.SketchConstraintKind.Should().BeNull();
    }

    [Theory]
    [InlineData(FeatureScriptConstraintType.MIRROR, SketchConstraintKind.Symmetric)]
    [InlineData(FeatureScriptConstraintType.TANGENT, SketchConstraintKind.Tangent)]
    public void DeferredConstraintsCanRetainKnownDxferConstraintKindWhenOneExists(
        FeatureScriptConstraintType constraintType,
        SketchConstraintKind sketchConstraintKind)
    {
        var capability = FeatureScriptConstraintCapabilities.Get(constraintType);

        capability.Support.Should().Be(FeatureScriptConstraintSupport.Deferred);
        capability.SketchConstraintKind.Should().Be(sketchConstraintKind);
    }

    [Fact]
    public void CapabilityTableSeparatesDeferredUnsupportedAndNotApplicableEntries()
    {
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.NONE).Support
            .Should().Be(FeatureScriptConstraintSupport.NotApplicable);

        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.OFFSET).Support
            .Should().Be(FeatureScriptConstraintSupport.Deferred);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.CIRCULAR_PATTERN).Support
            .Should().Be(FeatureScriptConstraintSupport.Deferred);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.LINEAR_PATTERN).Support
            .Should().Be(FeatureScriptConstraintSupport.Deferred);

        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.PROJECTED).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.PIERCE).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.RHO).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.EQUAL_CURVATURE).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.BEZIER_DEGREE).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
        FeatureScriptConstraintCapabilities.Get(FeatureScriptConstraintType.FREEZE).Support
            .Should().Be(FeatureScriptConstraintSupport.Unsupported);
    }

    [Fact]
    public void EveryCapabilityHasShortFutureFacingDiagnosticText()
    {
        FeatureScriptConstraintCapabilities.All.Should().OnlyContain(capability =>
            !string.IsNullOrWhiteSpace(capability.Diagnostic)
            && capability.Diagnostic.Length <= 160);
    }
}
