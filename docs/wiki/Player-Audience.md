# Player Audience

Player Audience shows privacy-safe regional demand using IW4MAdmin's local geolocation result.

![ServerPulse Player Audience](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/player-audience.png)

## What the page shows

- Visible countries that meet `MinimumCountrySampleSize`
- Unique players and sessions by country
- Average completed-session length
- The most popular join time in the configured dashboard timezone
- A leading audience and leading network join time

Flags are calculated locally from ISO country codes. ServerPulse does not call an external flag or analytics service.

## Privacy behaviour

IP addresses are not written into `ServerPulseData.json`. Country rows remain hidden until enough unique pseudonymous players are present. Reducing the threshold may make small communities identifiable, so the default minimum of three should be treated as a floor rather than a target.

Popular times use the ServerPulse dashboard timezone—not the player's local timezone. This makes the page suitable for planning announcements and events from the server operator's perspective.
