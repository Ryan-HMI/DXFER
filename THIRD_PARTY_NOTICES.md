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

## Planned Optional Solver Adapter

- PlaneGCS / FreeCAD solver code is the planned first 2D geometric constraint
  solver candidate. PlaneGCS is LGPL-family software. Any PlaneGCS integration
  must stay behind a replaceable DXFER solver adapter and must include the
  required LGPL notices and replacement/source access path before it is shipped.

## Excluded Dependency Class

- GPL-only sketch solver libraries are not allowed as DXFER runtime
  dependencies for the HMI-Sync integration path. They may be studied as
  references, but must not be linked, bundled, or required by production DXFER
  code.
