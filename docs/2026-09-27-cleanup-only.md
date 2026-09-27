# Cleanup-only measurement

Authenticated POST `/api/dxfer/normalize` accepts multipart `cleanupOnly=true`.
Existing normalization remains unchanged when this flag is absent.

The cleanup response has `cleanedDxf` (null when no rewrite is necessary),
`removedCount`, cut length, closed-contour count, bounds and explicit review
reasons. Coordinates are always interpreted as manufacturing inches. No
rotation, origin shift or grain override is performed.

Reuse exact-duplicate classification, then remove only uniquely handled source
entity records. Preserve all other source text, including annotations, tables
and custom records. Refuse referenced or unidentifiable duplicate removals.
Reparse and verify bounds, entity count, path length and contours before returning
a cleaned file. Unsafe geometry never supplies a guessed length or pierce count.
Trailing whitespace after complete DXF records is accepted; truncated records
still require review.

Test deployment only; HMI's guarded CLI handles storage versioning, checksums,
exact current revision ownership, audit, backups and dry runs.
