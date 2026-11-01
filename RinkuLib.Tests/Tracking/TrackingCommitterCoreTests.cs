using Rinku.Tracking;
using Rinku.Tracking.Committers;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public sealed class TrackingCommitterCoreTests
{
    [Fact]
    public void Core_executes_changed_items_then_deletes_and_runs_after_confirm()
    {
        var calls = new List<string>();
        var changed = new CommitRow("changed", changed: true);
        var deleted = new CommitRow("deleted");
        var added = new CommitRow("added");
        var tracking = new TrackingList<CommitRow>([changed, deleted]);
        tracking.Remove(deleted);
        tracking.Add(added);
        using IDisposable subscription = tracking.SubscribeConfirmations((_, item) => calls.Add($"confirm-insert:{item.Name}"), (_, item) => calls.Add($"confirm-update:{item.Name}"), item => calls.Add($"confirm-delete:{item.Name}"));
        var committer = new ProbeCommitter(calls);

        Assert.True(committer.Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "update:changed", "insert:added", "delete:deleted", "commit", "confirm-delete:deleted", "confirm-update:changed", "confirm-insert:added", "after-confirm", "dispose" }, calls);
        Assert.False(tracking.HasChanges);
    }

    [Fact]
    public void Prevalidation_checks_every_changed_item_before_state_is_created()
    {
        var calls = new List<string>();
        var invalid = new CommitRow("invalid", changed: true) { IsValid = false };
        var valid = new CommitRow("valid", changed: true);
        var tracking = new TrackingList<CommitRow>([invalid, valid]);

        Assert.False(new ProbeCommitter(calls).Commit(new object(), tracking));

        Assert.Equal(1, invalid.ValidationCalls);
        Assert.Equal(1, valid.ValidationCalls);
        Assert.Empty(calls);
    }

    [Fact]
    public void Rejected_results_are_accumulated_then_fail_the_state()
    {
        var calls = new List<string>();
        var invalid = new CommitRow("invalid", changed: true) { IsResultValid = false };
        var valid = new CommitRow("valid", changed: true);
        var tracking = new TrackingList<CommitRow>([invalid, valid]);

        Assert.False(new ProbeCommitter(calls).Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "update:invalid", "update:valid", "fail", "dispose" }, calls);
        Assert.True(invalid.HasPendingChange);
        Assert.True(valid.HasPendingChange);
    }

    [Fact]
    public void Explicit_result_validation_does_not_imply_result_storage()
    {
        var row = new CommitRow("row", changed: true);
        var tracking = new TrackingList<CommitRow>([row]);

        Assert.True(new ProbeCommitter([]).Commit(new object(), tracking));

        Assert.Equal(1, row.ResultValidationCalls);
        Assert.Equal(0, row.MetadataWrites);
    }

    [Fact]
    public void Core_does_not_implicitly_store_operation_results()
    {
        var row = new MetadataRow();
        var tracking = new TrackingList<MetadataRow>
        {
            row
        };

        Assert.True(new ProbeCommitter([]).Commit(new object(), tracking));

        Assert.Null(row.Metadata);
    }

    [Fact]
    public void Unsupported_work_is_rejected_before_state_is_created()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("added")
        };

        Assert.Throws<NotSupportedException>(() => new ProbeCommitter(calls, canInsert: false).Commit(new object(), tracking));
        Assert.Empty(calls);
    }

    [Fact]
    public void Execution_exception_fails_and_disposes_the_state_then_propagates()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("throw")
        };

        Assert.Throws<CommitException>(() => new ProbeCommitter(calls) { InsertError = new CommitException() }.Commit(new object(), tracking));
        Assert.Equal(new[] { "start", "insert:throw", "fail", "dispose" }, calls);
    }

    [Fact]
    public void Commit_rejection_fails_and_disposes_the_state_without_confirmation()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("added")
        };

        Assert.False(new ProbeCommitter(calls) { CommitResult = false }.Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "insert:added", "commit", "fail", "dispose" }, calls);
        Assert.True(tracking.IsAddedAt(0));
    }

    [Fact]
    public void Commit_exception_fails_and_disposes_the_state_then_propagates()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("added")
        };

        Assert.Throws<CommitException>(() => new ProbeCommitter(calls) { CommitError = new CommitException() }.Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "insert:added", "commit", "fail", "dispose" }, calls);
        Assert.True(tracking.IsAddedAt(0));
    }

    [Fact]
    public void Confirmation_rejection_does_not_fail_committed_state_or_run_after_confirm()
    {
        var calls = new List<string>();
        var row = new CommitRow("added") { ConfirmResult = false };
        var tracking = new TrackingList<CommitRow>
        {
            row
        };

        Assert.False(new ProbeCommitter(calls).Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "insert:added", "commit", "dispose" }, calls);
        Assert.True(tracking.IsAddedAt(0));
    }

    [Fact]
    public void Before_confirm_runs_after_commit_and_before_tracking_confirmation()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("added")
        };
        using IDisposable subscription = tracking.SubscribeConfirmations(onAdded: (_, item) => calls.Add($"confirm:{item.Name}"));

        Assert.True(new ProbeCommitter(calls) { RecordBeforeConfirm = true }.Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "insert:added", "commit", "before-confirm", "confirm:added", "after-confirm", "dispose" }, calls);
    }

    [Fact]
    public void After_confirm_exception_is_propagated_after_tracking_was_confirmed()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>
        {
            new("added")
        };

        Assert.Throws<CommitException>(() => new ProbeCommitter(calls) { AfterConfirmError = new CommitException() }.Commit(new object(), tracking));

        Assert.Equal(new[] { "start", "insert:added", "commit", "after-confirm", "dispose" }, calls);
        Assert.False(tracking.HasChanges);
    }

    [Fact]
    public void No_work_does_not_create_state_or_require_operations()
    {
        var calls = new List<string>();
        var tracking = new TrackingList<CommitRow>([new CommitRow("unchanged")]);

        Assert.True(new ProbeCommitter(calls, canInsert: false, canUpdate: false, canDelete: false).Commit(new object(), tracking));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Single_item_commit_confirms_only_after_persistence(bool insert)
    {
        var calls = new List<string>();
        var item = new IndependentRow(insert);
        ITrackingCommitter<object> committer = new ProbeCommitter(calls) { RecordBeforeConfirm = true };
        Assert.True(committer.CommitItem(new object(), item));
        Assert.False(item.IsNew);
        Assert.False(item.HasPendingChange);
        Assert.Equal(new[] { "start", insert ? "insert:single" : "update:single", "commit", "before-confirm", "after-confirm", "dispose" }, calls);
        calls.Clear();
        Assert.True(committer.CommitItem(new object(), item));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Single_item_validation_and_transaction_rejection_preserve_tracking(bool invalid)
    {
        var calls = new List<string>();
        var item = new IndependentRow(true) { IsValid = !invalid };
        Assert.False(new ProbeCommitter(calls) { CommitResult = false }.CommitItem(new object(), item));
        Assert.True(item.IsNew);
        Assert.True(item.HasPendingChange);
        Assert.Equal(invalid ? Array.Empty<string>() : new[] { "start", "insert:single", "commit", "fail", "dispose" }, calls);
    }

    [Fact]
    public void Single_item_result_rejection_rolls_back_without_confirmation()
    {
        var calls = new List<string>();
        var item = new IndependentRow(true) { IsResultValid = false };
        Assert.False(new ProbeCommitter(calls).CommitItem(new object(), item));
        Assert.True(item.IsNew);
        Assert.Equal(new[] { "start", "insert:single", "fail", "dispose" }, calls);
    }

    private sealed class IndependentRow(bool isNew) : CommitRow("single", changed: true), ITrackingConfirmationSource
    {
        public bool IsNew { get; private set; } = isNew;
        public event TrackingConfirmationHandler? Confirmed;
        public bool ConfirmInsert()
        {
            if (!ConfirmEdit()) return false;
            IsNew = false;
            Confirmed?.Invoke(this, TrackingConfirmation.Insert);
            return true;
        }
        public bool ConfirmUpdate()
        {
            if (!ConfirmEdit()) return false;
            Confirmed?.Invoke(this, TrackingConfirmation.Update);
            return true;
        }
    }

    private sealed class ProbeCommitter : TrackingCommitterCore<object, ProbeState, string, string, string>
    {
        private readonly List<string> _calls;
        private readonly bool _canInsert;
        private readonly bool _canUpdate;
        private readonly bool _canDelete;

        public ProbeCommitter(List<string> calls, bool canInsert = true, bool canUpdate = true, bool canDelete = true)
        {
            _calls = calls;
            _canInsert = canInsert;
            _canUpdate = canUpdate;
            _canDelete = canDelete;
        }

        public Exception? InsertError { get; init; }
        public bool CommitResult { get; init; } = true;
        public Exception? CommitError { get; init; }
        public Exception? AfterConfirmError { get; init; }
        public bool RecordBeforeConfirm { get; init; }

        protected override bool ValidateItem<TItem>(TItem item) => item is not IValidatable validation || validation.Validate();
        protected override bool HandleInsertResult<TItem>(TItem item, string result) => item is not IResultValidatable<string> validation || validation.ValidateResult(result);
        protected override bool HandleUpdateResult<TItem>(TItem item, string result) => item is not IResultValidatable<string> validation || validation.ValidateResult(result);
        protected override bool HandleDeleteResult<TItem>(TItem item, string result) => item is not IResultValidatable<string> validation || validation.ValidateResult(result);
        protected override bool CanInsert => _canInsert;
        protected override bool CanUpdate => _canUpdate;
        protected override bool CanDelete => _canDelete;
        protected override ProbeState StartCommit(object connection) { _calls.Add("start"); return new(_calls); }
        protected override string Insert<TItem>(TItem item, ProbeState state) { string name = Name(item); _calls.Add($"insert:{name}"); if (InsertError is not null) throw InsertError; return "insert"; }
        protected override string Update<TItem>(TItem item, ProbeState state) { string name = Name(item); _calls.Add($"update:{name}"); return "update"; }
        protected override string Delete<TItem>(TItem item, ProbeState state) { string name = Name(item); _calls.Add($"delete:{name}"); return "delete"; }
        protected override bool CommitState(ProbeState state) { _calls.Add("commit"); if (CommitError is not null) throw CommitError; return CommitResult; }
        protected override void FailCommit(ProbeState state) => _calls.Add("fail");
        protected override void BeforeConfirm(ProbeState state) { if (RecordBeforeConfirm) _calls.Add("before-confirm"); }
        protected override void AfterConfirm(ProbeState state) { _calls.Add("after-confirm"); if (AfterConfirmError is not null) throw AfterConfirmError; }

        private static string Name<TItem>(TItem item) => item switch { CommitRow row => row.Name, MetadataRow => "metadata", _ => "unknown" };
    }

    private sealed class ProbeState(List<string> calls) : IDisposable
    {
        public void Dispose() => calls.Add("dispose");
    }

    private class CommitRow(string name, bool changed = false) : IEditable, ICanDelete, ITrackingChanges, IValidatable, IResultValidatable<string>
    {
        public string Name { get; } = name;
        public bool HasPendingChange { get; private set; } = changed;
        public bool IsValid { get; set; } = true;
        public bool IsResultValid { get; set; } = true;
        public bool ConfirmResult { get; set; } = true;
        public int ValidationCalls { get; private set; }
        public int ResultValidationCalls { get; private set; }
        public int MetadataWrites { get; private set; }
        public bool IsEditing => HasPendingChange;
        public int TrackedMemberCount => 1;
        public bool EnsureEditing() { HasPendingChange = true; return true; }
        public bool ConfirmEdit() { if (ConfirmResult) HasPendingChange = false; return ConfirmResult; }
        public bool CancelEdit() { HasPendingChange = false; return true; }
        public bool ConfirmDelete() => ConfirmResult;
        public void SetMetadata(string metadata) => MetadataWrites++;
        public bool Validate() { ValidationCalls++; return IsValid; }
        public bool ValidateResult(string result) { ResultValidationCalls++; return IsResultValid; }
        public bool TryGetChange(int memberIndex, out TrackingChange change)
        {
            if (memberIndex == 0 && HasPendingChange)
            {
                change = new(nameof(Name), Name, Name);
                return true;
            }

            change = default;
            return false;
        }
    }

    private sealed class MetadataRow : IEditable
    {
        public string? Metadata { get; private set; }
        public bool IsEditing => false;
        public bool EnsureEditing() => true;
        public bool ConfirmEdit() => true;
        public bool CancelEdit() => true;
        public void SetMetadata(string metadata) => Metadata = metadata;
    }

    private sealed class CommitException : Exception { }
}
