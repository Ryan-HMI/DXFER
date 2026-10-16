# Deliberate Cleanup

Ryan requires automatic Sync artifact-upload deletion to target only CIRCLE
entities below 0.050-inch diameter. Other cleanup must be deliberate.

Remove implicit duplicate cleanup from ordinary DXF open, normalization and
Send to Sync. Keep the explicit Remove duplicates command and cleanupOnly API.
This does not add small-circle deletion to ordinary playground file open; that
policy belongs to Sync's shared artifact-upload hook, which preserves originals.

No layout changes. Existing geometry is not repaired or rewritten retroactively.
Tests cover ordinary open with/without normalization retaining duplicates,
explicit dedup still calculating correct contours/length, and actual browser
open/download/manual dedup/Undo/Redo with a unique small circle retained.
