# ServerPulse [![Release](https://img.shields.io/github/v/release/OllyMc27/ServerPulse?display_name=tag&style=flat-square)](https://github.com/OllyMc27/ServerPulse/releases/latest) [![Build](https://img.shields.io/github/actions/workflow/status/OllyMc27/ServerPulse/ci.yml?branch=main&style=flat-square&label=build)](https://github.com/OllyMc27/ServerPulse/actions/workflows/ci.yml) [![License](https://img.shields.io/github/license/OllyMc27/ServerPulse?style=flat-square)](LICENSE) ![Author](https://img.shields.io/badge/author-OllyMc27-2563eb?style=flat-square)

**Privacy-conscious server analytics, community insight and player-guidance tooling for IW4MAdmin.**

ServerPulse turns normal IW4MAdmin events into a native moderation and growth workspace. It shows where players join, what keeps them playing, which rotations lose them, what they are saying, and whether chat accusations become useful reports.

## Highlights

- Human-only traffic, retention, returning-player and short-session comparisons
- Map/mode performance, network activity heatmaps and disconnect outcomes
- Country analytics with flags and configurable privacy thresholds
- Redacted Community Voice excerpts across 33 configurable topics
- Evidence-led recommendations with sample size and confidence
- Optional Player Guidance with `!rep` reminders, target assistance and cooldowns
- Distinct-accuser escalation, report follow-through and repeated-target review
- Native IW4MAdmin styling, permissions, player-profile links and server telemetry

Raw chat is **not stored by default**. IP addresses are never written to ServerPulse data, general player identities are pseudonymised, and small country samples are hidden.

## Install

1. Download `ServerPulse.dll` from the [latest release](https://github.com/OllyMc27/ServerPulse/releases/latest).
2. Copy it to `IW4MAdmin/Plugins`.
3. Start IW4MAdmin to generate `Configuration/ServerPulse.json`.
4. Review the configuration, restart IW4MAdmin, then open **Admin → ServerPulse**.

If enabling Player Guidance, remove `ChatCheatMonitor.dll` first to prevent duplicate reminders.

## Workspace

| View | Answers |
| --- | --- |
| Traffic & retention | Which servers attract players and bring them back? |
| Rotation performance | Which map/mode combinations gain or lose population? |
| Busy times & exits | When is the network busiest and why do sessions end? |
| Player audience | Which privacy-safe regions and join times lead demand? |
| Community Voice | What are players actually complaining about or praising? |
| Player Guidance | Do accusations turn into proper reports, and who is repeatedly mentioned? |
| Action plan | Which changes deserve a measured experiment? |
| Data health | Is collection, storage and live telemetry operating correctly? |

## Documentation

The [ServerPulse wiki](https://github.com/OllyMc27/ServerPulse/wiki) contains the complete setup, configuration, dashboard, privacy, Player Guidance and troubleshooting guides. A full copy-ready configuration is also available in [`examples/ServerPulse.json`](examples/ServerPulse.json).

## Compatibility

ServerPulse v1.0.1 targets .NET 10 and the current IW4MAdmin plugin lifecycle. Player Guidance is disabled by default, so upgrading never begins messaging players until an owner opts in.

## License

[MIT](LICENSE)
