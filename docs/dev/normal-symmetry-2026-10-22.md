# Normal And Symmetry Constraints

Ryan requested persistent symmetry between two matching entities and a selected
line axis, including both position and size. Normal and Symmetric were disabled
UI placeholders, not completed constraints.

## Interaction Contract

Use existing constraint palette buttons, icons, selection cues, hotkey settings
and failure feedback. No new panel, card, spacing or dock behavior. Symmetry takes
the pair first and axis last; preserve that order in preselection and tool-first
workflows. Store correspondence, not a newly chosen pairing on every edit.
Normal uses a line endpoint at contact with a circle/arc and keeps the line radial
to that curve. Unsupported selections must give a specific refusal.

## Solver Contract

Use native PlaneGCS equations. Symmetry reflects point pairs about a nondegenerate
line; mirrored circles additionally have equal radii. Reflected arcs reverse
endpoint correspondence and retain equal sweeps. Normal keeps its contact point
on the finite curve and the curve center on the supporting line. Validate both
solver residuals and reconstructed native geometry. Fixed/locked geometry,
driving intent, history and native persistence must remain protected.

Implementation and verification in progress. No existing drawing repairs or
automatic constraints are implied, and no release is claimed yet.
