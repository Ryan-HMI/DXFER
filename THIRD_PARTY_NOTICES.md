# Third-Party Notices

DXFER-owned source code is licensed under the MIT license. Third-party
dependencies keep their own licenses and must remain isolated behind DXFER-owned
interfaces when they affect downstream applications such as HMI-Sync.

## Current Bundled Assets

- Bootstrap CSS assets under `src/DXFER.Web/wwwroot/bootstrap` are distributed
  under the MIT license by the Bootstrap authors.

## Optional Reference Corpora

- The Onshape FeatureScript Standard Library is MIT licensed by PTC Inc. DXFER
  does not vendor this corpus. `scripts/Sync-FeatureScriptStd.ps1` can clone the
  public mirror into ignored local artifacts for solver/tool design reference.
  Any copied substantial portion must retain the FeatureScript std MIT
  copyright and license notice.

## PlaneGCS Runtime

- `@salusoft89/planegcs` version 1.3.0, by Miroslav Sery / Salusoft89 and the
  included FreeCAD authors, is bundled under
  `src/DXFER.Blazor/wwwroot/vendor/planegcs`. The original license and source
  notices are retained there. Package metadata identifies LGPL-2.0-or-later;
  wrapper source headers specify LGPL-2.1-or-later.
- The corresponding pinned upstream source and build scripts are included in
  `source-9d26bae.tar.gz` alongside the runtime. See `DXFER-PROVENANCE.md` for
  commit, package integrity, source URL and hashes. This records the upstream
  association; a bit-for-bit rebuild has not been established.
- JavaScript and WASM remain separate replaceable assets, outside DXFER-owned
  solver interfaces and document types. The application does not prohibit
  replacement with compatible modified builds or debugging those modifications.
  Public asset paths begin `/_content/DXFER.Blazor/vendor/planegcs/`.
- Runtime selection is explicit. Bundling these assets alone does not mean a
  particular deployment is using PlaneGCS.

## Excluded Dependency Class

- GPL-only sketch solver libraries are not allowed as DXFER runtime
  dependencies for the HMI-Sync integration path. They may be studied as
  references, but must not be linked, bundled, or required by production DXFER
  code.
