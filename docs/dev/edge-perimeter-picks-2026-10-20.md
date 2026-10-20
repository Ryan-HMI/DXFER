# Edge-to-circle perimeter picking

Ryan clarified that the requested dimension is an edge-to-circle perimeter gap.
Body picks already used PerimeterDistance, but quadrant picks against an edge
were demoted to explicit points by the command factory and preview mapper. This
could produce a point-to-line value measured at the chosen quadrant instead.

The shared dimension classification now treats circle perimeter points as the
whole curve when paired with a straight edge, including polyline segments.
Preview and committed references agree in both pick orders. Explicit centers,
arc endpoints and point-only measurements keep their existing meaning. The
native perimeter equations are unchanged. The canvas module version is bumped
so a refresh loads the corrected picking behavior.

Four factory and four JavaScript regressions failed before the correction.
Additional center/arc-endpoint controls preserve point-to-edge behavior. The old
quadrant-to-line test intentionally changes from distance 5 to nearest gap 1.
The published browser test passes circle body/edge and circle quadrant/edge in
both orders, plus its previous arc/point control: placement, editing, Undo/Redo,
native DXF reopen and another edit. Exported geometry is independently measured
in the harness; screenshots inspected. No circuit or browser errors.

Evidence: `/tmp/dxfer-edge-perimeter-browser`,
`/tmp/dxfer-edge-perimeter-core-final.log`,
`/tmp/dxfer-edge-perimeter-js-full.log`.

No existing drawings are rewritten. A previously created point-to-line
dimension cannot safely be inferred to mean perimeter clearance; delete and
recreate that dimension with the corrected picking path. Dev-only rollout;
Production/Test and Sync remain unchanged.

## Installed verification

Dev `bb7ee56` was verified from the rendered tooltip on the public URL.
All four browser cases above passed again against the installed endpoint;
screenshots inspected, no browser/circuit errors. Evidence is in
`/tmp/dxfer-edge-perimeter-installed` and its sibling `.log` file.
Full core suite: 3,043 passed. Full JavaScript suite: 1,777 passed.
Google authentication remains configured and anonymous requests still redirect
to sign-in. No auth settings changed. Rollback publish directory:
`/home/ryan/dxfer-runtime/dev-publish-before-bb7ee56`.
Flux received the exact independent replay fixture; no independent acceptance
is claimed here.
