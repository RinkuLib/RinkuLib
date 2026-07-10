using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// A parser for a streamed shape that keeps the reader open while you iterate, an
/// <see cref="IEnumerable{T}"/> or async stream. Because the reader outlives the call, this parser owns the
/// cleanup and runs it once enumeration ends.
/// </summary>
public interface ILazyTypeParser<T> : ITypeParser<T> {
    /// <summary>
    /// Reads the result lazily, taking ownership of the reader and running <paramref name="callback"/> when
    /// enumeration reaches the end.
    /// </summary>
    T ParseAndOwn<TCallback>(DbDataReader reader, TCallback callback) where TCallback : ILazyTypeParserCallback;
}
/// <summary>Picks the right end-of-enumeration cleanup for a lazy parser from what needs disposing.</summary>
public static class LazyParserExtensions {
    /// <summary>
    /// Reads the result lazily and, when enumeration ends, disposes the reader plus whatever
    /// <paramref name="wasClosed"/> and <paramref name="disposeCommand"/> say to also clean up.
    /// </summary>
    public static T ParseAndOwn<T>(this ILazyTypeParser<T> lazyParser, DbDataReader reader, IDbCommand cmd, bool wasClosed, bool disposeCommand) {
        if (wasClosed && disposeCommand)
            return lazyParser.ParseAndOwn<DisposeReaderAndCommandAndCloseConnection>(reader, new(cmd));
        if (wasClosed)
            return lazyParser.ParseAndOwn<DisposeReaderAndCloseConnection>(reader, new(cmd));
        if (disposeCommand)
            return lazyParser.ParseAndOwn<DisposeReaderAndCommand>(reader, new(cmd));
        return lazyParser.ParseAndOwn<DisposeReader>(reader, new());
    }
}

/// <summary>
/// The cleanup a lazy parser runs when enumeration ends. Each shape below cleans up a different set of things,
/// picked from what the run owns.
/// </summary>
public interface ILazyTypeParserCallback {
    /// <summary>Runs the cleanup once the reader is exhausted.</summary>
    public void Invoke(DbDataReader reader);
}
/// <summary>Cleanup that disposes only the reader.</summary>
public struct DisposeReader : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) => reader.Dispose();
}
/// <summary>Cleanup that disposes the reader and the command.</summary>
public readonly struct DisposeReaderAndCommand(IDbCommand command) : ILazyTypeParserCallback {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Parameters.Clear();
        _command.Dispose();
    }
}
/// <summary>Cleanup that disposes the reader and command and closes the connection.</summary>
public readonly struct DisposeReaderAndCommandAndCloseConnection(IDbCommand command) : ILazyTypeParserCallback {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Connection?.Close();
        _command.Parameters.Clear();
        _command.Dispose();
    }
}
/// <summary>Cleanup that disposes the reader and closes the connection, leaving the command.</summary>
public readonly struct DisposeReaderAndCloseConnection(IDbCommand command) : ILazyTypeParserCallback {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Connection?.Close();
    }
}
/// <summary>Cleanup that does nothing, for when the caller keeps ownership.</summary>
public readonly struct DoNothing : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) { }
}
/// <summary>Cleanup that advances the reader to the next result set instead of disposing, used within a <see cref="Commands.MultiReader"/>.</summary>
public readonly struct GoToNextResultSet : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) => reader.NextResult();
}