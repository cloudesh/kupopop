# Private word banks

The server reads `data/word-lists.json` under `KUPOPOP_ROOT` at startup. This is
private host configuration, excluded from Git, plugin/server packages and logs.
The maintained server currently has 19 lists / 511 entries: 15 normal lists on
by default and four advanced lists off. Full banks are deliberately not in the
public repository. The executable source remains public and reviewable.

A fresh self-hosted server generates a small starter file automatically. Replace
it with your lists while the server is stopped, keeping it private and backed
up. Preserve accounts and certificates. Restart to load changed lists.

```json
[
  {
    "id": "starter",
    "name": "FFXIV Starter",
    "defaultEnabled": true,
    "complex": false,
    "words": ["Moogle", "Chocobo", "Cactuar"]
  }
]
```

Use 1–32 lists, unique lowercase letter/digit/hyphen IDs up to 40 characters,
names 2–40 characters, 2–2,000 distinct entries per list, and answers up to 64
characters. Total file limit is 2 MiB. At least one list must default on. Set
complex lists to `complex: true, defaultEnabled: false`. Invalid files fail
startup rather than silently changing the word pool. Each room's selections
are validated; duplicates, empty selections and unknown IDs are rejected.

The network catalog exposes only ID, name, count, complexity and default flag.
It never includes word arrays or a word-download endpoint. The active answer
still reaches its drawer, correct guessers and normal end-of-turn reveal; players
can collect words they encounter in play. This does not promise unlearnable words.

In Docker deployment the file lives in the persistent `accounts` volume at
`/var/lib/kupopop/data/word-lists.json`, readable by the unprivileged app user.
Back up the complete data directory privately. Never put this file in a Docker
build context, release ZIP, public gist, issue attachment or diagnostic capture.
