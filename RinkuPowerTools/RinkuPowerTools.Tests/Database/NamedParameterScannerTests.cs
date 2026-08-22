namespace RinkuPowerTools.Tests.Database;

public class NamedParameterScannerTests
{
    [Fact]
    public void Scan_FindsNamedAndPositionalParameters()
    {
        List<string> parameters = NamedParameterScanner.Scan(
            "SELECT @artistId, :title, $limit, $1, @artistId");

        Assert.Equal(["@artistId", ":title", "$limit", "$1"], parameters);
    }

    [Fact]
    public void Scan_IgnoresQuotedTextCommentsAndPostgreSqlCasts()
    {
        const string sql = """
SELECT
    '$fake',
    "@identifier",
    @real::integer,
    $tag$:ignored $alsoIgnored$tag$,
    $$ $ignored $$,
    :second
-- @lineComment
/* $blockComment */
""";

        List<string> parameters = NamedParameterScanner.Scan(sql);

        Assert.Equal(["@real", ":second"], parameters);
    }
    [Fact]
    public void Scan_IgnoresPostgreSqlEscapeStringsAndNestedBlockComments()
    {
        const string sql = "SELECT E'escaped\\\' @ignored', @real /* outer /* @nested */ :ignored */";

        List<string> parameters = NamedParameterScanner.Scan(sql);

        Assert.Equal(["@real"], parameters);
    }

}
