# Development

Kupo Skribbl is a native Dalamud client and an authoritative C# game server.
The client renders the game; the server decides turns, scores, votes and which
information each player may receive.

## Build and test

Use the .NET SDK selected by `global.json` and Dalamud API 15 development
assemblies. The plugin SDK finds the local launcher installation; set
`DALAMUD_HOME` when building elsewhere. Dependency lock files are committed.

```powershell
dotnet build plugin/Kupopop/Kupopop.csproj -c Release
dotnet run --project server/Kupopop.Tests -c Release
./Build-Release.ps1
```

The standalone test runner covers storage and migrations, authentication,
protocol validation, scoring, privacy, late joining and live WebSocket clients.
CI runs it on Windows and Linux. Windows also checks DPAPI credentials and
compiles the plugin against Dalamud.

## Code map

Paths below are relative to the repository root.

| Location | Responsibility |
| --- | --- |
| `plugin/Kupopop/Plugin.cs` | Commands, WindowSystem registration, notifications and unload |
| `plugin/Kupopop/MainWindow.cs` | Session lifecycle and main-thread event handling |
| `plugin/Kupopop/MainWindow.*.cs` | Individual pages, navigation and overlays |
| `plugin/Kupopop/CanvasWidget.cs` | Pointer input, local drawing preview and canvas texture |
| `plugin/Kupopop/Ui*.cs` | Scoped styling, layout and native artwork |
| `plugin/Kupopop/CredentialVault.cs` | Service-bound, Windows DPAPI-protected credentials |
| `plugin/Kupopop/GameAudio.cs` | Local sound queue and native buffer ownership |
| `plugin/Kupopop.Shared/` | Protocol, bounded transport, pixel operations and shared models |
| `server/Kupopop.Server/GameEndpoints.cs` | HTTP authentication and WebSocket lifecycle |
| `server/Kupopop.Server/GameEngine.cs` | Session ownership, command dispatch and room rules |
| `server/Kupopop.Server/GameEngine.Turns.cs` | Guess scoring and turn transitions |
| `server/Kupopop.Server/GameEngine.Social.cs` | Relationship-based access and private invitations |
| `server/Kupopop.Server/AccountStore*.cs` | SQLite accounts, preferences, relationships and statistics |
| `server/Kupopop.Server/WordBank.cs` | Private word configuration and word selection |
| `server/Kupopop.Tests/` | Regression checks, grouped by feature |

## Ownership and concurrency

The engine lock serializes room mutations. It can call the account store, whose
own lock protects its SQLite connection; the store must not call back into the
engine while holding that lock. Socket sends enqueue bounded messages rather
than waiting for network I/O under the engine lock.

Network tasks enqueue validated events for the client. The main thread owns
ImGui state and textures. Async completions carry their originating client so a
retired session cannot update a newly opened window. Closing cancels HTTP work,
sends a normal WebSocket close and clears local room state. Unexpected drops
retain a 30-second reconnect reservation on the server.

Native ImGui movement owns window dragging. WindowSystem owns focus and closing;
the normal frame is hidden except in debug mode. Styles stay scoped to the plugin.
Textures and sound buffers have explicit owners and are released on unload.

## Game invariants

- Only the active drawer can paint, undo or publish a canvas cursor. Commands
  carry a turn ID and are checked against the server deadline.
- Word offers go only to the drawer. Done-mode chat goes only to the drawer and
  players who solved the word. Recipient filtering happens before transmission.
- Correct guesses score once per player per turn. Match scores and known-word
  membership survive leaving and rejoining.
- A round freezes its drawing order. New players can guess immediately and enter
  the next round. A match accepts at most 16 distinct participants, including
  departed players; existing participants may return when room capacity allows.
- Vote eligibility and thresholds are fixed when a ballot starts. Disconnecting
  does not lower the threshold. The leader cannot be vote-kicked.
- Host-written lists stay in room memory and make the match unranked. They never
  enter the curated bank. Suggestions are separate, manually reviewed records.

## Storage and privacy

Protocol version 5 and SQLite schema 6 are independent of plugin release numbers.
SQLite uses parameterized statements, WAL, full synchronous commits and explicit
transactions. Credentials are random secrets stored as hashes on the server.
Migration preserves the original legacy JSON file and rejects unknown schemas.
Do not replace account data during an upgrade.

Profile sharing, lifetime statistics and closed-window invitation checks require
user choices. Names on leaderboards require a separate opt-in. Tracked accounts
show anonymous names and their portraits by default; a recognisable portrait may
still identify its owner. Users can hide portraits or leave leaderboards.
Disabling statistics deletes their totals. Custom-list matches and partial-match
entrants do not contribute to lifetime match statistics.

Friend requests and profile views require a prior room encounter or relationship;
there is no global player search. Recent partners are session-only. Invitations
expire after five minutes. Optional closed-window checks use one bounded HTTPS
request per minute without a game socket or online presence.

The private word bank is runtime configuration, not a public asset. Send only
catalog metadata and the words needed for the current turn. Never put credentials,
private chat, answers, unused choices or drawing payloads in diagnostics.

## Service boundaries

The published client connects to `https://play.kupopop.com`. It does not bypass
certificate checks or follow redirects carrying credentials. The backend listens
on loopback behind a trusted TLS proxy; forwarded headers are accepted only from
trusted proxies. HTTP requests, WebSocket messages, fragments and queues have
size limits. Per-account command budgets survive reconnects, and a whole-message
deadline prevents stalled senders from retaining connections indefinitely.

Current bounds are 10,000 accounts, 100 rooms and 256 upgraded connections.
These are resource limits, not load-test capacity claims. The single engine lock
and synchronous database access favour consistency; measure contention before
increasing capacity. Multiple accounts and coordinated answer sharing remain
abuse limitations. Server patching, backups and monitoring are operational duties.

## Releases

Bump `Directory.Build.props` before publishing changed binaries. The build uses
an explicit package allowlist: plugin/shared assemblies, dependency metadata,
manifest and installer icon. Runtime data and operator tools are never packaged.

`Build-Release.ps1` writes `artifacts/testing.zip`, a candidate `artifacts/repo.json`
and the ignored local `plugin/releases/testing.zip`. The tracked
`plugin/releases/repo.json` is the live feed. CI uploads and verifies the new ZIP
before promoting that feed. Never advertise an unavailable download or overwrite
an existing release with different binaries. Feed-only commits do not rebuild.
If publication fails after creating a draft, inspect that draft before removing
it and retrying. A published release must be corrected with a new version.

Before release, run the regression suite, compile the plugin and check direct and
transitive dependency advisories. For submission or UI changes, personally test
inside FFXIV: open/close, unload/reload, both themes, UI scales, drawing and undo,
private chat, reconnects, invitations and sound. Native previews cannot verify
the game's graphics backend or real frame cost; use Dalamud Plugin Statistics.

See the [official submission requirements](https://dalamud.dev/plugin-publishing/submission/)
and [technical considerations](https://dalamud.dev/plugin-development/technical-considerations/)
before requesting official listing. Custom distribution is not official approval.
