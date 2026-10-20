# PlaneGCS Dependency Notices

This inventory distinguishes inspected declarations from unresolved build
provenance. It does not certify legal sufficiency, resolve license
interpretation, or authorize an otherwise excluded dependency.

## PlaneGCS And FreeCAD

At upstream commit `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`:

- npm `package.json` declares `LGPL-2.0-or-later`.
- The retained root `LICENSE` contains LGPL 2.1.
- Wrapper/build-script headers declare LGPL 2.1-or-later.
- Vendored FreeCAD solver headers declare Library GPL version 2-or-later.

Preserve these declarations instead of allowing package metadata to override
individual source notices. The source archive retains the FreeCAD and wrapper
copyright notices, including Miroslav Sery / Salusoft89, Konstantinos Poulios,
Victor Titov, Jurgen Riegel and Werner Mayer; this list is not a replacement
for the full per-file headers.

## Dependency Reference Set

Texts are under
`src/DXFER.Blazor/wwwroot/vendor/planegcs/notices/`. Their exact upstream URLs,
reference commits and SHA-256 hashes are recorded in `notices/SOURCES.json`.

| Component | Evidence and supplied files | Remaining uncertainty |
| --- | --- | --- |
| Boost | CMake uses Boost headers. `boost/LICENSE_1_0.txt` is the official Boost Software License text from reference tag `boost-1.85.0`. | Original APT Boost version is unknown. This reference tag is not a claim about the published WASM. Identify actual headers and retain applicable per-file notices. |
| Eigen | CMake includes `/usr/include/eigen3`; source uses Eigen. `eigen/COPYING.*` is the complete root notice set from reference tag `3.4.0`, including README, MPL2, BSD, MINPACK, LGPL, GPL and Apache texts. | Original APT Eigen version is unknown. Identify which files were compiled; the presence of a reference license text does not prove its code is linked. |
| Emscripten | Recipe names emsdk `6.0.10`. `emscripten/LICENSE` is from the corresponding Emscripten source tag and includes MIT/NCSA and its additional notices. | The original compiler/image digest has not been verified against the npm binary. |
| C/C++ runtime | `emscripten/musl-COPYRIGHT`, `libcxx-LICENSE.TXT`, and `compiler-rt-LICENSE.TXT` come from that same pinned Emscripten tree. | Determine actual linked runtime objects and other bundled code from a verified build; do not infer inclusion of every reference component. |
| Build tools | npm lockfile, generator/templates, TypeScript, tree-sitter, CMake, Closure and emsdk tooling are build inputs. | Record exact tools and inspect emitted-code notices separately from tools that only ran during compilation. |

The Eigen reference set includes GPL text because the upstream source
distribution includes that text. It is not an assertion that GPL-only code
is in PlaneGCS, and does not waive DXFER's existing GPL-only runtime exclusion.
Do not infer license classification from either the presence or absence of a
notice file alone.

Eigen's `COPYING.README` describes `EIGEN_MPL2_ONLY`. The pinned PlaneGCS
CMake recipe does not explicitly set that flag. Enabling it in a future
controlled rebuild is a documented build change, not evidence about the
original binary. Its success would not replace the broader inventory.

## Complete The Inventory

1. Recover original build provenance if possible. Otherwise keep it unknown
   and inventory a separately labeled, reproducible replacement build.
2. Record exact binary and source package versions for Eigen, Boost and their
   dependencies, including distribution patches. Retain source artifacts.
3. Use compiler dependency information and link maps to identify included
   headers/runtime objects. Inspect embedded third-party notices and
   generated JS; do not rely solely on top-level SPDX fields.
4. Compare the selected versions' notices with this reference set. Add missing
   applicable texts/copyright notices and explain any retained reference-only
   documents. Do not silently overwrite upstream originals.
5. Review library-version declarations and the chosen distribution arrangement
   with the responsible reviewer. Record unresolved questions explicitly.
6. Verify source and notice access in the actual shipped artifact using
   [REPLACEMENT.md](REPLACEMENT.md). Preserve the dependency manifest with each
   replacement's own source and output hashes.

Official evidence:

- [Pinned CMake source](https://github.com/Salusoft89/planegcs/blob/9d26baef0b240fe0eef4186d4e5e5e66ec3620b7/planegcs/CMakeLists.txt)
- [Pinned Dockerfile](https://github.com/Salusoft89/planegcs/blob/9d26baef0b240fe0eef4186d4e5e5e66ec3620b7/Dockerfile)
- [Eigen reference licensing readme](https://gitlab.com/libeigen/eigen/-/blob/3147391d946bb4b6c68edd901f2add6ac1f31f8c/COPYING.README)
- [Emscripten reference license](https://github.com/emscripten-core/emscripten/blob/d6c521a7f05449857c76bd99e396895583cf2083/LICENSE)
