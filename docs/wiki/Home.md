# ServerPulse Wiki

ServerPulse is a privacy-conscious analytics, community-insight and player-guidance plugin for IW4MAdmin. It turns normal server events into practical answers about traffic, retention, rotations, player geography, community feedback and report behaviour.

![ServerPulse overview](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/serverpulse-overview.png)

> Screenshots use representative sample data to demonstrate a populated network. ServerPulse does not alter server settings automatically.

## Start here

- [Installation](Installation)
- [Configuration](Configuration)
- [Dashboard guide](Dashboard-Guide)
- [Traffic and retention](Traffic-and-Retention)
- [Rotation performance](Rotation-Performance)
- [Busy times and exits](Busy-Times-and-Exits)
- [Player audience](Player-Audience)
- [Community Voice](Community-Voice)
- [Player Guidance](Player-Guidance)
- [Action plan](Action-Plan)
- [Data health](Data-Health)
- [Commands](Commands)
- [Privacy and data](Privacy-and-Data)
- [Troubleshooting](Troubleshooting)

## What ServerPulse collects

ServerPulse listens to IW4MAdmin's existing connection, match, chat and monitoring events. It does not require game-side scripts, database migrations or an external analytics service.

The plugin records human sessions, aggregate population samples, map rounds, categorised chat signals, optional guidance events and server incidents in `Configuration/ServerPulseData.json`.

## Safe defaults

- Raw chat storage is disabled.
- Only matching chat messages can be retained as short redacted excerpts.
- IP addresses are never written to ServerPulse analytics.
- General player identifiers are installation-specific pseudonyms.
- Country rows below the configured sample threshold are hidden.
- Player Guidance is disabled until explicitly enabled.
- Staff escalation only counts distinct accusers and requires a resolved target.

ServerPulse produces operational signals—not proof of cheating and not automatic punishment decisions.

## Related projects

- [IW4MAdmin](https://github.com/RaidMax/IW4M-Admin) provides the server administration and event platform ServerPulse extends.
- [DemosToDiscord](https://github.com/OllyMc27/DemosToDiscord) adds match-demo delivery and a native cheating-case review workflow.
