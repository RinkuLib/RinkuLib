using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// The driver for a multi-row parse. It allocates the emitted state struct once per group, folds rows into it
/// through <see cref="IMultiRowState{T}.Read"/>, owns the reader advance so the emitted state carries no loop,
/// and materialises the result with <see cref="IMultiRowState{T}.Build"/>. <typeparamref name="TState"/> is the
/// emitted struct, reached through the <see cref="IMultiRowState{T}"/> constraint so its calls are unboxed.
/// </summary>
public class MultiRowTypeParser<T, TState> : BaseTypeParser<T>
    where TState : IMultiRowState<T>, new() {
    /// <inheritdoc/>
    public override CommandBehavior Behavior => CommandBehavior.SingleResult;
    /// <inheritdoc/>
    public override T Default() => throw new RinkuNoRowsException();
    /// <inheritdoc/>
    public override (bool CanContinue, T Result) Parse(DbDataReader reader) {
        var state = new TState();
        bool more = true;
        while (state.Read(reader))
            if (!reader.Read()) {
                more = false;
                break;
            }
        return (more, state.Build());
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, T Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var state = new TState();
        bool more = true;
        while (state.Read(reader))
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) {
                more = false;
                break;
            }
        return (more, state.Build());
    }
}

/// <summary>
/// The driver for a multi-row parse whose result is a collection at the top of the query. It is the ordinary
/// multi-row driver except that no rows give an empty collection, built from the state's empty buffers, rather
/// than throwing, matching how <see cref="ListTypeParser{T}"/> reads a top-level list.
/// </summary>
public sealed class MultiRowCollectionTypeParser<T, TState> : MultiRowTypeParser<T, TState>
    where TState : IMultiRowState<T>, new() {
    /// <inheritdoc/>
    public override T Default() => new TState().Build();
}
