using System.Data;
using System.Data.Common;
using System.Reflection;
using Rinku.Mapping.Emission;

namespace Rinku.Mapping.Parsers.Defaults;

/// <summary>
/// The driver for a multi-row parse. It allocates the emitted state struct once per group, folds rows into it
/// through <see cref="IMultiRowState{T}.Read"/>, owns the reader advance so the emitted state carries no loop,
/// and materialises the result with <see cref="IMultiRowState{T}.Build"/>. <typeparamref name="TState"/> is the
/// emitted struct, reached through the <see cref="IMultiRowState{T}"/> constraint so its calls are unboxed.
/// </summary>
public class MultiRowTypeParser<T, TState> : BaseTypeParser<T>
    where TState : IMultiRowState<T>, new() {
    private readonly ColumnInfo[] Schema;
    private readonly CommandBehavior ReaderBehavior;
    private int Disposed;
    /// <summary>Creates a driver with the conservative multi-result behavior.</summary>
    public MultiRowTypeParser(ColumnInfo[] schema) : this(schema, CommandBehavior.SingleResult) { }
    /// <summary>Creates a driver carrying the reader behavior negotiated from the emitted plan.</summary>
    public MultiRowTypeParser(ColumnInfo[] schema, CommandBehavior behavior) {
        Schema = schema;
        ReaderBehavior = (behavior | CommandBehavior.SingleResult) & ~CommandBehavior.SingleRow;
    }
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) => schema.EquivalentTo(Schema);
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ReaderBehavior;
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
    public override ValueTask<(bool CanContinue, T Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var state = new TState();
        while (state.Read(reader)) {
            var pending = reader.ReadAsync(ct);
            if (!pending.IsCompletedSuccessfully)
                return ContinueAsync(pending, reader, state, ct);
            if (!pending.Result)
                return new((false, state.Build()));
        }
        return new((true, state.Build()));
    }
    private static async ValueTask<(bool CanContinue, T Result)> ContinueAsync(Task<bool> pending, DbDataReader reader, TState state, CancellationToken ct) {
        if (!await pending.ConfigureAwait(false))
            return (false, state.Build());
        while (state.Read(reader))
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return (false, state.Build());
        return (true, state.Build());
    }
    /// <inheritdoc/>
    public override void Dispose() {
        if (Interlocked.Exchange(ref Disposed, 1) != 0)
            return;
        var fields = typeof(TState).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        List<IGeneratedParserTarget> targets = [];
        for (int i = 0; i < fields.Length; i++) {
            if (fields[i].FieldType != typeof(object) || fields[i].GetValue(null) is not IGeneratedParserTarget target)
                continue;
            fields[i].SetValue(null, null);
            bool found = false;
            for (int j = 0; j < targets.Count; j++)
                if (ReferenceEquals(targets[j], target)) {
                    found = true;
                    break;
                }
            if (!found)
                targets.Add(target);
        }
        for (int i = 0; i < targets.Count; i++)
            targets[i].Dispose();
    }
}

/// <summary>
/// The driver for a multi-row parse whose result is a collection at the top of the query. It is the ordinary
/// multi-row driver except that no rows give an empty collection, built from the state's empty buffers, rather
/// than throwing, matching how <see cref="ListTypeParser{T}"/> reads a top-level list.
/// </summary>
public class MultiRowCollectionTypeParser<T, TState> : MultiRowTypeParser<T, TState>
    where TState : IMultiRowState<T>, new() {
    /// <summary>Creates a collection driver with the conservative multi-result behavior.</summary>
    public MultiRowCollectionTypeParser(ColumnInfo[] schema) : base(schema) { }
    /// <summary>Creates a collection driver carrying the reader behavior negotiated from the emitted plan.</summary>
    public MultiRowCollectionTypeParser(ColumnInfo[] schema, CommandBehavior behavior) : base(schema, behavior) { }
    /// <inheritdoc/>
    public override T Default() => new TState().Build();
}
