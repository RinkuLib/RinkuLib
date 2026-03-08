using System.Collections.ObjectModel;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.Tools;

/// <summary>
/// Represents the schema and state of a single column received by the engine.
/// </summary>
public struct ColumnInfo(string Name, Type Type, bool IsNullable) {
    /// <summary>The name of the column as it appears in the result set.</summary>
    public string Name = Name;
    /// <summary>The C# type corresponding to the column data.</summary>
    public Type Type = Type;
    /// <summary>
    /// Indicates if the schema identifies this column as potentially containing null values.
    /// </summary>
    public bool IsNullable = IsNullable;
}
/// <summary>
/// A simple conparar that compare the array sequence instead of the simple array reference
/// </summary>
public sealed class ArrayContentComparer<T> : IEqualityComparer<T[]> where T : struct {
    /// <summary>
    /// The reusable instance
    /// </summary>
    public static readonly ArrayContentComparer<T> Instance = new();
    ///<inheritdoc/>
    public bool Equals(T[]? x, T[]? y) {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        return x.AsSpan().SequenceEqual(y);
    }
    ///<inheritdoc/>
    public int GetHashCode(T[] obj) {
        var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes(obj.AsSpan()));
        return hash.ToHashCode();
    }
}
/// <summary>Provides extensions for <see cref="ColumnInfo"/></summary>
public static class Helper {
    /// <summary>
    /// Extracts the schema from a <see cref="DbDataReader"/> and converts it into a 
    /// <see cref="ColumnInfo"/> array for the Negotiation Phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColumnInfo[] GetColumns(this DbDataReader reader) { 
        var schema = reader.GetColumnSchema();
        var columns = new ColumnInfo[schema.Count];
        for (var i = 0; i < schema.Count; i++) {
            var column = schema[i];
            string name = column.ColumnName ?? string.Empty;
            if (column.IsAliased == false && column.IsExpression == true)
                name = string.Empty;
            columns[i] = new ColumnInfo {
                Name = name,
                Type = column.DataType ?? typeof(object),
                IsNullable = column.AllowDBNull ?? true
            };
        }
        return columns;
    }
    /// <summary>
    /// Extracts the schema from a <see cref="DbDataReader"/> and converts it into a 
    /// <see cref="ColumnInfo"/> array for the Negotiation Phase. (column will always be considered nullable)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColumnInfo[] GetColumnsFast(this DbDataReader reader) {
        int fieldCount = reader.FieldCount;
        if (fieldCount == 0)
            return [];

        var columns = new ColumnInfo[fieldCount];

        for (int i = 0; i < fieldCount; i++) {
            columns[i] = new ColumnInfo {
                Name = reader.GetName(i) ?? string.Empty,
                Type = reader.GetFieldType(i) ?? typeof(object),
                IsNullable = true
            };
        }

        return columns;
    }
    /// <summary>
    /// Compares two schema arrays for structural equality. 
    /// This is used to determine if a cached parser can be reused for a new request.
    /// </summary>
    public static bool EquivalentTo(this ColumnInfo[] candidate, ColumnInfo[] stored) {
        if (candidate.Length != stored.Length)
            return false;
        for (var i = 0; i < candidate.Length; i++) {
            ref var c = ref candidate[i];
            ref var s = ref stored[i];
            if (c.Type != s.Type 
            || (!s.IsNullable && c.IsNullable) 
            || !string.Equals(c.Name, s.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
    /// <summary>
    /// Makes a mapper from the columns names while preventing duplication by adding #i for each duplicating instances
    /// </summary>
    public static Mapper MakeMapper(this ColumnInfo[] cols) {
        var mapper = Mapper.GetMapper(cols.Select(c => c.Name));
        if (mapper.Count == cols.Length)
            return mapper;
        var deduplicatedNames = new string[cols.Length];
        var seen = new Dictionary<string, int>(cols.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cols.Length; i++) {
            string originalName = cols[i].Name;
            if (seen.TryGetValue(originalName, out int suffix)) {
                string newName;
                do {
                    newName = $"{originalName}#{suffix++}";
                } while (seen.ContainsKey(newName));

                deduplicatedNames[i] = newName;
                seen[originalName] = suffix;
                seen[newName] = 2;
            }
            else {
                deduplicatedNames[i] = originalName;
                seen[originalName] = 2;
            }
        }
        return Mapper.GetMapper(deduplicatedNames);
    }
}