# Kupo Skribbl server

Separate .NET 10 Kupopop backend, using built-in WebSockets and atomic JSON
account persistence. See [HOSTING.md](HOSTING.md) for one-click startup packages.

Supported operating systems: Linux and Windows. The maintained service runs on
Ubuntu at `https://play.kupopop.com`; see [deploy/README.md](deploy/README.md) for
the unattended Docker Compose setup.

The user has accepted a separate C# backend. Planned responsibilities include
accounts, profiles, room discovery/state, drawing/guessing coordination, timers,
hints, and authoritative match scoring. Persistent Kupo-Points/KupoScore is deferred.

The initial implementation is available, with automated engine/network tests.
Run self-hosted copies through a packaged launcher or Docker Compose. The release
plugin uses the maintained service; other endpoints require a source build.
