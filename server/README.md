# Server

Separate .NET 10 Kupopop backend, using built-in WebSockets and atomic JSON
account persistence. See [HOSTING.md](HOSTING.md) for one-click startup packages.

Required operating systems: Linux and Windows. Hosting is undecided.

The user has accepted a separate C# backend. Planned responsibilities include
accounts, profiles, room discovery/state, drawing/guessing coordination, timers,
hints, and authoritative match scoring. Persistent Kupo-Points/KupoScore is deferred.

The initial implementation is available, with automated engine/network tests.
Run the server through its packaged launcher; hosting provider remains undecided.
