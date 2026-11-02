Kupo Skribbl 1.0.5 — Preparation for review

Reliability fixes, performance improvements and clearer source organization.

- Handle large reconnect snapshots and detect connections that stop responding.
- Preserve match-stat eligibility when unrelated privacy settings change.
- Expire disconnected sessions outside rooms; host and vote kicks now ban the account from that room.
- Make fresh starter banks support all word-choice settings.
- Reuse canvas buffers, batch profile reads, share broadcast encoding and limit per-frame event processing.
- Reduce routine diagnostics and move file writes off the game thread.
- Separate pages and command handlers, standardize C# formatting and update the code map.
- Record sound sources and include license notices in plugin packages.

Dalamud API 15, protocol 5 and SQLite schema 6. Existing accounts and privacy preferences are preserved.
