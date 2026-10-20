# Radial Drag Capacity Diagnosis

Read-only application investigation, 2026-10-20. No application source,
timeout, deployment, or .NET build changes. The isolated diagnostic harness
and evidence in this directory are not runtime dependencies.

## Measurements

The existing `planeGcsBenchmark.mjs` fixture creates one source circle and
127/255/511 generated radial copies, then drags the source center from
(30,20) to (31,21). These are **drag solves**, not a universal measure of
pattern creation or arbitrary drawings. Each sample used a fresh Node
22.22.1 child, 128 MiB V8 old space, 4096 WASM pages, and an external
30-second diagnostic deadline. The application's five-second timeout was
not changed. Runs were sequential on a shared development host.

Retained three-round run, all nine samples solved:

| Count | Solve time range | Median solve | Median native call | WASM memory after solve | Maximum observed peak RSS |
| --- | --- | --- | --- | --- | --- |
| 128 | 214.55-216.74 ms | 216.13 ms | 200.15 ms | 16 MiB | 133.9 MiB |
| 256 | 1491.68-1506.84 ms | 1504.47 ms | 1480.95 ms | 20.5 MiB | 143.8 MiB |
| 512 | 11335.27-11392.03 ms | 11342.87 ms | 11304.78 ms | 65.56 MiB | 161.5 MiB |

Initialization was 9.1-9.9 ms. At 512, pushing primitives took about 16.6 ms
and applying the result about 2.3 ms. Native solving accounts for 99.66% of
the median total solve. The native call contains diagnosis and optimization,
not just iterative steps. The observed median solve ratios are 6.96x and
7.54x for each doubling.

An earlier exploratory three-round run returned 11.29/13.78/11.36 seconds
at 512. Do not treat 11.4 seconds as a guaranteed upper bound. Neither run
tested concurrent children, other hardware, other source geometry, or larger
constraint sets.

## Cause

This is CPU-bound native computation, not the old fixed-heap failure,
initialization, JSON exchange, or a five-second internal convergence limit.
At 512, the retained samples use about 11.6 seconds of user CPU and only
65.56 MiB of WASM memory, well below the 256 MiB ceiling. A separate sampled
CPU profile is dominated by WASM frames, not JavaScript garbage collection.
The shipped WASM's profile frames are unnamed function indices; that
profile alone does not identify individual C++ routines.

Source tracing explains the expensive path:

1. `planeGcsPatterns.js:130` adds equal-length and angle relationships for
   the radial copies. At 512 the mapper produces 513 points, 512 circles,
   1022 radial lines, 511 equal-length constraints and 511 angle constraints.
   There are 2052 stored native parameters, including constants.
2. `planeGcsSolver.js:986` creates temporary drag goals. This fixture has
   three: two center coordinates and a radius stability preference.
   `dist/sketch/gcs_wrapper.js:374` tags temporary goals -1.
3. In the pinned source archive, `planegcs/GCS.cpp:1997` selects
   `solve(subSystems[cid], subSystemsAux[cid], ...)` when both hard
   geometry and auxiliary targets are present. It does **not** use the
   requested LM algorithm for that mixed-priority component.
4. That overload at `GCS.cpp:4620` allocates dense matrices and calls
   `qp_eq` at line 4680. `planegcs/qp_eq.cpp:36` performs dense
   `FullPivHouseholderQR` and constructs the full Q matrix. Enlarging the
   connected radial center system therefore makes each optimization step
   expensive, even though it has few remaining degrees of freedom.

In the native 512 trace, sparse rank reporting completes at 570.5 ms
(1536 unknown parameters, 1533 constraints, rank 1533). The one-variable
LM component starts at 12938.7 ms, immediately before completion.
Together with the native dispatch above, this locates the long interval
in the mixed-priority solve after diagnosis, not in the small LM component.
The near-cubic timing trend is consistent with the dense algebra; exact
per-function native times and mixed-priority iteration counts were not
instrumented.

The benchmark-only `--diagnostic-hard-goals` control promotes the three
temporary goals to hard constraints before native insertion. This forces
different solver semantics and is **not a proposed fix or capacity pass**.
It still took 10.68 seconds, with native tracing showing repeated LM
iterations on the large center system. Changing goal priority is therefore
neither a safe semantic shortcut nor sufficient performance relief.

Native file/line references above refer to the unchanged algorithm source
inside vendor `rebuild-20261020/source-growth256.tar.gz`, based on upstream
commit `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`.

## Timeout And UI Implications

A 30-second diagnostic deadline completed all measured unaltered samples.
A 15-second deadline would have admitted these observed standalone runs,
but the sample size and shared-host variation do not justify a production
guarantee. The current five-second server deadline necessarily refuses this
512 fixture. No deadline was changed.

Increasing that deadline alone would **not** make this workflow nonblocking:

- `DrawingCanvas.razor.cs:587` invokes its drag callback synchronously, then
  returns `Task.FromResult` at line 593.
- `DrawingWorkbench.razor.cs:2301` calls the synchronous adapter drag path.
  The adapter calls `runtime.Solve` at `PlaneGcsSketchSolverAdapter.cs:52`.
- `PlaneGcsNodeRuntime.cs:63` synchronously waits with
  `GetAwaiter().GetResult()` while the child computes. The Web app registers
  this runtime without overriding its default five-second deadline
  (`Program.cs:55`).

Child isolation keeps native failure out of the application process and
other circuits have separate solver state. It does not free the requesting
server circuit/event handler while this synchronous call waits. The browser
can continue local painting, but server-driven interactions in that circuit
must wait. More concurrent long-running children would also compete for CPU;
no concurrent capacity claim follows from these sequential samples.

Recommendation: retain the current bound and report this as a measured
capacity limitation. A larger bounded budget is plausible for an explicit
asynchronous, cancellable operation with stale-result checks and resource
admission, but is not an interactive-drag fix. Investigating native
problem-size reduction, sparse/reduced-coordinate priority solving, or
better feasible initial guesses would require a separately approved
algorithm/design change and regression work. None was implemented here.

## Reproduction And Evidence

```sh
timeout --kill-after=2s 30s node --max-old-space-size=128 \
  --wasm-max-mem-pages=4096 --experimental-default-type=module \
  docs/dev/planegcs-runtime/rebuild/radial-timing.mjs 512
```

Use counts 128/256/512 and repeat with fresh children. Append
`--native-debug` for native trace logging. The hard-goal control flag is
diagnostic only, as explained above.

- `evidence/radial-timing-three-rounds.jsonl`: raw results, phase timings,
  memory, parameter counts, CPU usage and exit status.
- `evidence/radial-inputs.sha256`: source/runtime/harness hashes verified
  unchanged before and after the retained three-round run.
- `evidence/radial-environment.json`: host/tool/deadline context.
- `evidence/radial128-native-trace.jsonl`,
  `radial256-native-trace.jsonl`, `radial512-native-trace.jsonl`: native
  trace timestamps; the 512 trace also had CPU profiling enabled.
- `evidence/radial512.cpuprofile`: raw Node CPU profile.
- `evidence/radial512-hard-goals-control.jsonl`: altered-semantics control.

The underlying performance limitation is diagnosed, **not fixed**.
No business records were changed and no data repair is indicated by this
benchmark. This does not alter the existing release capacity gates.
