Kupo Skribbl 0.13.0 polishes the drawing book and hardens server connections.

- Original hand-lettered title on the home page and header, with a fuller marker outline and warm rose/gold/teal accents.
- A smiling moogle artist with a pencil, faint sketch marks on sticky notes and a small paperclip.
- Updated transparent installer icon, a versioned icon URL and a packaged local icon. Release verification now checks the icon is included.
- Excessive WebSocket traffic is rejected before JSON dispatch. Idle/partial messages have a finite receive deadline, and account budgets remain consistent during reconnects.
- Existing game rules, scores, account data, privacy preferences and protocol remain unchanged.

API 15 build and automated/native preview checks passed. This remains an experimental release; in-game verification is still needed.
