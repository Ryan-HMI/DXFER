# Appearances Presets

## Request and Cause

The solid-color background control replaced the original Foundry gradient rather
than preserving it as a choice. Ryan requested the original blue fade and a darker
grey preset, with canvas/panel controls under File > Appearances.

Restore the exact original CSS gradient: `radial-gradient(ellipse at center,
#172832 0%, #0b1016 70%)`. Grey uses #202020 canvas and #181818 panels. Foundry uses
the original #10141b panel palette. Custom canvas and panel colors are independent;
the shared text/control/hover/border colors adapt to panel luminance. Layer colors,
geometry, history and document metadata are not appearance settings.

## Layout Contract

- Reuse File menu command styling and the existing dialog positioning/focus-trap
  helper; controls are removed from the Layers section.
- Center a 360px dialog over the canvas, bounded by the desktop viewport, with
  two-column label/control alignment and internal overflow if necessary.
- Preset uses a select; colors use swatches. Close is a fixed 28px icon target;
  Reset is an explicit icon/text command. Font sizes remain fixed, no overlap.
- Palette docking, positions, size preferences and canvas geometry remain unchanged.
- Escape closes and restores canvas focus. Tab stays within the dialog.

## Persistence and Rendering

Versioned browser preference `dxfer.canvasAppearance.v2` stores preset, canvas and
panel colors. Previous #262626 default migrates back to Foundry; valid other custom
colors remain custom. Invalid or blocked storage falls back safely. Reset restores
Foundry. CSS/JS assets are cache-busted together.

The renderer paints the selected background before geometry each frame, matching
the original farthest-corner radial ellipse, so switching from solid to gradient
does not retain stale browser background paint. The initial CSS default is also
Foundry. No drawing or business-record repair is
required; existing drawing/layer colors are not rewritten.

## Verification and Deployment

1730 core tests and 896 JavaScript tests pass. Web build passes. Local browser
checks cover actual gradient/solid pixels, File menu, all presets, independent
canvas/panel colors, light-panel header/input/status contrast, reset, migration,
reload persistence, unchanged native exports/layers/history, centered dialog at
1100px, focus trapping and Escape. Screenshots inspected at 1440px and 1100px.
Evidence: `/tmp/appearances-local`, `/tmp/appearances-browser-local.log`,
`/tmp/fade-core.log`, `/tmp/fade-js.log`, `/tmp/fade-build.log`.

Dev deployed `92ac1dd83082f8f38ad84be4b2ea274cc80bba31` on 2026-10-18 at
23:20 UTC. Public browser replay independently reads the rendered build tooltip
and passes the same appearance checks, with no page errors. Four exported DXFs
audit with zero errors/fixes. Public screenshots inspected. Evidence:
`/tmp/appearances-dev`, `/tmp/appearances-browser-dev.log`.
Public canvas interaction smoke also passes the Collinear `lines` fixture:
selection, relation creation, Undo/Redo, native reopen and projected drag after
reopen remain functional with gradient painting (`/tmp/appearances-geometry.log`).

All four installed assembly hashes match the candidate; ten runtime settings
retained, health 200, anonymous root 302, Sync callbacks disabled. Production/Test
processes unchanged. Rollback publish retained at
`/home/ryan/dxfer-runtime/dev-publish-before-92ac1dd`.

Ryan reaffirmed Foundry as the default in Slack delivery
`695c89d9437b5f29dbdd6a0d`. This is owner verification, not independent Flux/Ryan
visual acceptance. No recurring trivial-UI reminder is required.
