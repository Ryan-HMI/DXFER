using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public readonly record struct MeasurementSpan(Point2 Start, Point2 End, bool Approximate = false)
{
    public MeasurementResult Result => MeasurementService.Measure(Start, End);
}
