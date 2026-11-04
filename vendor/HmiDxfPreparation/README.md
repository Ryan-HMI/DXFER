# Pinned HMI DXF Preparation

Source: HMI-Sync commit `f8674c7f09e974797062ba012e37f4ea2625509a`.
DXFER compiles this pinned implementation into `DXFER.CadIO` using repository-relative
Compile links. Building and testing do not require a Sync checkout or application.

## Source Contract

The five algorithm files under `Infrastructure/` and `Production/` are byte-identical
copies of the corresponding files under upstream `HmiSync.Web/`. Their original
namespaces and internal visibility are preserved. Do not reformat, rename, or
independently modify these algorithms in DXFER.

`Production/BatchDxfLayer.cs` contains only the original namespace and public record
declaration extracted from `HmiSync.Web/Production/BatchExportContracts.cs`. No other
application contracts, services, or UI types are included. CadIO grants test access
to internals; the algorithms have not been made public.

`provenance.json` records each upstream path and whole-file SHA256, its local path
and SHA256, and any mechanical extraction. Algorithm hashes must match upstream
hashes exactly. The local `.gitattributes` files retain LF bytes across checkouts.

## Regression Coverage

`tests/DXFER.Core.Tests/ProductionPreparationVendor/` contains the existing Sync
layer, tear-slot, legacy-format, cut-layer, and pure small-circle tests. Original
test namespaces and retained test bodies are unchanged. Layer, tear-slot, and
legacy test files are byte-identical imports.

The cut-layer import excludes the CypNest runtime theory and Python helper, which
depend on `BatchExportStage`, `CypNestTestRuntime`, and an external Python runtime.
The small-circle import includes only the four pure test methods and their
fixtures, excluding the file-backup test, EngineeringPage upload integration, and
HTTP helpers. Unused imports are removed from these two extracted suites.

`VendorSourceParityTests` verifies the pinned manifest, complete algorithm inventory,
raw-byte SHA256 parity for every import, and CadIO's relative Compile links. It
does not contact upstream or depend on git history at test time.

From the repository root:

```sh
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter 'FullyQualifiedName~HMISync.Tests.Production.BatchDxf|FullyQualifiedName~HMISync.Tests.Engineering.DxfSmallCircleFilterTests|FullyQualifiedName~ProductionPreparationVendor'
```

## Version Updates

Algorithm updates are vendored as a new pinned Sync version, never handwritten
approximations or local algorithm forks. Fix shared behavior in Sync first, select
the reviewed commit, and import its original bytes. Update the standalone record
and upstream tests mechanically as needed, documenting every extraction in the
manifest. Regenerate upstream and local SHA256 values, update this pin and the
parity test's expected commit, then run parity and behavior tests before review.

This vendor package supplies reusable code only. It does not wire production
cleanup into Workbench or an API, deploy anything, or repair existing records.
