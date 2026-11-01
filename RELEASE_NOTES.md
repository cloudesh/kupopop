Kupo Skribbl 0.6.0 simplifies room setup and adds configurable game rules.

- A continuous, lighter paper texture removes repeating shaded bands.
- Home cards have no small icons and read Join via code, Host Room and Browse Lobbies, with updated descriptions. Numbered page marks are removed and secondary pages have a clear Back to home/room action.
- Host Room opens Basic settings for name, duration, rounds, maximum players, word lists and public/private visibility. Advanced holds hint interval, one to eight private word offers (default four), and separate vote-kick/vote-skip switches. The decorative right-hand note is removed.
- Room rules are enforced on the server. Disabled votes cannot be bypassed by sending commands directly; the leader can still moderate through artist-row actions.
- A guess one insertion/deletion/substitution/adjacent swap away receives private So close! feedback instead of appearing in room chat. It earns no points and reveals neither the answer nor the near-miss text to others. Ordinary and done-mode chat retain their existing behavior.
- The game uses a true square 384 x 384 canvas. Artists, canvas and chat share their visible height; draggable column separators are removed, and hint/tool controls sit above the whole row.

Protocol 4 requires both plugin and server updates; older clients must update. Existing profiles, credentials and private word banks are preserved. Restarting the server ends active rooms.

