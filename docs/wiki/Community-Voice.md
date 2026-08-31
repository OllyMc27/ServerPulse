# Community Voice

Community Voice answers a question that category totals cannot: **what were players actually saying?**

## How it works

1. A human player sends a chat message.
2. ServerPulse compares it with the configured `ChatCategories` phrases.
3. A message may match more than one category but is counted once in message totals.
4. If redacted excerpts are enabled, a short sanitised copy is retained with server and rotation context.

Default categories cover cheating, lag, connection, stability, maps, modes, voting, bots, balance, spawns, weapons, progression, rules, toxicity, admins, population, requests, team-killing, camping, exploits, AFK players, voice/chat spam, joining, downloads, match flow, spawning, commands, ban appeals, Zombies, settings, events and positive feedback.

## Reading the page

- **Matched messages** counts distinct messages, not the number of category matches.
- **Complaint/request signals** excludes positive-only feedback.
- **Positive feedback** shows messages matching the Positive category.
- **Signals per 100 player-hours** normalises complaint volume against completed human playtime.

Use the topic and server filters to find the exact redacted excerpts behind a change in counts.

## Negation handling

Common positive phrases such as `no lag`, `good ping`, `not cheating` and `not a cheater` are protected from the corresponding negative category. Custom phrase lists should still be reviewed for ambiguous matches.

## Privacy choices

- `StoreRawChat: false` is recommended.
- `StoreRedactedChatExcerpts: true` makes the page actionable without retaining full raw chat.
- Disabling both retains category counts only.
- Existing count-only records cannot be reconstructed after excerpt storage is enabled.
