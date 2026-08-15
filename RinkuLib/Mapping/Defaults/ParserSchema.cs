namespace Rinku.Mapping.Defaults;

internal readonly struct ParserSchema(ColumnInfo[] columns) {
    private readonly ColumnInfo[] Columns = columns;

    internal static ParserSchema Exact(ColumnInfo[] columns) => new(columns);

    internal bool Accepts(ColumnInfo[] candidate) {
        if (candidate.Length != Columns.Length)
            return false;
        for (int i = 0; i < candidate.Length; i++) {
            ref var c = ref candidate[i];
            ref var s = ref Columns[i];
            if (c.Type != s.Type || (!s.IsNullable && c.IsNullable)
                || (s.Name is not null && !string.Equals(c.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }
}
