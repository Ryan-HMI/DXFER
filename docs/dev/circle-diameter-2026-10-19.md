# Circle creation diameter

## Contract

Center-circle creation displays and keys diameter, with the existing diameter symbol beside the numeric input. A keyed value D produces radius D/2 and a persistent driving Diameter dimension. Pointer-only placement stays free unless other intentional constraints apply. Arc and polygon radius inputs, and existing saved Radius dimensions, retain their meaning; no drawing migration.

Preserve desktop palette/header/inspector layout, input width, clamping, focus and keyboard behavior. Reserve a small noninteractive symbol area beside the existing input without covering digits or stealing pointer events. Reuse the existing diameter display convention and dimension input styling. No new panel, wrapping or responsive layout changes.

## Verification

- Shared creation factories now consume the diameter key, create radius D/2 and retain a driving Diameter dimension. Legacy radius keys and existing Radius dimensions are deliberately unchanged. Pointer-only circle placement adds no forced driving size.
- Focused core tests: 34 pass, including 15 new diameter/compatibility/native-persistence cases. Full core suite: 1947 pass. JavaScript suite: 943 pass. Web build: zero warnings/errors.
- Local actual-browser replay: four cases pass for diameter creation, symbol, 5-to-6 edit, Undo/Redo/reopen, leading decimal `.25`, untyped circle and arc-radius control. Evidence: `/tmp/dxfer-circle-diameter-local2/`. The first run exposed an overstrict mouse-center assertion (2.000000303 versus 2), not a geometry defect; only mouse-picked coordinates use pointer tolerance, while keyed radius and diameter remain strictly checked.
- Circle preview screenshot inspected: symbol is visible beside the unchanged-size input, with no digit overlap. The symbol has a dark backing for contrast on custom light backgrounds. CSS and canvas module versions are updated.
- Final local replay also verifies symbol/input clearance and light-canvas contrast: `/tmp/dxfer-circle-diameter-local-final/`.
- Installed Dev only at `2989909dcc8712f3225f02e7f33dba150bf030b9`, 2026-10-19 05:42 UTC. Public browser verified the rendered build and all four cases, including approximately 3px symbol/input clearance and dark/light backgrounds. Evidence: `/tmp/dxfer-circle-diameter-dev/`; no browser errors, 15 exported DXFs with valid native checksums and zero DXF audit errors/fixes.
- Health 200, anonymous root 302 to normal sign-in, installed assemblies matched the candidate, and all 10 tracked runtime settings were preserved. Production/Test processes were unchanged. Dev warning/error journal was empty after deployment. Sync callbacks remain disabled.
- The shared circle-creation input, geometry factory and dimension factory are corrected together. No existing records rewritten; no migration or repair required. Ryan's separate dropped-decimal/live-preview report remains open; this bounded circle test is not acceptance of that report.
