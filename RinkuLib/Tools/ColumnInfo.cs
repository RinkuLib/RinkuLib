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
    /// <summary>
    /// Performs a comparison based on type, name (case-insensitive), and nullability.
    /// </summary>
    public readonly bool Equals(ColumnInfo column) 
        => column.IsNullable == IsNullable
        && column.Type == Type
        && string.Equals(column.Name, Name, StringComparison.OrdinalIgnoreCase);
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
    public static ColumnInfo[] GetColumns(this DbDataReader reader)
        => reader.GetColumnSchema().GetColumns();

    /// <summary>
    /// Converts a <see cref="ReadOnlyCollection{DbColumn}"/> it into a 
    /// <see cref="ColumnInfo"/> array for the Negotiation Phase.
    /// </summary>
    public static ColumnInfo[] GetColumns(this ReadOnlyCollection<DbColumn> schema) {
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
    /// Compares two schema arrays for structural equality. 
    /// This is used to determine if a cached parser can be reused for a new request.
    /// </summary>
    public static bool Equal(this ColumnInfo[] cols1, ColumnInfo[] cols2) {
        if (cols1.Length != cols2.Length)
            return false;
        for (var i = 0; i < cols1.Length; i++)
            if (!cols1[i].Equals(cols2[i]))
                return false;
        return true;
    }
}