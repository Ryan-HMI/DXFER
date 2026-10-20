# Dependency License Reference Texts

These files supplement, and do not replace, the unmodified PlaneGCS `../LICENSE`
and source archive. See `SOURCES.json` for official pinned source URLs and
checksums.

- `boost/`: Boost Software License 1.0 from the Boost 1.85.0 reference tag.
- `eigen/`: complete root `COPYING.*` reference set from Eigen 3.4.0.
- `emscripten/`: Emscripten 6.0.10 root license and selected bundled C/C++
  runtime notices (musl, libc++, compiler-rt).

**The original APT Boost and Eigen versions are unknown.** The versions above
identify the copied notice texts, not the dependency versions used to produce
the npm WASM. Emscripten 6.0.10 is named by the pinned build recipe, but its
use for that binary has not been independently reproduced.

The separate `../rebuild-20261020/` replacement now uses these exact Boost
1.85.0, Eigen 3.4.0 and Emscripten 6.0.10 sources. Its manifest records the
new input archives, image digest and output hashes. This establishes those
versions for the modified rebuild only, not retroactively for the npm binary.
No license text was removed or rewritten for that replacement.

Reference Eigen GPL/LGPL texts do not establish that code under those terms
was compiled into this runtime, nor authorize GPL-only dependencies. A
linked-code inventory and version-specific notice reconciliation remain
release gaps.

License wording is retained. `eigen/COPYING.APACHE` has an added final newline;
`SOURCES.json` records both its upstream and distributed checksum. Other
supplied texts are byte-for-byte copies of the indicated official files.

The engineering runbooks and release-gap inventory live in the DXFER source
repository under `docs/dev/planegcs-runtime/`. Rebuild evidence is recorded
separately; no legal certification or deployment approval is conveyed by this
notice set.
