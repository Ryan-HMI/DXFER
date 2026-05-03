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
                case PolylineEntity polyline:
                    WritePolyline(builder, polyline);
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
}
