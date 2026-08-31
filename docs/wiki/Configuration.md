# Configuration

ServerPulse generates `Configuration/ServerPulse.json` on first start. A complete copy-ready example is maintained in the repository: [examples/ServerPulse.json](https://github.com/OllyMc27/ServerPulse/blob/main/examples/ServerPulse.json).

Restart IW4MAdmin after editing the file unless a command specifically reloads the relevant subsystem.

## Core settings

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Enabled` | `true` | Enables analytics collection |
| `EnableWebfrontDashboard` | `true` | Registers Admin → ServerPulse |
| `WebfrontMinimumPermission` | `Moderator` | Minimum dashboard permission |
| `TimeZone` | host default | Dashboard times and activity buckets |
| `StateFilePath` | `Configuration/ServerPulseData.json` | Persistent analytics state |
| `PopulationSnapshotSeconds` | `60` | Live population sampling interval; clamped to 15–900 |
| `ExcludeBots` | `true` | Excludes bots from human analytics and guidance targets |
| `BounceThresholdSeconds` | `120` | Maximum duration counted as a short session |
| `Debug` | `false` | Enables additional diagnostic logging |

## Retention and limits

| Setting | Default |
| --- | ---: |
| `RawDataRetentionDays` | `30` |
| `AggregateRetentionDays` | `730` |
| `MaxSessions` | `100000` |
| `MaxMapRounds` | `25000` |
| `MaxPopulationSamples` | `250000` |
| `MaxChatSignals` | `25000` |

Raw chat signals and Player Guidance events follow `RawDataRetentionDays`. Session, map and population aggregates use the longer aggregate retention period.

## Chat privacy

Recommended settings:

```json
{
  "StoreRawChat": false,
  "StoreRedactedChatExcerpts": true,
  "ChatExcerptMaximumLength": 160
}
```

With redacted excerpts enabled, matching messages have addresses, URLs, invites, email addresses, long platform identifiers and colour codes removed before storage. Setting both storage options to `false` keeps category counts but prevents Community Voice from showing what was said.

## Country analytics

```json
{
  "EnableCountryAnalytics": true,
  "MinimumCountrySampleSize": 3
}
```

Country data comes from IW4MAdmin's local geolocation service. IP addresses are not copied into the ServerPulse state. Countries below the minimum unique-player sample are omitted from the table.

## Chat categories

`ChatCategories` maps a display category to a list of phrases. Matching is case-insensitive and supports multi-category messages. Review the complete example before replacing the defaults; v1.0.1 includes 33 categories.

## Per-server analytics overrides

Use the IW4MAdmin server ID or endpoint as the key:

```json
"ServerOverrides": {
  "127.0.0.1:28960": {
    "Enabled": true,
    "TimeZone": "Europe/London",
    "ExcludeBots": true,
    "EnableChatAnalysis": true,
    "EnableCountryAnalytics": true
  }
}
```

Player Guidance has its own nested `ServerOverrides` so response behaviour can differ from analytics collection.

## Anonymisation salt

If `AnonymizationSalt` is empty, ServerPulse generates a random value and saves it. Keep it private and stable: changing it changes the pseudonymous player keys and prevents old and new sessions from being linked for retention analysis.
