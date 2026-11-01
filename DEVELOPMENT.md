# Development and architecture

## Build

Use the .NET SDK pinned by `global.json` and an installed Dalamud API 15 development
folder. The plugin SDK locates the launcher installation; CI sets `DALAMUD_HOME`.

```powershell
dotnet build plugin/Kupopop/Kupopop.csproj -c Release
dotnet run --project server/Kupopop.Tests -c Release
./Build-Release.ps1
```

The test runner covers storage, protocol privacy, drawing rules, credentials,
scoring and sixteen real WebSocket clients. Windows and Linux run in CI.

`artifacts/` contains the candidate feed and plugin ZIP. `plugin/releases/testing.zip` remains a local convenience output and is
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
versions reject unsupported databases. Deployment and account maintenance are managed privately.
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

## Review findings addressed in 0.8.1

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
avatar transparency, legacy white pixels, undo, moogle presets, vote clicks, private
near misses, done chat, reconnect, final ties and update-notice dismissal.

Keep diagnostics free of credentials, private chat, answers, unused choices and
full pixel payloads. Appearance/log preferences are behind `/kuposkribbl debug`.
Public lobby listing remains opt-in and excludes player profiles. The published
plugin uses a single maintained service; user-defined backends require a source
build. Review current [Dalamud publishing requirements](https://dalamud.dev/plugin-publishing/approval-process/)
before an official submission; no official approval is claimed.

## Social features and visibility

Protocol 5 adds account-scoped friend requests, accepted friendships, invitations,
and shared profiles. AccountStore.Social.cs owns normalized unique names, private
preferences, relationships and optional totals. GameEngine.Social.cs authorizes
social commands against accepted relationships or bounded session partners.
MainWindow.Social.cs owns the scoped UI. No global account search exists.

Name keys use compatibility normalization, collapsed whitespace and invariant
case. SQLite enforces uniqueness transactionally. Schema 3 preserves identities,
credential verifiers and pixels; duplicate legacy names get deterministic suffixes.

Friendship acceptance grants mutual presence. Invitations require an accepted
online friend outside a room, expire after five minutes, and validate recipient,
room phase and capacity again when accepted. All notices stay inside the plugin.
Recent partners are limited to 30 per session and clear on intentional close.

Statistics and profile sharing default off. Completed matches record only players
who opted in before the match and stayed opted in. Changing privacy or resetting
stats during a match excludes that match. Only authoritative totals are retained;
no chat, answers, drawings or game-world information is stored with them. Turning
statistics off deletes totals. They are not a spendable currency. Shared profiles
require a prior room encounter or relationship; public directories are absent.

Window startup is closed. MainWindow starts network work only while open, scopes
async completions to their owning client and cancels the retired client on close.
Normal WebSocket closure removes membership immediately; unexpected disconnects
retain the bounded grace. Reopening waits for the old close handshake. Operational
monitoring uses a private local snapshot, with no public HTTP route or identity data.

0.9.1: SQLite schema 4 adds an opt-in invitation-check preference with default off, preserving all existing sharing/stats flags. Authenticated no-store inbox checks are bounded to 32 KiB, IP-limited and account-limited; they do not attach peers. Offline invitations require mutual friendship and the recipient's opt-in; queue contents are ephemeral, five-minute expiring, capped per account and globally. The optional client checker runs once per minute only while the window is closed, deduplicates IDs, caps visible notices, and uses Dalamud's notification manager. No timer reopens the window or creates a game socket. New account sharing is an explicit setup form choice; old privacy settings are not migrated to public. UI-only portrait backdrops preserve stored pixels.

0.9.2: the standard Dalamud frame is mandatory; obsolete paper drag handles and frame preferences are removed. Native title-bar movement and WindowSystem own position, pinning and closing. New room setup explicitly requests six word offers; the protocol fallback remains four for older clients and minimal starter banks. Leader immunity is enforced server-side, and promotion clears an existing kick ballot targeting the new leader. Out-of-turn cursor packets are silently discarded, while malformed active-drawer cursors remain rejected. New-account requests can atomically persist explicitly selected stats/sharing/invitation preferences; old privacy settings are never silently enabled.

0.9.3: hide the native title bar/background outside ShowDebug, retaining native ImGui empty-space movement and WindowSystem focus/close handling. The debug view shows the standard frame. Paper-header pin/close controls are presentation/actions only; no custom drag input or position controller exists. Frameless snapshot fades are disabled and the main game viewport remains forced.

Leaderboards: schema 5 adds separate named/hidden/portrait preferences and drawing-like totals. Stats consent remains required; anonymous display names and visible portraits are the default for tracked accounts. Named listing is explicitly enabled by its owner, independently of shared-profile visibility. Queries are authenticated, use allow-listed sort keys, and return at most 50 rows without account IDs. Opt-out filters every metric; disabling/resetting stats erases drawing likes too. Drawing likes are server-authoritative, drawer-excluding and turn-bound, and duplicate senders remain remembered through reconnect. No sender identity or drawing payload is persisted with likes. Existing job preset IDs are cleared transactionally during migration, leaving account seeds, custom pixels and credentials intact.

0.10.1: Leaderboards and Friends omit redundant page headings. The ranking child scrolls independently above a fixed compact footer containing pagination, portrait/name checkboxes and opt-out; server defaults and privacy choices are unchanged. The leaderboard exposure explanation remains in Profile/onboarding rather than the ranking page. Main navigation tab padding fits the available column width.

0.11 release review: clients never author score awards, timer/phase changes or like totals. Server validation covers actor identity, room ownership, active drawer/turn, deadline, solve state, privacy filtering and fixed vote eligibility. Added bounded command-name validation before dispatch, a dedicated snapshot budget (3 burst/1 per second), and a shared leaderboard-operation budget (4 burst/1 per second). These account budgets survive reconnects. Coordinated answer sharing, multiple-account voting/likes, and farming curated-room scores remain product/identity limitations; this review is not a penetration-test certificate.

Room lists use a private command field rather than public RoomOptions. The server validates up to 64 distinct comma-separated 2–48-character words within 1,600 input characters, overrides client-supplied count metadata, allows custom-only rooms, and returns the list only to the current host between matches. No changes during play, no persistence, no bank merge, and no lifetime game/point/like credit for rooms containing custom words. Existing rare custom-word offers remain separate.

SQLite schema 6 adds a private suggestions inbox. It accepts only explicit authenticated submissions, validates kind/category/content, enforces persistent per-account one-minute/five-per-day quotas and a 5,000-entry global bound. No payload is logged and there is no public inbox reader. Operator CLI --suggestions reads the newest 100 entries as escaped JSON. Suggestions never alter the curated bank automatically. Frame, canvas and portrait behaviour remain unchanged; new tool art is native geometry without bundled images.

0.11.1: suggested word categories may be newly proposed 2–40-character non-control labels; they remain private inbox metadata until maintainer review, with the existing persistent quotas. The form has tabs and Back only at the top, placeholders instead of redundant prompts, and focus-aware multiline hint rendering. Profile alignment uses the same 36-unit allowance as the native-geometry Back control. Local owner tools use a DPAPI-encrypted credential and a private SSH askpass helper; no credentials or helper binaries are distributed.

0.11.2: toolbar art uses consistent 1.35-unit ink outlines, faint material washes, optically aligned stationery silhouettes and a subtle selected-state border. It retains the existing 28-unit hit targets, native geometry, tool IDs and behavior. No new image/font assets or server changes.

0.11.3: owner supplied cartoon references supersede fine-line tool art. Original native geometry uses flat yellow/pink/blue fills and fixed black 2-unit outlines; dark paper has a narrow pale outer keyline. Button targets remain 28 units and selection remains theme-scoped. No reference images are copied or bundled.

0.12.0: home Suggestions aligns to the centered cards' right edge. Sparse stationary fibres overlay the continuous game paper texture, with faint pencil edge marks and a small fold. Four owner-supplied PCM WAVs are embedded resources; metadata chunks are omitted. GameSoundEvents maps confirmed same-room/turn state transitions to local cues, including the final-solver/intermission case and tied top-three match ranks. Repeated snapshots and reconnect baselines stay silent. A four-entry queue preserves correct-guess then podium ordering. Native asynchronous waveform playback owns independent device handles and unmanaged buffers; completed buffers are reset/unprepared before freeing. It does not stop other plugins or invoke FFXIV audio or network APIs. Closing/leaving/disconnecting clears playback; appearance settings can disable sounds. Server/protocol/storage unchanged.

0.12.1: a fifth supplied clock WAV ticks at displayed seconds 10 through 1 of drawing, synchronized with the existing server-offset timer. Countdown state deduplicates frames, skips missed seconds after stalls and suppresses repeats after clock correction. It resets per room/turn and never runs during choosing, recaps, disconnected or closed sessions. Clock ticks yield to queued/active result cues and never build a backlog. The Game sounds setting controls all five cues.

0.12.2: correct_guess is room-wide. Compare newly solved flags for known members on confirmed drawing/intermission transitions, never parse chat or words. The drawer and already-solved members hear other correct guesses. Join/reconnect baselines and repeated states remain silent; other cues remain personal where applicable. Server/protocol unchanged.

0.13.0: original stroke lettering in UiBrand is shared between the header and the larger home cover; no font/art files are extracted or generated. UiStationery.Logo adds a smiling pencil-holding moogle and exports to the transparent 512px installer PNG. Sticky notes receive sparse edge marks and the Host note a paperclip. Native empty-space dragging and compact 3:2 geometry stay unchanged. Custom DalamudPackager.targets includes only runtime binaries/manifest plus images/icon.png; Build-Release normalizes nested ZIP separators and checks icon bytes. IconUrl is versioned to avoid a stale URL cache. Dalamud normal third-party installs still load IconUrl; the bundled image supplies the dev/local path. A cached failed load may require reopening the installer or restarting Dalamud.

Release review: authentication/recovery, SQLite/queries/migrations, game authority, chat/word privacy, votes/likes/custom lists, invitations/leaderboards, bounded queues/HTTP/WebSockets, cancellation, native audio/texture lifetimes and packaging were inspected. Fixes: receive loop now limits an authenticated account to 384 complete messages in a burst, refilling at 160/sec before JSON parsing, independently of existing command budgets. A 45-second whole-message deadline bounds idle and trickled messages (normal client ping is every 15 sec). InputRateLimiter serializes access between receive loops and the engine during reconnects. Protocol 5 and schema 6 are unchanged. No dependency advisories were reported by NuGet including transitive packages at review time. This review is not an external penetration test; collusion, multi-account farming, host/OS maintenance and actual in-game testing remain separate concerns.

0.14.0: lower header wordmark/moogle by eight logical units and reserve the extra row height. A non-interactive native pom bookmark sways from the existing movement spring. Home-cover moogle has a fixed invisible tap target, a bounded spring impulse and four short-lived ink stars. It restores the cursor and never shifts cards, sends network traffic or captures window dragging outside its own tap target. Reduced motion suppresses the bounce/star travel. /kupo aliases both existing open commands and is removed on unload. README banner is an original static export of the same native geometry, excluded from the plugin package.

0.14.0 footer addition: small requested cloudesh credit and a native red heart share the existing version baseline; body space remains reserved. External directory proposal: https://github.com/Senither/dalamud-plugin-listing/pull/19 (listing remains pending maintainer acceptance). Repository metadata uses descriptive game/Dalamud keywords and a direct installation anchor; no popularity or approval claims.

0.15.0: remove the separate header bookmark. Only the home moogle uses a1.7× pom, a proportional longer/thicker stalk, a low-amplitude continuous idle sine and the existing tap impulse/stars. Its fixed hit target includes the enlarged pom. Reduced motion suppresses idle/tap movement. Home card geometry, header brand positioning and native movement remain unchanged. No additional homepage game is included; all interaction stays local.

1.0.0: Profile defaults to a committed portrait/name/lifetime-record preview, with an explicit Change profile picture action. Onboarding Draw my own explicitly enters the editor. Draft reset/cancel reloads existing account pixels/preset/seed without network mutation. Saving details outside picture editing omits avatar/reset/preset fields. Saved profiles return to the preview, and a private authenticated self-profile request supplies own statistics without opening the shared-profile popup or changing consent. Existing shared-profile popups reuse the stat-card renderer. Close clears own cached profile. Home notes rotate only their decorative vertices by small fixed angles; text, controls and hit targets remain upright. GitHub releases with a nonzero major version are regular latest releases; distribution remains through the same custom feed and API15/protocol5/schema6 are unchanged.

1.0.1: all item tooltips centrally require pointer hover with NoNavOverride and one continuous second over the same window-scoped ID/rectangle. Frame gaps, target/layout changes and mouse presses restart the timer. Focused toolbar/header controls no longer request help when the pointer is elsewhere. The resize-corner uses a bounded region query rather than the prior widget's LastItem, with unclipped bounds inside the hovered root window. Existing compact tooltip padding is preserved. No global style timing changes, network calls or backend/protocol/storage changes. Native regression reproduces the former focus leak and verifies target switches, leave/return, clicks and corner behavior.

1.0.3: joining now works during choosing/drawing/intermission by code, public browser or accepted invitation. Public rooms remain discoverable; occupancy includes reconnect-reserved seats. Current-round roster is frozen and the next round snapshots connected members, so new entrants guess immediately and draw from the next round without extending the current round or configured match length. Final-round arrivals draw next match. Match scores and per-turn known-word IDs survive membership removal, preventing duplicate solve/drawer awards after rejoin. Existing liked-drawing and ballot eligibility guards remain intact. Score/result/known-word records are bounded to sixteen distinct match participants, including departures; a new seventeenth identity waits for next match while an existing participant may return. Partial-match entrants do not enter the initial lifetime-stat eligibility set. Protocol 5 and SQLite schema 6 unchanged.
