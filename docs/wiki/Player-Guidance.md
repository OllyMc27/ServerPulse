# Player Guidance

Player Guidance is the optional ChatCheatMonitor-compatible module included in ServerPulse. It recognises likely accusations, helps the sender use IW4MAdmin's official report command and measures whether that reminder led to a report.

It does **not** determine that a player is cheating and never issues punishment automatically.

![ServerPulse Player Guidance](https://raw.githubusercontent.com/wiki/OllyMc27/ServerPulse/images/player-guidance.png)

## What the page shows

- Accusation signals detected in chat
- Official reports observed after guidance
- Report follow-through within the configured window
- Repeatedly mentioned targets grouped by unique accusers
- Category context such as aimbot, wallhack, tracking or movement
- Privacy-safe guidance, report and staff-escalation events
- Admin review of unresolved signals with bounded surrounding chat and the players present at capture time
- Optional one-click escalation of a manually resolved signal into DemosToDiscord

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
  "RetainAdminReviewContext": true,
  "ReviewContextBeforeSeconds": 60,
  "ReviewContextAfterSeconds": 30,
  "ReviewContextMaximumMessages": 20,
  "EnableDemosToDiscordEscalation": true,
  "NotifyStaff": false
}
```

Start with private reminders. Public or `Both` modes can add unnecessary chat noise on busy servers.

## Detection

Rules support whole-word phrases, substring phrases and custom regular expressions. Detection removes game colour codes, normalises Unicode and punctuation, recognises common leetspeak and collapses separated-letter evasion such as `w a l l h a c k`.

Exclusion phrases prevent known benign messages from triggering. Invalid regular expressions are reported in Data health and skipped without disabling valid rules.

## Target assistance

ServerPulse first looks for a complete online name, then accepts a partial fragment such as `slc` only when it identifies exactly one eligible player. Names and numeric IW4MAdmin client IDs are supported by `!rep`. Ambiguous partials remain unresolved rather than guessing. Bots and the accusing player are not eligible targets.

## Resolving an unresolved signal

Open the signal under **Admin → ServerPulse → Player guidance**. The review panel shows the configured chat window and the non-bot players who were in the match when the signal was captured. An administrator can:

- resolve the target without creating a case;
- resolve and create a DemosToDiscord proactive-review case;
- retry a failed DemosToDiscord handoff; or
- dismiss the signal with an optional note.

The target, reviewer, status, notes and resulting DemosToDiscord case ID are retained with the event. DemosToDiscord must be installed and its `AcceptServerPulseCases` setting enabled for case creation.

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

Guidance events retain the category, matched rule, response outcome, server/rotation context, pseudonymous reporter, resolved target identifier/display name and optional redacted excerpt. When `RetainAdminReviewContext` is enabled, an admin-only bounded snapshot also retains nearby chat, cleaned speaker names and the players present at capture time. These records follow `RawDataRetentionDays`.
