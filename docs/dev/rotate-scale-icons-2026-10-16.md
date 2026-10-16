# Rotate and Scale icons

Keep existing 20px SVGs inside 32px toolbar targets, inherited state colors, accessible names/tooltips, docking and wrapping. Rotate becomes a 300-degree circular stroke with a pronounced filled arrowhead. Scale uses a small filled square and large outlined square sharing the lower-left corner, without a diagonal arrow. No tool handlers, shortcuts or document behavior change. Verify at actual size and on installed DXFer Dev; Production/Test unchanged. No stored data needs repair.

Verification: build passes with zero warnings/errors; 1,544 .NET and 752 JavaScript tests pass. Actual browser verifies paths, nonblank rasterized pixels, tooltips and unchanged 32px targets/20px glyphs at 1x/2x. Screenshots inspected in `/tmp/dxfer-transform-icons-evidence`. Existing tests requiring the old designs were updated to the explicitly requested shapes. Only runtime artwork changed; no solver or data repair involved.
