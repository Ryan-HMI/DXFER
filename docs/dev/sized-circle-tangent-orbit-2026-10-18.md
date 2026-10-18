# Sized-circle tangent orbit

Ryan delivery30042d4ff59979e054754322, screenshotF0CA60DVAM8, shows an
origin-anchored R10 circle and an externally tangent secondary circle. This
differs from the prior free-primary-radius regression. Native original requested;
the screenshot does not expose every stored constraint.

## Root cause and correction

After a direct drag attempted to change the primary driving radius, the fallback
could project translation along a tangent line but not around a tangent circle.
Four new internal/external, reversed-reference cases failed in core and preview.

The existing validated translation candidate path now projects a circle center
onto the original internal/external tangent orbit, keeping both radii. All
constraints, dimensions, Fix and layer guards still apply before commit. Preview
mirrors the projection. A cursor at the peer center or radial-only no-op refuses
without history. The canvas module URL is versioned to refresh preview code.

This supports center translation and translation of circles with driving size.
A free-radius grip remains a resize request: it is not silently converted into
an orbit, nor is moving a center to accommodate a resize implemented here.
Arbitrary coupled systems and arc contact domains are not newly supported.

## Verification

1680 .NET and840 JavaScript tests pass. New tests cover both tangent branches,
both reference orders, whole-Fixed peer, driving secondary radius, secondary
center Fix refusal/removal recovery, scales0.00001/1/10000, off-axis geometry,
degenerate cursor position and radial-only no-op. Existing free-radius cases
and incompatible free-radius resize refusal remain covered.

The existing anchoredTangentDrag.browser.mjs now includes driving-primary
center-orbit cases. Sixteen local actual-canvas cases pass held preview/release,
history, native reopen and edit again, including two resize refusals with no
history. Sixteen exports audit with zero errors/fixes. Held-orbit screenshot
inspected.

## Installed verification

Installed7f8e712c63449a7126f9f043b69d003d9e75e00e on DXFer Dev at
2026-10-18T18:36Z. Release publish passed. Public browser independently read
the rendered7f8e712 build and replayed all16 cases;16 public exports audit
zero errors/fixes and held-orbit screenshot was inspected. Flux independent
replay remains pending. Public browser log:/tmp/dxfer-orbit-dev-browser.log.

Published/live assembly hashes match;10 runtime settings unchanged, health200,
anonymous root302. Normal sign-in, independent Dev storage and disabled Sync
callbacks retained. Production/Test PIDs unchanged. Rollback retained at
/home/ryan/dxfer-runtime/dev-publish-before-7f8e712.

No existing drawings or manufacturing records rewritten. This repairs the shared
drag behavior; valid saved constraints need no data repair. Ryan's exact native
drawing remains unverified until supplied. Production/Test remain out of scope.
