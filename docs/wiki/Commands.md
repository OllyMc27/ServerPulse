# Commands

ServerPulse retains the familiar ChatCheatMonitor command names to simplify migration.

| Command | Permission | Purpose |
| --- | --- | --- |
| `!ccmstatus` | Moderator | Shows whether guidance is enabled, response mode and retained counts |
| `!ccmstats` | Moderator | Shows accusations, reports, reminders, resolved targets, alerts and top categories |
| `!ccmtest <message>` | Moderator | Tests detection and target resolution without sending a player reminder |
| `!ccmreload` | SeniorAdmin | Rebuilds detection rules from the active configuration |

## Suggested test

1. Run `!ccmstatus` and confirm guidance is enabled.
2. Run `!ccmtest PlayerName is cheating`.
3. With two real players connected, have one send `PlayerName is cheating`.
4. Confirm the sender receives the private reminder.
5. Have the sender use `!rep PlayerName testing`.
6. Open **ServerPulse → Player Guidance** and confirm the accusation, reminder and official-report follow-through appear.

Staff alerts require `NotifyStaff: true`, a resolved target and multiple distinct accusers. One test player repeating a message will not trigger one.
