Kupo Skribbl 0.5.1 polishes the game UI following in-game feedback.

- Timer stays large and right-aligned without a clock icon. Hints have distinct letter slots and word spacing rather than a continuous underline; hover shows the letter count.
- Chat history has no extra cream child background or borders. Individual cards are translucent over the blue note. Done-mode names/messages are gray with a status icon; server cards retain their information icon.
- A circle-icon button opens Line, Rectangle and Ellipse. Redundant Square/Circle choices are removed from the menu; protocol 3 compatibility is preserved.
- The turn recap uses a themed 2-1-3 podium, with first place raised in the center. The current drawer is highlighted in the artists list without an extra text row.
- Canvas vote buttons are translucent and borderless. Every accepted kick/skip vote announces its tally and how many more votes are required. Duplicate/rejected votes do not generate announcements.
- Word selection uses a centered translucent panel, softer cards and centered options.

Protocol 3 remains compatible with 0.5.0 clients. Profiles, credentials and private word banks are preserved; deploying the server restarts active rooms. File diagnostics still exclude words, credentials, chat and drawing/cursor contents.

