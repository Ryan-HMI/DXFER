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
        var contours = typeof(DrawingWorkbench).GetProperty("CurrentContours", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initial = Document(1, DrawingUnits.Inches);
        field.SetValue(workbench, initial);
        var first = (CutPathLengthResult)property.GetValue(workbench)!;
        ((ClosedContourResult)contours.GetValue(workbench)!).ClosedContourCount.Should().Be(1);
        field.SetValue(workbench, new DrawingDocument(initial.Entities.Append(
            new CircleEntity(EntityId.Create("second"), new Point2(10, 0), 1)), [], [], initial.Metadata));
        ((ClosedContourResult)contours.GetValue(workbench)!).ClosedContourCount.Should().Be(2);
        field.SetValue(workbench, Document(2, DrawingUnits.Inches));
        ((CutPathLengthResult)property.GetValue(workbench)!).CutLengthInches.Should()
            .BeApproximately(first.CutLengthInches!.Value * 2, 0.000001m);
        field.SetValue(workbench, initial);
        property.GetValue(workbench).Should().Be(first);
        ((ClosedContourResult)contours.GetValue(workbench)!).ClosedContourCount.Should().Be(1);
        field.SetValue(workbench, Document(2, DrawingUnits.Unspecified));
        ((CutPathLengthResult)property.GetValue(workbench)!).CutLengthInches.Should()
            .BeApproximately(first.CutLengthInches!.Value * 2, 0.000001m);
        ((ClosedContourResult)contours.GetValue(workbench)!).ClosedContourCount.Should().Be(1);
    }

    private static DrawingDocument Document(double radius, DrawingUnits units) => new(
        [new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), radius)], [], [],
        DrawingDocumentMetadata.Empty with { Units = units });
}
