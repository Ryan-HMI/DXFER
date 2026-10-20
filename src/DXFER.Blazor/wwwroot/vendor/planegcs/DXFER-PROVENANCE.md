# PlaneGCS Distribution

Unmodified npm package: `@salusoft89/planegcs@1.3.0`.

- Upstream: https://github.com/Salusoft89/planegcs
- npm integrity: `sha512-ugIkFCPnKRK1Yed+5297S/RmmfgEhIp5Xxx8PYhgfbcS+AxYV3chZ8LMGlEbEqrkbK6fwmNVLtEpLFq5KDl2lw==`
- npm-reported source commit: `9d26baef0b240fe0eef4186d4e5e5e66ec3620b7`
- WASM SHA256: `af9d1874fc895726bd267703a698a86c82d35b7415a02fe5184ebf65ddbc04af`
- Included source archive: `source-9d26bae.tar.gz`
- Source archive SHA256: `6641e73b41f638621d5f95ae35dfadb45b42c1e43afbeab613753de851566be3`

The archive contains the corresponding wrapper, bundled FreeCAD solver sources,
binding generators, build scripts and Docker build recipe from the pinned
upstream commit. The published WASM was obtained from npm; a bit-for-bit rebuild
has not been verified. Upstream license and source copyright notices are retained.
The package metadata says LGPL-2.0-or-later; individual source headers specify
LGPL-2.1-or-later. Read the supplied license and notices rather than assuming
the package metadata overrides those headers.

WASM and its JavaScript wrapper remain separate replaceable files. DXFER's
integration module is outside this vendor directory. Replacing them with a
compatible modified build does not require recompiling application assemblies.
