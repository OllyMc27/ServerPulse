# Privacy and Data

ServerPulse is designed to provide operational value without creating an unnecessary archive of player data.

## Stored by default

- Pseudonymous player/session keys
- Server, game, friendly map and friendly mode context
- Session start/end, duration and general disconnect outcome
- Aggregate population samples
- Categorised chat signals with short redacted excerpts
- Country code/name from IW4MAdmin geolocation, subject to display thresholds
- Monitoring incidents

## Not written to ServerPulse analytics

- Player IP addresses
- Full raw chat, unless explicitly enabled
- Passwords, RCON credentials or webhook credentials

## Player identifiers

General analytics use an HMAC pseudonym derived from the IW4MAdmin identity and the installation's `AnonymizationSalt`. This supports repeat/retention analysis without writing the source identifier.

Player Guidance is an administrative workflow. When enabled, it retains the target's cleaned display name, IW4MAdmin client/network IDs and review status. `RetainAdminReviewContext` also keeps a bounded admin-only snapshot of nearby chat and the players present when an accusation was captured. This is deliberately separate from general Community Voice storage and follows raw retention.

## Redaction

Redacted excerpts remove:

- game colour codes;
- IPv4-style addresses and ports;
- HTTP/WWW links;
- Discord invite links;
- email addresses;
- long numeric platform identifiers.

Redaction is a risk-reduction measure, not a guarantee that free-form chat can never contain personal information. Choose retention values appropriate to your community and jurisdiction.

## Country privacy

Country rows are hidden until `MinimumCountrySampleSize` unique players are present. Flags are calculated locally from the two-letter country code. The displayed popular time uses the configured dashboard timezone.

## Backups and deletion

Back up `Configuration/ServerPulseData.json` if analytics history matters. Deleting this file while IW4MAdmin is stopped resets ServerPulse history without modifying IW4MAdmin's own database. Treat backups according to the same retention and access policy as the live file.
