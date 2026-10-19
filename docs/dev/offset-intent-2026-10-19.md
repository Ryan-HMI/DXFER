# Persistent offset intent

Dev-only candidate; no production/test changes.

## Contract

- Each new offset contour references its source entities and carries an Offset relation.
- A keyed positive gap also creates an editable driving Offset dimension.
- Source edits propagate to the dependent contour. Incompatible target edits refuse atomically.
- No guessed links for previously saved geometry-only offsets.
- Reuse existing canvas dimension labels, leader placement, edit/delete and glyph hit targets.
  Offset glyph uses the existing Offset tool icon. No panel width or docking changes.
  Labels float with geometry and remain in the drawing viewport; desktop layout stays compact.
- History and native save/reopen retain links, dimensions and failed intent.
- Verify source changes, gap edits, locked/fixed conflicts and real preview/release before deployment.

## Implementation

The old command saved only copied geometry. New command commits now add an explicit
Offset constraint, with ordered source entity IDs and the dependent contour ID.
Keyed gaps add an Offset driving dimension. The shared contour engine regenerates
dependents during source changes and dimension edits; full-contour validation rejects
nonuniform copies rather than accepting a matching distance at one point.

Constraint and dimension enums round-trip through existing native DXF serialization.
Deleting the Offset glyph removes its gap dimension and leaves both geometries.
Deleting a source removes dangling intent, not the remaining copy. Deleting only the
gap dimension retains the offset relationship with a free gap. Unkeyed offsets carry
the relationship without inventing a driving value.

Offset-containing drag previews use coalesced core requests, with stale/canceled
responses discarded. Preview and commit both enforce layer protection. Hover targets
refresh against preview geometry so no old-position highlight remains.

## Verification

- Core suite: 1,797 passing cases, including 17 new offset-intent cases.
- JavaScript suite: 936 passing cases, including glyph/witness and stale-preview tests.
- Browser: 1188x761 desktop; rectangle, closed polyline, polygon and open contour;
  keyed inset/collapse/recovery; editable gap; Undo/Redo and native reopen/edit again.
- Held-pointer screenshot and pixel checks verify the offset follows the source and
  neither the old copy nor old hover highlight remains. Original source geometry and
  existing intent are unchanged by creation.
- 20 browser-exported DXFs audit with zero errors/fixes. Local evidence:
  `/tmp/offset-intent-local`. Web build: zero warnings/errors.

## Boundaries

Existing geometry-only offsets are not inferred or relinked. Recreate those offsets to
get the new intent. No saved manufacturing files or business records were changed.
The existing unsupported spline/ellipse/joined-arc and ambiguous contour refusals remain.
Persistent relationships cap straight source/target contours at 2048 segments and refuse
changes that alter the dependent polyline vertex count or cannot uniquely identify its
source edges. Existing vertex indices are preserved across ring rotation/reversal.
General simultaneous solving of arbitrary additional target
constraints is not claimed. Failed edits retain geometry and report unresolved intent.
Production/Test remain on older native readers; use this feature in DXFer Dev.
