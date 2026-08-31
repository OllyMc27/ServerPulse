# Busy Times and Exits

Busy Times and Exits shows when human demand is strongest and how completed sessions ended.

![ServerPulse Busy Times and Exits](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/busy-times-and-exits.png)

## Activity heatmap

The heatmap averages network population by weekday and hour in the configured ServerPulse timezone. Brighter cells indicate stronger demand. Use it to schedule events, announcements and rotation trials when enough players are present to measure the result.

The headline cards show peak network population, busiest time, average completed-session length and short-session rate.

## Exit outcomes

ServerPulse uses the best disconnect reason IW4MAdmin supplies and groups it into an operational outcome such as:

- quit normally;
- lost connection;
- timed out;
- kicked by an administrator;
- removed for inactivity;
- banned.

Each row includes its session count, share and average session length. A sudden rise in lost connections or timeouts is a reason to compare servers, round transitions, RCON latency and host logs.

Ordinary departures without a more specific IW4MAdmin reason may fall back to a general quit or lost-connection group.
