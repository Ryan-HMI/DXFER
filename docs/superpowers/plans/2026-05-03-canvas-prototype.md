# DXFER Canvas Prototype Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first DXFER canvas prototype: a .NET 8 Blazor app rendering document-backed 2D entities with hover highlighting, click selection, and prototype pan/zoom/fit.

**Architecture:** Create a .NET 8 solution with `DXFER.Core` as the document and geometry source of truth, `DXFER.Blazor` as a reusable Razor component library, and `DXFER.Web` as the first runnable host. The canvas is a JavaScript module that renders serialized document state and reports hover/selection events back to Blazor.

**Tech Stack:** .NET 8 target framework, Blazor Web App host, Razor Class Library, xUnit, vanilla JavaScript canvas module.

---

## File Structure

- `DXFER.sln`: solution file.
- `src/DXFER.Core/DXFER.Core.csproj`: .NET 8 core library.
- `src/DXFER.Core/Geometry/Point2.cs`: immutable 2D point/vector value type.
- `src/DXFER.Core/Geometry/Bounds2.cs`: immutable axis-aligned bounds type.
- `src/DXFER.Core/Geometry/Transform2.cs`: affine transform helper for future prep commands.
- `src/DXFER.Core/Documents/EntityId.cs`: stable entity identifier.
- `src/DXFER.Core/Documents/DrawingEntity.cs`: base entity abstraction plus serializable DTO shape.
- `src/DXFER.Core/Documents/LineEntity.cs`: line entity.
- `src/DXFER.Core/Documents/CircleEntity.cs`: circle entity.
- `src/DXFER.Core/Documents/ArcEntity.cs`: arc entity.
- `src/DXFER.Core/Documents/PolylineEntity.cs`: polyline entity.
- `src/DXFER.Core/Documents/DrawingDocument.cs`: document aggregate.
- `src/DXFER.Core/Documents/SampleDrawingFactory.cs`: generated fixture drawing for the canvas milestone.
- `tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj`: xUnit tests.
- `tests/DXFER.Core.Tests/Geometry/BoundsTests.cs`: bounds and geometry behavior tests.
- `tests/DXFER.Core.Tests/Documents/SampleDrawingFactoryTests.cs`: fixture document tests.
- `src/DXFER.Blazor/DXFER.Blazor.csproj`: Razor Class Library.
- `src/DXFER.Blazor/Components/DrawingCanvas.razor`: reusable canvas component.
- `src/DXFER.Blazor/Components/DrawingCanvas.razor.css`: component layout.
- `src/DXFER.Blazor/Components/DrawingCanvas.razor.cs`: component code-behind.
- `src/DXFER.Blazor/Interop/CanvasDocumentDto.cs`: DTOs passed to JavaScript.
- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`: JavaScript canvas renderer and hit tester.
- `src/DXFER.Web/DXFER.Web.csproj`: Blazor Web App host.
- `src/DXFER.Web/Components/Pages/Home.razor`: prototype page hosting the drawing canvas.
- `src/DXFER.Web/Components/Layout/MainLayout.razor`: app shell layout.

## Task 1: Scaffold The .NET 8 Solution

**Files:**
- Create: `DXFER.sln`
- Create: `src/DXFER.Core/DXFER.Core.csproj`
- Create: `src/DXFER.Blazor/DXFER.Blazor.csproj`
- Create: `src/DXFER.Web/DXFER.Web.csproj`
- Create: `tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj`

- [ ] **Step 1: Create the solution and project folders**

Run:

```powershell
dotnet new sln -n DXFER
dotnet new classlib -n DXFER.Core -o src/DXFER.Core -f net8.0
dotnet new razorclasslib -n DXFER.Blazor -o src/DXFER.Blazor -f net8.0
dotnet new blazor -n DXFER.Web -o src/DXFER.Web -f net8.0 --interactivity Server
dotnet new xunit -n DXFER.Core.Tests -o tests/DXFER.Core.Tests -f net8.0
dotnet sln DXFER.sln add src/DXFER.Core/DXFER.Core.csproj
dotnet sln DXFER.sln add src/DXFER.Blazor/DXFER.Blazor.csproj
dotnet sln DXFER.sln add src/DXFER.Web/DXFER.Web.csproj
dotnet sln DXFER.sln add tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj
dotnet add src/DXFER.Blazor/DXFER.Blazor.csproj reference src/DXFER.Core/DXFER.Core.csproj
dotnet add src/DXFER.Web/DXFER.Web.csproj reference src/DXFER.Blazor/DXFER.Blazor.csproj
dotnet add src/DXFER.Web/DXFER.Web.csproj reference src/DXFER.Core/DXFER.Core.csproj
dotnet add tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj reference src/DXFER.Core/DXFER.Core.csproj
```

Expected: all project creation and reference commands exit `0`.

- [ ] **Step 2: Remove unused template files**

Delete generated placeholder files that are not part of the DXFER shape:

```powershell
Remove-Item -LiteralPath src/DXFER.Core/Class1.cs -ErrorAction SilentlyContinue
Remove-Item -LiteralPath tests/DXFER.Core.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
```

Expected: command exits `0`.

- [ ] **Step 3: Build the empty scaffold**

Run:

```powershell
dotnet build DXFER.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit the scaffold**

