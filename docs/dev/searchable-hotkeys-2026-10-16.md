# Searchable hotkey panel

Layout contract: hamburger menu gains a direct Hotkeys command opening the existing assignment panel. Keep the View palette entry too. Search stays fixed between header and scrollable rows; footer/errors remain visible, with a constrained 520px panel and desktop-height bound. Command labels wrap within a flexible left column; key recorders use a stable right column. Search matches command names or assigned chords case-insensitively. Empty results show a concise status. Focus search on opening; Escape closes. Preserve browser-stored bindings and existing duplicate-key validation. Filtering must retain row identity so key-recorder events never assign the wrong command. Do not invoke drawing tools while entering a shortcut or searching.

## Verification

The hamburger lacked an entry to the existing panel; key resolution itself was not removed. Menu and View now share the same panel/service/storage. Keyed rows preserve recorder identity through filtering. Accepted-value metadata restores the displayed shortcut following a rejected duplicate without rebinding its listener.

Actual local browser checks pass for both entry points, opening focus, command/chord search, no results, filtered assignments, duplicate rejection with honest displayed value, clearing, Escape, custom key execution and reload persistence. Screenshots at `/tmp/dxfer-hotkey-search-evidence` inspected at1188x761. Existing bindings are not reset; no saved record repair required. Dev only.
