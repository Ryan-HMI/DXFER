using DXFER.Core.Geometry;

namespace DXFER.Blazor.Interop;

public sealed record CanvasCreationSnapDto(Point2 Point, string Kind, string[] ReferenceKeys);
