# Corner confirmation

## Reproduction

Current Dev 5898136: with the inspector hidden, activating Chamfer keeps it
hidden. Key-in and preview work, but Apply commits and leaves Chamfer active.
There is no explicit Confirm/Cancel pair. With the inspector visible the bottom
information bar omits the command status. Reproduced with two unconstrained
10-unit perpendicular lines, distance 2; no customer drawing modified.

## Layout and interaction contract

- Reuse the right operation inspector and existing Foundry tokens. Starting
  Fillet/Chamfer reveals it without changing independent palette preferences.
- Numeric Radius/Distance with unit label, live preview and the existing canvas
  size handle. Input has a stable width and preserves invalid drafts.
- Confirm and Cancel stay visible below feedback; 32px minimum target, wrapping
  as a group if necessary. Confirm requires a valid preview, commits once and
  exits; Cancel/Escape discards the preview and clears selection. Enter in the
  size input or canvas confirms. Explicit buttons keep their own keyboard action.
- The bottom information bar retains operation feedback even with the inspector
  visible. Text wraps within its grid track, never crowds the size controls.
- Hidden inspector fallback groups size and actions in a wrapping toolbar.
  No full-screen modal, oversized tiles, or changes to drawing coordinates.
- Verify at 1188x761 and 1100x705: key-in, handle, invalid recovery, both cancel
  paths, confirmation, Undo/Redo and native reopen. Desktop only.

## Verification

- Four component regressions failed before the fix (activation and confirmation,
  each for Fillet/Chamfer). The focused 16-test suite now passes.
- Actual browser testing exposed a second shared cause: the global canvas Enter
  handler committed a preview even when Cancel had focus. Added a failing JS
  regression and scoped canvas confirmation to canvas focus; numeric input and
  buttons retain their own keyboard handlers.
- Full suites: 1,712 .NET and 864 JavaScript tests pass; Web build passes.
- Local browser: separate lines, open/closed polylines, both tools, key-in,
  held size drag, invalid/oversized input recovery, Confirm/Enter and
  Cancel/Escape, one-step history and native reopen pass. Preview never leaks
  into exported geometry or creates Undo history. A driving 10-unit edge refuses
  size 2 for both tools unchanged, with Confirm disabled and visible feedback.
- Desktop 1188x761 and 1100x705 checked, including rapid numeric entry and
  hidden-inspector recovery. Six generated DXFs audit with zero errors/fixes.
- Owner evidence: `/tmp/corner-confirm-local`, `/tmp/corner-confirm-core.log`,
  `/tmp/corner-confirm-js.log`; source browser harness is
  `tests/DXFER.Blazor.Tests/interactiveCorners.browser.mjs`.

The shared corner interaction and keyboard-routing causes are fixed. No saved
drawings were changed or require migration. Prior intentional Apply commits
remain saved edits, not something to reverse automatically. Dev rollout and
independent acceptance are recorded separately below.

## Dev rollout

- Installed `a9d15a1cc8fccc45a88598723f7c8f983564c690` on
  https://dxferdev.sync.harrisonmetals.com at 2026-10-18T19:13Z.
  Application fix is `a5dc9c8`; follow-up explicitly waits for Blazor inspector
  visibility in browser assertions/screenshots.
- Published from a clean pushed ref. Four installed assembly hashes match;
  ten runtime settings preserved, health 200, anonymous root 302. Normal Google
  sign-in, independent Dev state and disabled Sync callbacks retained.
  Production/Test runtime PIDs unchanged.
- Full public browser replay passes with rendered build `a9d15a1`: both tools,
  lines/open/closed polylines, numeric/handle preview, invalid and driving
  refusal, Cancel via Enter, Escape, Confirm via click/input/canvas Enter,
  collapsed inspector, rapid typing, one-step history and native reopen.
- Six public exports audit 0 errors/0 fixes. Public screenshots inspected;
  evidence `/tmp/corner-confirm-dev` and `/tmp/corner-confirm-dev-browser.log`.
  Temporary local QA process stopped. Flux independent replay/Ryan acceptance
  are not claimed by these owner-run checks.
