# DXFER PlaneGCS Growth Rebuild

This is a **modified build**, not the original npm binary. The active
`../dist/planegcs_dist/planegcs.js` and `planegcs.wasm` pair was rebuilt from
upstream commit `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`.
No solver algorithm source was changed.

The original npm artifact had one fixed memory: 337 pages, 22,085,632 bytes.
It aborted on the representative Rectangular-256 benchmark. This rebuild
enables memory growth with a compiled **256 MiB maximum**, initial memory
16 MiB, and explicit abort-on-exhaustion. The Node host separately enforces
the same 4096-page per-memory ceiling. Neither setting is a total RSS limit.

`PROVENANCE.json` records exact new inputs and output hashes. Two independent
source extractions produced identical generated bindings, JS loader and WASM;
the second build used no network. Original build versions remain unknown.
The unchanged npm wrapper files remain in place; only the generated pair
was replaced after testing with those wrappers.

- `upstream-npm-1.3.0.tgz`: retained original registry artifact.
- `../source-9d26bae.tar.gz`: retained unmodified upstream source archive.
- `source-growth256.tar.gz`: patched source, generated bindings/interfaces,
  unchanged upstream license and build scripts. The additional dependency
  sources are pinned by URL/hash in `PROVENANCE.json`, not embedded here.
- `../LICENSE` and `../notices/`: preserved license and dependency notices.
- Repository `docs/dev/planegcs-runtime/rebuild/`: executable build recipe,
  input checksums, build logs, package inventory and validation evidence.

Local candidate validation passed 302 PlaneGCS JS tests. Rectangular-256/512
and Radial-256 now solve, but Radial-512 still exceeded the benchmark's
five-second timeout. This is not unlimited-capacity acceptance, legal
certification, publication approval or deployment evidence.
