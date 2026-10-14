# Spline trim fidelity

## Cause And Correction

Trimming replaced the retained portion with a degree-one spline through display
samples. Undo retained the original, but Redo and export correctly persisted the
already-degraded result. This was a trim construction defect, not an export defect.

The shared operation now retains knot parameters and splits supported splines
by homogeneous knot insertion, preserving degree, rational weights and the
retained curve. Fit-point Hermite spans are represented as equivalent cubic
Bezier spans before trimming. Unsupported non-clamped/discontinuous curves and
higher-degree/fit-point extension fail unchanged. Degree-one extension preserves
its existing parameterization. Existing intersection discovery remains numerical;
this is not a claim of exhaustive intersection detection for arbitrary curves.

Power Trim's existing status slot now shows actual results and refusals instead
of masking them with a constant modal prompt. Layout contract is in
`trim-result-feedback-2026-10-14.md`; no new controls or layout changes.

## Verification And Rollout

Dev20b645c deployed October14 around19:25UTC, including workerfbc058b via
63c47ab/60878b7 and the exact Flux fixture20b645c. The earlier worker commit was
amended before handoff; integration retains its final identical source and test
contents plus the additional Flux regression.807 .NET and273 JS tests pass.

Installed browser checks use two independent cubic fixtures, including Flux's
negative-Y case. Both retain degree3 and the four expected control points,
preserve unrelated geometry, and pass Undo/Redo plus Download/New/Open/Download.
The positive-Y browser regression failed on the prior deployment with degree1,
and passes with this fix. No browser errors. Screenshots inspected.

Independent ezdxf reads both exports with zero audit errors or fixes. The Flux
export retains controls(2,-4),(2.75,-4.5),(3.5,-5),(4,-4). After accounting for
ezdxf's parameter normalization,10001 samples differ from the original retained
cubic by at most2.98e-15 inches. This is a sampled comparison, not an asserted
global error bound. The exact control polygon is checked separately.

Local browser harness: `/tmp/dxfer-spline-trim-live-qa.cjs`, with `--flux` for
the independent negative-Y fixture. Actual browser supplies a projected hit;
raw cursor coordinates are not passed directly to the core trim API.

Normal Google authentication and disabled Sync callbacks retained; health200,
anonymous root302, ten runtime settings preserved and all four installed
assemblies match Release. Production/Test runtime PIDs unchanged. Rollback:
`/home/ryan/dxfer-runtime/dev-publish-before-20b645c`.

No existing drawings were rewritten. Previously flattened saved curves cannot
be reconstructed reliably; use Undo while available or recover a verified
original. Flux independent retesting requested, not claimed accepted here.
