using System.Data;
using System.Runtime.CompilerServices;

namespace RinkuLib.DbRegister;

/// <summary></summary>
public static class ActionNameCache {
    /// <summary>Correspond to the parameter name that will be used to bind to</summary>
    public const string BindParamName = "@ID";
    /// <summary>A simple array that holds @ID00 through @ID99</summary>
    public static readonly string[] BindParamNames = [.. Enumerable.Range(0, 100).Select(i => $"{BindParamName}{i:00}")];
    /// <summary>Build the string placing the 00, 01, 02... after the variables and concateneting the result into a single string</summary>
    public static string GenerateMultiQuery(string query, int indexAfterVar, int count) {
        int totalLength = (query.Length * count) + (count * 2);
        if (count > 100)
            totalLength += count - 100;
        if (count > 1000)
            totalLength += count - 1000;
        if (count > 10000)
            throw new Exception("Too many parameters");

        return string.Create(totalLength, (query, indexAfterVar, count), (dest, state) => {
            var (q, idx, c) = state;
            ReadOnlySpan<char> head = q.AsSpan(0, idx);
            ReadOnlySpan<char> tail = q.AsSpan(idx);
            int pos = 0;
            int i = 0;
            var nb = c <= 100 ? c : 100;
            for (; i < nb; i++) {
                head.CopyTo(dest[pos..]);
                pos += head.Length;

                dest[pos++] = (char)('0' + (i / 10));
                dest[pos++] = (char)('0' + (i % 10));

                tail.CopyTo(dest[pos..]);
                pos += tail.Length;
            }
            if (c <= 100)
                return;
            nb = c <= 1000 ? c : 1000;
            for (; i < nb; i++) {
                head.CopyTo(dest[pos..]);
                pos += head.Length;

                dest[pos++] = (char)('0' + (i / 100));
                dest[pos++] = (char)('0' + ((i / 10) % 10));
                dest[pos++] = (char)('0' + (i % 10));

                tail.CopyTo(dest[pos..]);
                pos += tail.Length;
            }
            if (c <= 1000)
                return;
            for (; i < c; i++) {
                head.CopyTo(dest[pos..]);
                pos += head.Length;

                dest[pos++] = (char)('0' + (i / 1000));
                dest[pos++] = (char)('0' + ((i / 100) % 10));
                dest[pos++] = (char)('0' + ((i / 10) % 10));
                dest[pos++] = (char)('0' + (i % 10));

                tail.CopyTo(dest[pos..]);
                pos += tail.Length;
            }
        });
    }

    /*/// <summary>Build the string placing the " IN (@ID00 ... @IDCount)"</summary>
    public static string BuildInClause(string query, string idCol, int colIdx, int varIdx, int count) {
        // Math: template.Length - 1 (for =) + 5 (for " IN (") + (count * 6 (for "@IDxx,")) - 1 (for ",") + 1 (for ")") - BindParamName.Length
        int totalLength = query.Length + 4 + (count * 6) - BindParamName.Length + idCol.Length;
        if (count > 100)
            totalLength += count - 100;
        if (count > 1000)
            totalLength += count - 1000;
        if (count > 10000)
            totalLength += count - 10000;
        if (count > 100000)
            throw new Exception("does not support binding with over 100000 items at once");
        return string.Create(totalLength, (query, idCol, colIdx, varIdx, count), (span, state) => {
            var (tpl, idCol, colIdx, varIdx, c) = state;
            tpl.AsSpan(0, colIdx).CopyTo(span);
            int pos = colIdx;
            idCol.CopyTo(span[pos..]);
            pos += idCol.Length;
            tpl.AsSpan(colIdx, varIdx - colIdx).CopyTo(span[pos..]);
            pos += varIdx - colIdx;
            " IN (".AsSpan().CopyTo(span[pos..]);
            pos += 5;
            var count = c;
            if (count > 100)
                count = 100;
            for (int i = 0; i < count; i++) {
                BindParamNames[i].AsSpan().CopyTo(span[pos..]);
                pos += BindParamName.Length + 2;
                span[pos++] = ',';
            }
            if (c > 100) {
                for (int i = 100; i < c; i++) {
                    BindParamName.AsSpan().CopyTo(span[pos..]);
                    pos += BindParamName.Length;
                    if (!i.TryFormat(span[pos..], out int charsWritten))
                        throw new InvalidOperationException("Span too small for ID formatting.");
                    pos += charsWritten;
                    span[pos++] = ',';
                }
            }
            span[pos - 1] = ')';
            tpl.AsSpan(varIdx + BindParamName.Length + 1).CopyTo(span[pos..]);
        });
    }*/
    /// <summary>Build a variable name using the index when its over 100"</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetParamName(int index) {
        int digitCount = index switch {
            < 1000 => 3,
            < 10000 => 4,
            < 100000 => 5,
            < 1000000 => 6,
            _ => throw new InvalidOperationException("index should be between 100 and 100000")
        };
        return string.Create(BindParamName.Length + digitCount, index, (span, index) => {
            BindParamName.AsSpan().CopyTo(span);
            if (!index.TryFormat(span[BindParamName.Length..], out _)) {
                throw new InvalidOperationException("Failed to format parameter index.");
            }
        });
    }
}
