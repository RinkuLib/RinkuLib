using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace RinkuLib.DbParsing;

/// <summary>
/// Registers provider-independent reader callbacks for column types whose provider-specific value cannot be
/// obtained through the default <see cref="DbDataReader.GetFieldValue{T}(int)"/> choice.
/// </summary>
/// <remarks>
/// The key is the type reported by the reader schema. The callback chooses the actual value type and remains
/// responsible for using the provider's own reader API. This keeps provider knowledge outside Rinku.
/// </remarks>
public static class DbColumnReaderRegistry {
    private static readonly ConcurrentDictionary<Type, IColumnReader> Readers = [];
    internal static readonly MethodInfo ReadRegisteredMethod = typeof(DbColumnReaderRegistry).GetMethod(
        nameof(ReadRegistered), BindingFlags.Static | BindingFlags.Public, null,
        [typeof(DbDataReader), typeof(int), typeof(Type)], null)!;

    /// <summary>
    /// Registers how a schema column of <typeparamref name="TColumn"/> is read as <typeparamref name="TValue"/>.
    /// </summary>
    /// <param name="reader">The provider callback used for a non-null column value.</param>
    public static void Register<TColumn, TValue>(Func<DbDataReader, int, TValue> reader) {
        ArgumentNullException.ThrowIfNull(reader);
        ColumnReader<TColumn, TValue>.Reader = reader;
        Readers[typeof(TColumn)] = ColumnReader<TColumn, TValue>.Instance;
        TypeParsingInfo.AddOrSet(typeof(TValue), BaseTypeInfo.Instance);
        TypeParsingInfo.TouchConfiguration();
    }

    /// <summary>Invokes a registered callback from generated mapping code.</summary>
    public static object? ReadRegistered(DbDataReader reader, int ordinal, Type columnType) {
        if (!TryGet(columnType, out var columnReader))
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"No reader is registered for {columnType}.");
        return columnReader.ReadValue(reader, ordinal);
    }

    internal static bool TryGet(Type columnType, out IColumnReader reader) {
        return Readers.TryGetValue(columnType, out reader!);
    }

    internal static bool HasValueType(Type valueType)
        => Readers.Values.Any(reader => reader.ValueType == valueType);
}

internal interface IColumnReader {
    /// <summary>The CLR type produced by the reader callback.</summary>
    Type ValueType { get; }
    /// <summary>Reads one value through the external callback.</summary>
    object? ReadValue(DbDataReader reader, int ordinal);
}

/// <summary>The generated-code holder for a registered column reader callback.</summary>
internal sealed class ColumnReader<TColumn, TValue> : IColumnReader {
    internal static readonly ColumnReader<TColumn, TValue> Instance = new();
    internal static Func<DbDataReader, int, TValue> Reader = null!;
    /// <inheritdoc/>
    public Type ValueType => typeof(TValue);
    /// <inheritdoc/>
    public object? ReadValue(DbDataReader reader, int ordinal) => Reader(reader, ordinal);

}
