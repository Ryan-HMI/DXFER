# Coupled orientation: independent QA findings

Status: OPEN. Flux-reported safe refusals; local reproduction and correction
remain required. No regression or geometry-corruption claim.

## Driving angle with free Parallel peer

Slack source: 1792264826.676259, installed Dev d8e284e.

- A=(0,0)-(10,0), whole entity Fixed.
- B=(0,0)-(6,8), C=(4,0)-(10,8), Parallel B/C.
- Change driving Angle(A,B) from 53.13010235415598 to 60 degrees.
- Actual: refusal, geometry unchanged despite C being free.
- Control: removing only Parallel allows the same 60-degree edit.
- Expected: compatible rotation propagates to C while retaining A and all intent.

## Endpoint rotation through orientation chain

Slack source: 1792264446.271689, installed Dev 6dc419a.

- A has driving length12 with only its start Fixed.
- A Parallel B; B Perpendicular C; B/C otherwise free.
- Diagonal endpoint drag on A refuses; removing A Parallel B permits rotation
  while retaining fixed start and length. No redundant closing relation needed.
- Exact coordinates and native fixture remain with Flux; do not invent them.

Both reports suggest missing coupled propagation, but a shared implementation
cause is not yet established. Verify valid edits, fixed-peer incompatibility,
constraint-order variants, recovery, Undo/Redo and native reopen/edit-again.
No existing document repair has been performed.
