# Kupo Skribbl

A native Dalamud drawing-and-guessing game for Final Fantasy XIV. Draw with
friends, race to solve the word, and make a little avatar of your own.

**Experimental 0.6.1 — compiled and automatically tested; in-game testing required.**

## Try it

The plugin automatically connects to **https://play.kupopop.com**. Players do not
need to run a server or enter an address. An unavailable service shows a short
connection page; it retries automatically and opens the game when ready.

Custom plugin repository URL:

`https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json`

Add it under Dalamud Settings -> Experimental -> Custom Plugin Repositories,
install **Kupo Skribbl**, and open **`/kuposkribbl`** (`/kupopop` remains an alias).
The installation ID stays `Kupopop` so existing installations update normally.
This custom release is not an
official Dalamud repository submission.

Self-hosting/development: [Windows/Linux server packages](https://github.com/cloudesh/kupopop/releases/tag/v0.6.1)
and [unattended Linux deployment](server/deploy/README.md) remain available. The
stable PC launcher is **[Start-My-Server.cmd](Start-My-Server.cmd)**; its data stays
in `server/local/`. A development plugin can target localhost by changing
`OfficialService.Origin` before building. The distributed plugin uses the single
maintained service and has no connection settings.

Profiles belong to the backend that created them. A local development profile
does not automatically migrate to the maintained server; its encrypted local
credential and server data remain intact.

[Testing and logs](TESTING.md) · [Hosting instructions](server/HOSTING.md)

## Included

- Welcome/account setup with private recovery codes and protected device login.
- Generated avatars and a skippable editor for hand-drawn profile avatars.
- Join via code, Host Room, and Browse Lobbies with opt-in public listings.
- Attached Basic / Advanced / Word Lists setup tabs, sliders and selectable list buttons.
- A resizable 3:2 paper window, centered home choices and a saved light/dark theme.
- Up to 16 players; default 90-second drawing turns and two complete rounds.
- Pencil, palette, flood fill, and eraser; user list left, canvas center, chat right.
- Server-owned timing, hints, solve-order scores, and drawer rewards.
- One chat input for guessing/conversation, with private done-mode chat for the
  current drawer and correct guessers.
- Self-contained Windows x64/Linux x64 server packages and file diagnostics.

The maintained service has 19 private word lists with 511 entries. Standard
categories default on; advanced mechanics/terminology/ultimate/actions lists
default off. Room hosts multi-select lists by name/count; full word banks never
travel in the catalog. Private word data is configured separately from open
server source; see [word-list hosting](server/WORD_LISTS.md).

Both canvases have stroke undo. Portraits support transparency, seven moogle
presets and 22 game-provided job emblems. Mute is removed; a server-authoritative
drawer vote requires unanimity below five players or 70% of others for five or
more, with a thirty-second ballot. Protocol 4 requires matching updated clients
and server for the square 384 x 384 canvas; existing profiles are preserved.

Drawers choose privately from one to eight words (four by default); a 15-second selection timeout is
separate from drawing time. Random-position hints and six-second answer/top-three
recaps pace turns. The toolkit includes shape previews and a 32-color popup.
Live canvas cursors show the drawer's tool; chat uses compact message cards.
Leader kicks are on artist-row right-click; Kick/Skip votes sit on the canvas.
Dismissible update notices stay inside the plugin, with manual updates through
Dalamud. One-letter near misses receive private “So close!” feedback without
echoing the guess to the room. Cursor positions, guesses and unused word choices
never go in diagnostic logs.

No Kupo-Points, shop,
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
