Kupopop Draw 0.3.1 adds sticky notes and a frameless paper silhouette.

- The native title bar and rectangular background are hidden by default, exposing the paper border and transparent corners.
- Drag the moogle/title area to move the book. Integrated pin/close controls and native resizing remain available. Connection can restore the standard frame and its usual window menu.
- Menu cards use yellow, rose and sage sticky notes; artists, chat and profile panels have matching pastel notes with adhesive bands and curled corners.
- Backing sheets rotate in opposing directions around the window, panels and canvas, rather than all stepping down/right. Drawing pixels and interactive controls stay square.
- Paper colors remain visible through the texture. Menu actions respect the notes' bottom padding, and window minimum sizes no longer apply UI scale twice.
- Native component rendering at 980/820 pixels wide and input simulation checked header click/drag and pinned movement.

Requires Dalamud API 15 and a running Kupopop server. Protocol 1 remains compatible with the previous server; account data and custom avatars are preserved.
Compiled and automatically tested; in-game rendering, window controls and input still require testing.

See TESTING.md, UI_DESIGN.md, and server/HOSTING.md for usage, appearance, logs, and hosting.