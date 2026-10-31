Kupo Skribbl 0.4.0 adds transparent portraits, drawing undo, word-list choices and drawer voting.

- Profile editor supports transparent pixels, transparent fill and erasing, with a checkerboard preview. Default reset now loads a truly transparent background; existing custom white pixels remain unchanged.
- Choose seven editable moogle presets or 22 job emblems from the portrait drop-down. Emblems load from the installed game; only a preset ID is shared.
- Undo removes the last complete stroke/fill on both canvases. Gameplay undo is validated on the server and synchronized to everyone; history is bounded to 24 actions and resets each turn.
- Room creation offers a multi-select Word Lists drop-down showing List name - amount. The maintained service has 19 private lists with 511 entries, with 15 normal lists enabled and four advanced lists disabled by default.
- Full word banks stay in private server data and are not shipped in plugin/server release binaries or GitHub source. Clients receive only metadata and answers permitted by game state. Players can still learn words by playing; this is not a guarantee against collecting previously seen answers.
- Removes Mute. Vote to kick appears on the current drawer. Fewer than five connected players require all other players; five or more require 70% of the other players rounded up. Ballots last 30 seconds, fix eligibility/threshold at creation, allow one vote per person, and expire/reset with the turn.
- Protocol 2 requires plugin and server 0.4.0. Profiles, credentials and certificates are preserved; deploying the server ends active rooms. Self-hosted fresh installs receive a small starter bank and can import private lists.

