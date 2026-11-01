# Private word banks

The server reads `data/word-lists.json` under `KUPOPOP_ROOT` at startup. This is
private host configuration, excluded from Git, plugin/server packages and logs.
The maintained server has 39 lists / 2,631 entries across FFXIV (428), General
(1,586), Internet Memes (174), and Video Games (443). Thirty normal lists default
on; nine advanced lists default off. Entries count list memberships; pools
deduplicate repeated answers. Full banks are deliberately not in the
public repository. The executable source remains public and reviewable.

A fresh self-hosted server generates a four-word starter file automatically. Replace
it with your lists while the server is stopped, keeping it private and backed
up. Preserve accounts and certificates. Restart to load changed lists.

```json
[
  {
    "id": "starter",
    "name": "FFXIV Starter",
    "defaultEnabled": true,
    "complex": false,
    "words": ["Moogle", "Chocobo", "Cactuar", "Spriggan"]
  }
]
```

Use 1–64 lists, unique lowercase letter/digit/hyphen IDs up to 40 characters,
names 2–40 characters, 2–2,000 distinct entries per list, and answers up to 64
characters. Total file limit is 2 MiB. At least one list must default on. Set
complex lists to `complex: true, defaultEnabled: false`. Invalid files fail
startup rather than silently changing the word pool. Each room's selections
are validated; duplicates, empty selections and unknown IDs are rejected. A room
selection must contain at least as many distinct answers as the configured
one-to-eight word offers (four by default). Untouched
legacy three-word starter files upgrade automatically; custom files remain intact.

Lists may include a `category` string (defaults to FFXIV) and a private `aliases`
object mapping canonical answers to arrays of accepted spellings. Each key must
belong to that list, with at most twelve aliases of 1–64 characters. Aliases are
server-only; the selected lists constrain which aliases apply. Canonical answers
and aliases ignore case, accents, spacing and punctuation. We deliberately keep
one-edit mistakes as private near-miss feedback instead of awarding false solves.

The network catalog exposes only ID, name, category, count, complexity and default flag.
It never includes word arrays or a word-download endpoint. The configured candidate words
reach only the current drawer during selection. The chosen answer then reaches
its drawer, correct guessers and normal end-of-turn reveal; players
can collect words they encounter in play. This does not promise unlearnable words.

In Docker deployment the file lives in the persistent `accounts` volume at
`/var/lib/kupopop/data/word-lists.json`, readable by the unprivileged app user.
Back up the complete data directory privately. Never put this file in a Docker
build context, release ZIP, public gist, issue attachment or diagnostic capture.

The November curation favors concrete objects, visible actions, iconic creatures,
characters and scenes. Abstract expansion/encounter/place themes and specialist
references remain optional. Recognition and drawability are editorial judgments,
not a guarantee every group will know every reference. The previous bank is
backed up privately before replacement. No artwork, prose or remote list dump is
copied into the plugin. Reference checks include the official
[FFXIV job guide](https://na.finalfantasyxiv.com/jobguide/battle/),
[Eorzea Database](https://na.finalfantasyxiv.com/lodestone/playguide/db/),
[Mario character guide](https://mario.nintendo.com/ca/characters/),
[Pokédex](https://www.pokemon.com/us/pokedex), and
[Minecraft creature articles](https://www.minecraft.net/en-us/article/meet-enderman).
Meme names were cross-checked with encyclopedia entries including
[Doge](https://knowyourmeme.com/memes/doge) and
[Distracted Boyfriend](https://knowyourmeme.com/memes/distracted-boyfriend).
These are reference checks alongside editorial review, not exhaustive external
verification of each prompt or a popularity certification.

Rare custom words use a separate ephemeral path: the server privately offers a
10% chance per turn when the host enables it. The drawer can enter a 2–48
character answer before the normal selection deadline. Timeout still chooses
an ordinary offered word. User-entered answers never modify this file.