Run:

```powershell
git add DXFER.sln src tests
git commit -m "Scaffold DXFER .NET solution"
```

Expected: commit succeeds.

## Task 2: Add Core Geometry And Document Entities

**Files:**
- Create: `tests/DXFER.Core.Tests/Geometry/BoundsTests.cs`
- Create: `tests/DXFER.Core.Tests/Documents/SampleDrawingFactoryTests.cs`
- Create: `src/DXFER.Core/Geometry/Point2.cs`
- Create: `src/DXFER.Core/Geometry/Bounds2.cs`
- Create: `src/DXFER.Core/Geometry/Transform2.cs`
- Create: `src/DXFER.Core/Documents/EntityId.cs`
- Create: `src/DXFER.Core/Documents/DrawingEntity.cs`
- Create: `src/DXFER.Core/Documents/LineEntity.cs`
- Create: `src/DXFER.Core/Documents/CircleEntity.cs`
- Create: `src/DXFER.Core/Documents/ArcEntity.cs`
- Create: `src/DXFER.Core/Documents/PolylineEntity.cs`
- Create: `src/DXFER.Core/Documents/DrawingDocument.cs`
- Create: `src/DXFER.Core/Documents/SampleDrawingFactory.cs`

- [ ] **Step 1: Write failing geometry tests**

Create `tests/DXFER.Core.Tests/Geometry/BoundsTests.cs`:

```csharp
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Geometry;

public sealed class BoundsTests
{
    [Fact]
    public void LineBoundsIncludeBothEndpoints()
    {
        var line = new LineEntity(EntityId.Create("L1"), new Point2(-2, 3), new Point2(5, -7));

        var bounds = line.GetBounds();

        bounds.MinX.Should().Be(-2);
        bounds.MinY.Should().Be(-7);
        bounds.MaxX.Should().Be(5);
        bounds.MaxY.Should().Be(3);
        bounds.Width.Should().Be(7);
        bounds.Height.Should().Be(10);
    }

    [Fact]
    public void DocumentBoundsUnionAllEntityBounds()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("L1"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("C1"), new Point2(4, 5), 2)
        });

        var bounds = document.GetBounds();

        bounds.MinX.Should().Be(0);
        bounds.MinY.Should().Be(0);
        bounds.MaxX.Should().Be(10);
        bounds.MaxY.Should().Be(7);
    }

    [Fact]
    public void TransformRotatesLineAroundOrigin()
    {
        var line = new LineEntity(EntityId.Create("L1"), new Point2(2, 0), new Point2(2, 3));

        var rotated = (LineEntity)line.Transform(Transform2.RotationDegrees(90));

        rotated.Start.X.Should().BeApproximately(0, 0.000001);
        rotated.Start.Y.Should().BeApproximately(2, 0.000001);
        rotated.End.X.Should().BeApproximately(-3, 0.000001);
        rotated.End.Y.Should().BeApproximately(2, 0.000001);
    }
}
```

Create `tests/DXFER.Core.Tests/Documents/SampleDrawingFactoryTests.cs`:

```csharp
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
        document.Entities.Select(e => e.Id.Value).Should().OnlyHaveUniqueItems();
        document.GetBounds().Width.Should().BeGreaterThan(0);
        document.GetBounds().Height.Should().BeGreaterThan(0);
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj
```

Expected: tests fail because `DXFER.Core.Documents` and `DXFER.Core.Geometry` types do not exist.

- [ ] **Step 3: Add FluentAssertions test package**

Run:

```powershell
dotnet add tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj package FluentAssertions --version 6.12.0
```

Expected: package add succeeds.

- [ ] **Step 4: Implement core geometry and entities**

Create the files listed for this task with these public APIs:

```csharp
namespace DXFER.Core.Geometry;

public readonly record struct Point2(double X, double Y)
{
    public Point2 Transform(Transform2 transform) => transform.Apply(this);
}
```

```csharp
namespace DXFER.Core.Geometry;

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public static Bounds2 FromPoints(IEnumerable<Point2> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext()) return Empty;
        var minX = enumerator.Current.X;
        var minY = enumerator.Current.Y;
        var maxX = enumerator.Current.X;
        var maxY = enumerator.Current.Y;
        while (enumerator.MoveNext())
        {
            var point = enumerator.Current;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        return new Bounds2(minX, minY, maxX, maxY);
    }

    public static Bounds2 Empty => new(0, 0, 0, 0);

    public Bounds2 Union(Bounds2 other) =>
        new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
}
```

