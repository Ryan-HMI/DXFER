# DXFER Sync Production Slice Design

## Goal

Create a stripped-down DXFER production branch for HMI-Sync that reliably opens one trusted DXF artifact, normalizes its flat-pattern orientation, relocates final bounds to origin, and saves the result back to Sync through a callback API.

## Branch And Worktree Layout

- `main`: stays as the shared upstream baseline.
- `dev`: keeps the current full DXFER workbench and active-development features.
- `production`: hosts the Sync-facing production slice.
- `.worktrees/dev`: local worktree for full-feature DXFER development.
- `.worktrees/production`: local worktree for the production slice.

## Production Scope

The production app supports:

- DXF input from a local path or a Sync-provided download URL.
- Auto best-fit rotation to minimize bounding dimensions.
- Final translation so the normalized document bounds have `MinX = 0` and `MinY = 0`.
- Grain direction metadata: `None`, `X`, or `Y`.
- Manual override tracking when the user changes the auto-normalized result.
- Save through a Sync callback API.
- Explicit job-folder export for offline/debug recovery only.

The production app does not support direct HMI-Sync database writes, direct Sync storage writes, customer-reference manufacturing metadata import, or unbounded active-development CAD tools as part of the normal Sync launch path.

## Launch Contract

Sync launches DXFER with query parameters:

- `syncBaseUrl`: base URL for Sync callback endpoints.
- `artifactId`: Sync artifact identifier.
- `jobId`: short-lived edit job identifier.
- `editToken`: short-lived token scoped to the artifact/job.
- `inputPath`: local trusted DXF path when available.
- `downloadUrl`: Sync-provided DXF download URL when `inputPath` is not available.
- `returnUrl`: Sync URL to navigate back to after save or cancel.
- `jobFolder`: optional Sync-owned debug/recovery folder.

DXFER treats Sync as source of truth. Launch parameters identify the edit job only; they do not authorize DXFER to write databases or storage directly.

## Save Contract

On Save, DXFER posts multipart form data to Sync:

- `artifactId`
- `jobId`
- `editToken`
- `normalizedDxf`: normalized DXF file.
- `metadataJson`: DXFER metadata JSON.
- `boundingWidth`
- `boundingHeight`
- `rotationDegrees`
- `originShiftX`
- `originShiftY`
- `grainDirection`: `None`, `X`, or `Y`.
- `manualOverride`: `true` when user action changed the auto-normalized result.

Sync validates token and artifact ownership, stores a new artifact version, updates dimensions/metadata, and marks geometry status clean.

## Normalization

DXFER reads the DXF into `DrawingDocument`, computes a best-fit rotation, applies it around the current bounds center, then translates the rotated document so final bounds minimum is at origin.

The best-fit rotation algorithm uses supported entity sample points, evaluates candidate angles from point-pair edge directions modulo 180 degrees, and chooses:

1. Smallest bounding area.
2. Smallest maximum bounding dimension.
3. Smallest absolute rotation.

For empty or degenerate documents, DXFER leaves rotation at `0` and applies no meaningful shift.

## Error Handling

- Missing or invalid launch parameters keep DXFER in local file-open mode and disable callback save.
- Failed download or unreadable input path shows an error and leaves the document unchanged.
- Failed callback save keeps the normalized result in memory and offers job-folder export when configured.
- Job-folder export writes `normalized.dxf` and `dxfer.json`; Sync must explicitly import this recovery package.

## Testing

Focused coverage should include:

- Normalization chooses a rotation that improves a rotated rectangle's axis-aligned bounds.
- Normalization relocates final `MinX` and `MinY` to zero.
- Tie-breaking prefers smaller max dimension and then smaller absolute rotation.
- Sync callback payload includes normalized DXF, metadata JSON, dimensions, rotation, origin shift, grain direction, and manual override.
- Production launch parameters parse without depending on direct Sync storage or DB access.

## Self Review

- No placeholders remain in this spec.
- Production responsibility is limited to DXF editing and callback save.
- Sync remains source of truth for identity, authorization, storage, and clean geometry status.
- The branch/worktree layout matches the approved `production` and `dev` naming.
