using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchCutLengthTests
{
    [Fact]
    public void ReadoutRecalculatesForEditsUndoAndUnitChanges()
    {
        var workbench = new DrawingWorkbench();
        var field = typeof(DrawingWorkbench).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var property = typeof(DrawingWorkbench).GetProperty("CurrentCutLength", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initial = Document(1, DrawingUnits.Inches);
        field.SetValue(workbench, initial);
        var first = (CutPathLengthResult)property.GetValue(workbench)!;
        field.SetValue(workbench, Document(2, DrawingUnits.Inches));
        ((CutPathLengthResult)property.GetValue(workbench)!).CutLengthInches.Should()
            .BeApproximately(first.CutLengthInches!.Value * 2, 0.000001m);
        field.SetValue(workbench, initial);
        property.GetValue(workbench).Should().Be(first);
        field.SetValue(workbench, Document(2, DrawingUnits.Unspecified));
        ((CutPathLengthResult)property.GetValue(workbench)!).CutLengthInches.Should().BeNull();
    }

    private static DrawingDocument Document(double radius, DrawingUnits units) => new(
        [new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), radius)], [], [],
        DrawingDocumentMetadata.Empty with { Units = units });
}
