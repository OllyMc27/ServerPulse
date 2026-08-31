# ServerPulse Wiki

ServerPulse is a privacy-conscious analytics, community-insight and player-guidance plugin for IW4MAdmin. It turns normal server events into practical answers about traffic, retention, rotations, player geography, community feedback and report behaviour.

## Start here

- [Installation](Installation)
- [Configuration](Configuration)
- [Dashboard guide](Dashboard-Guide)
- [Community Voice](Community-Voice)
- [Player Guidance](Player-Guidance)
- [Commands](Commands)
- [Privacy and data](Privacy-and-Data)
- [Troubleshooting](Troubleshooting)

## What ServerPulse collects

ServerPulse listens to IW4MAdmin's existing connection, match, chat and monitoring events. It does not require game-side scripts, database migrations or an external service.

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
