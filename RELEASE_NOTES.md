Kupo Skribbl 0.6.2 refines drawing and removes UI clutter.

- Frameless dragging uses a current-pointer anchor applied before rendering; paper and child content move together. Snapshot fades are disabled for the transparent book. Native geometry checks pass; FFXIV/DX ghosting still requires owner confirmation.
- Room sliders use equal-width handles with live values underneath and no limit captions. Word-list buttons no longer have radio circles. Icon ink is centered within buttons; tooltips use compact padding.
- Initial profile setup and avatar editing have simpler headers. All pages use a small version label and visible resize-corner marks instead of a motto footer.
- Chat input matches the blue/gray note. Leave/save/error messages are temporary, dismissible toasts inside the plugin rather than a notice that moves the layout.
- The word and hint are centered directly above the square canvas. Drawing tools sit in a translucent bottom-left panel that can collapse and remembers its state. Tool/popup interaction does not paint through to the canvas.
- The backend accepts bounded drawing-delivery bursts while retaining its sustained drawing, shape/fill, cursor and ordinary-command budgets. Profile writes retain their five-second cooldown. A 16-client check sends 120 drawing operations to all 15 viewers.

Protocol 4 remains compatible with 0.6.0/0.6.1. Deploy the 0.6.2 backend for the improved drawing limiter. Server upgrades preserve accounts and word lists and end active rooms. Start-My-Server.cmd and permanent local state are preserved.

