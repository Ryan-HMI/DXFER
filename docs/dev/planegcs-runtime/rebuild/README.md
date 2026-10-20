# Reproducible Growth-Enabled Runtime

Build ID: `dxfer-planegcs-1.3.0-growth256-20261020`.
This is a modified replacement, not reproduction of the original npm binary.
The original fixed 337-page memory aborted on Rectangular-256. Two independent
builds of the patch here produced byte-identical generated bindings, loader
and WASM. The second build ran with networking disabled.

## Exact Inputs

- PlaneGCS source commit `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`,
  retained in the vendor `source-9d26bae.tar.gz`.
- Emscripten 6.0.10, Linux amd64 image
  `emscripten/emsdk@sha256:e077d54e2b8970575ebc4f185ac1de0b95c05f2b266134d4ba27449af7aebf65`.
- Boost 1.85.0 and Eigen commit
  `3147391d946bb4b6c68edd901f2add6ac1f31f8c` (3.4.0), as source archives,
  not unversioned APT packages. Archive hashes are in `inputs.sha256`.
- Node 20.19.0/npm 10.8.2 for the locked binding-generator dependencies.
  The image's Node 24.19.0 cannot build the locked tree-sitter 0.20.6 native
  addon; no npm dependency upgrades or lockfile changes were made to work
  around this. The compiler still uses its bundled Node 24.19.0.
- Exact base-image package inventory and tool versions are in `evidence/`.
  No extra APT packages were installed. Original npm-binary build inputs
  remain unknown; these are new, explicitly selected inputs.

The only PlaneGCS source modification is `growth-256m.patch`:
`ALLOW_MEMORY_GROWTH=1`, `MAXIMUM_MEMORY=268435456`, and
`ABORTING_MALLOC=1`. Explicit abort-on-exhaustion preserves child isolation;
growth must not silently change failed native allocation into unchecked null
pointers. No algorithm changes or hand-edited compiled binaries were used.
See the official [growth](https://emscripten.org/docs/tools_reference/settings_reference.html#allow-memory-growth),
[maximum-memory](https://emscripten.org/docs/tools_reference/settings_reference.html#maximum-memory)
and [allocation-failure](https://emscripten.org/docs/tools_reference/settings_reference.html#aborting-malloc)
settings.

## Repeat The Build

Use a new disposable directory, not a managed publish path. Docker access is
required. Input archives can be downloaded from the exact URLs recorded in
the vendor `rebuild-20261020/PROVENANCE.json`. Retain the archives and npm
cache; the recipe verifies their checksums before extracting source.

```sh
docker pull emscripten/emsdk@sha256:e077d54e2b8970575ebc4f185ac1de0b95c05f2b266134d4ba27449af7aebf65
bash docs/dev/planegcs-runtime/rebuild/rebuild.sh \
  /absolute/scratch/first /absolute/scratch/inputs /absolute/scratch/npm-cache
bash docs/dev/planegcs-runtime/rebuild/rebuild.sh \
  /absolute/scratch/second /absolute/scratch/inputs /absolute/scratch/npm-cache --offline
diff -u /absolute/scratch/first/evidence/output.sha256 /absolute/scratch/second/evidence/output.sha256
diff -u /absolute/scratch/first/evidence/generated-bindings.sha256 /absolute/scratch/second/evidence/generated-bindings.sha256
```

The recipe bounds each container to two CPUs, 4 GiB memory and 256 PIDs.
Native compilation has a 900-second limit. `SOURCE_DATE_EPOCH=0` is a fixed
reproducibility input, not a claimed commit timestamp. The generated loader
and WASM are a pair; do not replace only one of them.

The retained `source-growth256.tar.gz` includes the patched source and
generated bindings/interfaces. It also includes the build scripts used for
these two builds. For direct use of that already-patched tree, run
`install-bindings-deps.sh` and `build-in-container.sh` with it mounted at
`/src`, verified archives at `/inputs`, and the recipe at `/recipe`.
The repository-level `rebuild.sh` instead starts from the unmodified upstream
archive and applies the patch, preventing accidental double-patching.

## Observed Validation

- New binary: one unshared i32 memory, minimum **256 pages / 16 MiB**,
  maximum **4096 pages / 256 MiB**.
- Real Rectangular-256 solve grew memory from 16,777,216 to 24,248,320
  bytes. A separate post-disposal growth probe reached 268,435,456 bytes
  and rejected one more page. See `evidence/actual-memory-growth.jsonl`.
- Disposable candidate snapshot: **302 JS tests passed, none failed or
  skipped**, using the existing npm wrapper and the rebuilt generated pair.
- Subsequent current-worktree run: **353 passed, 4 failed, none skipped**
  across 357 tests. All four failures are partial-ellipse expectations in
  `planeGcsEditContracts.test.mjs`: two expect an older unsupported-message
  string and two expect unsupported where the current implementation solves.
  See `evidence/regression-current.log`. This is not an integrated green
  result; those tests/behavior require review by their owner.
- Benchmarks: Rectangular 16/64/128/256/512 and Radial 16/64/128/256 solved.
  Rectangular-256 took about 298 ms and 115 MiB RSS; Rectangular-512 about
  1.79 seconds and 165 MiB RSS; Radial-256 about 1.58 seconds.
  **Radial-512 exceeded the five-second child timeout.** This is a bounded
  performance failure, not a passing capacity claim.

To repeat the growth/cap probe against the current worktree assets:

```sh
timeout --kill-after=2s 8s node --max-old-space-size=128 \
  --wasm-max-mem-pages=4096 --experimental-wasm-type-reflection \
  --experimental-default-type=module \
  docs/dev/planegcs-runtime/rebuild/verify-memory.mjs \
  tests/DXFER.Blazor.Tests/planeGcsBenchmark.mjs Rectangular 256
```

The reflection flag is diagnostic only. The production runtime uses the
4096-page limit without reflection. The WASM ceiling is per memory, not
total process RSS; admission limits and external resource planning still
matter.

## Retained Materials And Gates

Vendor `rebuild-20261020/` contains the original npm tarball, the rebuilt
source archive, modification notice and detailed provenance. Existing
upstream and dependency license texts remain intact. The original npm
identity manifest is historical; `ACTIVE-SHA256SUMS` checks the current
package and newly retained artifacts.

Large dependency archives and the npm cache are retained in
`/tmp/dxfer-planegcs-growth-rebuild-20261020/`; exact URLs/hashes allow their
retrieval, but `/tmp` is not durable release storage. Archive these inputs
and the pinned builder image in the release evidence store before relying
on offline reproduction long-term. The recipe/archive is engineering
evidence, not legal certification.

Final integrated .NET/browser verification, fresh clean-publish checks,
recipient source/notice download checks, and the Radial-512 performance
decision remain open. No publish or deployment was performed for this rebuild.
