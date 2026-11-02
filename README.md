<p align="center">
  <img src="plugin/images/banner.png" alt="Kupo Skribbl — a Pictionary-style FFXIV drawing game" width="100%" />
</p>

<h1 align="center">Kupo Skribbl · FFXIV Pictionary for Dalamud</h1>

<p align="center">A Pictionary-style drawing and guessing game inside Final Fantasy XIV for 2–16 players.</p>

<p align="center">
  <a href="#install-in-dalamud"><strong>Install in Dalamud</strong></a> ·
  <a href="#how-to-play">How to play</a> ·
  <a href="https://github.com/cloudesh/kupopop/releases">What's new</a> ·
  <a href="https://github.com/cloudesh/kupopop/issues">Feedback &amp; help</a>
</p>

<p align="center">
  <a href="https://github.com/cloudesh/kupopop/releases"><img src="https://img.shields.io/github/downloads/cloudesh/kupopop/total?style=flat-square&amp;color=9a506b&amp;label=downloads" alt="GitHub release downloads" /></a>
  <a href="https://github.com/cloudesh/kupopop/releases/latest"><img src="https://img.shields.io/github/v/release/cloudesh/kupopop?style=flat-square&amp;color=42766b&amp;label=latest" alt="Latest Kupo Skribbl release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/cloudesh/kupopop?style=flat-square&amp;color=9c7844" alt="MIT license" /></a>
</p>

| Multiplayer | Profiles | Word lists |
| --- | --- | --- |
| Private rooms, public lobbies and friend invitations. | Custom drawn portraits and optional leaderboards. | FFXIV, everyday objects, memes, video games and custom words. |

Requires **Dalamud / XIVLauncher**. Connects automatically to our shared server.

## Install in Dalamud

1. Open **Dalamud Settings → Experimental → Custom Plugin Repositories**.
2. Paste this address, add it and save:

   ```text
   https://raw.githubusercontent.com/cloudesh/kupopop/main/plugin/releases/repo.json
   ```

3. Find **Kupo Skribbl** in the plugin installer and install it.
4. Open with **`/kupo`**, **`/kuposkribbl`** or **`/kupopop`**.

Create a profile and save your recovery code somewhere private. Never share it.

**Unofficial plugin:** not listed in the official Dalamud repository. Each player needs the plugin installed.

## How to play

1. Join via code, host a room or browse public lobbies.
2. On your turn, choose a word and draw it.
3. Guess in chat. Earlier correct guesses earn more points; correct guesses also reward the drawer.
4. The highest score after the final round wins.

Hosts choose the timer, rounds and word lists. Custom-word matches are unranked. Join an ongoing match to guess immediately and draw from the next round (or next match if it is the final round).

Right-click a player to add a friend or view their shared profile. Send invitations from **Friends**.

## Privacy

Closing the plugin leaves your room and disconnects you. Optional background invitation checks run once a minute and never reopen it automatically.

Choose whether to record statistics, share your profile and receive background invitations during setup or in **Profile → Privacy & invitations**. Shared profiles are visible to friends and people you played with; there is no player search.

Recorded statistics appear on leaderboards with your portrait and an anonymous name by default. A recognisable portrait can identify you. You can show your name, hide your portrait, leave leaderboards, or reset/disable statistics.

## Feedback

[Report a bug](https://github.com/cloudesh/kupopop/issues) with your plugin version and steps to reproduce it. Use **Suggestions** in the plugin for ideas or new words. Never include credentials or private chat.

For contributors: [build and architecture guide](DEVELOPMENT.md) · [release notes](https://github.com/cloudesh/kupopop/releases) · [licenses and notices](THIRD_PARTY_NOTICES.md).

The download badge counts downloads, not unique players.

Not affiliated with Square Enix or skribbl.io.
