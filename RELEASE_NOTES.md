Kupo Skribbl 0.7.0 adds broader word themes and a more consistent drawing UI.

- Frameless dragging uses ImGui's native move controller inside the main game viewport. The separate position controller is removed. WindowSystem close, focus, pin and native resizing remain. Native movement tests pass; FFXIV ghosting still needs owner confirmation.
- All pages share one compact header without the art-book subheading. Room name/round, centered word and invitation actions occupy one row. Settings pages match the paper and leave room for the small version label. Informational toasts omit the plugin-name heading.
- Drawing tools occupy one translucent bottom-left line with an inline collapse arrow. Word choices and answer recaps center on the canvas. Recaps and final match results have portrait podiums; tied totals share victory, and departed participants retain their scores.
- A server-owned 10% rare offer lets the drawer type their own word before the selection deadline. Hosts can disable it. Custom answers are bounded, private until normal reveal, ephemeral and excluded from diagnostics.
- Word-list settings have FFXIV, General, Internet Memes and Video Games groups, category switches and independent collapse controls. Hosts can change settings between matches. The maintained service has 39 private lists / 2,631 entries, including 1,586 general prompts. Nine specialist lists default off. Fresh self-hosted servers retain the small starter bank until configured.
- Private accepted-answer aliases support regional spellings, plurals, abbreviations and common name variants. Other one-edit misses still receive private So close! feedback. Full word banks and aliases stay off client catalog packets and release packages.
- Chat keeps the named next-artist announcement and removes redundant drawing-start and turn-finished messages.

Protocol 4 uses additive fields and remains compatible with older 0.6 clients. Deploy the 0.7 server for custom words, grouped catalogs, aliases and final standings. Server upgrades preserve accounts and certificates and end active rooms. The maintained private bank is backed up before its curated replacement. Start-My-Server.cmd and permanent local state are preserved.

