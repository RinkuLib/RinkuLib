using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rinku.Mapping;
using Rinku.Internal;

namespace Rinku;

/// <summary>
/// Reports that a command was run without a connection.
/// </summary>
public sealed class RinkuNoConnectionException()
    : RinkuBindingException(ErrorCodes.NoConnection, "no connection was set with the command");

/// <summary>
/// Reports that the returned columns could not create the requested type.
/// </summary>
public sealed class RinkuNoParserException : RinkuMappingException {
    /// <summary>The type no parser could be built for.</summary>
    public Type TargetType { get; }
    /// <summary>The columns returned by the query.</summary>
    public string Schema { get; }

    internal RinkuNoParserException(Type targetType, string schema)
        : base(ErrorCodes.NoParserForSchema, $"cannot make the parser for {targetType} with the schema ({schema})") {
        TargetType = targetType;
        Schema = schema;
    }
}

/// <summary>Reports that a query requiring a row returned no rows.</summary>
public sealed class RinkuNoRowsException()
    : RinkuReadException(ErrorCodes.NoRows, "No values were returned from the query");

/// <summary>
/// Reports that the returned rows do not meet the rules of the requested result type.
/// </summary>
public class RinkuShapeException(string message)
    : RinkuReadException(ErrorCodes.ShapeRefusedResult, message);

/// <summary>Provides the standard failures used by custom Rinku components.</summary>
public static class Refuse {
    /// <summary>Throws when a command has no connection.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoConnection() => throw new RinkuNoConnectionException();

    /// <summary>Returns <paramref name="connection"/>, or raises <see cref="ErrorCodes.NoConnection"/>.</summary>
    public static T Connected<T>(T? connection) where T : class
        => connection ?? throw new RinkuNoConnectionException();

    /// <summary>Throws when the returned columns cannot create <paramref name="targetType"/>.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoParser(Type targetType, ColumnInfo[] cols)
        => throw new RinkuNoParserException(targetType,
            string.Join(", ", cols.Select(c => $"{c.Type.ShortName()}{(c.IsNullable ? "?" : "")} {c.Name}")));

    /// <summary>Throws when a result type requires a row and none was returned.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoRows() => throw new RinkuNoRowsException();
}