```csharp
namespace DXFER.Core.Geometry;

public readonly record struct Transform2(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static Transform2 Identity => new(1, 0, 0, 1, 0, 0);

    public static Transform2 RotationDegrees(double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Transform2(cos, -sin, sin, cos, 0, 0);
    }

    public static Transform2 Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public Point2 Apply(Point2 point) =>
        new(point.X * M11 + point.Y * M12 + OffsetX, point.X * M21 + point.Y * M22 + OffsetY);
}
```

Entity implementations must expose:

```csharp
namespace DXFER.Core.Documents;

public readonly record struct EntityId(string Value)
{
    public static EntityId Create(string value) => new(value);
}

public abstract record DrawingEntity(EntityId Id)
{
    public abstract string Kind { get; }
    public abstract Bounds2 GetBounds();
    public abstract DrawingEntity Transform(Transform2 transform);
}
```

`LineEntity`, `CircleEntity`, `ArcEntity`, and `PolylineEntity` must inherit `DrawingEntity`, implement bounds, implement transforms, and store typed parameters. `ArcEntity` may compute prototype bounds from sampled start/mid/end points for this milestone.

`DrawingDocument` must expose `IReadOnlyList<DrawingEntity> Entities` and `Bounds2 GetBounds()`.

`SampleDrawingFactory.CreateCanvasPrototype()` must return a document with several lines, one circle, one arc, and one polyline.

- [ ] **Step 5: Run tests to verify GREEN**

Run:

```powershell
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj
```

Expected: tests pass.

- [ ] **Step 6: Commit core model**

Run:

```powershell
git add src/DXFER.Core tests/DXFER.Core.Tests
git commit -m "Add core drawing model"
```

Expected: commit succeeds.

## Task 3: Add Blazor Canvas Component Boundary

**Files:**
- Create: `src/DXFER.Blazor/Interop/CanvasDocumentDto.cs`
- Create: `src/DXFER.Blazor/Components/DrawingCanvas.razor`
- Create: `src/DXFER.Blazor/Components/DrawingCanvas.razor.cs`
- Create: `src/DXFER.Blazor/Components/DrawingCanvas.razor.css`

- [ ] **Step 1: Add component files**

Create `CanvasDocumentDto.cs` with DTOs:

```csharp
namespace DXFER.Blazor.Interop;

public sealed record CanvasDocumentDto(IReadOnlyList<CanvasEntityDto> Entities, CanvasBoundsDto Bounds);

public sealed record CanvasEntityDto(
    string Id,
    string Kind,
    IReadOnlyList<CanvasPointDto> Points,
    CanvasPointDto? Center,
    double? Radius,
    double? StartAngleDegrees,
    double? EndAngleDegrees);

public sealed record CanvasPointDto(double X, double Y);

public sealed record CanvasBoundsDto(double MinX, double MinY, double MaxX, double MaxY);
```

Create `DrawingCanvas.razor`:

```razor
@namespace DXFER.Blazor.Components
@using DXFER.Core.Documents

<div class="drawing-shell">
    <div class="drawing-toolbar">
        <button type="button" @onclick="FitToExtentsAsync">Fit</button>
        <span>Hover: @(HoveredEntityId ?? "none")</span>
        <span>Selected: @SelectedEntityIds.Count</span>
    </div>
    <canvas @ref="_canvas" class="drawing-canvas"></canvas>
</div>
```

Create code-behind that accepts `[Parameter] public DrawingDocument? Document { get; set; }`, maps it to DTOs, imports `./_content/DXFER.Blazor/drawingCanvas.js`, and exposes `[JSInvokable]` callbacks `OnEntityHovered(string? entityId)` and `OnEntityClicked(string entityId)`.

Create CSS that makes the shell full-height, dark canvas, and compact toolbar.

- [ ] **Step 2: Build to verify component compiles**

Run:

```powershell
dotnet build src/DXFER.Blazor/DXFER.Blazor.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit component boundary**

Run:

```powershell
git add src/DXFER.Blazor
git commit -m "Add Blazor drawing canvas boundary"
```

Expected: commit succeeds.

## Task 4: Implement JavaScript Canvas Rendering, Hover, And Selection

**Files:**
- Create: `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- Modify: `src/DXFER.Blazor/Components/DrawingCanvas.razor.cs`

- [ ] **Step 1: Add JavaScript module**

Create `drawingCanvas.js` with exported functions:

