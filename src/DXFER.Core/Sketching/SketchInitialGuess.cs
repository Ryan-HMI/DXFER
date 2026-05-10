using System.Collections.ObjectModel;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public sealed class SketchInitialGuess
{
    public SketchInitialGuess(IEnumerable<KeyValuePair<string, Point2>> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var copiedPoints = points.ToArray();
        foreach (var point in copiedPoints)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(point.Key);
        }

        Points = Array.AsReadOnly(copiedPoints);
    }

    public ReadOnlyCollection<KeyValuePair<string, Point2>> Points { get; }

    public static SketchInitialGuess Point(Point2 point) =>
        new(new[] { new KeyValuePair<string, Point2>("point", point) });

    public static SketchInitialGuess Entity(IEnumerable<KeyValuePair<string, Point2>> points) =>
        new(points);
}
