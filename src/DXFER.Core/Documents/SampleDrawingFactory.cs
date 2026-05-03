using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public static class SampleDrawingFactory
{
    public static DrawingDocument CreateCanvasPrototype() =>
        new(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("outline-bottom"), new Point2(0, 0), new Point2(120, 0)),
            new LineEntity(EntityId.Create("outline-right"), new Point2(120, 0), new Point2(120, 80)),
            new LineEntity(EntityId.Create("outline-top"), new Point2(120, 80), new Point2(0, 80)),
            new LineEntity(EntityId.Create("outline-left"), new Point2(0, 80), new Point2(0, 0)),
            new CircleEntity(EntityId.Create("bolt-hole"), new Point2(35, 40), 10),
            new ArcEntity(EntityId.Create("relief-arc"), new Point2(88, 40), 18, 210, 510),
            new PolylineEntity(
                EntityId.Create("bend-path"),
                new[]
                {
                    new Point2(20, 18),
                    new Point2(48, 18),
                    new Point2(64, 34),
                    new Point2(96, 34),
                    new Point2(104, 56)
                })
        });
}
