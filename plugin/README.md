# Plugin

Native Dalamud plugin source and release packaging.

First product: Kupo Skribbl, an in-game drawing-and-guessing game with a persistent Kupopop
profile. Future games will arrive through plugin updates.

Clients connect automatically to `https://play.kupopop.com`. The service indicator
is read-only and navigation is gated during connection/reconnection. There is no
server selector. `/kuposkribbl` opens the game; `/kupopop` remains an alias.

Native C# rendering is implemented and compiled against API 15. In-game testing
is required; see the root TESTING.md.

Confirmed opening flow: warm welcome/account setup, then a game screen with
Join Room, Make Room, and Public Rooms. Homeworld Rooms is outside scope.

Custom avatar drawing is skippable. Supply a default drawn-looking avatar with
randomized colors and facial features. Use one chat input for conversation and
guesses. Done mode includes the current drawer and correct guessers, with their
messages hidden from players still guessing. Match scores follow solve order
and are server-authoritative; persistent Kupo-Points remain deferred.

Default match settings: 90 seconds per drawing turn and two rounds, with every
player drawing once per round. The host can configure duration and round count.

Read the root project plan and Dalamud compatibility notes before implementation.
