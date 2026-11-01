# Development and architecture

## Build

Use the .NET SDK pinned by `global.json` and an installed Dalamud API 15 development
folder. The plugin SDK locates the launcher installation; CI sets `DALAMUD_HOME`.

```powershell
dotnet build plugin/Kupopop/Kupopop.csproj -c Release
dotnet run --project server/Kupopop.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File server/Test-ServerTools.ps1
./Build-Release.ps1
```

The test runner covers storage, protocol privacy, drawing rules, credentials,
scoring and sixteen real WebSocket clients. After packaging, run it again to
include the self-contained Windows TLS executable. Linux runs in CI.

`artifacts/` contains the candidate feed, plugin ZIP and Windows/Linux server
bundles. `plugin/releases/testing.zip` remains a local convenience output and is
not tracked. `plugin/releases/repo.json` is the live installer feed. The release
workflow uploads and verifies assets before promoting that feed; feed-only
commits do not trigger another release build. Bump `Directory.Build.props` for
updates. Never publish private runtime state with a package.

## Source map

| Area | Responsibility |
| --- | --- |
| `plugin/Kupopop/Plugin.cs` | Dalamud registration, window ownership and unload |
| `MainWindow.cs` | UI state, main-thread event handling, connection/retry work |
| `MainWindow.Pages.cs` | Page rendering and navigation |
| `MainWindow.Overlays.cs` | Canvas tools, choices, podiums and notifications |
| `CanvasWidget.cs`, `AvatarTextures.cs` | Local previews and bounded texture ownership |
| `Ui*.cs`, `PomMotion.cs` | Scoped native stationery style, geometry and interaction |
| `CredentialVault.cs`, `DiagnosticLog.cs`, `UpdateChecker.cs` | Protected login, bounded diagnostics and metadata checks |
| `plugin/Kupopop.Shared/` | Versioned messages, bounded transport, deterministic pixels and shared helpers |
| `server/Kupopop.Server/Program.cs` | Hosting, limits, lifecycle and maintenance commands |
| `GameEndpoints.cs`, `Peer.cs` | HTTP authentication and bounded WebSocket sessions |
| `GameEngine.cs` | Serialized authoritative room state and per-recipient privacy |
| `AccountStore.cs` | Transactional SQLite persistence and one-time legacy import |
| `WordBank.cs`, `GuessMatching.cs` | Private host configuration, offers and accepted guesses |
| `InputRateLimiter.cs`, `FileLog.cs` | Account budgets and private operational diagnostics |
| `server/Kupopop.Tests/` | Storage, rules, privacy and live transport regression checks |
| `server/hosting/`, `server/deploy/` | Packaged launchers and unattended deployment templates |

Small helpers remain separate where they have distinct ownership or independently
testable behavior. There is no downloaded game code, frontend web server, ORM,
external database daemon or optional runtime plugin loader.

## Server design

Kestrel listens on loopback. Public deployments use Caddy for trusted HTTPS/WSS,
sharing its network namespace in Docker. Only ports 80 and 443 are published.
Forwarded headers are accepted from the default trusted loopback proxy, one hop.
The container runs as `app`; accounts and certificates use separate volumes.

One engine lock serializes state transitions, ensuring turn ownership, scoring,
undo and ballot consistency. Per-recipient snapshots filter secret words,
choices, solved-state access and done-mode chat. Player profiles are assembled
once per state broadcast, rather than once for every recipient. Pixel operations
use deterministic indexed arrays; local previews never determine server scores.

SQLite uses indexed primary/access/recovery keys, prepared parameters, WAL and
full synchronous commits. Each write is transactional and becomes visible only
after commit. Reads return independent pixel arrays. The store serializes access
to its connection; independent connections still observe committed rows. Schema
versions reject unsupported databases. See HOSTING.md for migration/backup steps.
Private word lists remain an editable configuration file, separate from account
records and never exposed as a word-bank endpoint.

## Client design

Network tasks use bounded queues; the main thread consumes events and owns
ImGui/Dalamud state and textures. HTTP bodies, packet sizes, fragment counts,
queue counts and queue bytes are bounded. Credentials cannot follow redirects;
account responses are validated before saving. Device tokens are DPAPI-protected
and bound to their service origin. Shutdown cancels requests and socket work.

WindowSystem retains close ordering, focus, pinning and resizing. Native ImGui
movement owns dragging in the main game viewport. Styles are scoped to the
plugin. Font/icon assets load from the installed game, and texture caches are
bounded. Canvas texture refresh is coalesced while pointer previews stay local.

## Review findings addressed in 0.8.0

| Finding | Change and regression coverage |
| --- | --- |
| Whole-file account rewrites and linear recovery lookup | SQLite transactions and unique indexes; migration, restart and concurrent writers |
| Potential partial credentials after failed writes | Commit-before-return; injected write failures leave prior credentials usable |
| Recovery could race an authenticated socket handshake | Credential recheck inside the session/revocation lock; stale attach rejected |
| Reconnect reset per-socket input budgets | Account-scoped rate-limit objects retain cooldowns and budgets |
| Unbounded HTTP bodies / queue byte accumulation | Shared bounded reader, whole-request deadlines, disabled redirects and 4 MiB inbound queue budget |
| Malformed state could reach UI arrays | Packet/profile/canvas validation before enqueue |
| Excessive empty fragments could evade message byte limits | Independent 256-fragment limit |
| Repeated snapshot profile work | Shared player views per broadcast |
| Rotated logs escaped expiry; log I/O could affect gameplay | Seven-day rotation cleanup, hourly scans and nonfatal diagnostic failures |
| Older SDK embedded an older servicing runtime | Self-contained server packages explicitly use .NET/ASP.NET Core 10.0.14 |
| Obsolete helpers and duplicated documentation | Removed unused mascot/word-selection paths; consolidated documentation and build outputs |

Current dependency advisories are checked with `dotnet list ... package
--vulnerable --include-transitive`. The server's SQLite packages are pinned by
its lock file. No published vulnerability was reported during this review.
These checks do not certify security or establish an internet-scale capacity.
Deployment limits are 10,000 accounts, 100 rooms and 256 upgraded connections.
Host OS patching, off-host backup retention and operational monitoring remain
deployment responsibilities. Containers use a read-only root filesystem,
dropped capabilities and no-new-privileges; writable data is confined to the
private volume and temporary storage. Release archives use an explicit allowlist.

## Manual verification

Test actual FFXIV rendering and unload/reload before official submission. Check
all pages, both themes and multiple UI scales; native previews use substitute
fonts/services and cannot verify the game's graphics backend.

Verify dragging, pinning, Escape/close ordering, the resize corner, consistent
headers, square canvas and aligned side panels. Word selection/recaps must be
canvas-centered; toolbox/palette interaction must not paint underneath. Verify
avatar transparency, legacy white pixels, undo, job icons, vote clicks, private
near misses, done chat, reconnect, final ties and update-notice dismissal.

Keep diagnostics free of credentials, private chat, answers, unused choices and
full pixel payloads. Appearance/log preferences are behind `/kuposkribbl debug`.
Public lobby listing remains opt-in and excludes player profiles. The published
plugin uses a single maintained service; user-defined backends require a source
build. Review current [Dalamud publishing requirements](https://dalamud.dev/plugin-publishing/approval-process/)
before an official submission; no official approval is claimed.
