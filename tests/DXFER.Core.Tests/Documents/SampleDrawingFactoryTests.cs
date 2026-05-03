using DXFER.Core.Documents;
using FluentAssertions;

namespace DXFER.Core.Tests.Documents;

public sealed class SampleDrawingFactoryTests
{
    [Fact]
    public void CanvasPrototypeFixtureHasSelectableEntitiesAndBounds()
    {
        var document = SampleDrawingFactory.CreateCanvasPrototype();

        document.Entities.Should().HaveCountGreaterThanOrEqualTo(6);
        document.Entities.Select(entity => entity.Id.Value).Should().OnlyHaveUniqueItems();
        document.GetBounds().Width.Should().BeGreaterThan(0);
        document.GetBounds().Height.Should().BeGreaterThan(0);
    }
}
