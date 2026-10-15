using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class CornerIntentPreservationTests
{
    public static TheoryData<bool, bool> Operations => new()
    {
        { false, false }, { false, true }, { true, false }, { true, true }
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public void InvalidatedDrivingLengthRejectsAtomically(bool legacy, bool fillet)
    {
        foreach (var keys in new[]
        {
            new[] { "h" }, new[] { "h:start", "h:end" },
            new[] { "h|point|start|0|0", "h|point|end|10|0" }
        })
        foreach (var kind in new[] { SketchDimensionKind.LinearDistance, SketchDimensionKind.HorizontalDistance })
        {
            var document = Document(new SketchDimension("length", kind, keys, 10, new(5, -2), true));
            Apply(document, legacy, fillet, out var next).Should().BeFalse();
            next.Should().BeSameAs(document);
            document.Dimensions.Single().Value.Should().Be(10);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void ReferenceLengthRefreshesWithoutChangingIdentityOrAnchor(bool legacy, bool fillet)
    {
        var dimension = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "h|point|start|0|0", "h|point|end|10|0" }, 10, new(5, -2), false);
        var document = Document(dimension);
        Apply(document, legacy, fillet, out var next).Should().BeTrue();
        var refreshed = next.Dimensions.Single();
        refreshed.Id.Should().Be(dimension.Id);
        refreshed.Kind.Should().Be(dimension.Kind);
        refreshed.Anchor.Should().Be(dimension.Anchor);
        refreshed.IsDriving.Should().BeFalse();
        refreshed.Value.Should().BeApproximately(8, 1e-10);
        SketchReferenceResolver.TryGetPoint(next, refreshed.ReferenceKeys[0], out var point).Should().BeTrue();
        point.X.Should().BeApproximately(2, 1e-10);
        SketchDimensionSolverService.TryGetDimensionMeasurement(next, refreshed, out var value).Should().BeTrue();
        value.Should().BeApproximately(8, 1e-10);
        document.Dimensions.Single().Should().BeSameAs(dimension);
        ((LineEntity)document.Entities[0]).Start.Should().Be(new Point2(0, 0));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void CompatibleDrivingAndZeroReferenceDimensionsArePreserved(bool legacy, bool fillet)
    {
        foreach (var driving in new[] { false, true })
        {
            var dimension = new SketchDimension("zero", SketchDimensionKind.VerticalDistance,
                new[] { "h:start", "h:end" }, 0, new(5, -2), driving);
            var document = Document(dimension);
            Apply(document, legacy, fillet, out var next).Should().BeTrue();
            next.Dimensions.Single().Should().BeEquivalentTo(dimension);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void UnresolvableAffectedDimensionsRefuseInsteadOfBeingDropped(bool legacy, bool fillet)
    {
        foreach (var driving in new[] { false, true })
        foreach (var key in new[] { "h:center", "h|point|missing|0|0", "missing:end" })
        {
            var document = Document(new SketchDimension("bad", SketchDimensionKind.LinearDistance,
                new[] { "h:end", key }, 10, isDriving: driving));
            Apply(document, legacy, fillet, out var next).Should().BeFalse();
            next.Should().BeSameAs(document);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void FixedMovedReferencesRefuseButUnmovedOrSuppressedReferencesAllow(bool legacy, bool fillet)
    {
        foreach (var key in new[] { "h", "h:start", "h|point|start|0|0", "h|point|mid|5|0", "h:end" })
        foreach (var suppressed in new[] { false, true })
        {
            var constraint = new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { key },
                suppressed ? SketchConstraintState.Suppressed : SketchConstraintState.Satisfied);
            var document = Document(constraints: new[] { constraint });
            var expected = suppressed || key == "h:end";
            Apply(document, legacy, fillet, out var next).Should().Be(expected, $"Fix on {key}, suppressed={suppressed}");
            if (!expected) next.Should().BeSameAs(document);
            next.Constraints.Should().Contain(constraint);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EqualLengthConstraintCannotBeBrokenByCornerEdit(bool legacy, bool fillet)
    {
        var document = new DrawingDocument(Document().Entities.Append(
            new LineEntity(EntityId.Create("peer"), new(20, 0), new(30, 0))),
            Array.Empty<SketchDimension>(), new[]
            {
                new SketchConstraint("equal", SketchConstraintKind.Equal, new[] { "h", "peer" }, SketchConstraintState.Satisfied)
            });
        Apply(document, legacy, fillet, out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedLineAndPolylineSegmentUsesSameDrivingPolicy(bool fillet)
    {
        foreach (var driving in new[] { false, true })
        {
            var document = new DrawingDocument(new DrawingEntity[]
            {
                new PolylineEntity(EntityId.Create("path"), new Point2[] { new(0, 0), new(10, 0), new(10, 10) }, false),
                new LineEntity(EntityId.Create("v"), new(0, 0), new(0, 10))
            }, new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
                new[] { "path|segment|0:start", "path|segment|0:end" }, 10, isDriving: driving) },
                Array.Empty<SketchConstraint>());
            var applied = Current(document, fillet, new[] { "path|segment|0", "v" }, out var next);
            applied.Should().Be(!driving);
            if (driving) next.Should().BeSameAs(document);
            else next.Dimensions.Single().Value.Should().BeApproximately(8, 1e-10);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void NativePolygonCornersRemainUnsupportedAndUnchanged(bool legacy, bool fillet)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("polygon"), new(0, 0), 5, 0, 4)
        }, new[] { new SketchDimension("radius", SketchDimensionKind.Radius,
            new[] { "polygon" }, 5, isDriving: true) }, Array.Empty<SketchConstraint>());
        bool applied;
        DrawingDocument next;
        if (legacy) applied = Apply(document, true, fillet, out next);
        else applied = Current(document, fillet, new[] { "polygon|segment|0", "polygon|segment|1" }, out next);
        applied.Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    private static DrawingDocument Document(SketchDimension? dimension = null, SketchConstraint[]? constraints = null) =>
        new(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("h"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("v"), new(0, 0), new(0, 10))
        }, dimension is null ? Array.Empty<SketchDimension>() : new[] { dimension },
            constraints ?? Array.Empty<SketchConstraint>());

    private static bool Apply(DrawingDocument document, bool legacy, bool fillet, out DrawingDocument next) =>
        legacy
            ? fillet
                ? DrawingModifyService.TryFilletSelectedLines(document, new[] { "h", "v", "polygon" }, 2, out next)
                : DrawingModifyService.TryChamferSelectedLines(document, new[] { "h", "v", "polygon" }, 2, out next)
            : Current(document, fillet, new[] { "h", "v" }, out next);

    private static bool Current(DrawingDocument document, bool fillet, string[] keys, out DrawingDocument next) =>
        fillet
            ? DrawingModifyService.TryFilletSelectedCorner(document, keys, 2, name => EntityId.Create(name + "-new"), out next)
            : DrawingModifyService.TryChamferSelectedCorner(document, keys, 2, name => EntityId.Create(name + "-new"), out next);
}
