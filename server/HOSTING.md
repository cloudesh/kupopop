# Kupopop Draw server: first run

Download and fully extract the Windows x64 or Linux x64 server ZIP from
https://github.com/cloudesh/kupopop/releases/tag/v0.3.0. The packages include .NET;
players and hosts do not need to install an SDK or runtime.

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
3. Keep the console open. In the plugin, use `https://localhost:7443`.

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

- `data/accounts.json`: profiles, custom avatars, and access/recovery verifiers.
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
