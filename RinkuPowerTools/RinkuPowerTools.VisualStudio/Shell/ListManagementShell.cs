using System.Runtime.Serialization;
using System.Windows;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools.VisualStudio.Shell;

[DataContract]
public abstract class ListManagementShell<T> : NotifyPropertyChangedObject {
    protected const int NewWorkspaceIndex = int.MaxValue;

    private readonly Debouncer _filterDebouncer;
    private IReadOnlyList<T> _sourceItems;

    private string _filterText = string.Empty;
    private int _displayIndex = -1;
    protected int _workspaceIndex = -1;

    protected ListManagementShell(int debounceMs = 250) {
        _sourceItems = [];
        FilteredItems = [];
        _filterDebouncer = new Debouncer(debounceMs, ApplyFilter);
        NewCommand = new AsyncCommand(OpenNewWorkspaceAsync);
        CommitCommand = new AsyncCommand(CommitAsync);
        DeleteCommand = new AsyncCommand(DeleteAsync);
        CancelCommand = new AsyncCommand(CancelAsync);
    }
    protected void Init(IReadOnlyList<T> sourceItems) {
        _sourceItems = sourceItems ?? throw new ArgumentNullException(nameof(sourceItems));
        ApplyFilter();
    }

    [DataMember] public BulkObservableCollection<DisplayItem<T>> FilteredItems { get; }

    [DataMember]
    public string FilterText {
        get => _filterText;
        set {
            if (_filterText == value)
                return;
            _filterText = value;
            _filterDebouncer.Invoke();
        }
    }

    [DataMember]
    public int DisplayIndex {
        get => _displayIndex;
        set {
            if (_displayIndex == value)
                return;

            _ = OnDisplayIndexChangedAsync(value);
        }
    }
    [DataMember] public bool IsWorkspaceVisible => _workspaceIndex >= 0;
    [DataMember] public bool IsNewWorkspace => _workspaceIndex == NewWorkspaceIndex;
    [DataMember] public virtual Visibility DeleteVisible => Visibility.Collapsed;
    [DataMember] public bool IsCommitEnabled => CanCommit();
    [DataMember] public virtual string NewText => "+ Add";
    [DataMember] public virtual string CommitText => "Save";
    [DataMember] public virtual string DeleteText => "Delete";
    [DataMember] public virtual string CancelText => "Cancel";
    [DataMember] public IAsyncCommand NewCommand { get; }
    [DataMember] public IAsyncCommand CommitCommand { get; }
    [DataMember] public IAsyncCommand DeleteCommand { get; }
    [DataMember] public IAsyncCommand CancelCommand { get; }
    protected abstract bool MatchesFilter(T item);
    protected abstract void LoadWorkspace(T? item);
    public abstract Task CommitAsync(object? arg, CancellationToken ct = default);
    protected virtual bool CanCommit() => HasUnsavedChanges();
    protected abstract bool HasUnsavedChanges();
    protected virtual string GetDisplayDescription(T item) => item?.ToString() ?? string.Empty;
    protected virtual Task DeleteAsync(object? arg, CancellationToken ct = default) => Task.CompletedTask;
    protected virtual Task<bool> ConfirmDiscardAsync(CancellationToken ct = default) => Task.FromResult(true);
    protected T? GetWorkspaceItem() {
        if (_workspaceIndex < 0 || _workspaceIndex >= _sourceItems.Count)
            return default;
        return _sourceItems[_workspaceIndex];
    }
    protected void RefreshShellState() {
        RaiseNotifyPropertyChangedEvent(nameof(IsWorkspaceVisible));
        RaiseNotifyPropertyChangedEvent(nameof(IsNewWorkspace));
        RaiseNotifyPropertyChangedEvent(nameof(DeleteVisible));
        RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
    }
    public Task OpenNewWorkspaceAsync(object? arg, CancellationToken ct = default)
        => TryChangeWorkspaceIndexAsync(NewWorkspaceIndex, ct);
    public async Task CancelAsync(object? arg, CancellationToken ct = default) {
        if (_workspaceIndex >= 0 && HasUnsavedChanges() && !await ConfirmDiscardAsync(ct))
            return;
        _workspaceIndex = -1;
        RefreshShellState();
        _displayIndex = -1;
        RaiseNotifyPropertyChangedEvent(nameof(DisplayIndex));
    }
    public async Task<bool> TryChangeWorkspaceIndexAsync(int workspaceIndex, CancellationToken ct = default) {
        if ((workspaceIndex < 0 || workspaceIndex >= _sourceItems.Count)
            && workspaceIndex != NewWorkspaceIndex)
            return false;
        if (_workspaceIndex >= 0 && HasUnsavedChanges() && !await ConfirmDiscardAsync(ct))
            return false;
        if (workspaceIndex == NewWorkspaceIndex) {
            _displayIndex = -1;
            RaiseNotifyPropertyChangedEvent(nameof(DisplayIndex));
            LoadWorkspace(default);
        }
        else {
            LoadWorkspace(_sourceItems[workspaceIndex]);
        }
        _workspaceIndex = workspaceIndex;
        RefreshShellState();
        return true;
    }
    protected void ApplyFilter() {
        RaiseNotifyPropertyChangedEvent(nameof(FilterText));
        FilteredItems.PauseListening();

        var workspaceItem = GetWorkspaceItem();
        FilteredItems.Clear();
        int displayIndex = -1;
        for (int i = 0; i < _sourceItems.Count; i++) {
            var item = _sourceItems[i];
            if (!MatchesFilter(item))
                continue;
            int index = FilteredItems.Count;
            FilteredItems.Add(new DisplayItem<T>(item, GetDisplayDescription(item)));

            if (workspaceItem is not null &&
                EqualityComparer<T>.Default.Equals(item, workspaceItem))
                displayIndex = index;
        }

        FilteredItems.ResumeListening();
        if (displayIndex != _displayIndex) {
            _displayIndex = displayIndex;
            RaiseNotifyPropertyChangedEvent(nameof(DisplayIndex));
        }
    }
    private async Task OnDisplayIndexChangedAsync(int newDisplayIndex, CancellationToken ct = default) {

        if (newDisplayIndex < 0 || newDisplayIndex >= FilteredItems.Count)
            return;

        var item = FilteredItems[newDisplayIndex].Value;
        for (int i = 0; i < _sourceItems.Count; i++) {
            if (!EqualityComparer<T>.Default.Equals(_sourceItems[i], item))
                continue;

            if (await TryChangeWorkspaceIndexAsync(i, ct))
                _displayIndex = newDisplayIndex;
            RaiseNotifyPropertyChangedEvent(nameof(DisplayIndex));

            return;
        }
    }
}