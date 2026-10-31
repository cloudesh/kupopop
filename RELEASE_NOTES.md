Kupo Skribbl 0.3.3 introduces the game's new name and automatic connection.

- Plugin installer name, book heading, window title, server labels and documentation now use Kupo Skribbl. Open with /kuposkribbl; /kupopop remains an alias.
- Automatically connects to https://play.kupopop.com using trusted HTTPS/WSS. Connection controls and server address settings have been removed.
- A short "Trying to connect" page gates play during outages and sign-in. Retries continue automatically with a delay capped at 30 seconds. The header shows Connected when the service/profile is ready.
- Default moogle avatars blend into surrounding paper; white facial features, custom artwork, existing avatar seeds and local account files are preserved.
- Appearance preferences and file diagnostics remain available through the plugin installer settings action or /kuposkribbl debug, once connected.
- Adds an unattended Linux Docker Compose deployment with persistent account/TLS storage. Windows/Linux packages and the permanent local launcher remain available.

Profiles are specific to their backend. Previously created local-development profiles do not automatically migrate to the new maintained service.

