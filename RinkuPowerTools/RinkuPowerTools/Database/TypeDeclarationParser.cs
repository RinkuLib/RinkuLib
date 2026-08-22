namespace RinkuPowerTools;

internal readonly record struct ParsedTypeDeclaration(string Name, string? FirstArgument, string? SecondArgument);

internal static class TypeDeclarationParser {
    public static ParsedTypeDeclaration Parse(string value) {
        string text = value.Trim();
        int open = text.IndexOf('(');
        if (open < 0)
            return new ParsedTypeDeclaration(text, null, null);

        int close = text.IndexOf(')', open + 1);
        if (close < 0)
            throw new InvalidOperationException($"Invalid database type declaration '{value}'.");

        string name = text[..open].Trim();
        string suffix = text[(close + 1)..].Trim();
        if (suffix.Length != 0)
            name = suffix[0] == '[' ? name + suffix : name + " " + suffix;

        ReadOnlySpan<char> args = text.AsSpan(open + 1, close - open - 1).Trim();
        int comma = args.IndexOf(',');
        if (comma < 0)
            return new ParsedTypeDeclaration(name, args.ToString().Trim(), null);

        return new ParsedTypeDeclaration(
            name,
            args[..comma].ToString().Trim(),
            args[(comma + 1)..].ToString().Trim());
    }

    public static int ParseSize(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
            return -1;
        if (int.TryParse(value, out int size))
            return size;
        throw new InvalidOperationException($"Invalid size '{value}'.");
    }

    public static byte ParseByte(string? value, string label) {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (byte.TryParse(value, out byte parsed))
            return parsed;
        throw new InvalidOperationException($"Invalid {label} '{value}'.");
    }
}
