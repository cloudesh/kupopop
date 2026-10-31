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
