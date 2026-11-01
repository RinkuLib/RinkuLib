using System.Xml.Linq;

namespace RinkuPowerTools.Cli.Tests;

public class BuildIntegrationTests {
    [Fact]
    public void BuildTargetRefreshesAllAndSkipsDesignTimeBuilds() {
        string targetPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../RinkuPowerTools.Cli.Build/build/RinkuPowerTools.Cli.Build.targets"));
        XDocument document = XDocument.Load(targetPath);
        XElement automatic = Assert.Single(document.Descendants("Target"), target => target.Attribute("Name")?.Value == "RinkuPowerToolsRefreshBeforeCompile");
        XElement target = Assert.Single(document.Descendants("Target"), target => target.Attribute("Name")?.Value == "RinkuPowerToolsRefresh");
        XElement exec = Assert.Single(target.Elements("Exec"));
        string condition = Assert.IsType<string>(target.Attribute("Condition")?.Value);
        string command = Assert.IsType<string>(exec.Attribute("Command")?.Value);

        Assert.Contains("DesignTimeBuild", condition);
        Assert.Contains("RinkuPowerToolsRefreshOnBuild", automatic.Attribute("Condition")!.Value);
        Assert.DoesNotContain("RinkuPowerToolsRefreshOnBuild", condition);
        Assert.Equal("RinkuPowerToolsRefresh", automatic.Attribute("DependsOnTargets")!.Value);
        Assert.Equal("ResolveProjectReferences", target.Attribute("DependsOnTargets")!.Value);
        Assert.Contains("refresh", command);
        Assert.Contains("MSBuildProjectFullPath", command);
        Assert.DoesNotContain("--config", command);
        Assert.DoesNotContain("Exists(", condition);
    }
}
