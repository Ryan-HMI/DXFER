namespace DXFER.CadIO;

public sealed record DxfWriteOptions(GrainAnnotation? GrainAnnotation = null, bool IncludeNativeDocument = false);

public sealed record GrainAnnotation(string Label, double AngleDegrees);
