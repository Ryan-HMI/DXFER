# PlaneGCS Runtime Replacement

This is an operator runbook for a disposable verification environment, not a
deployment command or evidence that replacement acceptance has passed.

## Asset Boundary

The current DXFER module `src/DXFER.Blazor/wwwroot/planeGcsSolver.js` imports
`./vendor/planegcs/dist/index.js`. The upstream entry point imports its generated
loader from `dist/planegcs_dist/planegcs.js`, which resolves the separate
`planegcs.wasm` beside itself unless `locateFile` overrides it.

`createPlaneGcsSolver({ wasmPath })` passes a WASM location override;
`wasmBinary` can supply bytes. These are module initialization arguments,
not an implemented arbitrary-module-path setting in the application UI.
The top-level library import is still a fixed relative path.

Replace the generated loader and WASM as a compatible pair. Prefer replacing
the entire built `dist/` module tree together, retaining its relative paths
and the package's `"type": "module"` metadata for Node. Compiler and binding
changes can alter the minified JS/WASM import/export contract. A different
WASM file alone is not guaranteed compatible.

The public static-asset prefix is:

```text
/_content/DXFER.Blazor/vendor/planegcs/
```

For a published application, inspect the actual static-web-assets provider and
publish layout. Do not assume a development worktree change modifies the
installed runtime. The server integration resolves `planeGcsSolver.js` through
the web-root provider, with a published `wwwroot/_content/DXFER.Blazor/`
fallback, and passes it to `Solver/planeGcsHost.mjs`.

## Runtime Memory And Lifecycle Limits

Verified locally on Node **v22.22.1**. The original and modified binaries must
not be conflated:

- The original npm binary exports one memory, named `B`, with **minimum = maximum = 337
  pages**, `shared: false`, `index: i32`. At 65,536 bytes per page this is
  **22,085,632 bytes**, approximately 21.06 MiB. It is fixed-size:
  `memory.grow(1)` rejects. These are binary/type-reflection observations,
  not an inferred Emscripten default or an original-build provenance claim.
- The server launches Node with `--wasm-max-mem-pages=4096`, a **256 MiB
  ceiling per WASM memory**. A separate growable-memory probe reached 4096
  pages and rejected both growth to 4097 pages and an initial allocation of
  4097 pages. The real pinned solver passed fixed-start/driving-length
  fixtures with this flag enabled.
- The flag does **not** enlarge the original binary's smaller fixed memory.
  That binary aborted on Rectangular-256 and has been replaced in the
  worktree by the [reproducible growth rebuild](rebuild/README.md).
  The active replacement declares **minimum 256 pages / 16 MiB** and
  **maximum 4096 pages / 256 MiB**. Real growth beyond the old heap and
  rejection above the compiled maximum were verified. Equation-count
  optimizations and representative workload acceptance remain separate;
  Radial-512 still exceeded the five-second benchmark timeout.
- `--max-old-space-size=128` limits V8 old-space, not total process RSS.
  The WASM cap is also not an aggregate/process RSS limit: replacement
  libraries can create multiple memories, and JS, stacks and native code
  use additional memory. Recheck memory count and growth for replacements.
- The runtime admits at most eight child processes per application process,
  retaining independent circuit-owned solver instances. Idle children are
  eligible for cleanup after 60 seconds. Circuit disposal, transport failure
  and solve timeout terminate the owned child. An unsuccessful termination
  retains ownership and its budget slot for reaper retries.
- Explicit native `Aborted(...)` results and WASM traps retire the child;
  the failed operation must preserve input geometry and the next request must
  start fresh. Ordinary constraint failures need not retire a healthy child.
- Transport frames are limited to 8 MiB; stderr is drained in bounded chunks
  with a 1 MiB cumulative budget per child. Host writes await completion.
- `--experimental-default-type=module` makes the shared `.js` module graph
  explicit. That flag is available in Node 20.10+, but the complete runtime
  flag combination and WASM cap were validated here only on Node 22.22.1.
  An unsupported executable/flag must fail closed, never silently drop limits.

These limits are not a credential sandbox or an OS-enforced total-memory
quota. The child environment is cleared, but its OS account permissions
remain those of the application. Do not substitute an address-space limit
for WASM memory limits: V8 reserves virtual address space beyond committed
WASM memory.

## Fresh Published Runtime Verification

The earlier clean-publish smoke preceded the resource-limit changes.
It is **not** acceptance evidence for the final runtime. Repeat these checks
after integration in a new disposable publish, without deploying or modifying
any managed runtime directory.

1. Record the candidate commit/source state and hashes of `DXFER.Core.dll`,
   `Solver/planeGcsHost.mjs`, the shared mapper and vendor WASM. Confirm the
   candidate contains the final runtime changes, not merely newer source files.
