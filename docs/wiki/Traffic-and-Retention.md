# Traffic and Retention

Traffic and Retention compares human demand and session quality across monitored servers.

![ServerPulse Traffic and Retention](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/traffic-and-retention.png)

## Headline metrics

- **Monitored servers:** enabled servers represented in the selected period.
- **Sessions:** completed human sessions retained by ServerPulse.
- **Unique players:** pseudonymous players seen during the period.
- **Short-session rate:** sessions ending within `BounceThresholdSeconds`.

## Server comparison

Each row combines current human population with historical sessions, unique players, average completed-session length, returning-player rate and short-session rate. The assessment summarises sample quality and relative performance.

Use the view to compare similar servers and rotations. A high online count can coexist with weak retention, while a smaller server may have a loyal returning audience.

## Useful checks

- Prioritise rows with enough completed sessions for a stable comparison.
- Compare returning and short-session rates together.
- Test changes on one or two servers before applying them network-wide.
- Revisit the same period after the experiment; do not compare a busy weekend directly with a quiet weekday.

Bots remain visible elsewhere in IW4MAdmin but are excluded from these human analytics when `ExcludeBots` is enabled.
