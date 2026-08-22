namespace RinkuPowerTools.Tests.Misc;

public class ConfigFileNameTests
{
    [Theory]
    [InlineData("rinkupt.json")]
    [InlineData("rinkupt.Reporting.json")]
    [InlineData("RINKUPT.Admin.JSON")]
    [InlineData(@"C:\Project\rinkupt.json")]
    public void ConfigFileName_AcceptsSupportedNames(string path)
    {
        Assert.Matches(SharedRegex.ConfigFileName(), path);
    }

    [Theory]
    [InlineData("rinkupt.Reporting.Other.json")]
    [InlineData("rinkupt.txt")]
    [InlineData("other.json")]
    public void ConfigFileName_RejectsOtherNames(string path)
    {
        Assert.DoesNotMatch(SharedRegex.ConfigFileName(), path);
    }
}
