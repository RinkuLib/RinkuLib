namespace RinkuPowerTools;

public class MethodParameterInfo {
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public static class CodeParameterParser {
    /// <summary>
    /// Extracts parameters for a specific method directly from a raw source code string,
    /// skipping the initial 'this DbConnection' extension target.
    /// </summary>
    public static List<MethodParameterInfo> ExtractParameters(string sourceCode, string methodName) {
        var parameters = new List<MethodParameterInfo>();
        if (string.IsNullOrWhiteSpace(sourceCode))
            return parameters;

        int length = sourceCode.Length;
        int braceLevel = 0;
        int i = 0;

        while (i < length) {
            char c = sourceCode[i];

            if (c == '"') {
                i++;
                while (i < length && sourceCode[i] != '"') { if (sourceCode[i] == '\\') i++; i++; }
                i++;
                continue;
            }
            if (c == '/' && i + 1 < length && sourceCode[i + 1] == '/') {
                while (i < length && sourceCode[i] != '\n')
                    i++;
                continue;
            }
            if (c == '/' && i + 1 < length && sourceCode[i + 1] == '*') {
                i += 2;
                while (i + 1 < length && !(sourceCode[i] == '*' && sourceCode[i + 1] == '/'))
                    i++;
                i += 2;
                continue;
            }

            if (c == '{') { braceLevel++; i++; continue; }
            if (c == '}') { braceLevel--; i++; continue; }

            if (braceLevel >= 1) {
                if (IsMatchAtPosition(sourceCode, i, methodName) && IsTokenBoundary(sourceCode, i, methodName.Length)) {
                    int ptr = i + methodName.Length;

                    while (ptr < length && char.IsWhiteSpace(sourceCode[ptr]))
                        ptr++;

                    if (ptr < length && sourceCode[ptr] == '(') {
                        string paramBlock = ExtractParenthesesContent(sourceCode, ptr);
                        return ParseParameterBlock(paramBlock);
                    }
                }
            }
            i++;
        }

        return parameters;
    }

    private static bool IsMatchAtPosition(string source, int index, string target) {
        if (index + target.Length > source.Length)
            return false;
        return source.AsSpan(index, target.Length).Equals(target, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTokenBoundary(string source, int index, int matchLength) {
        bool beforeIsValid = index == 0 || (!char.IsLetterOrDigit(source[index - 1]) && source[index - 1] != '_');
        bool afterIsValid = (index + matchLength >= source.Length) || (!char.IsLetterOrDigit(source[index + matchLength]) && source[index + matchLength] != '_');
        return beforeIsValid && afterIsValid;
    }

    private static string ExtractParenthesesContent(string source, int openParenIndex) {
        int parenLevel = 0;
        int start = openParenIndex + 1;

        for (int i = start; i < source.Length; i++) {
            if (source[i] == '(')
                parenLevel++;
            if (source[i] == ')') {
                if (parenLevel == 0)
                    return source[start..i];
                parenLevel--;
            }
        }
        return string.Empty;
    }

    private static List<MethodParameterInfo> ParseParameterBlock(string parameterBlock) {
        var list = new List<MethodParameterInfo>();
        if (string.IsNullOrWhiteSpace(parameterBlock))
            return list;

        var segments = parameterBlock.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments.Skip(1)) {
            var cleaned = segment.Trim();
            if (string.IsNullOrEmpty(cleaned))
                continue;

            var parts = cleaned.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) {
                list.Add(new MethodParameterInfo {
                    Name = parts[^1],
                    Type = string.Join(" ", parts[..^1])
                });
            }
        }
        return list;
    }
}