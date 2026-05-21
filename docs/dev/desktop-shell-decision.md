# DXFER Desktop Shell Decision

Date: 2026-05-21

## Decision

Defer `DXFER.Desktop` for the current V1 architecture work. Keep stabilizing the web-hosted Blazor prototype and the Stage 7 canvas/module split before adding a Windows desktop shell project.

Do not create `src/DXFER.Desktop` until the repo has a concrete runtime target, packaging expectation, and acceptance-test plan for native file access.

## Rationale

- The current production path is still the Blazor/Web app in `src/DXFER.Web` with the shared canvas implementation in `src/DXFER.Blazor`.
- Stage 7G still has active canvas boundary work. Adding a desktop shell now would duplicate the same volatile canvas surface while hit testing, dimension input lifecycle, and tool interaction state are still being split.
- Stage 7D through 7F already added safer browser-based DXF handling: normalized DXF export, `.dxfer.json` sidecar export, document metadata, unsupported-entity warnings, and reference-only edit guardrails.
- The V1 design still wants local production file access eventually, but the repo does not yet define whether that should be Blazor Hybrid, WebView2 hosted local web app, or another desktop packaging route.

## Current File-Access Workaround

Until the desktop shell is explicitly planned:

- Open DXF files through the browser file picker.
- Save normalized output through the existing DXF download path.
- Keep the matching `.dxfer.json` sidecar download with source hashes, normalized hashes, units, trust/reference mode, bounds, warnings, and unsupported entity counts.
- Treat DWG and other reference-only files as external-viewer handoffs unless a future CAD IO adapter adds trusted read support.

## Risks While Deferred

- Browser downloads require the user to place the normalized DXF and `.dxfer.json` sidecar together.
- There is no native file association, file locking, recent-file list, or direct save-over-original behavior.
- The app cannot directly launch an installed DWG/reference viewer from the browser sandbox.
- Offline packaging, update flow, and installer behavior remain undecided.
- Native file-system permissions and trust prompts still need threat-modeling before production desktop use.

## Acceptance Tests Before Creating `DXFER.Desktop`

A desktop implementation plan must define tests for:

- Opening local DXF files without losing source filename/hash metadata.
- Saving normalized DXF plus `.dxfer.json` sidecar to a user-selected folder.
- Preserving reference-only guardrails for untrusted or unsupported source documents.
- Showing unsupported entity and missing-unit warnings in the same inspector/status surfaces as the web app.
- Preventing accidental mutation of reference-only documents from sketch, trim, dimension, drag, and construction tools.
- Running a desktop smoke test that creates geometry, selects and drags it, dimensions it, saves it, closes/reopens it, and verifies no metadata loss.

## Next Step

When the web prototype stabilizes, write a separate implementation plan that compares Blazor Hybrid, WebView2 over local Kestrel, and any installer/runtime constraints before introducing the desktop project.
