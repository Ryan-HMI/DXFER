using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsPatternRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedDirectionEditsPassAdapterValidationAndNativeReopen(bool independentV)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var baseline = new DrawingDocument([
            new CircleEntity(EntityId.Create("seed"), new(3, 2), 1),
            new LineEntity(EntityId.Create("u"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("v"), new(20, 0), new(20, 10))
        ], [], [new("fixed-u-start", SketchConstraintKind.Fix, ["u:start"], SketchConstraintState.Satisfied)]);
        DrawingPatternService.TryCreate(baseline, new PatternDefinition
        {
            Id = "linked", SourceEntityIds = ["seed"], Kind = PatternKind.Staggered,
            Rows = 2, Columns = 3, XPitch = -8, YPitch = 6, StaggerFraction = 0.25,
            UDirectionReference = "u", VDirectionReference = independentV ? "v" : null
        }, out var document, out var reason).Should().BeTrue(reason);
        solver.TryApplyDrag(document, "u:end", new(10, 0), new(8, 6), false,
            out var result, out var status).Should().BeTrue(status);
        DrawingPatternService.TryValidate(result, out reason).Should().BeTrue(reason);
        var edge = result.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "u");
        edge.Start.Should().Be(new Point2(0, 0));
        edge.End.X.Should().BeApproximately(8, 1e-6);
        edge.End.Y.Should().BeApproximately(6, 1e-6);
        var dxf = DXFER.CadIO.DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true));
        var reopened = DXFER.CadIO.DxfDocumentReader.Read(dxf, out var restored);
        restored.Should().BeTrue();
        reopened.Metadata.Patterns.Single().UDirectionReference.Should().Be("u");
        reopened.Metadata.Patterns.Single().StaggerFraction.Should().Be(0.25);
        solver.TryApplyDrag(reopened, "u:end", edge.End, new(6, 8), false,
            out var edited, out status).Should().BeTrue(status);
        DrawingPatternService.TryValidate(edited, out reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData(PatternKind.Rectangular)]
    [InlineData(PatternKind.Staggered)]
    [InlineData(PatternKind.Radial)]
    public void RealRuntimeMembersFollowSeedCenterThroughEquations(PatternKind kind)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(kind);
        var solver = new PlaneGcsSketchSolverAdapter(observed);

        solver.TryApplyDrag(original, "seed:center", new(3, 2), new(4, 3), false,
            out var result, out var status).Should().BeTrue(status);

        var seed = Circle(result, "seed");
        seed.Center.X.Should().BeApproximately(4, 1e-6);
        seed.Center.Y.Should().BeApproximately(3, 1e-6);
        seed.Radius.Should().BeApproximately(1, 1e-6);
        AssertPattern(original, result);
        AssertNoAutomaticMemberFixes(original, observed.Request!);
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
    }

    [Theory]
    [InlineData(PatternKind.Rectangular)]
    [InlineData(PatternKind.Staggered)]
    [InlineData(PatternKind.Radial)]
    public void RealRuntimeMembersFollowDrivingSeedRadius(PatternKind kind)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(kind);
        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [], [Radius(2)]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        Circle(result.Document, "seed").Radius.Should().BeApproximately(2, 1e-6);
        AssertPattern(original, result.Document);
        AssertNoAutomaticMemberFixes(original, observed.Request!);
        result.Document.Dimensions.Single().Id.Should().Be("radius");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRuntimeLockedOrExplicitlyFixedMemberRefusesIncompatibleSeedRadius(bool explicitFix)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(PatternKind.Rectangular);
        var memberId = original.Metadata.Patterns.Single().Members[0].EntityId;
        if (explicitFix)
            original = new(original.Entities, original.Dimensions,
                [new("fixed-member", SketchConstraintKind.Fix, [memberId], SketchConstraintState.Satisfied)], original.Metadata);
        else
        {
            var styles = new Dictionary<string, DxfEntityStyle>(original.Metadata.EntityStyles)
            {
                [memberId] = new("locked")
            };
            original = new(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
            {
                EntityStyles = styles,
                Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0"), ["locked"] = new("locked", IsLocked: true) }
            });
        }

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [], [Radius(2)]));

        result.Status.Should().Be(SketchSolveStatus.Failed, string.Join(" ", result.Diagnostics));
        observed.Request!.FixedReferenceKeys.Should().Contain(memberId);
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("failed");
        for (var index = 0; index < original.Entities.Count; index++)
            result.Document.Entities[index].Should().BeSameAs(original.Entities[index]);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.Document.Dimensions.Single().Value.Should().Be(2);
        DrawingPatternService.TryValidate(result.Document, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public void RealRuntimeFixedExternalPeerMovesFreeSeedThroughGeneratedMemberReference()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(PatternKind.Radial);
        original = new(original.Entities.Append(new PointEntity(EntityId.Create("peer"), new(12, 4))),
            original.Dimensions, original.Constraints, original.Metadata);
        var memberId = original.Metadata.Patterns.Single().Members[0].EntityId;
        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [
            new("fixed-peer", SketchConstraintKind.Fix, ["peer"], SketchConstraintState.Satisfied),
            new("join", SketchConstraintKind.Coincident, [$"{memberId}:center", "peer:center"])
        ], []));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        Circle(result.Document, memberId).Center.X.Should().BeApproximately(12, 1e-6);
        Circle(result.Document, memberId).Center.Y.Should().BeApproximately(4, 1e-6);
        Circle(result.Document, "seed").Center.Should().NotBe(Circle(original, "seed").Center);
        result.Document.Entities.Last().Should().BeSameAs(original.Entities.Last());
        AssertPattern(original, result.Document);
        AssertNoAutomaticMemberFixes(original, observed.Request!);
        result.Document.Constraints.Should().OnlyContain(c =>
            SketchConstraintService.ValidateConstraint(result.Document, c).State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DirectGeneratedMemberDragStillRefusesBeforeInvokingRuntime()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(PatternKind.Radial);
        var memberId = original.Metadata.Patterns.Single().Members[0].EntityId;
        var center = Circle(original, memberId).Center;

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, $"{memberId}:center",
            center, new(center.X + 1, center.Y + 1), false, out var result, out var status).Should().BeFalse();

        result.Should().BeSameAs(original);
        status.Should().Contain("generated pattern members");
        observed.Request.Should().BeNull();
    }

    [Theory]
    [InlineData(0.000001)]
    [InlineData(0.01)]
    public void PurePatternValidationStillRejectsMeaningfulMemberDrift(double drift)
    {
        var original = Document(PatternKind.Radial);
        var memberId = original.Metadata.Patterns.Single().Members[0].EntityId;
        var member = Circle(original, memberId);
        var changed = member with { Center = new(member.Center.X + drift, member.Center.Y) };
        var candidate = new DrawingDocument(original.Entities.Select(entity =>
            entity.Id.Value == memberId ? changed : entity), original.Dimensions, original.Constraints, original.Metadata);

        DrawingPatternService.TryValidate(candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("inconsistent");
        Circle(candidate, memberId).Should().BeSameAs(changed, "validation must never repair a member");
        Circle(original, memberId).Should().BeSameAs(member);
    }

    [Fact]
    public void RealRuntimeRadialArcRadiusEditPreservesSweepAcrossAngleWrap()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(PatternKind.Radial,
            new ArcEntity(EntityId.Create("seed"), new(3, 2), 1, 0, 90));
        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [], [Radius(2)]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.Document.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        DrawingPatternService.TryValidate(result.Document, out var reason).Should().BeTrue(reason);
        var seed = (ArcEntity)result.Document.Entities[0];
        var seedSweep = seed.EndAngleDegrees - seed.StartAngleDegrees;
        if (seedSweep < 0) seedSweep += 360;
        foreach (var member in original.Metadata.Patterns.Single().Members)
        {
            var arc = (ArcEntity)result.Document.Entities.Single(e => e.Id.Value == member.EntityId);
            var angle = member.Column * 360.0 / original.Metadata.Patterns.Single().Count;
            var expected = seed.Center.Transform(Transform2.RotationDegreesAbout(angle, new(1, -2)));
            arc.Center.X.Should().BeApproximately(expected.X, 1e-6);
            arc.Center.Y.Should().BeApproximately(expected.Y, 1e-6);
            arc.Radius.Should().BeApproximately(2, 1e-6);
            Math.IEEERemainder(arc.StartAngleDegrees - seed.StartAngleDegrees - angle, 360).Should().BeApproximately(0, 1e-7);
            Math.IEEERemainder(arc.EndAngleDegrees - seed.EndAngleDegrees - angle, 360).Should().BeApproximately(0, 1e-7);
            var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
            if (sweep < 0) sweep += 360;
            sweep.Should().BeApproximately(seedSweep, 1e-7, "members preserve the solved seed sweep; no angle driver was added");
        }
        original.Entities.Cast<ArcEntity>().Should().OnlyContain(arc => arc.Radius == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRuntimeRadialEllipseTranslationPreservesAxisVectorsAndParameters(bool partial)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(PatternKind.Radial, new EllipseEntity(EntityId.Create("seed"),
            new(3, 2), new(2, 1), 0.5, partial ? 330 : 0, partial ? 80 : 360));

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, "seed:center", new(3, 2), new(4, 3),
            false, out var result, out var status).Should().BeTrue(status);

        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        result.Metadata.Should().BeSameAs(original.Metadata);
        result.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        DrawingPatternService.TryValidate(result, out var reason).Should().BeTrue(reason);
        var seed = (EllipseEntity)result.Entities[0];
        seed.Center.X.Should().BeApproximately(4, 1e-6);
        seed.Center.Y.Should().BeApproximately(3, 1e-6);
        foreach (var member in original.Metadata.Patterns.Single().Members)
        {
            var ellipse = (EllipseEntity)result.Entities.Single(e => e.Id.Value == member.EntityId);
            var angle = member.Column * 360.0 / original.Metadata.Patterns.Single().Count;
            var center = seed.Center.Transform(Transform2.RotationDegreesAbout(angle, new(1, -2)));
            var axis = seed.MajorAxisEndPoint.Transform(Transform2.RotationDegrees(angle));
            ellipse.Center.X.Should().BeApproximately(center.X, 1e-7);
            ellipse.Center.Y.Should().BeApproximately(center.Y, 1e-7);
            ellipse.MajorAxisEndPoint.X.Should().BeApproximately(axis.X, 1e-7);
            ellipse.MajorAxisEndPoint.Y.Should().BeApproximately(axis.Y, 1e-7);
            ellipse.MinorRadiusRatio.Should().BeApproximately(seed.MinorRadiusRatio, 1e-7);
            ellipse.StartParameterDegrees.Should().BeApproximately(seed.StartParameterDegrees, 1e-7);
            ellipse.EndParameterDegrees.Should().BeApproximately(seed.EndParameterDegrees, 1e-7);
        }
        ((EllipseEntity)original.Entities[0]).Center.Should().Be(new Point2(3, 2));
    }

    [Fact]
    public void RealRuntimeRadialRationalSplineEndpointEditPreservesNativeTopologyAndWeights()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var initial = new SplineEntity(EntityId.Create("seed"), 3,
            [new(3, 2), new(4, 3), new(5, 1), new(6, 2)], [0, 0, 0, 0, 1, 1, 1, 1], [1, 0.8, 1.2, 1]);
        var original = Document(PatternKind.Radial, initial);

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, "seed:start", new(3, 2), new(4, 3),
            false, out var result, out var status).Should().BeTrue(status);

        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        result.Metadata.Should().BeSameAs(original.Metadata);
        result.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        DrawingPatternService.TryValidate(result, out var reason).Should().BeTrue(reason);
        var seed = (SplineEntity)result.Entities[0];
        seed.ControlPoints[0].X.Should().BeApproximately(4, 1e-6);
        seed.ControlPoints[0].Y.Should().BeApproximately(3, 1e-6);
        foreach (var spline in result.Entities.Cast<SplineEntity>())
        {
            spline.Degree.Should().Be(initial.Degree);
            spline.Knots.Should().Equal(initial.Knots);
            spline.Weights.Should().Equal(initial.Weights);
            spline.FitPoints.Should().Equal(initial.FitPoints);
            spline.StartTangentHandle.Should().Be(initial.StartTangentHandle);
            spline.EndTangentHandle.Should().Be(initial.EndTangentHandle);
        }
        foreach (var member in original.Metadata.Patterns.Single().Members)
        {
            var spline = (SplineEntity)result.Entities.Single(e => e.Id.Value == member.EntityId);
            var transform = Transform2.RotationDegreesAbout(member.Column * 360.0 /
                original.Metadata.Patterns.Single().Count, new(1, -2));
            spline.ControlPoints.Should().HaveCount(seed.ControlPoints.Count);
            for (var index = 0; index < seed.ControlPoints.Count; index++)
            {
                var expected = seed.ControlPoints[index].Transform(transform);
                spline.ControlPoints[index].X.Should().BeApproximately(expected.X, 1e-7);
                spline.ControlPoints[index].Y.Should().BeApproximately(expected.Y, 1e-7);
            }
        }
        ((SplineEntity)original.Entities[0]).ControlPoints.Should().Equal(initial.ControlPoints);
    }

    private static void AssertNoAutomaticMemberFixes(DrawingDocument original, PlaneGcsRequest request) =>
        request.FixedReferenceKeys.Should().NotIntersectWith(original.Metadata.Patterns.Single().Members.Select(m => m.EntityId));

    private static void AssertPattern(DrawingDocument original, DrawingDocument result)
    {
        result.Metadata.Should().BeSameAs(original.Metadata, "pattern settings and ownership must not be rewritten");
        result.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        DrawingPatternService.TryValidate(result, out var reason).Should().BeTrue(reason);
        var definition = original.Metadata.Patterns.Single();
        var seed = Circle(result, "seed");
        foreach (var member in definition.Members)
        {
            var actual = Circle(result, member.EntityId);
            Point2 expected;
            if (definition.Kind == PatternKind.Radial)
                expected = seed.Center.Transform(Transform2.RotationDegreesAbout(
                    member.Column * 360.0 / definition.Count, definition.Center));
            else
            {
                var stagger = definition.Kind == PatternKind.Staggered && member.Row % 2 != 0 ? 0.5 : 0;
                var shift = new Point2((member.Column + stagger) * definition.XPitch, member.Row * definition.YPitch)
                    .Transform(Transform2.RotationDegrees(definition.AngleDegrees));
                expected = new(seed.Center.X + shift.X, seed.Center.Y + shift.Y);
            }
            actual.Center.X.Should().BeApproximately(expected.X, 1e-6);
            actual.Center.Y.Should().BeApproximately(expected.Y, 1e-6);
            actual.Radius.Should().BeApproximately(seed.Radius, 1e-6);
        }
        Circle(original, "seed").Center.Should().Be(new Point2(3, 2));
        Circle(original, "seed").Radius.Should().Be(1);
    }

    private static CircleEntity Circle(DrawingDocument document, string id) =>
        (CircleEntity)document.Entities.Single(e => e.Id.Value == id);

    private static SketchDimension Radius(double value) =>
        new("radius", SketchDimensionKind.Radius, ["seed"], value, isDriving: true);

    private static DrawingDocument Document(PatternKind kind, DrawingEntity? seed = null)
    {
        var baseline = new DrawingDocument([seed ?? new CircleEntity(EntityId.Create("seed"), new(3, 2), 1)], [], [],
            new DrawingDocumentMetadata { SourceFileName = "pattern-engine-fixture.dxf", Units = DrawingUnits.Inches });
        // Fixture creation only. No procedural reconciliation occurs after the solve.
        DrawingPatternService.TryCreate(baseline, new PatternDefinition
        {
            Id = "array", SourceEntityIds = ["seed"], Kind = kind, Rows = 2, Columns = 3,
            XPitch = 8, YPitch = 6, AngleDegrees = 32, Count = 5, Center = new(1, -2),
            StepAngleDegrees = 45, FullCircle = true
        }, out var document, out var reason).Should().BeTrue(reason);
        return document;
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists);
        if (node is null) throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }

    private sealed class ObservedRuntime(IPlaneGcsRuntime inner) : IPlaneGcsRuntime
    {
        public PlaneGcsRequest? Request { get; private set; }
        public PlaneGcsResponse? Response { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            return Response = inner.Solve(request);
        }
    }
}
