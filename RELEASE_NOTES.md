Kupo Skribbl 0.8.0 is a maintenance and storage update. Gameplay rules and protocol 4 remain unchanged.

- Accounts now use SQLite with transactional writes, unique credential indexes, WAL, integrity checks and an online backup command. The first startup imports legacy accounts.json once, preserving identities, verifiers, names, avatar seeds, pixels and presets. The original JSON remains a private rollback copy.
- Credential rotation is atomic. WebSocket attachment rechecks credentials under the session lock, preventing recovery/revocation races. Account input limits and profile cooldowns survive reconnects.
- Client HTTP bodies, whole-request durations, inbound packet structures and queue bytes are bounded. Credential requests do not follow redirects. Excessive empty WebSocket fragments are rejected.
- State broadcasts assemble player data once for all recipients. Endpoints and client event dispatch have clearer boundaries; shared response reading replaces duplicated code. Unused mascot and word-selection code has been removed.
- Rotated logs expire correctly, retention scans run hourly, and diagnostic I/O failures cannot interrupt gameplay. Database files and sidecars are excluded from release archives.
- Duplicate documentation, obsolete plans and checked-in binary output are removed. README, DEVELOPMENT, HOSTING and WORD_LISTS explain use, source ownership, testing, deployment and configuration.

Back up the complete private data directory before upgrading. Do not copy only a running SQLite main file: use --backup-database or stop the server first. The private word catalog and existing certificates remain unchanged. Server restart ends active rooms. The stable PC launcher and server/local state are preserved.

Built for Dalamud API 15, Windows and Linux. Automated checks run outside FFXIV; in-game rendering, dragging and reload behavior still require verification.