```javascript
export function createDrawingCanvas(canvas, dotnetRef) {
  const state = {
    canvas,
    dotnetRef,
    document: null,
    hoveredId: null,
    selectedIds: new Set(),
    view: { scale: 1, offsetX: 0, offsetY: 0 },
    dragging: false,
    lastPointer: null
  };

  const resize = () => {
    const rect = canvas.getBoundingClientRect();
    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.floor(rect.width * ratio));
    canvas.height = Math.max(1, Math.floor(rect.height * ratio));
    canvas.getContext('2d').setTransform(ratio, 0, 0, ratio, 0, 0);
    draw(state);
  };

  canvas.addEventListener('pointermove', event => handlePointerMove(state, event));
  canvas.addEventListener('pointerdown', event => handlePointerDown(state, event));
  canvas.addEventListener('pointerup', () => { state.dragging = false; state.lastPointer = null; });
  canvas.addEventListener('wheel', event => handleWheel(state, event), { passive: false });
  window.addEventListener('resize', resize);
  resize();

  return {
    setDocument(document) {
      state.document = document;
      fitToExtents(state);
      draw(state);
    },
    fitToExtents() {
      fitToExtents(state);
      draw(state);
    },
    dispose() {
      window.removeEventListener('resize', resize);
    }
  };
}
```

The module must implement:

- line, polyline, circle, and arc drawing
- world-to-screen and screen-to-world transforms
- hit testing with a screen-space tolerance
- hover style distinct from selected style
- click toggles selected entity ID
- middle/right drag or Shift+left drag pans
- wheel zooms around cursor
- fit to extents

- [ ] **Step 2: Wire module from component code-behind**

Ensure `DrawingCanvas.razor.cs` calls:

```csharp
_module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/DXFER.Blazor/drawingCanvas.js");
_canvasInstance = await _module.InvokeAsync<IJSObjectReference>("createDrawingCanvas", _canvas, _dotNetRef);
await _canvasInstance.InvokeVoidAsync("setDocument", dto);
```

- [ ] **Step 3: Build to verify interop compiles**

Run:

```powershell
dotnet build DXFER.sln
```

Expected: build succeeds.

- [ ] **Step 4: Commit canvas renderer**

Run:

```powershell
git add src/DXFER.Blazor
git commit -m "Add interactive canvas renderer"
```

Expected: commit succeeds.

## Task 5: Add Runnable Web Prototype And Verify In Browser

**Files:**
- Modify: `src/DXFER.Web/Components/Pages/Home.razor`
- Modify: `src/DXFER.Web/Components/Layout/MainLayout.razor`
- Modify: `src/DXFER.Web/Components/Layout/NavMenu.razor` if generated template includes one.
- Modify: `src/DXFER.Web/wwwroot/app.css`

- [ ] **Step 1: Replace home page with DXFER prototype**

`Home.razor` should render the sample document:

```razor
@page "/"
@using DXFER.Blazor.Components
@using DXFER.Core.Documents

<PageTitle>DXFER Canvas Prototype</PageTitle>

<DrawingCanvas Document="_document" />

@code {
    private readonly DrawingDocument _document = SampleDrawingFactory.CreateCanvasPrototype();
}
```

- [ ] **Step 2: Simplify layout**

Make the app body full viewport height and remove template marketing content. Keep visible title text minimal: `DXFER`.

- [ ] **Step 3: Run tests and build**

Run:

```powershell
dotnet test DXFER.sln
dotnet build DXFER.sln
```

Expected: tests pass and build succeeds.

- [ ] **Step 4: Start the dev server**

Run:

```powershell
dotnet run --project src/DXFER.Web/DXFER.Web.csproj --urls http://localhost:5197
```

Expected: app listens on `http://localhost:5197`.

- [ ] **Step 5: Browser verification**

Open `http://localhost:5197` and verify:

- fixture entities are visible
- hovering near an entity changes its highlight
- clicking toggles selection
- selected state remains after hover moves away
- Fit button restores extents
- wheel zoom and drag pan work

- [ ] **Step 6: Commit runnable prototype**

Run:

```powershell
git add src/DXFER.Web src/DXFER.Blazor
git commit -m "Add runnable canvas prototype"
```

Expected: commit succeeds.

## Execution Notes

- Use subagents with disjoint write ownership after Task 1 is complete:
  - Core worker owns `src/DXFER.Core/**` and `tests/DXFER.Core.Tests/**`.
  - Blazor worker owns `src/DXFER.Blazor/Components/**` and `src/DXFER.Blazor/Interop/**`.
  - Canvas worker owns `src/DXFER.Blazor/wwwroot/drawingCanvas.js`.
  - Web host worker owns `src/DXFER.Web/**`.
- Do not let third-party library types leak into public app or component APIs.
- Keep the first milestone on fixture geometry if DXF import is not ready.
- Leave Auto Orient Bounds for a later stretch pass after the canvas prototype is usable.
