namespace DXFER.Core.Sketching;

public static class FeatureScriptConstraintCapabilities
{
    private static readonly FeatureScriptConstraintCapability[] CapabilityEntries =
    {
        NotApplicable(
            FeatureScriptConstraintType.NONE,
            "NONE is a FeatureScript sentinel and does not create a DXFER sketch constraint."),
        Supported(
            FeatureScriptConstraintType.COINCIDENT,
            SketchConstraintKind.Coincident,
            "COINCIDENT is supported for resolvable DXFER sketch references."),
        Supported(
            FeatureScriptConstraintType.PARALLEL,
            SketchConstraintKind.Parallel,
            "PARALLEL is supported for line-like DXFER references."),
        Supported(
            FeatureScriptConstraintType.VERTICAL,
            SketchConstraintKind.Vertical,
            "VERTICAL is supported for lines or point pairs."),
        Supported(
            FeatureScriptConstraintType.HORIZONTAL,
            SketchConstraintKind.Horizontal,
            "HORIZONTAL is supported for lines or point pairs."),
        Supported(
            FeatureScriptConstraintType.PERPENDICULAR,
            SketchConstraintKind.Perpendicular,
            "PERPENDICULAR is supported for line-like DXFER references."),
        Supported(
            FeatureScriptConstraintType.CONCENTRIC,
            SketchConstraintKind.Concentric,
            "CONCENTRIC is supported for circle-like DXFER references."),
        Deferred(
            FeatureScriptConstraintType.MIRROR,
            "MIRROR maps to DXFER symmetric intent, but live symmetric constraint solving is deferred.",
            SketchConstraintKind.Symmetric),
        Supported(
            FeatureScriptConstraintType.MIDPOINT,
            SketchConstraintKind.Midpoint,
            "MIDPOINT is supported for resolvable line and point references."),
        Deferred(
            FeatureScriptConstraintType.TANGENT,
            "TANGENT can be stored and validated, but tangent geometry solving is deferred.",
            SketchConstraintKind.Tangent),
        Supported(
            FeatureScriptConstraintType.EQUAL,
            SketchConstraintKind.Equal,
            "EQUAL is supported for compatible line lengths or circle-like radii."),
        DimensionOnly(
            FeatureScriptConstraintType.LENGTH,
            SketchDimensionKind.LinearDistance,
            "LENGTH is handled as a DXFER driving distance dimension, not a constraint kind."),
        DimensionOnly(
            FeatureScriptConstraintType.DISTANCE,
            SketchDimensionKind.LinearDistance,
            "DISTANCE is handled as a DXFER driving distance dimension, not a constraint kind."),
        DimensionOnly(
            FeatureScriptConstraintType.ANGLE,
            SketchDimensionKind.Angle,
            "ANGLE is handled as a DXFER driving angle dimension, not a constraint kind."),
        DimensionOnly(
            FeatureScriptConstraintType.RADIUS,
            SketchDimensionKind.Radius,
            "RADIUS is handled as a DXFER driving radius dimension, not a constraint kind."),
        Unsupported(
            FeatureScriptConstraintType.NORMAL,
            "NORMAL has no DXFER constraint or dimension equivalent yet."),
        Supported(
            FeatureScriptConstraintType.FIX,
            SketchConstraintKind.Fix,
            "FIX is supported for whole-entity and sub-reference locking."),
        Unsupported(
            FeatureScriptConstraintType.PROJECTED,
            "PROJECTED constraints are not modeled by DXFER sketch constraints yet."),
        Deferred(
            FeatureScriptConstraintType.OFFSET,
            "OFFSET exists as modify-tool intent, but persistent offset constraints are deferred."),
        Deferred(
            FeatureScriptConstraintType.CIRCULAR_PATTERN,
            "CIRCULAR_PATTERN exists as modify-tool intent, but persistent pattern constraints are deferred."),
        Unsupported(
            FeatureScriptConstraintType.PIERCE,
            "PIERCE has no DXFER constraint or cross-sketch reference model yet."),
        Deferred(
            FeatureScriptConstraintType.LINEAR_PATTERN,
            "LINEAR_PATTERN exists as modify-tool intent, but persistent pattern constraints are deferred."),
        Unsupported(
            FeatureScriptConstraintType.MAJOR_DIAMETER,
            "MAJOR_DIAMETER for ellipses is not a DXFER constraint or driving dimension yet."),
        Unsupported(
            FeatureScriptConstraintType.MINOR_DIAMETER,
            "MINOR_DIAMETER for ellipses is not a DXFER constraint or driving dimension yet."),
        Unsupported(
            FeatureScriptConstraintType.QUADRANT,
            "QUADRANT has no DXFER curve-parameter constraint model yet."),
        DimensionOnly(
            FeatureScriptConstraintType.DIAMETER,
            SketchDimensionKind.Diameter,
            "DIAMETER is handled as a DXFER driving diameter dimension, not a constraint kind."),
        Unsupported(
            FeatureScriptConstraintType.SILHOUETTED,
            "SILHOUETTED projection constraints are not modeled by DXFER sketches yet."),
        Unsupported(
            FeatureScriptConstraintType.CENTERLINE_DIMENSION,
            "CENTERLINE_DIMENSION has no DXFER centerline dimension model yet."),
        Unsupported(
            FeatureScriptConstraintType.INTERSECTED,
            "INTERSECTED projection constraints are not modeled by DXFER sketches yet."),
        Unsupported(
            FeatureScriptConstraintType.RHO,
            "RHO conic parameter constraints are not modeled by DXFER sketches yet."),
        Unsupported(
            FeatureScriptConstraintType.EQUAL_CURVATURE,
            "EQUAL_CURVATURE has no DXFER spline or curve curvature constraint model yet."),
        Unsupported(
            FeatureScriptConstraintType.BEZIER_DEGREE,
            "BEZIER_DEGREE has no DXFER Bezier subtype constraint model yet."),
        Unsupported(
            FeatureScriptConstraintType.FREEZE,
            "FREEZE is not a DXFER constraint; use FIX for supported fixed references.")
    };

