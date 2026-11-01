# Kupo Skribbl server: first run

Download and fully extract the Windows x64 or Linux x64 server ZIP from
https://github.com/cloudesh/kupopop/releases/tag/v0.5.1. The packages include .NET;
players and hosts do not need to install an SDK or runtime.

The maintained service is **https://play.kupopop.com**. Regular players connect
automatically; they do not need these self-hosting tools. For unattended Linux
hosting, see [the Docker Compose deployment](#kupo-skribbl-unattended-linux-server).

Version 0.6.0 uses protocol 4: update both plugin and server for square canvases
and configurable word offers/voting. Existing accounts
and certificates are preserved, but a restart ends active rooms. Private word
banks live in `data/word-lists.json`; see [WORD_LISTS.md](WORD_LISTS.md). The
maintained service's full bank is not bundled; fresh hosts generate a starter
file and may replace it with their own lists.

## Windows: local in-game testing

For this source workspace, use **`Start-My-Server.cmd` in the project root** each
time. It builds the current source with the installed .NET 10 SDK and starts it
at `https://localhost:7443`. The build is incremental, so unchanged starts are
quick. Stop an existing server with Ctrl+C before starting an updated one.

This workspace launcher keeps accounts/certificates in `server/local/data/` and
logs in `server/local/logs/`, so replacing build output preserves your account.
It copies existing data from `artifacts/server-win-x64/data/` on first use only
when the permanent data folder is empty; it keeps the original files. Back up the
permanent data folder and do not delete it when updating.

For a standalone downloaded server package:

1. Double-click **Start-Local.cmd** in the extracted server folder.
2. First run generates a localhost-only development certificate. Check its
   displayed fingerprint, then type `TRUST` if you want to install that certificate
   in your Windows user's trusted certificate store. No administrator access is
   normally required. Subsequent starts reuse it without asking again.
3. Keep the console open. A source-built plugin can use `https://localhost:7443`
   by changing `OfficialService.Origin` before building. Release clients always
   use the maintained service and have no connection settings.

Only the machine running this server can use its localhost address. Other PCs
must use a publicly trusted DNS endpoint as described below. No certificate
validation bypass exists in the plugin. A local certificate is development setup,
not a publicly trusted maintainer service or evidence of official approval.

The local trust step is explicit. To undo it, remove the matching `localhost`
certificate by fingerprint from your Current User / Trusted Root Certification
Authorities store using Windows certificate management. Do not remove unrelated
certificates. Local certificates last one year; stop the server and remove only
`data/localhost.pfx` when intentionally replacing an expired development cert.

## Public hosting: Windows or Linux

Before first launch, arrange a DNS hostname pointing at this server and allow
incoming TCP ports **80 and 443**. Hosting/NAT/firewall configuration is outside
the executable; the launcher does not modify system firewall rules.

- **Windows:** double-click **Start-Server.cmd** and enter the DNS hostname.
- **Linux:** run **`bash start-server.sh`** and enter the DNS hostname.

The hostname is saved in `hostname.txt`. Public hosting downloads the pinned Caddy
2.11.7 release from its official GitHub repository, verifies its checksum manifest
and archive, and starts it in front of the game server. Caddy automatically
obtains and renews publicly trusted TLS certificates. External clients use
`https://YOUR-HOSTNAME`; the game backend listens only on loopback port 5080.

Linux requires standard `bash`, `curl`, `tar`, and `sha256sum`, plus the operating
system libraries required by a .NET 10 Linux x64 application (for example a
supported Ubuntu installation). The package is not an ARM or Alpine/musl build.
If Linux denies low-port binding, grant the downloaded Caddy binary the bind
capability (`sudo setcap cap_net_bind_service=+ep ./caddy` where supported), then
run the launcher again. Use your host's service manager for unattended restarts.

Keep the launcher/console alive. Ctrl+C stops the game server and its proxy.
Only one server process may use a data directory. Change the game backend port
only if you also change the proxy configuration; port 5080 is the packaged
public-hosting default. Never expose the backend's plain loopback HTTP port.

Linux local TLS testing is available through `bash start-local.sh`; its generated
`data/localhost.cer` must be trusted using the testing machine's normal certificate
store. The launcher does not install a system certificate on Linux.

## Persistence, backups, logs

### SQLite migration and backups

On first 0.8 startup, `data/accounts.json` is validated and imported in a single
transaction into `data/accounts.db`. IDs, names, avatar bytes/seeds/presets and
access/recovery hashes are preserved. The schema version prevents a second import.
The JSON file remains unchanged as a private pre-migration rollback copy; the
running service reads/writes SQLite exclusively. Corrupt/unsupported data fails
startup instead of creating replacement accounts. Back up the complete data
folder before upgrading and verify the migration before retiring that backup.

SQLite uses WAL, so do not copy only `accounts.db` from a running server. Create
an online snapshot with the server executable and a new destination filename:

```sh
KUPOPOP_ROOT=/path/to/state ./Kupopop.Server --backup-database /private/backup/accounts.db
KUPOPOP_ROOT=/path/to/state ./Kupopop.Server --database-check
```

On Windows, set `$env:KUPOPOP_ROOT` and run the equivalent `.exe` commands.
In Docker, `docker compose exec game /app/Kupopop.Server --backup-database
/var/lib/kupopop/accounts-backup.db` makes a consistent snapshot in the private
volume. Copy that snapshot out with `docker cp`. The command refuses to overwrite
existing files. Maintenance output contains integrity status and row count only.
A stopped-server backup of the whole data directory is also safe and includes
the database plus any WAL/SHM sidecars. Restore only while the server is stopped.

The private word catalog remains `data/word-lists.json`, an editable host
configuration file. Preferences and protected device credentials are client
configuration, separate from the account database.

- `data/accounts.db`: SQLite profiles, custom avatars, and access/recovery verifiers.
  Back up the complete `data/` directory while the server is stopped. Protect
  backups and server files from other machine users. Do not publish this directory.
- `data/localhost.pfx`: private development certificate; protect it and never upload.
- `logs/server-YYYYMMDD.log`: connection, state, timing, and exception diagnostics.
  Seven-day retention, 10 MiB rollover per daily file; its `.previous` file is also
  retained for the day. No account tokens, recovery codes, answers, or chat text.
- `logs/caddy-error.log`: public TLS/proxy errors. Caddy access logging is not
  enabled by the packaged configuration.

Matches/rooms and match scores are in memory. Restarting preserves accounts and
avatars, but ends active rooms. Kupo-Points, Lodestone verification, and homeworld
room discovery are not implemented. Different server instances have independent
accounts; keep the same hostname and backed-up data when moving a server.

## Health and diagnostics

`GET /health` returns the server version and protocol number without listing
players. Account endpoints are rate-limited to 10 requests/minute per IP, and
WebSocket handshakes to 60/minute per IP. Drawing and chat have per-connection
limits and bounded queues. Account capacity is currently 10,000 and room capacity
100 per process; these are deployment limits rather than load-tested guarantees.

`KUPOPOP_ROOT` selects the folder holding data/logs. `KUPOPOP_PORT` overrides the
backend port. `--test-http` is solely for isolated loopback automated testing;
it is never used by normal launchers and is not accepted by the plugin.

# Kupo Skribbl unattended Linux server

This deployment runs the game and Caddy using Docker Compose. It starts after
host reboots and retains profiles and TLS certificates in named Docker volumes.
The backend remains on loopback; only ports 80 and 443 are published.

Copy these deployment files and the published Linux x64 `Kupopop.Server` binary
into a dedicated folder. Copy `Caddyfile.example` to `Caddyfile`, replace the
hostname with the public DNS name pointing to the host, then run:

```sh
docker compose up -d --build
docker compose ps
docker compose logs --tail=30 game
```

The first run pulls official .NET dependency and Caddy images and obtains a
trusted TLS certificate. Incoming TCP 80 and 443 must be reachable. The game
process runs as the unprivileged `app` container user. No accounts or passwords
belong in the image/build context, configuration, or repository.

For an update, back up account data, replace the binary, then run
`docker compose up -d --build game`. This ends active rooms but preserves profiles.
Do not run `docker compose down -v`: that deliberately deletes persistent volumes.

With the game stopped, an account backup can be made from its existing container:

```sh
umask 077
mkdir -p backups
docker compose stop game
docker cp "$(docker compose ps -aq game):/var/lib/kupopop/data" "backups/data-$(date -u +%Y%m%d-%H%M%S)"
docker compose start game
```

Keep backups private and off-host. For a full host migration, preserve the
`accounts` and `certificates` volumes, deployment files, and the same hostname.
Caddy access logs are not enabled; game diagnostics omit chat, words and tokens.
