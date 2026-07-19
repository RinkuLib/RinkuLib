using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.Exceptions;

/// <summary>
/// The command handed to a run carried no connection, so there is nothing to execute against.
/// </summary>
public sealed class RinkuNoConnectionException()
    : RinkuBindingException(ErrorCodes.NoConnection, "no connection was set with the command");

/// <summary>
/// No construction path for the target type could be filled from the columns the query returned. Carries
/// the type it gave up on and the schema it was offered.
/// </summary>
public sealed class RinkuNoParserException : RinkuMappingException {
    /// <summary>The type no parser could be built for.</summary>
    public Type TargetType { get; }
    /// <summary>The columns the negotiation had to work with.</summary>
    public string Schema { get; }

    internal RinkuNoParserException(Type targetType, string schema)
        : base(ErrorCodes.NoParserForSchema, $"cannot make the parser for {targetType} with the schema ({schema})") {
        TargetType = targetType;
        Schema = schema;
    }
}

/// <summary>The query returned no rows and the requested shape has no way to say so.</summary>
public sealed class RinkuNoRowsException()
    : RinkuReadException(ErrorCodes.NoRows, "No values were returned from the query");

/// <summary>
/// A result shape refused the rows it was handed. Raised by the shape's own parser rather than by the
/// engine, and available to a parser you write the same way it is to the built-in ones.
/// </summary>
public class RinkuShapeException(string message)
    : RinkuReadException(ErrorCodes.ShapeRefusedResult, message);

/// <summary>Raises the conditions that are checked from many places, so each lives at one site.</summary>
public static class Refuse {
    /// <summary>Raised when a command reaches a run without a connection.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoConnection() => throw new RinkuNoConnectionException();

    /// <summary>Returns <paramref name="connection"/>, or raises <see cref="ErrorCodes.NoConnection"/>.</summary>
    public static T Connected<T>(T? connection) where T : class
        => connection ?? throw new RinkuNoConnectionException();

    /// <summary>Raised when the negotiation exhausted every construction path for <paramref name="targetType"/>.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoParser(Type targetType, ColumnInfo[] cols)
        => throw new RinkuNoParserException(targetType,
            string.Join(", ", cols.Select(c => $"{c.Type.ShortName()}{(c.IsNullable ? "?" : "")} {c.Name}")));

    /// <summary>Raised when a shape that needs a row got none.</summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NoRows() => throw new RinkuNoRowsException();
}
