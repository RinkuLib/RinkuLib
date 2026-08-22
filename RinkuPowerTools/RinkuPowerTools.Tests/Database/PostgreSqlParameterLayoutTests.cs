namespace RinkuPowerTools.Tests.Database;

public class PostgreSqlParameterLayoutTests
{
    [Fact]
    public void PositionalParameters_AreOrderedByPositionRatherThanOccurrence()
    {
        PostgreSqlParameterLayout layout = PostgreSqlParameterLayout.Parse("SELECT $2, $1, $2");

        Assert.True(layout.IsPositional);
        Assert.Equal(["$1", "$2"], layout.Names);
    }

    [Fact]
    public void NamedParameters_AreKeptInFirstOccurrenceOrder()
    {
        PostgreSqlParameterLayout layout = PostgreSqlParameterLayout.Parse("SELECT @second, :first, @second");

        Assert.False(layout.IsPositional);
        Assert.Equal(["@second", ":first"], layout.Names);
    }

    [Fact]
    public void EquivalentNamedPrefixes_DoNotCreateDuplicateParameters()
    {
        PostgreSqlParameterLayout layout = PostgreSqlParameterLayout.Parse("SELECT @value + :value");

        Assert.Equal(["@value"], layout.Names);
    }

    [Fact]
    public void MixedNamedAndPositionalParameters_AreRejected()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PostgreSqlParameterLayout.Parse("SELECT @value, $1"));

        Assert.Contains("cannot mix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SELECT $0")]
    [InlineData("SELECT $2")]
    [InlineData("SELECT $name")]
    public void InvalidPositionalLayouts_AreRejected(string sql)
    {
        Assert.Throws<InvalidOperationException>(() => PostgreSqlParameterLayout.Parse(sql));
    }

    [Fact]
    public void DollarQuotedStrings_AreNotParameters()
    {
        PostgreSqlParameterLayout layout = PostgreSqlParameterLayout.Parse("SELECT $$ $1 @ignored $$, @real");

        Assert.False(layout.IsPositional);
        Assert.Equal(["@real"], layout.Names);
    }
}
