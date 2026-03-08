using System.Data;

namespace RinkuLib.DbRegister;

/// <summary></summary>
public static class ActionNameCache {
    /// <summary>Correspond to the parameter name that will be used to bind to</summary>
    public const string BindParamName = "@ID";
    /// <summary>A simple array that holds @ID00 through @ID99</summary>
    public static readonly string[] BindParamNames = [.. Enumerable.Range(0, 100).Select(i => $"{BindParamName}{i:00}")];
    /// <summary>Build the string placing the " IN (@ID00 ... @IDCount)"</summary>
    public static string BuildInClause(string template, int tIdx, int count) {
        // Math: template.Length - 3 (for {0}) + 5 (for " IN (") + (count * 6 (for "@IDxx,")) - 1 (for ",") + 1 (for ")")
        int totalLength = template.Length + 2 + (count * 6);

        return string.Create(totalLength, (template, tIdx, count), (span, state) => {
            var (tpl, idx, c) = state;
            tpl.AsSpan(0, idx).CopyTo(span);
            int pos = idx;
            " IN (".AsSpan().CopyTo(span[pos..]);
            pos += 5;
            for (int i = 0; i < c; i++) {
                BindParamNames[i].AsSpan().CopyTo(span[pos..]);
                pos += 5;
                span[pos++] = ',';
            }
            span[pos - 1] = ')';
            tpl.AsSpan(idx + 3).CopyTo(span[pos..]);
        });
    }
}
