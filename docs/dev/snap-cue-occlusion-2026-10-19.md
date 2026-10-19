# Snap cue occlusion

## Layout contract

Keep the existing two-line snap cue, typography, palette sizes, docking and saved positions. Place the cue near its snap point within an eight-pixel canvas margin, without overlapping visible tool groups, Sync controls, dimension inputs or the pointer. Try the existing four quadrants first, then the nearest clear placement at obstruction boundaries. If no full label fits, suppress its canvas box rather than draw misleading partial text; retain the accessible snap description. No geometry, constraint or snapping changes.

## Cause

The renderer included dimension inputs and the pointer as placement obstacles but omitted tool palettes. Its fallback also returned an obstructed candidate when all four preferred positions were blocked. Share the existing Fit Extents palette-bound collection and remove that unsafe fallback.

## Verification

- Public baseline `10c5177` reproduces the OFF cue behind floating Grain, with the measured rectangles overlapping: `/tmp/dxfer-snap-cue-occlusion-baseline/`. Screenshot inspected.
- All 1008 JavaScript tests pass, including canvas-relative palette bounds, all preferred quadrants obstructed, and no available display space. Web build passes with zero warnings/errors.
- Local browser replay passes four combinations of 1188x761 / 1100x705 and automatic constraints ON/OFF. Floating, relocated and collapsed/right-docked Grain do not obscure the cue. Eight before/after native exports have matching geometry, dimensions and constraints, valid checksums and zero DXF audit errors/fixes. Undo remains disabled. Browser errors empty; screenshots inspected. Evidence: `/tmp/dxfer-snap-cue-occlusion-local/`. Local tooltip shows precommit base `f35595d`; corrected canvas SHA256 is `faf09602198649e39ce56060192d6c0beb0f909181c6c3c2ddf2e0391252d1a0`.
- No existing drawings or records require repair. Public deployment verification pending.
