using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace RinkuPowerTools;

public sealed record ConstantDefinition(string Name, string? Value, string? Type, string? Description, string? Parent);

/// <summary>Reads one constant tree. Absolute parent paths make cyclic references unrepresentable.</summary>
public static class ConstantsGenerator {
    private static readonly string[] Columns = ["Name", "Value", "Type", "Description", "Parent"];
    private static readonly string[] IntegerTypes = ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong"];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task<string> GenerateAsync(DbConnection connection, ExtensionSettings settings, CancellationToken ct) {
        var query = (settings.Constants ?? throw new InvalidOperationException("Constants source is missing.")).ToQuery();
        if (query.SourceType == QuerySourceType.StoredProcedure && !settings.GetDatabaseProvider().Supports(DatabaseCapabilities.StoredProcedures))
            throw new InvalidOperationException("This database does not support stored procedures.");
        string sql = await settings.GetSqlTextAsync(query, ct);
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try {
            using var command = connection.CreateCommand();
            command.CommandType = query.GetCommandType(); command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync(ct);
            if (reader.FieldCount != Columns.Length) throw new InvalidOperationException("Constants must return exactly five string columns: Name, Value, Type, Description, Parent.");
            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            if (Columns.Any(column => names.Count(name => string.Equals(name, column, StringComparison.OrdinalIgnoreCase)) != 1))
                throw new InvalidOperationException("Constants columns must be named Name, Value, Type, Description, Parent, without duplicates.");
            var ordinals = Columns.Select(reader.GetOrdinal).ToArray();
            List<ConstantDefinition> rows = [];
            while (await reader.ReadAsync(ct)) {
                string? Read(int column) => reader.GetValue(ordinals[column]) switch {
                    DBNull => null, string value => value,
                    _ => throw new InvalidOperationException($"Constants row {rows.Count + 1}, column {Columns[column]} must be a string or SQL NULL.")
                };
                rows.Add(new(Read(0) ?? throw new InvalidOperationException($"Constants row {rows.Count + 1} has no name."), Read(1), Read(2), Read(3), Read(4)));
            }
            if (await reader.NextResultAsync(ct)) throw new InvalidOperationException("Constants must return exactly one result set.");
            return GenerateCode(rows, settings.IsInternal);
        }
        finally { if (opened) await connection.CloseAsync(); }
    }

    public static string GenerateCode(IEnumerable<ConstantDefinition> constants, bool isInternal = false) {
        var root = new Node("", "");
        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (var row in constants) {
            Identifier(row.Name);
            if (row.Parent is "") throw new InvalidOperationException($"'{row.Name}': use SQL NULL for a root parent, not an empty string.");
            if (row.Parent is null && row.Value is not null) throw new InvalidOperationException($"'{row.Name}': a root must be a container with SQL NULL Value.");
            Node parent = root;
            if (row.Parent is not null) foreach (string part in row.Parent.Split('.')) parent = Child(parent, part);
            var node = Child(parent, row.Name);
            if (!declared.Add(node.Path)) throw new InvalidOperationException($"Duplicate constant '{node.Path}'.");
            node.Row = row;
        }
        Validate(root);
        var code = new StringBuilder("using System.ComponentModel;\n\n");
        foreach (var node in root.Children.Values.OrderBy(node => node.Name, StringComparer.Ordinal)) Emit(code, node, "", isInternal ? "internal" : "public");
        return code.ToString();
    }

    private sealed class Node(string name, string path) {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public ConstantDefinition? Row { get; set; }
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public Literal? Value { get; set; }
        public string? EnumType { get; set; }
    }
    private sealed record Literal(string Type, string Text, BigInteger? Integer = null);
    private static Node Child(Node parent, string name) {
        Identifier(name);
        if (parent.Name == name) throw new InvalidOperationException($"'{parent.Path}': a member/container cannot have its enclosing type's name '{name}' (including self references).");
        if (!parent.Children.TryGetValue(name, out var child)) parent.Children.Add(name, child = new(name, parent.Path.Length == 0 ? name : parent.Path + "." + name));
        return child;
    }
    private static string Identifier(string name) {
        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None) return "@" + name;
        if (!SyntaxFacts.IsValidIdentifier(name) || name.StartsWith('@')) throw new InvalidOperationException($"Invalid constant name/path segment '{name}'. Use a C# identifier; dots separate parent containers.");
        return name;
    }
    private static void Validate(Node node) {
        if (node.Row is { Value: not null } row) {
            if (node.Children.Count > 0) throw new InvalidOperationException($"'{node.Path}' has a value and is also used as a parent container.");
            try { node.Value = Parse(row.Value, row.Type); }
            catch (Exception error) when (error is FormatException or OverflowException or InvalidOperationException) { throw new InvalidOperationException($"'{node.Path}': {error.Message}", error); }
        }
        else if (node.Row?.Type is not null) throw new InvalidOperationException($"'{node.Path}': a container must have SQL NULL Type.");
        foreach (var child in node.Children.Values) Validate(child);
        if (node.Children.Count > 0 && node.Children.Values.All(child => child.Value?.Integer is not null)) node.EnumType = EnumType(node.Children.Values.Select(child => child.Value!).ToArray());
        if (node.EnumType is not null && node.Children.ContainsKey("value__")) throw new InvalidOperationException($"'{node.Path}.value__': this member name is reserved for enum storage.");
    }
    private static string? EnumType(Literal[] values) {
        int largest = values.Max(value => Bits(value.Type));
        bool signed = values.Any(value => value.Type is "sbyte" or "short" or "int" or "long");
        bool unsigned = values.Any(value => (value.Type is "byte" or "ushort" or "uint" or "ulong") && Bits(value.Type) == largest);
        if (signed && unsigned) {
            if (largest < 64) largest *= 2;
            else if (values.All(value => value.Integer >= 0)) signed = false;
            else if (values.Any(value => value.Integer > long.MaxValue)) return null;
        }
        return (largest, signed) switch { (8, true) => "sbyte", (8, false) => "byte", (16, true) => "short", (16, false) => "ushort", (32, true) => "int", (32, false) => "uint", (64, true) => "long", _ => "ulong" };
    }
    private static int Bits(string type) => type switch { "sbyte" or "byte" => 8, "short" or "ushort" => 16, "int" or "uint" => 32, _ => 64 };
    private static Literal Parse(string value, string? type) {
        type = type switch { "System.String" => "string", "System.Boolean" => "bool", "System.Char" => "char", "System.SByte" => "sbyte", "System.Byte" => "byte", "System.Int16" => "short", "System.UInt16" => "ushort", "System.Int32" => "int", "System.UInt32" => "uint", "System.Int64" => "long", "System.UInt64" => "ulong", "System.Single" => "float", "System.Double" => "double", "System.Decimal" => "decimal", _ => type };
        if (type is null) throw new InvalidOperationException("A value requires a Type.");
        if (IntegerTypes.Contains(type)) {
            var number = BigInteger.Parse(value, NumberStyles.Integer, Invariant);
            int bits = Bits(type); bool signed = type is "sbyte" or "short" or "int" or "long";
            var max = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
            var min = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
            if (number < min || number > max) throw new OverflowException($"Value '{value}' does not fit {type}.");
            string suffix = type switch { "uint" => "U", "long" => "L", "ulong" => "UL", _ => "" };
            return new(type, number.ToString(Invariant) + suffix, number);
        }
        return type switch {
            "string" => new(type, SymbolDisplay.FormatLiteral(value, true)),
            "char" when value.Length == 1 => new(type, SymbolDisplay.FormatLiteral(value[0], true)),
            "bool" => new(type, bool.Parse(value) ? "true" : "false"),
            "decimal" => new(type, decimal.Parse(value, NumberStyles.Float, Invariant).ToString(Invariant) + "M"),
            "float" => new(type, Floating(float.Parse(value, NumberStyles.Float, Invariant), "float", "F")),
            "double" => new(type, Floating(double.Parse(value, NumberStyles.Float, Invariant), "double", "D")),
            _ => throw new InvalidOperationException($"Unsupported type '{type}' or invalid value. Use a primitive C# scalar type.")
        };
    }
    private static string Floating(double value, string type, string suffix) => double.IsNaN(value) ? type + ".NaN" : double.IsPositiveInfinity(value) ? type + ".PositiveInfinity" : double.IsNegativeInfinity(value) ? type + ".NegativeInfinity" : value.ToString("R", Invariant) + suffix;
    private static void Emit(StringBuilder code, Node node, string indent, string access) {
        if (node.Row?.Description is { } description) code.AppendLine($"{indent}[Description({SymbolDisplay.FormatLiteral(description, true)})]");
        if (node.Value is { } value) { code.AppendLine($"{indent}{access} static readonly {value.Type} {Identifier(node.Name)} = {value.Text};"); return; }
        code.AppendLine($"{indent}{access} {(node.EnumType is null ? "static class" : "enum")} {Identifier(node.Name)}{(node.EnumType is null ? "" : " : " + node.EnumType)} {{");
        foreach (var child in node.Children.Values.OrderBy(child => child.Name, StringComparer.Ordinal)) {
            if (node.EnumType is null) Emit(code, child, indent + "    ", "public");
            else {
                if (child.Row?.Description is { } text) code.AppendLine($"{indent}    [Description({SymbolDisplay.FormatLiteral(text, true)})]");
                string suffix = node.EnumType switch { "uint" => "U", "long" => "L", "ulong" => "UL", _ => "" };
                code.AppendLine($"{indent}    {Identifier(child.Name)} = {child.Value!.Integer!.Value.ToString(Invariant)}{suffix},");
            }
        }
        code.AppendLine(indent + "}");
    }
}
