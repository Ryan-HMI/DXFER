using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXFER.Core.Sketching;

public interface IPlaneGcsRuntime
{
    PlaneGcsResponse Solve(PlaneGcsRequest request);
}

public sealed record PlaneGcsRequest(
    [property: JsonPropertyName("document")] PlaneGcsDocument Document,
    [property: JsonPropertyName("constraints")] IReadOnlyList<PlaneGcsConstraint> Constraints,
    [property: JsonPropertyName("dimensions")] IReadOnlyList<PlaneGcsDimension> Dimensions,
    [property: JsonPropertyName("fixedReferenceKeys")] IReadOnlyList<string> FixedReferenceKeys,
    [property: JsonPropertyName("initialGuesses")] IReadOnlyList<PlaneGcsInitialGuess> InitialGuesses,
    [property: JsonPropertyName("drag")] PlaneGcsDragRequest? Drag = null,
    [property: JsonPropertyName("attemptedDrivingDimensionIds")] IReadOnlyList<string>? AttemptedDrivingDimensionIds = null);

public sealed record PlaneGcsResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("entities")] IReadOnlyList<PlaneGcsEntity>? Entities = null,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<string>? Diagnostics = null,
    [property: JsonPropertyName("engine")] string? Engine = null,
    [property: JsonPropertyName("redundantDrivingDimensionIds")] IReadOnlyList<string>? RedundantDrivingDimensionIds = null);

public sealed record PlaneGcsDocument(
    [property: JsonPropertyName("entities")] IReadOnlyList<PlaneGcsEntity> Entities,
    [property: JsonPropertyName("constraints")] IReadOnlyList<PlaneGcsConstraint> Constraints,
    [property: JsonPropertyName("dimensions")] IReadOnlyList<PlaneGcsDimension> Dimensions,
    [property: JsonPropertyName("metadata")] JsonElement Metadata,
    [property: JsonPropertyName("hasPatterns")] bool HasPatterns);

public sealed record PlaneGcsPoint(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record PlaneGcsInitialGuess(
    [property: JsonPropertyName("referenceKey")] string ReferenceKey,
    [property: JsonPropertyName("point")] PlaneGcsPoint Point);

public sealed record PlaneGcsDragRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("start")] PlaneGcsPoint Start,
    [property: JsonPropertyName("end")] PlaneGcsPoint End,
    [property: JsonPropertyName("constrain")] bool Constrain,
    [property: JsonPropertyName("temporarySnapIntent")] PlaneGcsConstraint? TemporarySnapIntent = null);

public sealed record PlaneGcsConstraint(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("referenceKeys")] IReadOnlyList<string> ReferenceKeys,
    [property: JsonPropertyName("state")] string State);

public sealed record PlaneGcsDimension(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("referenceKeys")] IReadOnlyList<string> ReferenceKeys,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("anchor")] PlaneGcsPoint? Anchor,
    [property: JsonPropertyName("isDriving")] bool IsDriving,
    [property: JsonPropertyName("state")] string State);

public sealed record PlaneGcsEntity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("points")] IReadOnlyList<PlaneGcsPoint> Points,
    [property: JsonPropertyName("center")] PlaneGcsPoint? Center = null,
    [property: JsonPropertyName("radius")] double? Radius = null,
    [property: JsonPropertyName("startAngleDegrees")] double? StartAngleDegrees = null,
    [property: JsonPropertyName("endAngleDegrees")] double? EndAngleDegrees = null,
    [property: JsonPropertyName("isConstruction")] bool IsConstruction = false,
    [property: JsonPropertyName("majorAxisEndPoint")] PlaneGcsPoint? MajorAxisEndPoint = null,
    [property: JsonPropertyName("minorRadiusRatio")] double? MinorRadiusRatio = null,
    [property: JsonPropertyName("rotationAngleDegrees")] double? RotationAngleDegrees = null,
    [property: JsonPropertyName("sideCount")] int? SideCount = null,
    [property: JsonPropertyName("circumscribed")] bool? Circumscribed = null,
    [property: JsonPropertyName("controlPoints")] IReadOnlyList<PlaneGcsPoint>? ControlPoints = null,
    [property: JsonPropertyName("fitPoints")] IReadOnlyList<PlaneGcsPoint>? FitPoints = null,
    [property: JsonPropertyName("startTangentHandle")] PlaneGcsPoint? StartTangentHandle = null,
    [property: JsonPropertyName("endTangentHandle")] PlaneGcsPoint? EndTangentHandle = null,
    [property: JsonPropertyName("splineDegree")] int? SplineDegree = null,
    [property: JsonPropertyName("splineKnots")] IReadOnlyList<double>? SplineKnots = null,
    [property: JsonPropertyName("splineWeights")] IReadOnlyList<double>? SplineWeights = null,
    [property: JsonPropertyName("layerName")] string? LayerName = null,
    [property: JsonPropertyName("isLocked")] bool IsLocked = false,
    [property: JsonPropertyName("isHidden")] bool IsHidden = false,
    [property: JsonPropertyName("isGenerated")] bool IsGenerated = false);
