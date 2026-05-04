using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchConstraintTests
{
    [Fact]
    public void ConstraintStoresKindReferencesAndState()
    {
        var constraint = new SketchConstraint(
            "constraint-1",
            SketchConstraintKind.Coincident,
            new[] { "line-a:start", "circle-b:center" },
            SketchConstraintState.Satisfied);

        constraint.Id.Should().Be("constraint-1");
        constraint.Kind.Should().Be(SketchConstraintKind.Coincident);
        constraint.ReferenceKeys.Should().Equal("line-a:start", "circle-b:center");
        constraint.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void ConstraintReferencesAreCopiedFromConstructorInput()
    {
        var references = new[] { "line-a", "line-b" };

        var constraint = new SketchConstraint(
            "constraint-1",
            SketchConstraintKind.Parallel,
            references);

        references[1] = "mutated";

        constraint.ReferenceKeys.Should().Equal("line-a", "line-b");
        constraint.State.Should().Be(SketchConstraintState.Unknown);
    }
}
