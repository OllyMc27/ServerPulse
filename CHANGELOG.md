# Changelog

## 1.1.0

- Added conservative unique partial-name resolution for Player Guidance; ambiguous fragments remain unresolved.
- Added bounded admin-only surrounding chat and player-at-capture snapshots for unresolved signals.
- Added administrator resolve, dismiss, retry and resolve-and-create-case controls with anti-forgery and permission checks.
- Added optional process-local DemosToDiscord handoff for human-reviewed proactive evidence cases.
- Added executable coverage for partial matching, ambiguity safeguards and retained review state.

## 1.0.1

- Integrated ChatCheatMonitor as an optional, disabled-by-default Player guidance module.
- Added robust phrase, regex, colour-code, Unicode and leetspeak-aware accusation detection with exclusions and per-server overrides.
- Added target assistance, private/public reminder modes, player/server cooldowns and localised reminder templates.
- Improved staff escalation to require distinct accusers within the configured window rather than repeated messages from one player.
- Added privacy-safe tracking of accusation signals and official report commands, including 15-minute report follow-through measurement.
- Added a Player guidance webfront with repeated targets, unique accusers, recent contextual events, reminder outcomes and staff escalations.
- Preserved the `ccmstatus`, `ccmstats`, `ccmtest` and `ccmreload` administration commands for easier migration.
- Added complaint/request rates per 100 completed player-hours.
- Added country flags to Audience, Community voice and new Player guidance events without introducing external assets.

## 0.2.0

- Rebuilt the webfront as a compact overview with focused analytics drill-downs.
- Added paginated rotation views with reliable-sample, gaining-player and losing-player filters.
- Added a Community voice feed containing privacy-redacted matched chat excerpts, server and rotation context, topic/server filters and anonymised player references.
- Expanded the default chat vocabulary to 33 topics, including connection, stability, voting, weapons, progression, rules, toxicity, population, team-killing, exploits, voice chat, joining, downloads, match flow and requests.
- Added message IDs so one message matching several topics is counted and displayed once.
- Added stronger redaction for addresses, links, invites, email addresses and long platform identifiers.
- Cleaned IW4M colour codes from server names while retaining compatibility with existing stored analytics.
- Corrected peak population and activity calculations to aggregate the whole network.
- Reworked recommendations to separate positive feedback from complaints and include clearer evidence-led actions.
- Added sample-quality labels and clearer explanations for ambiguous disconnect and audience-time data.
- Added dependency-free country flags to audience analytics and new Community voice records.

## 0.1.0

- Initial ServerPulse preview.
- Added privacy-safe player session and population analytics.
- Added server, map, mode, activity, audience and chat-signal views.
- Added retention, bounce, disconnect and returning-player measurements.
- Added recommendation cards with confidence and sample size.
- Added native monitoring incidents and optional latency telemetry.
- Added configurable retention, privacy and per-server overrides.
