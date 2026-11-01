# Refresh on build

The packaged build hook calls the same CLI that a developer can run manually.

```xml
<Project>
  <PropertyGroup>
    <RinkuPowerToolsRefreshOnBuild Condition="'$(RinkuPowerToolsRefreshOnBuild)' == ''">true</RinkuPowerToolsRefreshOnBuild>
  </PropertyGroup>

  <Target Name="RinkuPowerToolsRefresh"
          BeforeTargets="CoreCompile"
          Condition="'$(RinkuPowerToolsRefreshOnBuild)' == 'true'
                     AND '$(DesignTimeBuild)' != 'true'">
    <Exec Command="dotnet &quot;$(RinkuPowerToolsCliPath)&quot; refresh --project &quot;$(MSBuildProjectFullPath)&quot;"
          WorkingDirectory="$(MSBuildProjectDirectory)" />
  </Target>
</Project>
```

`rinku refresh` discovers every valid `rinkupt*.json` beside the project and refreshes each one. If none exist, it returns `0` without doing anything. The target therefore does not need to understand configuration names or file layout.

The target excludes Visual Studio design-time builds so IntelliSense does not open database connections or regenerate files. Disable it for one build with:

```bash
dotnet build -p:RinkuPowerToolsRefreshOnBuild=false
```

The Visual Studio extension and CLI can coexist because both use the shared `RinkuPowerTools` implementation and the same configuration/generated files. CLI config saves use atomic replacement; generation remains owned by the shared implementation.
