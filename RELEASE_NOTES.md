Kupo Skribbl 0.5.2 fixes canvas vote input and makes drawing rooms more compact.

- Read-only canvases no longer intercept clicks meant for Vote kick / Vote skip. Accepted votes produce the existing server chat announcements with the tally and remaining votes.
- The countdown is translucent text inside the canvas at top right. Spectators see a blank canvas and selection countdown while the drawer chooses, with the choosing status in chat; the four choices remain private to the drawer.
- Drawing rooms have a shorter header, tighter top and bottom spacing, and only a small version number in the footer. Round and hint share a compact row.
- Chat removes the Room chat / Done mode heading, separator and thin adhesive-band line. Message backgrounds are slightly stronger while retaining the blue note tint. Done-mode privacy is still explained on the input and message icons.

Protocol 3 is unchanged and compatible with 0.5.0/0.5.1. Accounts, private word banks and server-authoritative voting/scoring are unchanged. Server upgrades preserve profiles and restart active rooms.

