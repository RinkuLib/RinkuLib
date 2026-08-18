using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku.Mapping; 
/// <summary>
/// Creates parsers for custom result types.
/// Add a maker to <see cref="TypeParser.TypeParserMakers"/> before the default makers.
/// </summary>
public interface ITypeParserMaker {
    /// <summary>Whether this maker claims <typeparamref name="T"/>.</summary>
    public bool CanHandle<T>();
    /// <summary>
    /// Builds the parser for <typeparamref name="T"/> over the given columns, or returns <see langword="false"/>
    /// to decline.
    /// </summary>
    /// <remarks>
    /// <paramref name="nullColHandler"/> is the requested root nullability. It is
    /// <see cref="TypeParser.GetDefaultNullColHandler{T}"/> (the type's own nullability) unless a caller
    /// replaced it. The method must accept any <see cref="INullColHandler"/>.
    /// </remarks>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser);
    /// <summary>
    /// Tries to run <typeparamref name="T"/> before the returned columns are known.
    /// Return <see langword="false"/> when a parser must be created after the reader opens.
    /// </summary>
    /// <param name="cmd">The command to run, not yet run.</param>
    /// <param name="cache">Turns the reader's columns into the parser for <typeparamref name="T"/>.</param>
    /// <param name="disposeCommand">Whether the command is this run's to dispose.</param>
    /// <param name="result">The result, when this maker took the run.</param>
    /// <remarks>
    /// Use this for a deferred result that must open the reader while it is consumed. Return
    /// <see langword="false"/> for buffered results or when the columns must be known first.
    /// </remarks>
    public bool TryColdStart<T>(DbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        result = default;
        return false;
    }
    /// <inheritdoc cref="TryColdStart{T}(DbCommand, ICacheGivingParser{T}, bool, out T)"/>
    public bool TryColdStart<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, [MaybeNullWhen(false)] out T result) {
        result = default;
        return false;
    }
    /// <summary>
    /// The asynchronous form of <see cref="TryColdStart{T}(DbCommand, ICacheGivingParser{T}, bool, out T)"/>.
    /// </summary>
    /// <param name="cmd">The command to run, not yet run.</param>
    /// <param name="cache">Turns the reader's columns into the parser for <typeparamref name="T"/>.</param>
    /// <param name="disposeCommand">Whether the command is this run's to dispose.</param>
    /// <param name="ct">The token the caller is running under.</param>
    /// <param name="result">The result, when this maker took the run.</param>
    /// <remarks>
    /// Return a completed task when no asynchronous work is needed.
    /// The synchronous and asynchronous methods may accept different result types.
    /// </remarks>
    public bool TryColdStartAsync<T>(DbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        result = null;
        return false;
    }
    /// <inheritdoc cref="TryColdStartAsync{T}(DbCommand, ICacheGivingParser{T}, bool, CancellationToken, out Task{T})"/>
    public bool TryColdStartAsync<T>(IDbCommand cmd, ICacheGivingParser<T> cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<T> result) {
        result = null;
        return false;
    }
    public bool TryColdStart(Type type, DbCommand cmd, ICacheGivingParser cache, bool disposeCommand, [MaybeNullWhen(false)] out object? result) {
        result = null;
        return false;
    }
    public bool TryColdStart(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, [MaybeNullWhen(false)] out object? result) {
        result = null;
        return false;
    }
    public bool TryColdStartAsync(Type type, DbCommand cmd, ICacheGivingParser cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<object?>? result) {
        result = null;
        return false;
    }
    public bool TryColdStartAsync(Type type, IDbCommand cmd, ICacheGivingParser cache, bool disposeCommand, CancellationToken ct, [MaybeNullWhen(false)] out Task<object?>? result) {
        result = null;
        return false;
    }
}

/// <summary>
/// Tests whether a parser produced by a maker can serve another schema. The parser cache knows only this
/// capability. the maker remains responsible for deciding what makes two generated parsers equivalent.
/// </summary>
public interface ITypeParserSchemaMatcher {
    /// <summary>Returns whether <paramref name="parser"/> accepts <paramref name="schema"/>.</summary>
    bool CanParse<T>(ITypeParser<T> parser, ColumnInfo[] schema);
}
