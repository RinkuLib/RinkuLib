namespace Rinku.Tracking.Committers;

/// <summary>Commits tracked items using a supplied database connection.</summary>
public interface ITrackingCommitter<in TConnection>
{
    /// <summary>Commits one item and returns whether the operation succeeded.</summary>
    bool CommitItem<TItem>(TConnection connection, TItem item) where TItem : ITrackingConfirmationSource;
    /// <summary>Commits all eligible items from a tracking source.</summary>
    bool Commit<TItem>(TConnection connection, ITrackingCommitSource<TItem> tracking);
}
