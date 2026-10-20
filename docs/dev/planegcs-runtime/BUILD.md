# PlaneGCS Build Evidence

The original prospective instructions below are retained as audit context.
The completed, growth-enabled candidate uses the executable
[pinned rebuild recipe](rebuild/README.md), not unversioned APT resolution.
It has two identical builds and a separately retained modified-source archive.

These are prospective instructions for an explicitly authorized, disposable
build environment. They were not executed during this documentation task.
Never run them inside a managed application publish directory.

## Source And Inputs

Use the included `source-9d26bae.tar.gz`, associated by npm metadata with
upstream commit `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`.
It includes the patched FreeCAD solver snapshot, headers, binding generators,
templates, CMake configuration, TypeScript, package lockfile and packaging
script. `planegcs/commit.txt` records FreeCAD commit
`5f8eac49f31626354ee69bf40616bac801ca5560`.

Do not run `npm run update-freecad`: its script clones the then-current FreeCAD
HEAD. Do not replace the vendored patched solver with a pristine FreeCAD checkout.

The pinned [upstream Dockerfile](https://github.com/Salusoft89/planegcs/blob/9d26baef0b240fe0eef4186d4e5e5e66ec3620b7/Dockerfile)
names `emscripten/emsdk:6.0.10` and installs these unversioned APT packages:

```text
libeigen3-dev
libboost-system-dev
libboost-thread-dev
libboost-program-options-dev
libboost-test-dev
libboost-filesystem-dev
```

The original image digest, image architecture, APT package versions, transitive
package versions, Node/npm versions and build log are **unknown**.
Today's resolution of that image tag or APT packages is not historical evidence.
The runtime's WASM has no `producers` custom section that establishes the
original compiler version.

## Prospective Rebuild

1. Check the repository artifact hashes using `SHA256SUMS`. Extract the source
   archive into a new scratch directory and retain a pristine copy.
2. Record the host OS/architecture, Node/npm versions and source archive hash.
   Run `npm ci`, not `npm update`; retain the unchanged upstream lockfile.
   Record native build prerequisites used by tree-sitter and any install logs.
3. Resolve the emsdk image to a digest and record its platform. In the scratch
   Dockerfile, use that digest and a fixed APT snapshot or exact package versions
   with retained package artifacts. Save the Dockerfile diff separately.
   Label these as **new rebuild inputs**, not recovered original inputs.
4. Build the image; capture `emcc --version`, `cmake --version`, compiler/linker
   versions and `dpkg-query -W` output inside it. Record the resulting image ID
   and a digest/archive that actually retains the installed dependencies.
   Preserve the corresponding dependency source archives and notices.
5. In that isolated source tree, use the upstream pipeline below.

```sh
npm ci
npm run build:docker
npm run build:bindings
npm run build:wasm
npm test
npm pack
sha256sum planegcs_dist/planegcs.js planegcs_dist/planegcs.wasm
sha256sum dist/planegcs_dist/planegcs.js dist/planegcs_dist/planegcs.wasm
```

`build:docker` uses the scratch Dockerfile. Run Docker only where the operator
has authorized it. `npm pack` invokes `prepack.sh`, which runs TypeScript and
copies the generated JS/WASM into `dist/`. Generated `bindings.cpp` and
`planegcs_dist` interfaces come from `build-bindings`, not the npm binary.
Record any script fixes needed to complete this pipeline; do not hide them
in the claimed upstream source.

6. Repeat from a second fresh extraction with the same frozen inputs. Compare
   the individual JS/WASM and wrapper files, not merely compressed tarball
   bytes, which can reflect archive metadata.
7. Separately compare with the original npm JS/WASM hashes in
   `PROVENANCE.json`. If different, retain the differences and investigate.
   Passing solver tests alone does not establish that the published binary
   was built from these exact inputs.

## Build Evidence Bundle

Retain source/patch hashes, all dependency versions and source URLs, image
digest/platform, installed package inventory, build environment, full build
commands/logs, generated bindings, runtime import/export inspection, output
hashes, test results and a linked-code license inventory. Separate original
observations from newly selected inputs in every manifest.

If the original dependency versions cannot be recovered, report that gap.
An independently reproducible replacement built from documented inputs is a
different result from reproducing the original npm WASM.
