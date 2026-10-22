# Pattern Editor Exit

Accept and Cancel must dismiss both the viewport pattern card and the side editor. Successful Accept retains the edit; Cancel discards it without history. Invalid Accept remains open. A selected source/member must not immediately reopen the dismissed card. Context-menu Show pattern controls and explicit Edit remain available. No layout, dimensions, docking or color changes.

The existing exit clears the draft but retains the shown-controls ID. Clear that visibility on successful acceptance and full cancellation, without changing tool-first center picking or intentional Show/Hide while editing. Explicit Cancel exits even during direction picking; canvas Escape may still cancel the direction-pick substep first.

## Verification

- Focused pattern component tests: 52 passed, including six new exit regressions.
- Full core/component suite: 3243 passed.
- Thirteen-case center-constraint browser regression passed on the isolated candidate, including native/history and Fixed refusal. Evidence: `/tmp/dxfer-pattern-exit-center-regression`.
- Repeated center-drag latency regression passed with 300 ms delayed server frames: p95 16.9/17 ms, no camera movement, and exact unchanged document after Cancel. Evidence: `/tmp/dxfer-pattern-exit-latency`.
- Exit-specific browser replay: 12/12 passed, zero browser errors. Radial/linear creation and editing Confirm/Cancel, context-menu reopen, history/native state, invalid confirmation, and U/V direction-picker cancellation verified. Screenshots inspected at 1100x705. Evidence: `/tmp/dxfer-pattern-exit-qa-r2`.
- Public Dev replay: 12/12 passed on tooltip-verified `4d127fc`, zero browser errors. Same native/history/exit assertions; screenshots inspected. Evidence: `/tmp/dxfer-pattern-exit-installed`. Independent Flux acceptance is separate.

This fixes shared UI state handling, not drawing data. No existing records need repair.

## Deployment

- DXFer Dev only: `4d127fc561738323221bb7940b99f5181594857f`, installed 2026-10-22T01:31:26Z at `https://dxferdev.sync.harrisonmetals.com`.
- Public authentication status retains configured Google sign-in. Runtime settings and disabled Sync callbacks unchanged.
- Production/Test services remained active with unchanged PIDs 3584417/3584475. HMI-Sync untouched.
- Rollback publish: `/home/ryan/dxfer-runtime/dev-publish-before-4d127fc`.
- Isolated QA service stopped after verification.
