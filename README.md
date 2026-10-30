# Kupopop Draw

A native Dalamud drawing-and-guessing game for Final Fantasy XIV. Draw with
friends, race to solve the word, and make a little avatar of your own.

**Experimental 0.3.1 — compiled and automatically tested; in-game testing required.**

## Try it

On this PC, double-click **[Start-My-Server.cmd](Start-My-Server.cmd)** in this
project folder. It builds the current server source and starts local HTTPS.
Use this same launcher after updates. Accounts, certificates, and logs live in
`server/local/`, separate from replaceable build output. Keep its console open.

Custom plugin repository URL:

`https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json`

Add it under Dalamud Settings -> Experimental -> Custom Plugin Repositories,
install **Kupopop Draw**, and open **`/kupopop`**. This custom release is not an
official Dalamud repository submission.

Download the [Windows/Linux server packages](https://github.com/cloudesh/kupopop/releases/tag/v0.3.1).
Windows local testing: extract the Windows server ZIP and double-click
**Start-Local.cmd**, complete its explicit first-run certificate trust step, then
use **`https://localhost:7443`** in the plugin. For multiple computers, host the
server behind its public DNS/TLS endpoint. There is no pre-operated public server.

[Testing and logs](TESTING.md) · [Hosting instructions](server/HOSTING.md)

## Included

- Welcome/account setup with private recovery codes and protected device login.
- Generated avatars and a skippable editor for hand-drawn profile avatars.
- Join Room, Make Room, and opt-in Public Rooms.
- Up to 16 players; default 90-second drawing turns and two complete rounds.
- Pencil, palette, flood fill, and eraser; user list left, canvas center, chat right.
- Server-owned timing, hints, solve-order scores, and drawer rewards.
- One chat input for guessing/conversation, with private done-mode chat for the
  current drawer and correct guessers.
- Self-contained Windows x64/Linux x64 server packages and file diagnostics.

Only three prototype FFXIV words are currently included. No Kupo-Points, shop,
Lodestone verification, or Homeworld Rooms yet. The shared Kupopop profile is
designed for future games; the initial product focuses on drawing.

## Source and build

- `plugin/Kupopop/`: native C# Dalamud API 15 plugin.
- `plugin/Kupopop.Shared/`: protocol, deterministic canvas, avatars, WebSocket client.
- `plugin/releases/`: local plugin ZIP and custom repository metadata.
- `server/Kupopop.Server/`: .NET 10 backend, account persistence, room/game rules.
- `server/Kupopop.Tests/`: engine, privacy, credential, and real-network checks.
- `server/hosting/`: Windows/Linux launchers for local and public hosting.
- `Build-Release.ps1`: builds the plugin and self-contained server archives.

See [TESTING.md](TESTING.md) for build commands and current rule defaults, and
[PROJECT_PLAN.md](PROJECT_PLAN.md) for the design record.

[Dalamud compatibility record](DALAMUD_COMPLIANCE.md) distinguishes published rules
from design judgments. Meaningful human review/testing and human-written official
submission text will be needed before pursuing an official listing.

MIT licensed. Not affiliated with Square Enix or skribbl.io.