2. Run from `/tmp`, using only published paths and a cleared environment.
   For example, replace the placeholder with an absolute disposable path:

   ```sh
   publish=/absolute/path/to/fresh/disposable-publish
   cd /tmp
   timeout --kill-after=2s 15s env -i LANG=C.UTF-8 /usr/bin/node \
     --max-old-space-size=128 --wasm-max-mem-pages=4096 \
     --experimental-default-type=module \
     "$publish/Solver/planeGcsHost.mjs" \
     "$publish/wwwroot/_content/DXFER.Blazor/planeGcsSolver.js" <<'JSON'
   {"id":1,"request":{"document":{"entities":[{"id":"edge","kind":"line","points":[{"x":0,"y":0},{"x":3,"y":4}]}],"constraints":[{"id":"axis","kind":"horizontal","referenceKeys":["edge"],"state":"satisfied"},{"id":"fixed","kind":"fix","referenceKeys":["edge:start"],"state":"satisfied"}],"dimensions":[{"id":"length","kind":"linearDistance","referenceKeys":["edge:start","edge:end"],"value":10,"isDriving":true}]},"constraints":[],"dimensions":[],"fixedReferenceKeys":[],"initialGuesses":[]}}
   JSON
   ```

   Require exit 0, matching response ID, engine `PlaneGCS`, status `solved`,
   and line endpoints `(0,0)` and `(10,0)` within numeric tolerance. Confirm
   the child exits after stdin EOF. This validates the published host/assets,
   **not** the .NET launch configuration: the command supplies its own flags.
3. Inspect the published binary's declared memory independently. This
   reflection flag is diagnostic only and is not a production launch option:

   ```sh
   env -i LANG=C.UTF-8 /usr/bin/node --experimental-wasm-type-reflection \
     --input-type=module --eval \
     'import {readFileSync} from "node:fs"; const m=new WebAssembly.Module(readFileSync(process.argv[1])); console.log(JSON.stringify(WebAssembly.Module.exports(m).filter(e=>e.kind==="memory")));' \
     "$publish/wwwroot/_content/DXFER.Blazor/vendor/planegcs/dist/planegcs_dist/planegcs.wasm"
   ```

   For the original npm binary, the single `B` memory has 337/337-page
   limits. For the current rebuilt candidate, require **256/4096 pages**.
   A fresh publish showing 337/337 still contains the obsolete fixed-heap
   binary. Always compare its hash with the intended build's provenance.
4. Exercise the **published .NET runtime**, not just the standalone host.
   Observe its actual child command line and published asset paths; require
   both heap-limit flags and explicit ESM mode. Verify independent runtimes
   use different PIDs. Dispose one and confirm only its child exits.
5. Verify idle expiry, timeout/abort recovery, unchanged input geometry,
   and that failed termination does not release its capacity slot. Check
   tracked child PIDs after tests rather than killing unrelated Node processes.
   Source-tree resource tests support these behaviors but do not by themselves
   establish that the published assemblies contain them.

Final integrated publish/runtime verification is still a release gate.

## Disposable Replacement Procedure

1. Use a separate checkout or copy of the published application, with no live
   credentials, shared business data or production process attached.
   Hash its application assemblies and original vendor files before testing.
2. Build a replacement using [BUILD.md](BUILD.md). Make a small, documented,
   observable modification inside the library, such as an initialization
   diagnostic with a unique marker. Keep the application-facing API compatible.
   Preserve source patches, notices, compiler settings and new output hashes.
3. Stage the complete replacement `dist/` tree at the same relative vendor
   path in the disposable copy. Preserve original source/provenance materials;
   add a clearly separate modified-build record and matching source archive.
   Do not label the replacement as the unmodified npm artifact.
4. Terminate the disposable scope's old Node solver child and instantiate a
   fresh scope. Node's cached module/WASM instances will not change merely
   because files on disk changed. For browser use, reload the module graph and
   invalidate relevant service-worker/CDN/browser caches.
5. Verify the loaded file hashes and the unique library marker. Verify that
   application assembly hashes did not change. Run actual solver fixtures
   and require an observed real WASM instance and the PlaneGCS engine result.
6. Exercise missing/mismatched assets: initialization must fail clearly and
   must not silently select the legacy engine or alter the input document.
7. Restore the original assets, clear the same caches/processes, and repeat
   the smoke fixture. Retain before/replacement/restored hashes and results.

Prospective repository checks, run only in that authorized scratch checkout:

```sh
node --test tests/DXFER.Blazor.Tests/planeGcsSolver.test.mjs
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter FullyQualifiedName~PlaneGcsNodeRuntimeIntegrationTests
```

Those tests are acceptance inputs, not a substitute for the modified-library
marker and unchanged-assembly evidence. No such tests were run for this
documentation-only change.

## Distribution Checks

- Serve WASM as `application/wasm`; ES modules need a usable JavaScript MIME
  type. Check browser CSP and CORS when the browser is the consumer.
- Verify the source archive and notice text files are actually downloadable.
  Extensionless `LICENSE`/`COPYING.*` files and `.tar.gz` may need explicit
  static-server MIME handling. A successful source-tree copy is not proof.
- Check Node filesystem access and package ESM resolution independently of
  browser fetches; the current server host reads local assets.
- Retain integrity checks, but document how an operator registers the hashes
  of an authorized modified library. Do not silently bypass integrity checks
  or hard-code only the original hash as the sole usable implementation.
- Any real rollout needs separate approval, staged compatible assets and a
  rollback plan. Replacing static assets can still require process/cache
  recycling even when no application assembly recompilation is needed.
