using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing; 
/// <summary>
/// Builds the parser for the types it claims, the seam that adds a new result shape. Register one in
/// <see cref="TypeParser.TypeParserMakers"/>, ahead of the defaults, and the engine offers it each
/// <c>T</c> in turn.
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
    /// overrode it, and any <see cref="INullColHandler"/> implementation may arrive here.
    /// </remarks>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser);
    /// <summary>
    /// Runs <typeparamref name="T"/> before its columns are known, or declines and leaves the engine to
    /// open the reader and build a parser first.
    /// </summary>
    /// <param name="cmd">The command to run, not yet run.</param>
    /// <param name="cache">Turns the reader's columns into the parser for <typeparamref name="T"/>.</param>
    /// <param name="disposeCommand">Whether the command is this run's to dispose.</param>
    /// <param name="result">The result, when this maker took the run.</param>
    /// <remarks>
    /// A shape whose rows are read as they are walked needs this, because opening the reader to learn a
    /// parser is what runs the command, and that cannot happen before the walk. Taking the run means
    /// working out what the columns call for as the rows come, so the run stays the ordinary one every
    /// parser takes. Every buffered shape declines and is handed its rows.
    /// <para>
    /// The engine offers a cold run to each maker in turn and takes the first that answers, so no order is
    /// assumed among them. Only a run with no parser yet comes here, which is once per command and shape,
    /// so the walk down the list costs nothing worth counting.
    /// </para>
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
    /// The asynchronous road of <see cref="TryColdStart{T}(DbCommand, ICacheGivingParser{T}, bool, out T)"/>,
    /// its own road rather than the same one under a token, since a shape may have real awaiting to do.
    /// </summary>
    /// <param name="cmd">The command to run, not yet run.</param>
    /// <param name="cache">Turns the reader's columns into the parser for <typeparamref name="T"/>.</param>
    /// <param name="disposeCommand">Whether the command is this run's to dispose.</param>
    /// <param name="ct">The token the caller is running under.</param>
    /// <param name="result">The result, when this maker took the run.</param>
    /// <remarks>
    /// A shape with nothing to await answers with a finished task, which is what a
    /// <see cref="IEnumerable{T}"/> does since its rows are walked synchronously whatever road asked for
    /// them. Declining here and taking the synchronous road are separate answers, so a shape may take one
    /// and not the other.
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
}