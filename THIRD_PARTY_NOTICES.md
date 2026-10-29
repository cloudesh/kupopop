# Third-party components

Kupopop's own source is MIT licensed. Dependencies keep their own licenses.

- The plugin references Dalamud and its ImGui bindings supplied by the user's
  Dalamud installation. These binaries are not included in the plugin ZIP.
  https://github.com/goatcorp/Dalamud
- The server packages include the .NET 10 / ASP.NET Core runtime under Microsoft's
  applicable open-source runtime licenses and notices.
  https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
  https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
  https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt
- Public-hosting launchers download Caddy 2.11.7 from its official GitHub release
  after checking a pinned checksum manifest and the archive checksum. Caddy uses
  the Apache License 2.0 and its dependency notices. It is not included in the
  initial server ZIP; downloaded release contents retain their notices.
  https://github.com/caddyserver/caddy/blob/v2.11.7/LICENSE

No skribbl.io code or artwork is included. Default avatar shapes are drawn
procedurally by Kupopop. This project is not affiliated with Square Enix.
