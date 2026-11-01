Kupo Skribbl 0.6.1 cleans up navigation and room setup and adds dark paper.

- The window resizes at a locked 3:2 ratio (990 x 660 default), with a square canvas and matching-height side panels. Old saved window shapes are corrected through native size constraints.
- Home choices are centered; the old headline/subheading are removed. Main navigation uses a consistent grouped style.
- Host Room has a left-aligned title and a right-aligned Back to home action. Basic, Advanced and Word Lists tabs join the settings panel directly.
- Numeric room controls are sliders with live values below. Word lists have their own selectable button grid with Normal lists / All / None shortcuts, and the hosting action stays visible while lists scroll.
- Lobby Refresh moves to the top right. Empty lobbies show a plain invitation and Host Room, which opens public Basic setup for review before creation.
- A saved light/dark paper setting is available through the header moon/sun and preferences. Dark mode covers panels, controls and popups while retaining drawing/portrait colors and readable canvas overlays.

Protocol 4 is unchanged and the existing 0.6.0 hosted backend remains compatible. This UI update does not require restarting live rooms or migrating accounts/word lists. Windows/Linux self-host packages are rebuilt from current source; Start-My-Server.cmd and permanent local data are preserved.

