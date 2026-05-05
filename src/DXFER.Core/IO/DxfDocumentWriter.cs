using System.Globalization;
using System.Text;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.IO;

public static class DxfDocumentWriter
{
    public static string Write(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        WritePair(builder, 0, "SECTION");
        WritePair(builder, 2, "ENTITIES");

        foreach (var entity in document.Entities)
        {
            if (entity.IsConstruction)
            {
                continue;
            }

            switch (entity)
            {
                case LineEntity line:
                    WriteLine(builder, line);
                    break;
                case CircleEntity circle:
                    WriteCircle(builder, circle);
                    break;
                case ArcEntity arc:
                    WriteArc(builder, arc);
                    break;
                case EllipseEntity ellipse:
                    WriteEllipse(builder, ellipse);
                    break;
                case PointEntity point:
                    WritePointEntity(builder, point);
                    break;
                case PolylineEntity polyline:
                    WritePolyline(builder, polyline);
                    break;
                case SplineEntity spline:
                    WriteSpline(builder, spline);
                    break;
            }
        }

        WritePair(builder, 0, "ENDSEC");
        WritePair(builder, 0, "EOF");
        return builder.ToString();
    }

    private static void WriteLine(StringBuilder builder, LineEntity line)
    {
        WritePair(builder, 0, "LINE");
        WritePair(builder, 5, line.Id.Value);
        WritePoint(builder, line.Start, 10, 20);
        WritePoint(builder, line.End, 11, 21);
    }

    private static void WriteCircle(StringBuilder builder, CircleEntity circle)
    {
        WritePair(builder, 0, "CIRCLE");
        WritePair(builder, 5, circle.Id.Value);
        WritePoint(builder, circle.Center, 10, 20);
        WritePair(builder, 40, Format(circle.Radius));
    }

    private static void WriteArc(StringBuilder builder, ArcEntity arc)
    {
        WritePair(builder, 0, "ARC");
        WritePair(builder, 5, arc.Id.Value);
        WritePoint(builder, arc.Center, 10, 20);
        WritePair(builder, 40, Format(arc.Radius));
        WritePair(builder, 50, Format(arc.StartAngleDegrees));
        WritePair(builder, 51, Format(arc.EndAngleDegrees));
    }

    private static void WriteEllipse(StringBuilder builder, EllipseEntity ellipse)
    {
        WritePair(builder, 0, "ELLIPSE");
        WritePair(builder, 5, ellipse.Id.Value);
        WritePoint(builder, ellipse.Center, 10, 20);
        WritePoint(builder, ellipse.MajorAxisEndPoint, 11, 21);
        WritePair(builder, 40, Format(ellipse.MinorRadiusRatio));
        WritePair(builder, 41, Format(DegreesToRadians(ellipse.StartParameterDegrees)));
        WritePair(builder, 42, Format(DegreesToRadians(ellipse.EndParameterDegrees)));
    }

    private static void WritePointEntity(StringBuilder builder, PointEntity point)
    {
        WritePair(builder, 0, "POINT");
        WritePair(builder, 5, point.Id.Value);
        WritePoint(builder, point.Location, 10, 20);
    }

    private static void WritePolyline(StringBuilder builder, PolylineEntity polyline)
    {
        WritePair(builder, 0, "LWPOLYLINE");
        WritePair(builder, 5, polyline.Id.Value);
        WritePair(builder, 90, polyline.Vertices.Count.ToString(CultureInfo.InvariantCulture));
        WritePair(builder, 70, "0");

        foreach (var vertex in polyline.Vertices)
        {
            WritePoint(builder, vertex, 10, 20);
        }
    }

    private static void WriteSpline(StringBuilder builder, SplineEntity spline)
    {
        WritePair(builder, 0, "SPLINE");
        WritePair(builder, 5, spline.Id.Value);
        WritePair(builder, 70, spline.Weights.Any(weight => Math.Abs(weight - 1d) > 0.000001) ? "12" : "8");
        WritePair(builder, 71, spline.Degree.ToString(CultureInfo.InvariantCulture));
        WritePair(builder, 72, spline.Knots.Count.ToString(CultureInfo.InvariantCulture));
        WritePair(builder, 73, spline.ControlPoints.Count.ToString(CultureInfo.InvariantCulture));
        WritePair(builder, 74, "0");

        foreach (var knot in spline.Knots)
        {
            WritePair(builder, 40, Format(knot));
        }

        if (spline.Weights.Any(weight => Math.Abs(weight - 1d) > 0.000001))
        {
            foreach (var weight in spline.Weights)
            {
                WritePair(builder, 41, Format(weight));
            }
        }

        foreach (var point in spline.ControlPoints)
        {
            WritePoint(builder, point, 10, 20);
        }
    }

    private static void WritePoint(StringBuilder builder, Point2 point, int xCode, int yCode)
    {
        WritePair(builder, xCode, Format(point.X));
        WritePair(builder, yCode, Format(point.Y));
    }

    private static void WritePair(StringBuilder builder, int code, string value)
    {
        builder.AppendLine(code.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(value);
    }

    private static string Format(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
