using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands; 
/// <summary> Made to hold a single parser for a single command</summary>
public class SingleParser<T>(string commandText) : ICacheUsingParser<T> {
    /// <summary></summary>
    public ITypeParser<T>? Parser { get; private set; }
    /// <summary></summary>
    public readonly string CommandText = commandText;

    /// <summary></summary>
    public T Query(DbCommand command, bool disposeCommand = false) {
        command.CommandText = CommandText;
        if (Parser is not null)
            return Parser.Query(command, disposeCommand);
        return command.Query(disposeCommand, null, this);
    }
    /// <summary></summary>
    public T Query(IDbCommand command, bool disposeCommand = false) {
        command.CommandText = CommandText;
        if (Parser is not null)
            return Parser.Query(command, disposeCommand);
        return command.Query(disposeCommand, null, this);
    }
    /// <summary></summary>
    public Task<T> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        command.CommandText = CommandText;
        if (Parser is not null)
            return Parser.QueryAsync(command, disposeCommand, ct);
        return command.QueryAsync(disposeCommand, null, this, ct);
    }
    /// <summary></summary>
    public Task<T> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        command.CommandText = CommandText;
        if (Parser is not null)
            return Parser.QueryAsync(command, disposeCommand, ct);
        return command.QueryAsync(disposeCommand, null, this, ct);
    }
    /// <inheritdoc/>
    public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
        var schema = reader.GetColumns();
        parser ??= TypeParser<T>.GetTypeParser(ref schema);
        Parser = parser;
    }
}
/// <summary> Made to hold a single parser for a single stored proc</summary>
public class StoredProcParser<T>(string procName) : ICacheUsingParser<T> {
    /// <inheritdoc/>
    public ITypeParser<T>? Parser { get; private set; }
    /// <inheritdoc/>
    public readonly string ProcName = procName;
    /// <summary></summary>
    public T Query(DbCommand command, bool disposeCommand = false) {
        command.CommandText = ProcName;
        command.CommandType = CommandType.StoredProcedure;
        if (Parser is not null)
            return Parser.Query(command, disposeCommand);
        return command.Query(disposeCommand, null, this);
    }
    /// <summary></summary>
    public T Query(IDbCommand command, bool disposeCommand = false) {
        command.CommandText = ProcName;
        command.CommandType = CommandType.StoredProcedure;
        if (Parser is not null)
            return Parser.Query(command, disposeCommand);
        return command.Query(disposeCommand, null, this);
    }
    /// <summary></summary>
    public Task<T> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        command.CommandText = ProcName;
        command.CommandType = CommandType.StoredProcedure;
        if (Parser is not null)
            return Parser.QueryAsync(command, disposeCommand, ct);
        return command.QueryAsync(disposeCommand, null, this, ct);
    }
    /// <summary></summary>
    public Task<T> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default) {
        command.CommandText = ProcName;
        command.CommandType = CommandType.StoredProcedure;
        if (Parser is not null)
            return Parser.QueryAsync(command, disposeCommand, ct);
        return command.QueryAsync(disposeCommand, null, this, ct);
    }
    /// <inheritdoc/>
    public void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser) {
        var schema = reader.GetColumns();
        parser ??= TypeParser<T>.GetTypeParser(ref schema);
        Parser = parser;
    }
}