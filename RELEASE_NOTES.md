Kupopop Draw 0.3.0 turns the drawing room into a little art book.

- Visible notebook binding, layered page edges, paper tabs, washi tape and folded sheet corners across the interface.
- Stronger paper material sampled from the flat interior of FFXIV's Mogpendium notebook.
- Native ink-style moogle with a pom that swings when you move the window and settles afterward. Connection has a reduced-motion option.
- Moogle-like default avatars with pointed ears, expressive faces and varied pom colors. Custom avatar drawings are preserved.
- Room settings use shared label/input/help rows and matching minus/value/plus controls, fixing their alignment.
- Compact 980 x 650 default, drawing-tool icons and the existing artists/canvas/chat layout remain.
- Paper texture load diagnostics and tests for avatar validity, animation bounds, settling and reduced motion.
- Installer feed is published only after its release ZIP is uploaded and verified, avoiding the temporary 404 during earlier releases.

Requires Dalamud API 15 and a running Kupopop server. The default endpoint remains https://localhost:7443.
Protocol 1 remains compatible with the previous server. Three prototype words remain; no persistent Kupo-Points or Lodestone verification yet.
Compiled and automatically tested; in-game rendering/input testing is still required.

Native notebook/form components were visually checked outside the game with a default font at 980 and 820 pixels wide. In-game verification is still required.
See TESTING.md, UI_DESIGN.md, and server/HOSTING.md for usage, appearance, logs, and hosting.
