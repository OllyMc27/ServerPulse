# Installation

## Requirements

- A current IW4MAdmin installation using the .NET 10 plugin lifecycle
- Permission to copy files into the IW4MAdmin directory
- Moderator or higher permission to open ServerPulse by default

## Fresh installation

1. Stop IW4MAdmin.
2. Download `ServerPulse.dll` from the [latest GitHub release](https://github.com/OllyMc27/ServerPulse/releases/latest).
3. Copy the DLL into `IW4MAdmin/Plugins`.
4. Start IW4MAdmin once.
5. Confirm the console contains a ServerPulse load line.
6. Open `Configuration/ServerPulse.json` and review the generated settings.
7. Restart IW4MAdmin after making changes.
8. Open **Admin → ServerPulse** in the webfront.

The analytics state is stored separately at `Configuration/ServerPulseData.json` by default.

## Upgrading

1. Stop IW4MAdmin.
2. Back up `Configuration/ServerPulse.json` and `Configuration/ServerPulseData.json`.
3. Replace the existing DLL with the new release.
4. Start IW4MAdmin and check the startup output and **Data health** page.

ServerPulse upgrades its state schema in place. Do not replace your state file with the example configuration.

## Migrating from ChatCheatMonitor

ServerPulse v1.0.1 includes ChatCheatMonitor's response engine as the optional **Player Guidance** module.

1. Back up `Configuration/ChatCheatMonitor.json`.
2. Copy custom categories, phrases, reminders and server overrides into the `PlayerGuidance` section of `ServerPulse.json`.
3. Stop IW4MAdmin.
4. Remove `ChatCheatMonitor.dll` from `Plugins` or move it completely outside the IW4MAdmin directory.
5. Set `PlayerGuidance.Enabled` to `true`.
6. Start IW4MAdmin and run `!ccmstatus` and `!ccmtest <message>`.

Never load both responders together: the same chat message could produce duplicate reminders.

## Uninstalling

Stop IW4MAdmin and remove `ServerPulse.dll`. The configuration and analytics JSON files may be retained for a later reinstall or removed manually if the history is no longer required.
