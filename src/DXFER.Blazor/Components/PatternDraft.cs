namespace DXFER.Blazor.Components;

public sealed class PatternDraft
{
    public string Mode { get; set; } = "Rectangular";
    public string Columns { get; set; } = "3";
    public string Rows { get; set; } = "2";
    public string PitchX { get; set; } = "1";
    public string PitchY { get; set; } = "1";
    public string Angle { get; set; } = "0";
    public string? UDirectionReference { get; set; }
    public string? VDirectionReference { get; set; }
    public string StaggerFraction { get; set; } = "0.5";
    public bool Staggered
    {
        get => Mode == "Staggered";
        set { if (Mode != "Radial") Mode = value ? "Staggered" : "Rectangular"; }
    }
    public string Count { get; set; } = "6";
    public string CenterX { get; set; } = "0";
    public string CenterY { get; set; } = "0";
    public bool FullCircle { get; set; } = true;
    public string StepAngle { get; set; } = "30";
}
