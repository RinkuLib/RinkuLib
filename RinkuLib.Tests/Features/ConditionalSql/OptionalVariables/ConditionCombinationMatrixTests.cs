using Rinku.Querying;
using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Exhaustive matrix over the marker key expressions (single, <c>!</c>, <c>&amp;</c>, <c>|</c>, and their
/// mixes) crossed with every on/off state of the keys involved, at each footprint placement. The expected
/// SQL comes from the marker rule: operators are applied from left to right without precedence and <c>!</c>
/// flips its key. The generator uses a reference evaluator, never the current output. Each row renders through
/// both paths, the values array and the usage map.
/// </summary>
public class ConditionCombinationMatrixTests {
    /// <summary>The marker rule: evaluate each operator from left to right.</summary>
    static bool Eval(string expr, HashSet<string> on) {
        bool KeyOn(string t) => t.StartsWith('!') ? !on.Contains(t[1..]) : on.Contains(t);
        var keys = expr.Split(['|', '&'], StringSplitOptions.TrimEntries);
        bool result = KeyOn(keys[0]);
        int key = 1;
        foreach (char op in expr.Where(c => c is '|' or '&')) {
            bool next = KeyOn(keys[key++]);
            result = op == '|' ? result || next : result && next;
        }
        return result;
    }

    static IEnumerable<string> Keys(string expr)
        => expr.Split(['|', '&'], StringSplitOptions.TrimEntries).Select(t => t.TrimStart('!')).Distinct();

    static readonly string[] AllExprs = [
        "Ka", "!Ka",
        "Ka|Kb", "!Ka|Kb", "Ka|!Kb", "!Ka|!Kb",
        "Ka&Kb", "!Ka&Kb",
        "Ka|Kb|Kc", "Ka|Kb|Kc|Kd",
        "Ka&Kb|Kc", "Ka|Kb&Kc", "!Ka&Kb|Kc", "Ka|!Kb&Kc",
        "Ka&Kb|Kc&Kd",
    ];
    static readonly string[] ShortExprs = ["Ka|Kb", "!Ka|Kb", "Ka|Kb|Kc", "Ka|Kb&Kc"];

    static readonly (string Template, string Kept, string Pruned)[] Placements = [
        ("SELECT * FROM t WHERE /*{K}*/x = 1",
         "SELECT * FROM t WHERE x = 1",
         "SELECT * FROM t"),
        ("SELECT a FROM t /*{K}*/INNER JOIN u ON u.i = t.i WHERE y = 2",
         "SELECT a FROM t INNER JOIN u ON u.i = t.i WHERE y = 2",
         "SELECT a FROM t WHERE y = 2"),
        ("SELECT a, /*{K}*/b FROM t",
         "SELECT a, b FROM t",
         "SELECT a FROM t"),
        ("SELECT * FROM t WHERE a = 1 AND /*{K}*/b = 2 AND c = 3",
         "SELECT * FROM t WHERE a = 1 AND b = 2 AND c = 3",
         "SELECT * FROM t WHERE a = 1 AND c = 3"),
        ("SELECT * FROM t WHERE (a = 1 OR /*{K}*/b = 2)",
         "SELECT * FROM t WHERE (a = 1 OR b = 2)",
         "SELECT * FROM t WHERE (a = 1)"),
        ("SELECT * FROM t WHERE /*{K}*/id IN (SELECT id FROM u WHERE z = 3)",
         "SELECT * FROM t WHERE id IN (SELECT id FROM u WHERE z = 3)",
         "SELECT * FROM t"),
        ("SELECT a, /*{K}*/b&, c FROM t",
         "SELECT a, b, c FROM t",
         "SELECT a FROM t"),
        ("SELECT Country, COUNT(*) FROM c /*{K}*/GROUP BY Country /*{K}*/HAVING COUNT(*) > 1",
         "SELECT Country, COUNT(*) FROM c GROUP BY Country HAVING COUNT(*) > 1",
         "SELECT Country, COUNT(*) FROM c"),
    ];

    public static TheoryData<string, string, string> Rows() {
        var rows = new TheoryData<string, string, string>();
        for (int p = 0; p < Placements.Length; p++) {
            var (template, kept, pruned) = Placements[p];
            foreach (var expr in p == 0 ? AllExprs : ShortExprs) {
                var keys = Keys(expr).ToArray();
                for (int bits = 0; bits < 1 << keys.Length; bits++) {
                    var on = new HashSet<string>(keys.Where((_, k) => (bits & (1 << k)) != 0));
                    rows.Add(template.Replace("{K}", expr), string.Join(",", on), Eval(expr, on) ? kept : pruned);
                }
            }
        }
        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Every_key_state_renders_the_formula_result(string template, string onCsv, string expected) {
        var query = new QueryCommand(template);
        var on = onCsv.Length == 0 ? [] : onCsv.Split(',');
        int len = query.QueryText.RequiredVariablesLength;

        var variables = new object?[len];
        foreach (var key in on)
            variables[query.Mapper.GetIndex(key)] = key;
        Assert.Equal(expected, query.QueryText.Parse(variables));

        Span<bool> usageMap = stackalloc bool[len];
        foreach (var key in on)
            usageMap[query.Mapper.GetIndex(key)] = true;
        Assert.Equal(expected, query.QueryText.Parse(usageMap, default));
    }
}
