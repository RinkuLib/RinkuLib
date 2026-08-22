namespace RinkuPowerTools;

internal readonly record struct PostgreSqlParameterLayout(
    IReadOnlyList<string> Names,
    bool IsPositional) {

    public static PostgreSqlParameterLayout Parse(string sql) {
        List<string> scanned = NamedParameterScanner.Scan(sql);
        if (scanned.Count == 0)
            return new PostgreSqlParameterLayout(Array.Empty<string>(), false);

        bool hasPositional = false;
        bool hasNamed = false;
        int maxPosition = 0;
        var positions = new HashSet<int>();
        var named = new List<string>(scanned.Count);
        var namedBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in scanned) {
            if (TryGetPosition(name, out int position)) {
                if (position <= 0)
                    throw new InvalidOperationException("PostgreSQL positional parameters start at $1.");

                hasPositional = true;
                positions.Add(position);
                if (position > maxPosition)
                    maxPosition = position;
                continue;
            }

            if (name[0] == '$')
                throw new InvalidOperationException($"PostgreSQL parameter '{name}' is not a valid positional parameter. Use $1, $2, ... or a named @/: parameter.");

            hasNamed = true;
            string body = name[1..];
            if (namedBodies.Add(body))
                named.Add(name);
        }

        if (hasPositional && hasNamed)
            throw new InvalidOperationException("PostgreSQL queries cannot mix positional and named parameters.");

        if (!hasPositional)
            return new PostgreSqlParameterLayout(named, false);

        if (positions.Count != maxPosition)
            throw new InvalidOperationException("PostgreSQL positional parameters must form a contiguous $1, $2, ... sequence.");

        var ordered = new string[maxPosition];
        for (int i = 0; i < ordered.Length; i++)
            ordered[i] = $"${i + 1}";

        return new PostgreSqlParameterLayout(ordered, true);
    }

    public static bool TryGetPosition(string name, out int position) {
        position = 0;
        if (name.Length < 2 || name[0] != '$')
            return false;

        for (int i = 1; i < name.Length; i++) {
            char c = name[i];
            if (c is < '0' or > '9') {
                position = 0;
                return false;
            }

            int digit = c - '0';
            if (position > (int.MaxValue - digit) / 10)
                throw new InvalidOperationException($"PostgreSQL positional parameter '{name}' is too large.");
            position = position * 10 + digit;
        }

        return true;
    }
}
