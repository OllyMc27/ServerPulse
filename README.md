# ServerPulse

**Privacy-friendly server analytics and growth insights for IW4MAdmin.**

ServerPulse turns normal IW4MAdmin events into a native webfront dashboard that helps server owners answer the questions that actually affect player traffic: when people play, which rotations keep them, where they come from, why they leave, and what they complain about.

## What it shows

- Live and historical human population, with bots counted separately
- Top servers ranked by demand, retention, returning players and short sessions
- Map and mode performance, including joins, leaves, peaks and population survival
- Hour-by-hour and day-by-day activity heatmaps
- Average session length, first-visit retention and disconnect reasons
- Privacy-thresholded country analytics using IW4MAdmin's local geolocation service
- Categorised chat signals for lag, maps, modes, cheating, balance, spawns and more
- Practical opportunity cards with sample size and confidence
- Native server monitoring, connection incidents and latency metrics when supported
- Per-server collection, chat-analysis and country-analysis overrides

Raw chat is **not stored by default**. Player identities are represented by installation-specific HMAC hashes, IP addresses are never written to the analytics file, bots can be excluded, and small country samples are hidden.

## Install

1. Download `ServerPulse.dll` from the latest release.
2. Copy it to `IW4MAdmin/Plugins`.
3. Start IW4MAdmin once to generate `Configuration/ServerPulse.json`.
4. Restart IW4MAdmin after changing the configuration.
5. Open **Admin → ServerPulse** in the webfront.

ServerPulse targets .NET 10 and the current IW4MAdmin plugin lifecycle.

## Dashboard

The initial dashboard contains eight focused views:

| View | Purpose |
| --- | --- |
| Overview | Network KPIs and the highest-priority opportunities |
| Servers | Compare traffic, session quality, returns and bounce rate |
| Maps & modes | Find rotations that gain or lose players |
| Activity | See the strongest hours and quiet gaps |
| Audience | Understand privacy-safe regional demand |
| Chat signals | Track recurring complaints and positive feedback |
| Recommendations | Turn collected evidence into suggested actions |
| Data health | Check live samples, storage, incidents and latency |

## Configuration

A complete example is available at [`examples/ServerPulse.json`](examples/ServerPulse.json). The most important privacy options are:

```json
{
  "StoreRawChat": false,
  "StoreRedactedChatExcerpts": false,
  "EnableCountryAnalytics": true,
  "MinimumCountrySampleSize": 3,
  "ExcludeBots": true
}
```

Analytics are stored in `Configuration/ServerPulseData.json`. Back this file up if you want to preserve history during a migration. Deleting it resets ServerPulse analytics without affecting IW4MAdmin data.

## Current status

Version 0.1.0 is the first public preview. It is ready for test servers, but recommendations become more useful after several days of representative traffic.

## License

[MIT](LICENSE)
