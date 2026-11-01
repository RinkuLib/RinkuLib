namespace RinkuPowerTools;

public sealed record ConstantsSource(QuerySourceType SourceType, string Target) {
    public string PropertyName => SourceType switch {
        QuerySourceType.Text => "ConstantsSQL",
        QuerySourceType.FromFile => "ConstantsPath",
        QuerySourceType.StoredProcedure => "ConstantsProc",
        _ => throw new InvalidOperationException($"Unsupported constants source: {SourceType}")
    };
    public QuerySetting ToQuery() {
        if (string.IsNullOrWhiteSpace(Target)) throw new InvalidOperationException("The constants source cannot be empty.");
        _ = PropertyName;
        return new QuerySetting { MethodName = "Constants", Target = Target, SourceType = SourceType };
    }
    public static bool TryGetSourceType(string propertyName, out QuerySourceType type) {
        switch (propertyName.ToUpperInvariant()) {
            case "CONSTANTSSQL": type = QuerySourceType.Text; return true;
            case "CONSTANTSPATH": type = QuerySourceType.FromFile; return true;
            case "CONSTANTSPROC": type = QuerySourceType.StoredProcedure; return true;
            default: type = default; return false;
        }
    }
}
