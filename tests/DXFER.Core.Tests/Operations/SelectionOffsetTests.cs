using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;
using DXFER.CadIO;
using DXFER.Core.Sketching;

namespace DXFER.Core.Tests.Operations;

public sealed class SelectionOffsetTests
{
    private static readonly Point2[] Rectangle = [new(0,0), new(10,0), new(10,6), new(0,6), new(0,0)];
    private static DrawingDocument Edges() => new(Enumerable.Range(0,4)
        .Select(i => (DrawingEntity)new LineEntity(EntityId.Create($"edge{i}"), Rectangle[i], Rectangle[i+1])).Reverse());

    [Theory]
    [InlineData(5, -1, -1, -1, 11, 7)]
    [InlineData(5, 1, 1, 1, 9, 5)]
    public void SelectedRectangleOffsetsAsOneClosedContour(double x, double y, double minX, double minY, double maxX, double maxY)
    {
        var document = Edges();
        DrawingModifyService.TryOffsetSelected(document, document.Entities.Select(e => e.Id.Value), new(x,y),
            kind => EntityId.Create("copy"), out var next, out var count).Should().BeTrue();
        count.Should().Be(1);
        next.Entities.Take(4).Should().Equal(document.Entities);
        var outline = next.Entities.Last().Should().BeOfType<PolylineEntity>().Subject;
        outline.Vertices.First().Should().Be(outline.Vertices.Last());
        var bounds = new DrawingDocument([outline]).GetBounds();
        bounds.MinX.Should().BeApproximately(minX, 1e-8);
        bounds.MinY.Should().BeApproximately(minY, 1e-8);
        bounds.MaxX.Should().BeApproximately(maxX, 1e-8);
        bounds.MaxY.Should().BeApproximately(maxY, 1e-8);
    }

    [Fact]
    public void ClosedPolylineExpandsInsteadOfTranslating()
    {
        var document = new DrawingDocument([new PolylineEntity(EntityId.Create("poly"), Rectangle)]);
        DrawingModifyService.TryOffsetSelected(document, ["poly"], new(5,-1), k => EntityId.Create("copy"), out var next, out _).Should().BeTrue();
        var bounds = new DrawingDocument([next.Entities.Last()]).GetBounds();
        bounds.MinX.Should().Be(-1);
        bounds.MaxX.Should().Be(11);
        bounds.MaxY.Should().Be(7);
    }

    [Fact]
    public void UnsupportedMemberDoesNotProducePartialOffset()
    {
        var document = new DrawingDocument([new LineEntity(EntityId.Create("line"), new(0,0), new(10,0)),
            new PointEntity(EntityId.Create("point"), new(3,3))]);
        DrawingModifyService.TryOffsetSelected(document, ["line","point"], new(2,1), k => EntityId.Create("copy"), out var next, out var count).Should().BeFalse();
        next.Should().BeSameAs(document);
        count.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenConnectedEdgesAndPolylineKeepJoinedCorner(bool polyline)
    {
        DrawingEntity[] entities = polyline ? [new PolylineEntity(EntityId.Create("p"), [new(0,0),new(10,0),new(10,6)])]
            : [new LineEntity(EntityId.Create("b"),new(10,6),new(10,0)),new LineEntity(EntityId.Create("a"),new(0,0),new(10,0))];
        var document = new DrawingDocument(entities);
        DrawingModifyService.TryOffsetSelected(document, entities.Select(e=>e.Id.Value), new(5,-1), k=>EntityId.Create("copy"), out var next, out var count).Should().BeTrue();
        count.Should().Be(1);
        next.Entities.Last().Should().BeOfType<PolylineEntity>().Which.Vertices.Should().BeEquivalentTo(new Point2[]{new(0,-1),new(11,-1),new(11,6)});
    }

    [Fact]
    public void PolygonOffsetsAndNativeRoundTripPreservesOriginalIntentAndLayer()
    {
        var source = new PolygonEntity(EntityId.Create("polygon"), new(0,0), 5, 45, 4);
        var constraint = new SketchConstraint("fixed", SketchConstraintKind.Fix,["polygon"],SketchConstraintState.Satisfied);
        var style = new DxfEntityStyle("Parts", ColorNumber: 3);
        var document = new DrawingDocument([source], [], [constraint], DrawingDocumentMetadata.Empty with
        { EntityStyles = new Dictionary<string,DxfEntityStyle>{{"polygon",style}} });
        DrawingModifyService.TryOffsetSelected(document,["polygon"],new(0,5),k=>EntityId.Create("copy"),out var next,out _).Should().BeTrue();
        next.Entities[0].Should().Be(source);
        next.Constraints.Should().Equal(document.Constraints);
        next.Metadata.EntityStyles["copy"].Should().Be(style);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(next,new(IncludeNativeDocument:true)));
        reopened.Entities.Should().BeEquivalentTo(next.Entities);
        reopened.Constraints.Should().BeEquivalentTo(next.Constraints);
    }

