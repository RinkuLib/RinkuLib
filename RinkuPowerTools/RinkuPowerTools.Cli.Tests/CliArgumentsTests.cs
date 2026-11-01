namespace RinkuPowerTools.Cli.Tests;

public class CliArgumentsTests {
    [Fact]
    public void ParsesPositionalsAndOptions() {
        CliArguments args = CliArguments.Parse(["query", "add", "GetAlbums", "--config", "dev", "--sql=SELECT 1", "--internal"]);

        Assert.Equal(["query", "add", "GetAlbums"], args.Positionals);
        Assert.Equal("dev", args.Get("config"));
        Assert.Equal("SELECT 1", args.Get("sql"));
        Assert.True(args.Has("internal"));
        Assert.True(args.GetBoolean("internal", false));
    }

    [Fact]
    public void BooleanOptionAcceptsExplicitFalse() {
        CliArguments args = CliArguments.Parse(["config", "set", "--internal", "false"]);
        Assert.False(args.GetBoolean("internal", true));
    }

    [Fact]
    public void RequireFailsForFlagWithoutValue() {
        CliArguments args = CliArguments.Parse(["query", "add", "X", "--sql"]);
        CliException ex = Assert.Throws<CliException>(() => args.Require("sql"));
        Assert.Equal(2, ex.ExitCode);
    }
}
