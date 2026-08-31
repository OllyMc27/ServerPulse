# Player Guidance

Player Guidance is the optional ChatCheatMonitor-compatible module included in ServerPulse v1.0.1. It recognises likely accusations, helps the sender use IW4MAdmin's official report command and measures whether that reminder led to a report.

It does **not** determine that a player is cheating and never issues punishment automatically.

![ServerPulse Player Guidance](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/player-guidance.png)

## What the page shows

- Accusation signals detected in chat
- Official reports observed after guidance
- Report follow-through within the configured window
- Repeatedly mentioned targets grouped by unique accusers
- Category context such as aimbot, wallhack, tracking or movement
- Privacy-safe guidance, report and staff-escalation events

Distinct accusers matter more than raw message volume. Administrators should still review formal reports and evidence before taking action.

## Safe starter configuration

```json
"PlayerGuidance": {
  "Enabled": true,
  "ResponseMode": "Private",
  "ReportCommand": "!rep",
  "PlayerCooldownSeconds": 45,
  "ServerCooldownSeconds": 20,
  "TrackReportCommands": true,
  "IgnoreTeamMessages": false,
  "EnableLeetNormalization": true,
  "EnableTargetAssistance": true,
  "MinimumTargetNameLength": 3,
  "NotifyStaff": false
}
```

Start with private reminders. Public or `Both` modes can add unnecessary chat noise on busy servers.

## Detection

Rules support whole-word phrases, substring phrases and custom regular expressions. Detection removes game colour codes, normalises Unicode and punctuation, recognises common leetspeak and collapses separated-letter evasion such as `w a l l h a c k`.

Exclusion phrases prevent known benign messages from triggering. Invalid regular expressions are reported in Data health and skipped without disabling valid rules.

## Target assistance

ServerPulse tries to resolve an online target from the accusation or the first argument of `!rep`. Names and numeric IW4MAdmin client IDs are supported. Ambiguous name matches remain unresolved rather than guessing. Bots are not eligible when `ExcludeBots` is enabled.

## Response modes

- `Disabled`: no detection or response for that scope.
- `Private`: tells only the accusing player how to report.
- `Public`: broadcasts the reminder, subject to the server cooldown.
- `Both`: sends private and public reminders.

Player and server cooldowns suppress repeated responses while retaining the underlying signal and outcome.

## Report follow-through

ServerPulse records an official report-command event and counts follow-through when the same pseudonymous reporter uses the command within 15 minutes of an accusation. When both events resolve targets, the targets must match.

This is a funnel measurement; it does not replace IW4MAdmin's actual report or evidence systems.

## Staff escalation

When `NotifyStaff` is enabled, an alert requires:

- a uniquely resolved online target;
- the configured number of **distinct accusers**;
- the same server, category and target;
- all signals inside `StaffAlertWindowSeconds`.

Repeated messages from one person cannot satisfy the threshold. The default threshold is three unique accusers within 120 seconds.

## Retained guidance data

Guidance events retain the category, matched rule, response outcome, server/rotation context, pseudonymous reporter, resolved target identifier/display name and optional redacted excerpt. They follow `RawDataRetentionDays`.
