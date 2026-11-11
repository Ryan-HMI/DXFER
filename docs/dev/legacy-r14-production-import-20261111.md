# Legacy R14 Production Import

Ryan reported a repeat blank DXFER canvas for AVIENT BC14-025. The exact current
Production source (artifact 957, Rev A) is an AC1014/R14 file, SHA-256
`762A3482BB8B5B589ACF3273F70D60EDAC69F5A7E9551C22588C31AAC29715F6`.
The original normalize request returned HTTP 400 before rendering any geometry.

## Shared Causes

- DIMSTYLE group 5 is the legacy arrow-block name, not a second identity; its
  actual identity is group 105. Duplicate genuine identities remain rejected.
- This exporter uppercases dictionary keys and standard space names.
- R13/R14 XRECORDs omit the later cloning flag. Omission is accepted only with
  one explicit supported version declaration in one HEADER. All payload fields
  still undergo validation.
- Legacy proxies store recognized display metadata and Onshape bend flags.
  Proxy support requires the correct object class from CLASSES, valid binary
  envelope and size, approved dictionary role, ownership, and reference checks.
  Display proxies remain intact in the working copy. Only the two known boolean
  bend keys on an already-discarded bend LINE can be detached; their proxy body
  cannot contain references. Unknown proxies and design associations still fail.
- Known legacy header roundtrip values are schema checked and retained. Explicit
  non-inch insertion units are refused, not silently interpreted as inches.
- Writer rounding to ten decimal places made three near-identical source circles
  exact duplicates on reopen. Round-trip double formatting preserves them;
  exact-only duplicate cleanup has not been widened to merge nearby geometry.

Format references: [DIMSTYLE](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm),
[symbol table handles](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-5AB9300F-F0AC-4ADE-89EA-A9D1D152D8B8.htm),
[proxy object envelope](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-F59F0EC3-D34D-4C1A-91AC-7FDA569EF016.htm).

## Verification

Regression tests reproduce the case-sensitive names, DIMSTYLE identity, legacy
flag omission, and writer precision failures before their fixes. Positive and
negative suites cover ownership, unknown roles/classes, malformed envelopes,
non-inch units, unsafe references, source preservation, and save/reopen behavior.

The source has 40 entities, including three bend lines. Preparation retains all
37 cut entities exactly, with bounds 15.05395326905554 by 15.41688491941893 inches
and cut length 64.2559995982958 inches. Export/reopen preserves these results.
Its separate branched/ambiguous contour warning remains; this work does not
certify the source topology or silently merge its near-coincident circles.

No live source file was altered during investigation. Existing missing metadata
requires explicit guarded reprocessing after deployment; no bulk historical
rewrite is part of this patch. Deployment and repair evidence is recorded
separately in new, uniquely named archive files.
