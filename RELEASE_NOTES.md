Kupopop Draw 0.3.2 cleans up the canvas and sticky-note controls.

- The central drawing area is one canvas sheet, without an outer panel, extra padding, or Drawing board heading. Drawing proportions and server coordinates remain unchanged.
- Wider artists panel, larger 40-unit portraits, and no Room host text row.
- Front-page notes have no scrollbars. Button anchors use the actual content region, eliminating the tiny overflow at their lower edge.
- Sticky notes use matching-color edges and soft shadows instead of cream backing sheets. Their actions use paper-tinted strips and ink rules, including the profile actions.
- Empty space near the top of the page now drags the book. Header controls retain click priority, and pinned books stay still.
- Native menu previews at 980/820 pixels wide reported zero horizontal/vertical overflow. Native input checks cover empty-top dragging and header-button clicks.

Requires Dalamud API 15 and a running Kupopop server. Protocol 1 remains compatible with the previous server; account data and custom avatars are preserved.
Compiled and automatically tested; in-game rendering and input still require testing.

See TESTING.md, UI_DESIGN.md, and server/HOSTING.md for usage, appearance, logs, and hosting.