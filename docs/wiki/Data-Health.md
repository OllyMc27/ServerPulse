# Data Health

Data Health confirms that the analytics pipeline is collecting useful information and exposes problems before they distort decisions.

![ServerPulse Data Health](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/data-health.png)

## Health cards

- **Configuration** reports whether settings passed validation.
- **Storage** reports whether the state file is readable and writable.
- **Sessions retained** shows the current session history size.
- **Population samples** shows the retained live-population measurements.

## Live server telemetry

The table shows each monitored server's game, friendly map and mode, human and bot population, RCON latency, event-pipeline state and latest capture time. Human analytics can exclude bots even though the telemetry table still displays them for operational context.

`Live` indicates normal event collection. `Polling` indicates that the host is using its supported fallback. `Recovering` means a recent interruption has not fully cleared.

## Open incidents

Open incidents describe monitoring or connectivity interruptions that have not recovered. Check the affected server, start time and explanatory text before trusting a gap in traffic or retention data.

A dash for a metric means the installed IW4MAdmin host does not expose it; it is not automatically an error. Configuration warnings and invalid Player Guidance expressions are also surfaced here without disabling otherwise valid rules.
