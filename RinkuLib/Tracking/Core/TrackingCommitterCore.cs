namespace Rinku.Tracking.Committers;

/// <summary>Coordinates the common commit steps for item-wise tracking.</summary>
public abstract class TrackingCommitterCore<TConnection, TState, TInsertResult, TUpdateResult, TDeleteResult> : ITrackingCommitter<TConnection> where TState : IDisposable
{
    /// <summary>Executes pending operations and confirms the tracking list when the commit succeeds.</summary>
    /// <remarks>A return value of <see langword="false"/> leaves pending tracking changes in place.</remarks>
    public bool Commit<TItem>(TConnection connection, ITrackingCommitSource<TItem> tracking)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tracking);

        bool hasWork = false;

        foreach ((TItem _, bool isInsert) in tracking.GetChanged())
        {
            hasWork = true;

            if (isInsert && !CanInsert)
                throw new NotSupportedException("Insert is not supported.");

            if (!isInsert && !CanUpdate)
                throw new NotSupportedException("Update is not supported.");
        }

        if (tracking.RemovedSpan.Length != 0)
        {
            hasWork = true;

            if (!CanDelete)
                throw new NotSupportedException("Delete is not supported.");
        }

        if (!hasWork)
            return true;

        if (!ValidatePending(tracking))
            return false;

        using TState state = StartCommit(connection);
        try {

            bool valid = true;

            try
            {
                foreach ((TItem item, bool isInsert) in tracking.GetChanged())
                {
                    if (isInsert)
                    {
                        TInsertResult result = Insert(item, state);

                        if (!HandleInsertResult(item, result))
                            valid = false;
                    }
                    else
                    {
                        TUpdateResult result = Update(item, state);

                        if (!HandleUpdateResult(item, result))
                            valid = false;
                    }
                }

                foreach (TItem item in tracking.RemovedSpan)
                {
                    TDeleteResult result = Delete(item, state);

                    if (!HandleDeleteResult(item, result))
                        valid = false;
                }
            }
            catch
            {
                FailCommit(state);
                throw;
            }

            if (!valid)
            {
                FailCommit(state);
                return false;
            }

            if (!CompleteState(state))
                return false;

            BeforeConfirm(state);

            if (!tracking.ConfirmChanges())
                return false;

            AfterConfirm(state);
            return true;
        }
        finally {
            foreach (var (pending, _) in tracking.GetChanged()) ResetItemParameters(pending);
            foreach (var pending in tracking.RemovedSpan) ResetItemParameters(pending);
        }
    }

    /// <summary>Persists one independent tracking item and confirms it after persistence succeeds.</summary>
    public bool CommitItem<TItem>(TConnection connection, TItem item) where TItem : ITrackingConfirmationSource
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        bool insert = item.IsNew;
        if (!insert && (item is not ITrackingChanges changes || !changes.HasChanges()))
            return true;
        if (insert && !CanInsert)
            throw new NotSupportedException("Insert is not supported.");
        if (!insert && !CanUpdate)
            throw new NotSupportedException("Update is not supported.");
        if (!ValidateItem(item))
            return false;

        using TState state = StartCommit(connection);
        try {
            bool valid;
            try
            {
                valid = insert ? HandleInsertResult(item, Insert(item, state)) : HandleUpdateResult(item, Update(item, state));
            }
            catch
            {
                FailCommit(state);
                throw;
            }
            if (!valid)
            {
                FailCommit(state);
                return false;
            }
            if (!CompleteState(state))
                return false;
            BeforeConfirm(state);
            if (!(insert ? item.ConfirmInsert() : item.ConfirmUpdate()))
                return false;
            AfterConfirm(state);
            return true;
        }
        finally { ResetItemParameters(item); }
    }

    protected virtual void ResetItemParameters<TItem>(TItem item) { }

    private bool CompleteState(TState state)
    {
        bool committed;

        try
        {
            committed = CommitState(state);
        }
        catch
        {
            FailCommit(state);
            throw;
        }

        if (!committed)
        {
            FailCommit(state);
            return false;
        }

        return true;
    }

    /// <summary>Gets whether insertion is supported.</summary>
    protected virtual bool CanInsert => true;
    /// <summary>Gets whether updating is supported.</summary>
    protected virtual bool CanUpdate => true;
    /// <summary>Gets whether deletion is supported.</summary>
    protected virtual bool CanDelete => true;

    /// <summary>Creates the local state for one commit.</summary>
    protected abstract TState StartCommit(TConnection connection);
    /// <summary>Executes one insertion and returns its persistence result.</summary>
    protected abstract TInsertResult Insert<TItem>(TItem item, TState state);
    /// <summary>Executes one update and returns its persistence result.</summary>
    protected abstract TUpdateResult Update<TItem>(TItem item, TState state);
    /// <summary>Executes one deletion and returns its persistence result.</summary>
    protected abstract TDeleteResult Delete<TItem>(TItem item, TState state);

    /// <summary>Validates pending insertions and updates before commit state is created.</summary>
    protected virtual bool ValidatePending<TItem>(ITrackingCommitSource<TItem> tracking)
    {
        bool valid = true;

        foreach ((TItem item, bool _) in tracking.GetChanged())
        {
            if (!ValidateItem(item))
                valid = false;
        }

        return valid;
    }

    /// <summary>Validates an item before commit state is created.</summary>
    protected virtual bool ValidateItem<TItem>(TItem item) => true;

    /// <summary>Handles an insertion result.</summary>
    protected virtual bool HandleInsertResult<TItem>(TItem item, TInsertResult result) => true;
    /// <summary>Handles an update result.</summary>
    protected virtual bool HandleUpdateResult<TItem>(TItem item, TUpdateResult result) => true;
    /// <summary>Handles a deletion result.</summary>
    protected virtual bool HandleDeleteResult<TItem>(TItem item, TDeleteResult result) => true;

    /// <summary>Completes the commit state after all operation results are accepted.</summary>
    protected virtual bool CommitState(TState state) => true;
    /// <summary>Rolls back or otherwise abandons the commit state after an operation is rejected or throws.</summary>
    protected virtual void FailCommit(TState state) { }
    /// <summary>Runs after the commit state succeeds and before tracking confirmation.</summary>
    protected virtual void BeforeConfirm(TState state) { }
    /// <summary>Runs after persistence and tracking confirmation both succeed.</summary>
    protected virtual void AfterConfirm(TState state) { }
}
