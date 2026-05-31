namespace DXFER.CadIO;

public sealed record DxfWriteOptions(GrainAnnotation? GrainAnnotation = null);

public sealed record GrainAnnotation(string Label, double AngleDegrees);
