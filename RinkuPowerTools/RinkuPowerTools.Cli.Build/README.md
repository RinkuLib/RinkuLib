# Rinku Power Tools build integration

Reference this package from a project that should refresh Rinku Power Tools generated code before compilation:

```xml
<PackageReference Include="RinkuPowerTools.Cli.Build" Version="0.1.0-alpha" PrivateAssets="all" />
```

The target invokes the packaged CLI as:

```bash
rinku refresh --project <current-project.csproj>
```

It refreshes all valid configurations, succeeds as a no-op when none exist, and skips design-time builds. Disable it for one build with `-p:RinkuPowerToolsRefreshOnBuild=false`.
