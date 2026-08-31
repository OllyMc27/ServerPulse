# Troubleshooting

## ServerPulse does not appear in the webfront

- Confirm `ServerPulse.dll` is directly inside `IW4MAdmin/Plugins`.
- Check the startup console for `ServerPulse ... loaded`.
- Confirm `EnableWebfrontDashboard` is `true`.
- Confirm the signed-in account meets `WebfrontMinimumPermission`.
- Restart IW4MAdmin after replacing the DLL.

## Community Voice shows counts but no messages

Set:

```json
"StoreRawChat": false,
"StoreRedactedChatExcerpts": true
```

Restart IW4MAdmin. Only new matching messages gain excerpts; older count-only signals cannot be reconstructed.

## Player Guidance sends no reminder

- Confirm `PlayerGuidance.Enabled` is `true`.
- Confirm `ResponseMode` is not `Disabled`.
- Run `!ccmstatus` and `!ccmtest <message>`.
- Check the configured phrases and exclusions.
- Remember that a recent reminder may be inside the player cooldown.
- If `IgnoreTeamMessages` is enabled, test in global chat.

## The target is unresolved

Use the exact visible player name or numeric IW4MAdmin client ID. Short or ambiguous names are deliberately not guessed. A bot is ignored when `ExcludeBots` is enabled.

## Staff alert does not fire

Alerts require `NotifyStaff: true`, a resolved target and the configured number of distinct accusers inside the time window. Repeating the same message from one account will not trigger escalation.

## Country rows are missing

- Confirm `EnableCountryAnalytics` is true globally and for the server.
- Small samples remain hidden until `MinimumCountrySampleSize` unique players is reached.
- Existing sessions collected before country analytics was enabled cannot be enriched retroactively.

## Maps or modes show technical names

ServerPulse uses the friendly alias supplied by IW4MAdmin when available. Unknown/custom rotations fall back to the source name. Confirm IW4MAdmin's map and game-mode mappings include the custom value.

## Data health shows a storage error

- Confirm the IW4MAdmin process can write to the configured `StateFilePath`.
- Check disk space and file permissions.
- Check the IW4MAdmin log for the full ServerPulse exception.
- Stop IW4MAdmin before manually restoring a state-file backup.

## Build warning for SQLitePCLRaw

The current IW4MAdmin SharedLibraryCore package transitively restores `SQLitePCLRaw.lib.e_sqlite3` 2.1.10, which NuGet flags with a security advisory. ServerPulse does not use SQLite directly; update the IW4MAdmin dependency when a compatible package release provides the fixed transitive version.
