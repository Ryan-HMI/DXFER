# Selection offset

## Contract before implementation

- Keep the existing compact Modify palette, canvas, inline distance editor and bottom feedback. No new panel, changed widths, wrapping or overflow behavior. Desktop workflow only.
- Offset the entire selected contour, including four-line rectangles, open/closed polylines and polygons. Keep source entities, dimensions and constraints unchanged. New contours inherit source styling.
- Use one distance measured from the closest selected contour; choose the side of each contour from the through point. Closed contours use inside/outside. Sharp joins have a bounded mitre limit of 5.
- Preview and commit use the same core calculation. Coalesce pointer preview requests and discard stale replies. Unsupported, branched, mixed-style connected paths or collapsed results refuse atomically with feedback, never a partial offset.
- Do not translate a spline and call that an offset. Curved spline/ellipse offsets remain unsupported. No automatic exploding or conversion of existing rectangles.
- Test inward/outward rectangles, unordered edges, polylines, polygons, multiple contours, invalid topology, original intent/style preservation and actual browser history/reopen.

## Status

Installed on DXFer DEV only at `11d3f606240769deb26910e9ac2319e9dbca9248`, 2026-10-19T03:42Z. Production/Test untouched. Normal sign-in and disabled Sync callbacks retained.

## Root cause and implementation

The old operation independently copied each selected entity through the pointer. Polyline and spline handling simply translated their vertices/control points. It never constructed an offset contour. The old JS preview repeated that incorrect approximation.

SelectionOffsetService now joins selected straight paths with NetTopologySuite LineMerger and uses polygon buffers/open offset curves with bounded sharp joins. Multiple independent contours use one distance and each contour's picked side. Circles and individual arcs stay analytic. Preview calls the same core operation, with one outstanding request and stale-result suppression. Numeric distance is independent of the side pick so oversized insets cannot flip sides or silently shrink. Rejected offsets keep their key-in and show the actual reason.

Rectangles remain four constrained lines. New straight offset contours are polylines; no special rectangle entity or implicit Explode operation was added. Original references, dimensions, constraints and styles remain untouched. Existing erroneous offset copies cannot be safely reconstructed automatically; use Undo or recreate from a verified original. No stored drawings are rewritten by deployment.

## Verification

- 1,780 core tests and 934 JS tests pass; Web build has zero warnings/errors.
- Four initial regressions fail against the old code (rectangle inward/outward, closed polyline, partial unsupported selection).
- Additional tests cover open chains, polygons, multiple contours, topology/branch refusal, metadata and native intent, circle/arc preservation, exact numeric distance, collapsed inset and mixed-style refusal, and async preview coalescing.
- Actual local browser: rectangle, closed polyline, polygon and open chain selection/preview/commit, original intent preservation, Undo/Redo and native download/reopen. Unsupported selection makes no edit; keyed inset 4 refuses without history, correction to 2 succeeds. No circuit/page errors. Preview and inset screenshots inspected.
- 15 local DXF exports audit with zero errors/fixes.
- Public Dev replay passes the same actual browser cases, including unsupported-selection refusal and keyed-inset refusal/recovery. Rendered build `11d3f60` verified; 15 public DXFs audit zero errors/fixes, served screenshot inspected, no page/circuit errors or service warning entries.
- Installed assemblies match the published candidate; all 10 tracked runtime settings retained, health 200 and anonymous root 302. Production/Test runtime PIDs unchanged. Rollback directory: `/home/ryan/dxfer-runtime/dev-publish-before-11d3f60`. Independent Flux/Ryan acceptance remains pending.

## Bounded limitations

Curved spline/ellipse offsets and multi-entity selections containing arcs explicitly refuse; these are not approximated or partially committed. Branched/crossing or mixed-style connected paths refuse. Insets that collapse or split a contour refuse. This is whole-contour offset, not a filled-region Boolean operation assigning hole semantics to nested independent contours. Explode remains a separate future operation and is not required to offset current rectangles/polylines.
