# Drag Preview Progress

The PlaneGCS canvas scheduler previously required a completed response to match
the newest pointer sequence. With a pointer event interval shorter than the
round trip, every response could be discarded until movement stopped.

Keep one solve in flight and coalesce queued positions, but display its completed
valid geometry within the same active drag/document/engine session. A changed
modifier or temporary snap intent still invalidates it. Superseded failures do
not rewind the preview; a current refusal restores the baseline. Old snap cues
are not accepted. Release still solves the exact final pointer, not the last
displayed preview. Escape, replacement, disposal and engine changes invalidate
late responses. No solver equations, constraints or native persistence changed.

Verification:
- New continuous-motion scheduler regression failed before the fix (0 != 1).
- 1,983 JavaScript tests passed, including 43 drag scheduler cases.
- Six Chromium scheduling checks passed with controlled async responses.
- 51 focused .NET component tests passed; Release publish passed.
- Real local PlaneGCS browser run displayed 42 different geometry previews
  during 1,864 ms of held motion. Exact release, corner/axis preservation,
  Escape without stale repaint, Undo/Redo and native reopen passed.
- Evidence: /tmp/dxfer-drag-preview-sustained-20261024/results.json and PNG/DXF files.

This fixes proven preview starvation, not all possible large-document or network
latency. Independent verification on Ryan's slow drawing remains required.
No stored drawings or records need repair from this change.
