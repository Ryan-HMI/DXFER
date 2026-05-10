namespace DXFER.Core.Sketching;

public sealed record FeatureScriptConstraintCapability(
    FeatureScriptConstraintType ConstraintType,
    FeatureScriptConstraintSupport Support,
    string Diagnostic,
    SketchConstraintKind? SketchConstraintKind = null,
    SketchDimensionKind? SketchDimensionKind = null)
{
    public string FeatureScriptName => ConstraintType.ToString();
}
