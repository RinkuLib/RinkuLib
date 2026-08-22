namespace RinkuPowerTools;

internal static class NamedParameterScanner {
    public static List<string> Scan(string sql) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadOnlySpan<char> span = sql.AsSpan();

        for (int i = 0; i < span.Length; i++) {
            char c = span[i];

            if (c == '\'' || c == '"' || c == '`') {
                bool postgresEscapeString = c == '\'' && i > 0 && span[i - 1] is 'E' or 'e' && (i < 2 || !IsNamePart(span[i - 2]));
                i = postgresEscapeString ? SkipEscapeQuoted(span, i) : SkipQuoted(span, i, c);
                continue;
            }

            if (c == '[') {
                i = SkipBracketed(span, i);
                continue;
            }

            if (c == '-' && i + 1 < span.Length && span[i + 1] == '-') {
                i = SkipLineComment(span, i + 2);
                continue;
            }

            if (c == '/' && i + 1 < span.Length && span[i + 1] == '*') {
                i = SkipBlockComment(span, i + 2);
                continue;
            }

            if (c is not ('@' or '$' or ':'))
                continue;

            if (c == '$' && TrySkipDollarQuoted(span, i, out int dollarQuotedEnd)) {
                i = dollarQuotedEnd;
                continue;
            }

            if (c == ':' && i + 1 < span.Length && span[i + 1] == ':') {
                i++;
                continue;
            }

            int start = i;
            int nameStart = i + 1;
            if (nameStart >= span.Length || !(IsNameStart(span[nameStart]) || c == '$' && char.IsDigit(span[nameStart])))
                continue;

            int end = nameStart + 1;
            while (end < span.Length && IsNamePart(span[end]))
                end++;

            string name = span[start..end].ToString();
            if (seen.Add(name))
                result.Add(name);
            i = end - 1;
        }

        return result;
    }

    private static bool TrySkipDollarQuoted(ReadOnlySpan<char> span, int start, out int end) {
        end = start;
        int delimiterEnd = start + 1;

        if (delimiterEnd < span.Length && span[delimiterEnd] == '$') {
            delimiterEnd++;
        }
        else {
            if (delimiterEnd >= span.Length || !IsNameStart(span[delimiterEnd]))
                return false;
            delimiterEnd++;
            while (delimiterEnd < span.Length && IsNamePart(span[delimiterEnd]))
                delimiterEnd++;
            if (delimiterEnd >= span.Length || span[delimiterEnd] != '$')
                return false;
            delimiterEnd++;
        }

        ReadOnlySpan<char> delimiter = span[start..delimiterEnd];
        int relative = span[delimiterEnd..].IndexOf(delimiter);
        if (relative < 0) {
            end = span.Length - 1;
            return true;
        }

        end = delimiterEnd + relative + delimiter.Length - 1;
        return true;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int SkipQuoted(ReadOnlySpan<char> span, int start, char quote) {
        for (int i = start + 1; i < span.Length; i++) {
            if (span[i] != quote)
                continue;
            if (i + 1 < span.Length && span[i + 1] == quote) {
                i++;
                continue;
            }
            return i;
        }
        return span.Length - 1;
    }

    private static int SkipEscapeQuoted(ReadOnlySpan<char> span, int start) {
        for (int i = start + 1; i < span.Length; i++) {
            if (span[i] == '\\') {
                if (i + 1 < span.Length)
                    i++;
                continue;
            }
            if (span[i] != '\'')
                continue;
            if (i + 1 < span.Length && span[i + 1] == '\'') {
                i++;
                continue;
            }
            return i;
        }
        return span.Length - 1;
    }

    private static int SkipBracketed(ReadOnlySpan<char> span, int start) {
        for (int i = start + 1; i < span.Length; i++) {
            if (span[i] != ']')
                continue;
            if (i + 1 < span.Length && span[i + 1] == ']') {
                i++;
                continue;
            }
            return i;
        }
        return span.Length - 1;
    }

    private static int SkipLineComment(ReadOnlySpan<char> span, int start) {
        for (int i = start; i < span.Length; i++)
            if (span[i] is '\r' or '\n')
                return i;
        return span.Length - 1;
    }

    private static int SkipBlockComment(ReadOnlySpan<char> span, int start) {
        int depth = 1;
        for (int i = start; i + 1 < span.Length; i++) {
            if (span[i] == '/' && span[i + 1] == '*') {
                depth++;
                i++;
                continue;
            }
            if (span[i] == '*' && span[i + 1] == '/') {
                depth--;
                if (depth == 0)
                    return i + 1;
                i++;
            }
        }
        return span.Length - 1;
    }
}
