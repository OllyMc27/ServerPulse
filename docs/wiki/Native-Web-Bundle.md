# Native web bundle development

The `feature/native-web-bundle` branch moves the ServerPulse workspace from a legacy template interaction to the native `/serverpulse` Razor route.

## What changed

- IW4MAdmin's page list now supplies the **ServerPulse** administrator sidebar entry.
- The route reads the logged-in user's permission claims before rendering.
- Overview and detail links remain under `/serverpulse` with their existing query parameters.
- Route and query changes reload the Razor page state during enhanced navigation, so sidebar links and drill-downs do not require a browser refresh.
- The bundle registers the `ph-chart-line-up` sidebar icon when the host exposes icon metadata.
- Bundle-owned responsive layout CSS is shipped in `wwwroot` and scoped by the host.
- Analytics collection, ServerPulse data, guidance, DemosToDiscord integration and configuration are unchanged.

The Razor page deliberately calls the established dashboard renderer during this migration stage. That protects the current analytics behaviour while the component owns native routing, permission checks, loading states and responsive host-themed presentation.

## Build and install

```powershell
dotnet build ServerPulse.sln -c Release
dotnet test ServerPulse.sln -c Release --no-build
```

The bundle is written to `ServerPulse/bin/Release/ServerPulse.zip`.

This ZIP requires an IW4MAdmin host with the experimental plugin-bundle support described in the [official bundle guide](https://github.com/RaidMax/IW4M-Admin/blob/feature/plugin-data-directories/docs/plugin-web-bundles.md). Put the ZIP in `IW4MAdmin/Plugins` and restart. Do not install both the bundle and the standalone DLL at the same time.

The production `main` branch and current releases remain standalone-DLL builds until the bundle host is ready for normal deployment.
