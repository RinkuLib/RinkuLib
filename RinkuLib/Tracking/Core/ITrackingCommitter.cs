namespace Rinku.Tracking.Committers;

public interface ITrackingCommitter<in TConnection>
{
    bool CommitItem<TItem>(TConnection connection, TItem item) where TItem : ITrackingConfirmationSource;
    bool Commit<TItem>(TConnection connection, ITrackingCommitSource<TItem> tracking);
}
