# Agent Workflow Notes

Date: 2026-05-04

## Incoming Feedback

When new feedback arrives while an implementation or verification pass is already in progress, finish the current pass before switching to the new item unless the new feedback directly affects the code or decision currently being worked on.

For DXFER, this means:

- Keep the active fix/test/verification loop coherent.
- Do not abandon a partially implemented patch for unrelated feedback.
- If the new feedback changes the current patch's requirements, fold it into the current patch immediately.
- If the new feedback is unrelated, capture it and continue after the active pass is verified or cleanly paused.