    private static readonly IReadOnlyList<FeatureScriptConstraintCapability> Capabilities =
        Array.AsReadOnly(CapabilityEntries);

    public static IReadOnlyList<FeatureScriptConstraintCapability> All => Capabilities;

    public static FeatureScriptConstraintCapability Get(FeatureScriptConstraintType constraintType)
    {
        foreach (var capability in Capabilities)
        {
            if (capability.ConstraintType == constraintType)
            {
                return capability;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(constraintType), constraintType, "Unknown FeatureScript constraint type.");
    }

    private static FeatureScriptConstraintCapability Supported(
        FeatureScriptConstraintType constraintType,
        SketchConstraintKind sketchConstraintKind,
        string diagnostic) =>
        new(constraintType, FeatureScriptConstraintSupport.Supported, diagnostic, sketchConstraintKind);

    private static FeatureScriptConstraintCapability Deferred(
        FeatureScriptConstraintType constraintType,
        string diagnostic,
        SketchConstraintKind? sketchConstraintKind = null) =>
        new(constraintType, FeatureScriptConstraintSupport.Deferred, diagnostic, sketchConstraintKind);

    private static FeatureScriptConstraintCapability DimensionOnly(
        FeatureScriptConstraintType constraintType,
        SketchDimensionKind sketchDimensionKind,
        string diagnostic) =>
        new(
            constraintType,
            FeatureScriptConstraintSupport.DimensionOnly,
            diagnostic,
            SketchDimensionKind: sketchDimensionKind);

    private static FeatureScriptConstraintCapability NotApplicable(
        FeatureScriptConstraintType constraintType,
        string diagnostic) =>
        new(constraintType, FeatureScriptConstraintSupport.NotApplicable, diagnostic);

    private static FeatureScriptConstraintCapability Unsupported(
        FeatureScriptConstraintType constraintType,
        string diagnostic) =>
        new(constraintType, FeatureScriptConstraintSupport.Unsupported, diagnostic);
}
