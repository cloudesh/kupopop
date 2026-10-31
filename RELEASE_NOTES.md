Kupo Skribbl 0.5.0 adds word choice, clearer turn presentation and a larger drawing toolkit.

- Drawers privately choose from four words within 15 seconds. The server auto-picks on timeout; the full configured drawing timer starts after selection. Unused choices never go to other players.
- Larger clock/timer at the canvas top right. Hints reveal randomized positions, retaining previously revealed letters.
- Six-second turn recaps dim the plugin and show the answer plus up to three earliest guessers and their awarded points. Solve order stays correct if a solver leaves.
- Artists show only portrait, name and points. Room leaders right-click a row to kick; this includes the current drawer. Vote Kick and Vote Skip are small controls in the lower-right canvas area. Skip uses the same frozen eligibility/threshold rules, ending the turn without removing its drawer.
- Compact, individually boxed chat messages. Game/server cards have an information icon.
- Shared drawer cursor shows canvas position, selected tool, brush size and color. Cursor data is ephemeral, canvas-bounded, drawer-authorized and rate-limited; it is not persisted or logged.
- Line, rectangle, square, ellipse and circle tools preview locally and commit on release as one undo action.
- A Color button opens 32 colors, with transparency additionally available in the portrait editor. Existing palette indexes and saved avatar colors are preserved.
- Reusable notifications appear inside the plugin's bottom-right corner. Update checks read bounded public feed metadata on startup and every ten minutes. New-version notices are dismissible per version; updates remain manual through Dalamud.

Protocol 3 requires updating both plugin and server. Deployment restarts active rooms while preserving profiles, credentials, certificates and private word banks. Stock three-word starter banks upgrade to four; custom selections need at least four distinct words.

