# Automatic snap constraint setting

## Layout and behavior contract

- File > Settings opens a centered compact dialog using the existing Appearances
  dimensions, theme tokens, close icon, focus trap and Escape/canvas-focus behavior.
- One checkbox row, aligned with its wrapping label, minimum 32px interaction
  height. Dialog width is at most 360px, bounded by the viewport; overflow scrolls
  inside the dialog. No canvas, header or palette rearrangement.
- Automatic snap constraints defaults on and is stored per browser, not in a
  drawing. Off preserves snap locations/cues but suppresses new inferred relations
  during creation and drag. It does not remove existing constraints, disable their
  enforcement, remove keyed dimensions or change intrinsic rectangle/slot topology.
- A newly drawn standalone axis-aligned line does not acquire H/V when off.
- Existing hover-acquired projections/intersections, perpendicular/normal and
  tangent candidates remain available. This setting does not expand the supported
  persistent reference model to every virtual or curved snap.

## Verification

- 1,757 Core/component tests and 921 JavaScript tests pass; Web build passes.
- New regression coverage: enabled/disabled origin, standalone H/V,
  perpendicular, tangent and typed projection intent; rectangle topology retained;
  drag capture disabled without disabling existing anchor constraints.
- Settings joins the observer lifecycle regression matrix (reentrant close,
  disposal during close, disconnected cleanup).
- Actual local browser: midpoint creation and endpoint drag with setting off/on,
  unchanged snap coordinates/source geometry, Undo/Redo, native reopen, existing
  Midpoint still enforced while off; preference survives reload; repeated dialog
  close/open and Escape focus return; 1188x761 and 1100x705 screenshots.
- Five local exported DXFs audit with zero errors/fixes. No page/circuit errors.
- DEV `dc108c0f4d5b70127d4d493d1ded8ec9a8d83415` installed
  2026-10-19T02:01Z. Public replay of the above browser scope passes, rendered
  revision matches, and five public DXFs audit with zero errors/fixes. Screenshots
  inspected at both desktop sizes. No page/circuit errors or server warnings.
- Four installed assembly hashes match, ten runtime settings preserved, health
  200, anonymous root 302. Normal sign-in and disabled Sync callbacks retained.
  Production/Test process IDs unchanged. Rollback:
  `/home/ryan/dxfer-runtime/dev-publish-before-dc108c0`.
- Evidence: `/tmp/automatic-snap-{core,js,build,publish,local,dev}.log`, screenshots
  and DXFs in `/tmp/automatic-snap-local` and `/tmp/automatic-snap-dev`.
- Owner verification only; independent replay requested, no blanket solver or
  all-snap acceptance.

## Scope and remaining work

The shared creation and drag capture boundaries now honor one browser preference.
Existing drawing constraints are never removed or reconstructed. No record repair
is required. Older files missing snap intent still require deliberate constraints,
not a speculative backfill.

Hover-acquired H/V guides and two-source implied intersections already exist in
`drawingCanvas.js`, as do a two-source virtual midpoint, line-start perpendicular/
curve-normal candidates and dynamic tangents. Supported H/V point sources persist
as two relations for an implied intersection. Virtual midpoint, circle quadrants,
and general curved intersection/reference persistence remain separate incomplete
cases; this change does not claim to finish every implicit snap.
