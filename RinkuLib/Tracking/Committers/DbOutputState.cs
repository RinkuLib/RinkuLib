using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Owns one database commit and its typed, per-item staging values.</summary>
public readonly struct DbOutputState<TItem, TOutput>(DbConnection connection, IsolationLevel? isolationLevel = null, int? timeout = null) : IDisposable {
    /// <summary>Gets the database commit state.</summary>
    public DbCommitState Database { get; } = new(connection, isolationLevel, timeout);
    internal List<(TItem Item, TOutput Output)> Outputs { get; } = [];

    /// <summary>Stages parameters without changing the item's accepted state.</summary>
    public void Stage(TItem item, TOutput parameters) => Outputs.Add((item, parameters));

    /// <summary>Discards staged values without changing tracking or the database transaction.</summary>
    public void Reset() => Outputs?.Clear();

    /// <inheritdoc/>
    public void Dispose() {
        Reset();
        Database.Dispose();
    }
}
