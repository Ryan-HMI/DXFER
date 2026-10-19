using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using DXFER.CadIO;
using FluentAssertions;
using DXFER.Blazor.Selection;

namespace DXFER.Core.Tests.Operations;

public sealed class OffsetIntentTests
{
    private static DrawingDocument Offset(DrawingEntity source, Point2 side, double? gap = 1)
    {
        var document = new DrawingDocument([source]);
        SelectionOffsetService.TryOffset(document, [source.Id.Value], side, _ => EntityId.Create("copy"),
            out var result, out _, out _, out _, out _, gap, retainIntent: true).Should().BeTrue();
        return result;
    }

    [Fact]
    public void KeyedOffsetStoresDrivingGapAndRelationThroughNativeReopen()
    {
        var document = Offset(new LineEntity(EntityId.Create("source"), new(0,0), new(10,0)), new(5,1));
        document.Constraints.Should().ContainSingle().Which.Kind.Should().Be(SketchConstraintKind.Offset);
        var dimension = document.Dimensions.Should().ContainSingle().Subject;
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.IsDriving.Should().BeTrue();
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument:true)));
        reopened.Constraints.Should().BeEquivalentTo(document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(document.Dimensions);
        var next = SketchDimensionSolverService.ApplyDimension(reopened,
            new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 2, dimension.Anchor, true));
        next.Entities.Last().Should().BeOfType<LineEntity>().Which.Start.Y.Should().Be(2);
        SketchDimensionSolverService.IsDimensionSatisfied(next, next.Dimensions.Single()).Should().BeTrue();
    }

    [Fact]
    public void PointerOffsetStoresRelationWithoutInventingDrivingDimension()
    {
        var document = Offset(new CircleEntity(EntityId.Create("source"), new(0,0), 3), new(5,0), null);
        document.Constraints.Should().ContainSingle();
        document.Dimensions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceDragFollowsAndFixedDependentRefuses(bool fixedTarget)
    {
        var document = Offset(new LineEntity(EntityId.Create("source"), new(0,0), new(10,0)), new(5,1));
        if (fixedTarget) document = new(document.Entities,document.Dimensions,document.Constraints.Append(
            new SketchConstraint("fix",SketchConstraintKind.Fix,["copy"],SketchConstraintState.Satisfied)));
        SketchGeometryDragService.TryApplyDrag(document,"source",new(5,0),new(5,3),false,out var moved,out _,false)
            .Should().Be(!fixedTarget);
        if (fixedTarget) moved.Entities.Should().Equal(document.Entities);
        else
        {
            moved.Entities.Last().Should().BeOfType<LineEntity>().Which.Start.Y.Should().Be(4);
            moved.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
            SketchDimensionSolverService.IsDimensionSatisfied(moved,moved.Dimensions.Single()).Should().BeTrue();
        }
    }

    [Fact]
    public void DependentEndpointCannotBreakUniformOffset()
    {
        var document = Offset(new LineEntity(EntityId.Create("source"),new(0,0),new(10,0)),new(5,1));
        SketchGeometryDragService.TryApplyDrag(document,"copy|point|end|10|1",new(10,1),new(12,2),false,out var result,out _,false).Should().BeFalse();
        result.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedContourGapEditAndSourceMove(bool inset)
    {
        var source = new PolylineEntity(EntityId.Create("source"),[new(0,0),new(10,0),new(10,6),new(0,6),new(0,0)]);
        var document = Offset(source,new(5,inset ? 1 : -1));
        var d=document.Dimensions.Single();
        var edited=SketchDimensionSolverService.ApplyDimension(document,new(d.Id,d.Kind,d.ReferenceKeys,2,d.Anchor,true));
        edited.Entities.Last().GetBounds().MinX.Should().Be(inset ? 2 : -2);
        SketchConstraintService.ValidateConstraint(edited,edited.Constraints.Single()).State.Should().Be(SketchConstraintState.Satisfied);
        SketchGeometryDragService.TryApplyDrag(edited,"source",new(5,0),new(5,2),false,out var moved,out _,false).Should().BeTrue();
        moved.Entities.Last().GetBounds().MinY.Should().Be(inset ? 4 : 0);
    }

    [Fact]
    public void CenterFixedCircleAllowsGapEditButWholeFixedDoesNot()
    {
        var document=Offset(new CircleEntity(EntityId.Create("source"),new(0,0),3),new(5,0));
        var d=document.Dimensions.Single();
        foreach(var key in new[]{"copy:center","copy"})
        {
            var fixedDoc=new DrawingDocument(document.Entities,document.Dimensions,document.Constraints.Append(
                new SketchConstraint("fixed",SketchConstraintKind.Fix,[key],SketchConstraintState.Satisfied)));
            var edited=SketchDimensionSolverService.ApplyDimension(fixedDoc,new(d.Id,d.Kind,d.ReferenceKeys,2,d.Anchor,true));
            edited.Entities.Last().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(key=="copy" ? 4 : 5);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void InvalidOrCollapsingGapRetainsFailedIntentWithoutChangingGeometry(double gap)
    {
        var document=Offset(new PolylineEntity(EntityId.Create("source"),[new(0,0),new(10,0),new(10,6),new(0,6),new(0,0)]),new(5,1));
        var d=document.Dimensions.Single();
        var result=new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,[],[new(d.Id,d.Kind,d.ReferenceKeys,gap,d.Anchor,true)]));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
    }

    [Fact]
    public void DeletingOffsetGlyphUnlinksGapWithoutDeletingGeometry()
    {
        var document=Offset(new LineEntity(EntityId.Create("source"),new(0,0),new(10,0)),new(5,1));
        var result=SelectionDeleteResolver.DeleteSelection(document,[$"constraint:{document.Constraints.Single().Id}"]);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Constraints.Should().BeEmpty();result.Document.Dimensions.Should().BeEmpty();
        var deleted=SelectionDeleteResolver.DeleteSelection(document,["source"]).Document;
        deleted.Constraints.Should().BeEmpty();deleted.Dimensions.Should().BeEmpty();
        deleted.Entities.Should().ContainSingle().Which.Id.Value.Should().Be("copy");
    }

    [Fact]
    public void SourceLengthEditPropagatesThroughOffsetChain()
    {
        var document=Offset(new LineEntity(EntityId.Create("source"),new(0,0),new(10,0)),new(5,1));
        SelectionOffsetService.TryOffset(document,["copy"],new(5,3),_=>EntityId.Create("second"),out document,out _,out _,out _,out _,2,true).Should().BeTrue();
        var result=new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,[],[
            new("length",SketchDimensionKind.LinearDistance,["source:start","source:end"],12,null,true)]));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.OfType<LineEntity>().Should().OnlyContain(l=>l.End.X-l.Start.X==12);
        result.Document.Constraints.Should().OnlyContain(c=>c.State==SketchConstraintState.Satisfied);
    }

    [Fact]
    public void ConstrainedFourLineRectangleUpdatesDependentContour()
    {
        Point2[] points=[new(0,0),new(10,0),new(10,6),new(0,6),new(0,0)];
        var edges=Enumerable.Range(0,4).Select(i=>(DrawingEntity)new LineEntity(EntityId.Create($"e{i}"),points[i],points[i+1])).ToArray();
        var constraints=Enumerable.Range(0,4).SelectMany(i=>new[]{
            new SketchConstraint($"corner{i}",SketchConstraintKind.Coincident,[$"e{i}:end",$"e{(i+1)%4}:start"],SketchConstraintState.Satisfied),
            new SketchConstraint($"axis{i}",i%2==0 ? SketchConstraintKind.Horizontal : SketchConstraintKind.Vertical,[$"e{i}"],SketchConstraintState.Satisfied)}).ToArray();
        var document=new DrawingDocument(edges,[],constraints);
        SelectionOffsetService.TryOffset(document,edges.Select(e=>e.Id.Value),new(5,-1),_=>EntityId.Create("copy"),
            out document,out _,out _,out _,out _,1,true).Should().BeTrue();
        var result=new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,[],[
            new("width",SketchDimensionKind.LinearDistance,["e0:start","e0:end"],12,null,true)]));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        var bounds=result.Document.Entities.Last().GetBounds();
        (bounds.MaxX-bounds.MinX).Should().Be(14);
        result.Document.Constraints.Should().OnlyContain(c=>c.State==SketchConstraintState.Satisfied);
    }

    [Fact]
    public void LockedDependentLayerIsNotChangedBySourceDrag()
    {
        var document=Offset(new LineEntity(EntityId.Create("source"),new(0,0),new(10,0)),new(5,1));
        document=DrawingLayerService.WithMetadata(document,document.Metadata with
        {
            EntityStyles=new Dictionary<string,DxfEntityStyle>{{"copy",new("Locked")}},
            Layers=new Dictionary<string,DxfEntityStyle>{{"Locked",new("Locked",IsLocked:true)}}
        });
        SketchGeometryDragService.TryApplyDrag(document,"source",new(5,0),new(5,2),false,out var candidate,out _,false).Should().BeTrue();
        DrawingLayerService.PreservesProtectedGeometry(document,candidate).Should().BeFalse("preview and commit must both refuse this candidate");
    }

    [Theory]
    [InlineData(1,false)]
    [InlineData(2,true)]
    public void GapEditPreservesDependentVertexIdentityAcrossRingStartAndDirection(int shift,bool reverse)
    {
        var document=Offset(new PolylineEntity(EntityId.Create("source"),[new(0,0),new(10,0),new(10,6),new(0,6),new(0,0)]),new(5,-1));
        var copy=(PolylineEntity)document.Entities.Last();
        var vertices=copy.Vertices.SkipLast(1).ToArray();
        if(reverse) vertices=vertices.Reverse().ToArray();
        vertices=vertices.Skip(shift).Concat(vertices.Take(shift)).ToArray();
        copy=new(copy.Id,vertices.Append(vertices[0]));
        document=new(document.Entities.Take(1).Append(copy),document.Dimensions,document.Constraints);
        var d=document.Dimensions.Single();
        var next=SketchDimensionSolverService.ApplyDimension(document,new(d.Id,d.Kind,d.ReferenceKeys,2,d.Anchor,true));
        var actual=((PolylineEntity)next.Entities.Last()).Vertices;
        for(var i=0;i<vertices.Length;i++)
        {
            actual[i].X.Should().Be(vertices[i].X<0 ? -2 : 12);
            actual[i].Y.Should().Be(vertices[i].Y<0 ? -2 : 8);
        }
    }
}
