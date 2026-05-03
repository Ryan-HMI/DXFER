using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class MeasurementService
{
    public static MeasurementResult Measure(Point2 start, Point2 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;

        return new MeasurementResult(deltaX, deltaY, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
    }

    public static bool TryMeasureEntity(DrawingEntity entity, out MeasurementResult measurement)
    {
        switch (entity)
        {
            case LineEntity line:
                measurement = Measure(line.Start, line.End);
                return true;
            case PolylineEntity { Vertices.Count: >= 2 } polyline:
                measurement = Measure(polyline.Vertices[0], polyline.Vertices[1]);
                return true;
            case CircleEntity circle:
                measurement = new MeasurementResult(circle.Radius * 2, circle.Radius * 2, circle.Radius * 2);
                return true;
            default:
                measurement = default;
                return false;
        }
    }
}
