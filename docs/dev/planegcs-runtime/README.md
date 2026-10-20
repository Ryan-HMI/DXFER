# PlaneGCS Runtime Distribution

Reviewed 2026-10-20. This folder records engineering evidence and release gaps,
not legal certification or approval to deploy.

The runtime is based on `@salusoft89/planegcs@1.3.0`, under
`src/DXFER.Blazor/wwwroot/vendor/planegcs/`. Existing
`DXFER-PROVENANCE.md`, upstream package files, the source archive, and the root
`THIRD_PARTY_NOTICES.md` remain owned by the integration workflow.

The active generated JS/WASM pair is now a separately identified
[growth-enabled rebuild](rebuild/README.md), not the original npm binary.
Its compiled memory grows from 16 MiB to at most 256 MiB. Original artifact
identity evidence below remains historical and is not relabeled as the
modified build's provenance.

## Evidence

- [PROVENANCE.json](PROVENANCE.json): artifact identity, official source URLs,
  checksums, and explicitly unknown original build inputs.
- [SHA256SUMS](SHA256SUMS): all 54 original npm package files and the pinned
  source archive; paths are relative to the DXFER repository root.
- [BUILD.md](BUILD.md): isolated rebuild and evidence collection instructions.
- [REPLACEMENT.md](REPLACEMENT.md): replaceable asset boundaries and acceptance.
- [DEPENDENCIES.md](DEPENDENCIES.md): notice inventory, reference versions,
  and unresolved linked-code identification.
- [rebuild/README.md](rebuild/README.md): pinned reproducible replacement,
  modified-source archive, output hashes, regression evidence and capacity limits.

The registry tarball matched its published SHA-512 integrity. The copied
package's 54 files matched the reviewed npm extraction byte-for-byte. The
source archive matched SHA-256
`6641e73b41f638621d5f95ae35dfadb45b42c1e43afbeab613753de851566be3`.
This verifies artifact identity, not that rebuilding the archive reproduces
the published WASM.

Run the current-package identity check from the DXFER repository root:

```sh
sha256sum -c docs/dev/planegcs-runtime/rebuild/ACTIVE-SHA256SUMS
```

## Release Gaps

| Gate | Evidence still needed |
| --- | --- |
| Original binary provenance | Actual builder image digest, architecture, exact APT versions, Node/npm versions and original build log. These are unknown, not inferred from today's package repositories. |
| Repeatable candidate build | Two identical builds completed, including an offline repeat. Move retained large dependency inputs/cache/image from scratch storage to durable release storage. |
| Original binary reproduction | The intentional growth-enabled replacement differs from npm. Original binary toolchain inputs remain unknown; reproducing that original binary is not claimed. |
| Dependency notices | Map compiled headers/runtime objects to exact source versions and retain their notices. The supplied reference license set is not proof of completeness. |
| Practical replacement | Run a deliberately modified compatible library through DXFER with unchanged application assemblies; show the changed library was actually loaded. |
| Distribution access | Verify published source archive, license and notice downloads, including content types and access from a recipient's environment. |
| Product acceptance | Real-engine geometry, document preservation, failure and recovery evidence owned by the integration/testing workflow. |

The initial documentation-only task did not replace runtime files. A later,
explicitly approved rebuild replaced only the generated pair after 302 JS
regressions passed. No deployment or final integrated acceptance is claimed.
The original `SHA256SUMS` will intentionally differ on those two active files;
use the retained original npm tarball for original-artifact comparisons.

The source archive includes `doc/*.pdf` reference documents. Preserve their
notices and review their redistribution metadata separately; they are not
required to compile the solver.