    [Fact]
    public void DisjointContoursUseOneDistanceNotSeparateThroughPoints()
    {
        var document = new DrawingDocument([new PolylineEntity(EntityId.Create("a"),Rectangle),
            new PolylineEntity(EntityId.Create("b"),Rectangle.Select(p=>new Point2(p.X+20,p.Y)))]);
        var id=0;
        DrawingModifyService.TryOffsetSelected(document,["a","b"],new(5,-1),k=>EntityId.Create($"copy{++id}"),out var next,out var count).Should().BeTrue();
        count.Should().Be(2);
        next.Entities.Skip(2).Select(e=>e.GetBounds().MaxX-e.GetBounds().MinX).Should().Equal(12,12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BranchAndCrossingRefuseWithoutPartialOutput(bool branch)
    {
        var document = new DrawingDocument([new LineEntity(EntityId.Create("a"),new(0,0),new(10,0)),
            new LineEntity(EntityId.Create("b"),new(5,branch ? 0 : -2),new(5,2))]);
        DrawingModifyService.TryOffsetSelected(document,["a","b"],new(2,1),k=>EntityId.Create("copy"),out var next,out var count).Should().BeFalse();
        next.Should().BeSameAs(document);count.Should().Be(0);
    }

    [Fact]
    public void CollapseAndMixedStyleContourRefuse()
    {
        var document = new DrawingDocument([new PolylineEntity(EntityId.Create("p"),Rectangle)]);
        DrawingModifyService.TryOffsetSelected(document,["p"],new(5,3),k=>EntityId.Create("copy"),out var next,out _).Should().BeFalse();
        next.Should().BeSameAs(document);
        var edges=Edges();
        document=new DrawingDocument(edges.Entities,[],[],DrawingDocumentMetadata.Empty with
        { EntityStyles=new Dictionary<string,DxfEntityStyle>{{"edge0",new("Other")}}});
        DrawingModifyService.TryOffsetSelected(document,document.Entities.Select(e=>e.Id.Value),new(5,-1),k=>EntityId.Create("copy"),out next,out _).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyticCircleAndArcRemainAnalytic(bool arc)
    {
        DrawingEntity source=arc ? new ArcEntity(EntityId.Create("r"),new(0,0),3,20,130) : new CircleEntity(EntityId.Create("r"),new(0,0),3);
        var document=new DrawingDocument([source]);
        DrawingModifyService.TryOffsetSelected(document,["r"],new(0,5),k=>EntityId.Create("copy"),out var next,out _).Should().BeTrue();
        if(arc) {var copy=next.Entities.Last().Should().BeOfType<ArcEntity>().Subject;copy.Radius.Should().Be(5);copy.StartAngleDegrees.Should().Be(20);copy.EndAngleDegrees.Should().Be(130);}
        else next.Entities.Last().Should().BeOfType<CircleEntity>().Which.Radius.Should().Be(5);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void KeyedInsetUsesExactDistanceAndRefusesCollapse(double amount,bool expected)
    {
        var document=Edges();
        SelectionOffsetService.TryOffset(document,document.Entities.Select(e=>e.Id.Value),new(5,1),k=>EntityId.Create("copy"),
            out var next,out _,out _,out _,out _,amount).Should().Be(expected);
        if(expected) {var bounds=next.Entities.Last().GetBounds();bounds.MinX.Should().Be(2);bounds.MaxY.Should().Be(4);}
        else next.Should().BeSameAs(document);
    }

    [Fact]
    public void JoinedArcAndLineRefuseRatherThanCreateDisconnectedCopies()
    {
        var document=new DrawingDocument([new LineEntity(EntityId.Create("line"),new(0,0),new(5,0)),
            new ArcEntity(EntityId.Create("arc"),new(5,3),3,270,360)]);
        DrawingModifyService.TryOffsetSelected(document,["line","arc"],new(2,-1),k=>EntityId.Create("copy"),out var next,out _).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Fact]
    public void SingleLineOffsetUsesPerpendicularDistanceBeyondEndpoint()
    {
        var document=new DrawingDocument([new LineEntity(EntityId.Create("line"),new(0,0),new(10,0))]);
        DrawingModifyService.TryOffsetSelected(document,["line"],new(20,3),k=>EntityId.Create("copy"),out var next,out _).Should().BeTrue();
        var line=next.Entities.Last().Should().BeOfType<LineEntity>().Subject;
        line.Start.Should().Be(new Point2(0,3));line.End.Should().Be(new Point2(10,3));
    }
}
