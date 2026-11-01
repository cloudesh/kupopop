# Kupo Skribbl

A native Dalamud drawing-and-guessing game for Final Fantasy XIV.

**Experimental 0.8.0 · Dalamud API 15 · Protocol 4.**

## Install

Add this under Dalamud Settings → Experimental → Custom Plugin Repositories:

`https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json`

Install **Kupo Skribbl**, then open `/kuposkribbl` (`/kupopop` is an alias).
Clients connect automatically to `https://play.kupopop.com`. The stable installation
ID is `Kupopop`, preserving existing configuration and device credentials.
This is a custom repository release; official listing has not been requested.

## Play

- Create a profile and save its private recovery code. Draw an avatar or use a
  moogle face or a locally loaded job emblem.
- Join via code, host a room, or browse opt-in public lobbies.
- Up to 16 players; defaults are 90 seconds per drawer and two rounds.
- Draw with pencil, fill, eraser, shapes, 32 colors and stroke undo.
- Choose privately from four words by default. A rare 10% custom-word option
  can be disabled by the host. Selection lasts 15 seconds before drawing time.
- FFXIV, General, Internet Memes and Video Games themes contain 39 private lists
  and 2,631 entries. Select whole categories or individual lists.
- Server-owned scoring, random letter hints, recaps and final-match podiums.
  Accepted spelling/name variants score normally; other close guesses get
  private feedback. Done-mode chat reaches the drawer and correct guessers only.
- Leader moderation and configurable room votes. Under five players, all other
  voters must agree; five or more requires 70%, rounded up.
- Light/dark paper themes, collapsible tools, compact notifications and a 3:2 window.

Profiles persist in SQLite. Rooms, drawings, chat and match scores are ephemeral.
Persistent currency, shops and Lodestone verification are outside this release.
The plugin performs no automated FFXIV-server interactions or player scans.

## Build and host

- [Development, architecture, review findings and testing](DEVELOPMENT.md)
- [Windows/Linux server setup, deployment and backups](server/HOSTING.md)
- [Private word-list configuration](server/WORD_LISTS.md)
- [Latest release and server downloads](https://github.com/cloudesh/kupopop/releases/latest)

Run **Start-My-Server.cmd** for PC development hosting. It incrementally builds
current source and preserves `server/local/`. Public plugin installations use the
maintained service; a source build can change `OfficialService.Origin` for local
hosting. Accounts belong to their originating backend.

Source lives under `plugin/` and `server/`; build output is ignored. The live
installer feed is promoted only after its matching release ZIP is verified.

MIT licensed. Not affiliated with Square Enix or skribbl.io. See
[component notices](THIRD_PARTY_NOTICES.md). In-game rendering, dragging and
unload/reload behavior require owner verification; automated checks run outside FFXIV.
