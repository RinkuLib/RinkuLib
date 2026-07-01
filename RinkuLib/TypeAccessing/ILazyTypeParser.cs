using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// A type parser that should take the disposing responsability
/// </summary>
public interface ILazyTypeParser<T> : ITypeParser<T> {
    /// <summary>
    /// Parser the result and callback when arrive at the end of the enumeration
    /// </summary>
    T ParseAndOwn<TCallback>(DbDataReader reader, TCallback callback) where TCallback : ILazyTypeParserCallback;
}
/// <summary></summary>
public static class LazyParserExtensions {
    /// <summary>
    /// Parser the result and callback when arrive at the end of the enumeration
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
/// An implementation of a callback strategy
/// </summary>
public interface ILazyTypeParserCallback {
    /// <summary>The method to call when ready</summary>
    public void Invoke(DbDataReader reader);
}
/// <summary></summary>
public struct DisposeReader : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) => reader.Dispose();
}
/// <summary></summary>
public readonly struct DisposeReaderAndCommand(IDbCommand command) : ILazyTypeParserCallback {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Parameters.Clear();
        _command.Dispose();
    }
}
/// <summary></summary>
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
/// <summary></summary>
public readonly struct DisposeReaderAndCloseConnection(IDbCommand command) : ILazyTypeParserCallback {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Connection?.Close();
    }
}
/// <summary></summary>
public readonly struct DoNothing : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) { }
}
/// <summary></summary>
public readonly struct GoToNextResultSet : ILazyTypeParserCallback {
    /// <inheritdoc/>
    public void Invoke(DbDataReader reader) => reader.NextResult();
}