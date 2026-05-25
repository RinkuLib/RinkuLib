using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.Extensibility.UI;
using RinkuPowerTools.Core;

namespace RinkuPowerTools;

[DataContract]
public class QueryManagerControlData : NotifyPropertyChangedObject {
    private CancellationTokenSource? _validationCts;
    private CancellationTokenSource? _filterCts;
    private readonly string _projectDirectory;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly ExtensionSettings _settings;

    private string _filterText = string.Empty;
    private int _currentIndex = -1;

    private string _editMethodName = string.Empty;
    private string _editTarget = string.Empty;
    private QuerySourceType _editTargetType = QuerySourceType.Text;

    private bool _hasMethodNameError;
    private bool _hasTargetError;
    private bool _isValid;
    private int _pendingIndex = -1;

    public QueryManagerControlData(VisualStudioExtensibility extensibility, string projectDirectory, ExtensionSettings settings) {
        _projectDirectory = projectDirectory;
        _extensibility = extensibility;
        _settings = settings;

        FilteredQueries = [.._settings.Queries.Select(q => new DisplayItem<QuerySetting>(q, q.MethodName))];
        StoredProcedureSuggestions = [];
        EditParameters = [];

        AddQueryCommand = new AsyncCommand(AddQueryAsync);
        UpdateQueryCommand = new AsyncCommand(UpdateQueryAsync);
        CancelQueryCommand = new AsyncCommand(CancelQueryAsync);
        DeleteQueryCommand = new AsyncCommand(DeleteQueryAsync);
        AddParameterCommand = new AsyncCommand(AddParameterAsync);
        DeleteParameterCommand = new AsyncCommand(DeleteParameterAsync);
        BrowseSQLFileCommand = new AsyncCommand(BrowseSQLFileAsync);

        _ = LoadStoredProceduresAsync();
    }

    #region Data Members (Properties)

    [DataMember]
    public BulkObservableCollection<DisplayItem<QuerySetting>> FilteredQueries { get; }

    [DataMember]
    public BulkObservableCollection<DisplayItem<string>> StoredProcedureSuggestions { get; }
    [DataMember]
    public BulkObservableCollection<EditParameterData> EditParameters { get; }
    [DataMember]
    public List<string> ParameterTypeSuggestions { get; } = [
    "bigint", "binary", "bit", "char", "date", "datetime", "datetime2",
    "decimal", "float", "image", "int", "money", "nchar", "ntext",
    "numeric", "nvarchar", "real", "smalldatetime", "smallint",
    "smallmoney", "text", "time", "timestamp", "tinyint",
    "uniqueidentifier", "varbinary", "varchar", "xml"
];
    [DataMember]
    public string FilterText {
        get => _filterText;
        set {
            if (_filterText != value) {
                _filterText = value;
                DebounceFilter();
            }
        }
    }

    [DataMember]
    public int CurrentIndex {
        get => _currentIndex;
        set => _ = SetCurrentIndexAsync(value);
    }

    [DataMember]
    public string EditMethodName {
        get => _editMethodName;
        set {
            if (_editMethodName != value) {
                _editMethodName = value;
                DebounceValidation();
            }
        }
    }

    [DataMember]
    public string EditTarget {
        get => _editTarget;
        set {
            if (_editTarget != value) {
                _editTarget = value;
                DebounceValidation();
            }
        }
    }

    [DataMember]
    public QuerySourceType EditTargetType {
        get => _editTargetType;
        set {
            if (SetProperty(ref _editTargetType, value))
                RunValidation();
        }
    }

    private void DebounceValidation() {
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        _ = Task.Delay(300, token).ContinueWith(t => {
            if (!t.IsCanceled) {
                RaiseNotifyPropertyChangedEvent(nameof(EditTarget));
                RaiseNotifyPropertyChangedEvent(nameof(EditMethodName));
                RunValidation();
            }
        }, TaskScheduler.Default);
    }

    private void DebounceFilter() {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        _ = Task.Delay(300, token).ContinueWith(t => {
            if (!t.IsCanceled) {
                RaiseNotifyPropertyChangedEvent(nameof(FilterText));
                ApplyFilter();
            }
        }, TaskScheduler.Default);
    }

    [DataMember] public bool HasMethodNameError { get => _hasMethodNameError; set => SetProperty(ref _hasMethodNameError, value); }
    [DataMember] public bool HasTargetError { get => _hasTargetError; set => SetProperty(ref _hasTargetError, value); }
    [DataMember] public bool IsValid { get => _isValid; set => SetProperty(ref _isValid, value); }

    [DataMember] public bool IsWorkspaceVisible => CurrentIndex >= 0;
    [DataMember] public bool IsDeleteEnabled => CurrentIndex < int.MaxValue;

    public bool IsDirty {
        get {
            if (CurrentIndex == int.MaxValue)
                return !string.IsNullOrEmpty(EditMethodName) ||
                       !string.IsNullOrEmpty(EditTarget);

            if (CurrentIndex < 0 || CurrentIndex >= FilteredQueries.Count)
                return false;

            var target = FilteredQueries[CurrentIndex].Value;
            if (EditMethodName != (target.MethodName ?? string.Empty) ||
                EditTarget != (target.Target ?? string.Empty) ||
                EditTargetType != target.SourceType)
                return true;

            if (EditParameters.Count != target.Parameters.Count)
                return true;

            for (int i = 0; i < EditParameters.Count; i++) {
                if (EditParameters[i].Name != target.Parameters[i].Name ||
                    EditParameters[i].Type != target.Parameters[i].Type ||
                    EditParameters[i].IsNullable != target.Parameters[i].IsNullable)
                    return true;
            }

            return false;
        }
    }

    [DataMember]
    public List<DisplayItem<QuerySourceType>> TargetTypeValues { get; } = [
        new DisplayItem<QuerySourceType>(QuerySourceType.Text, "Text SQL Query"),
        new DisplayItem<QuerySourceType>(QuerySourceType.StoredProcedure, "Stored Procedure"),
        new DisplayItem<QuerySourceType>(QuerySourceType.FromFile, "From file")
    ];

    [DataMember] public IAsyncCommand AddQueryCommand { get; }
    [DataMember] public IAsyncCommand UpdateQueryCommand { get; }
    [DataMember] public IAsyncCommand CancelQueryCommand { get; }
    [DataMember] public IAsyncCommand DeleteQueryCommand { get; }
    [DataMember] public IAsyncCommand AddParameterCommand { get; }
    [DataMember] public IAsyncCommand DeleteParameterCommand { get; }
    [DataMember] public IAsyncCommand BrowseSQLFileCommand { get; }

    #endregion

    #region Core Architecture Operations

    private async Task LoadStoredProceduresAsync() {
        try {
            using var cnn = _settings.GetConnection();
            await cnn.OpenAsync();

            DataTable proceduresTable = await Task.Run(() => cnn.GetSchema("Procedures"));

            var discoveredProcs = new List<string>();
            string? targetColumn = null;

            if (proceduresTable.Columns.Contains("ROUTINE_NAME"))
                targetColumn = "ROUTINE_NAME";
            else if (proceduresTable.Columns.Contains("routine_name"))
                targetColumn = "routine_name";
            else if (proceduresTable.Columns.Contains("name"))
                targetColumn = "name";

            if (targetColumn != null) {
                foreach (DataRow row in proceduresTable.Rows) {
                    string? procName = row[targetColumn]?.ToString();
                    if (!string.IsNullOrWhiteSpace(procName)) {
                        discoveredProcs.Add(procName);
                    }
                }
            }

            discoveredProcs.Sort();
            StoredProcedureSuggestions.PauseListening();
            StoredProcedureSuggestions.Clear();
            foreach (var proc in discoveredProcs)
                StoredProcedureSuggestions.Add(new DisplayItem<string>(proc, proc));
            StoredProcedureSuggestions.ResumeListening();
        }
        catch { }
    }
    private async Task BrowseSQLFileAsync(object? parameter, CancellationToken ct) {
        var options = new FileDialogOptions {
            Title = "Select SQL Script File",
            InitialDirectory = _projectDirectory,
            Filters = new DialogFilters([new("SQL Files", "*.sql")])
        };

        var selectedFilePath = await _extensibility.Shell().ShowOpenFileDialogAsync(options, ct);

        if (!string.IsNullOrWhiteSpace(selectedFilePath)) {
            string relativePath = Path.GetRelativePath(_projectDirectory, selectedFilePath);
            EditTarget = relativePath == "." || relativePath == ".." || string.IsNullOrWhiteSpace(relativePath)
                ? string.Empty : relativePath.Replace('\\', '/');
        }
    }
    private async Task SetCurrentIndexAsync(int targetIndex) {
        if (_pendingIndex >= 0) {
            _currentIndex = _pendingIndex;
            _pendingIndex = -1;
            if (_currentIndex != targetIndex) {
                RaiseNotifyPropertyChangedEvent(nameof(IsWorkspaceVisible));
                RaiseNotifyPropertyChangedEvent(nameof(CurrentIndex));
            }
            return;
        }

        if (_currentIndex == targetIndex)
            return;

        if (IsWorkspaceVisible && IsDirty) {
            bool discardChanges = await _extensibility.Shell().ShowPromptAsync(
                "You have unsaved changes. Discard them?", PromptOptions.OKCancel, CancellationToken.None);

            if (!discardChanges) {
                RaiseNotifyPropertyChangedEvent(nameof(CurrentIndex));
                return;
            }
        }

        _currentIndex = targetIndex;
        SyncSandboxToSelection();
        RaiseNotifyPropertyChangedEvent(nameof(CurrentIndex));
    }

    private bool MatchesFilter(QuerySetting query) {
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return (query.MethodName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (query.Target?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplyFilter() {
        FilteredQueries.PauseListening();
        QuerySetting? currentlySelectedModel = (CurrentIndex >= 0 && CurrentIndex < FilteredQueries.Count)
            ? FilteredQueries[CurrentIndex].Value
            : null;

        FilteredQueries.Clear();
        _pendingIndex = -1;
        foreach (var query in _settings.Queries) {
            if (MatchesFilter(query)) {
                FilteredQueries.Add(new DisplayItem<QuerySetting>(query, query.MethodName));

                if (query == currentlySelectedModel)
                    _pendingIndex = FilteredQueries.Count - 1;
            }
        }

        FilteredQueries.ResumeListening();
        CurrentIndex = _pendingIndex;
        RaiseNotifyPropertyChangedEvent(nameof(IsWorkspaceVisible));
        RaiseNotifyPropertyChangedEvent(nameof(CurrentIndex));
    }

    private void SyncSandboxToSelection() {
        EditParameters.PauseListening();
        EditParameters.Clear();
        if (CurrentIndex >= 0 && CurrentIndex < FilteredQueries.Count) {
            var selected = FilteredQueries[CurrentIndex].Value;
            EditMethodName = selected.MethodName ?? string.Empty;
            EditTarget = selected.Target ?? string.Empty;
            EditTargetType = selected.SourceType;
            foreach (var p in selected.Parameters) {
                EditParameters.Add(new EditParameterData {
                    Name = p.Name,
                    Type = p.Type,
                    IsNullable = p.IsNullable
                });
            }
        }
        else if (CurrentIndex == int.MaxValue) {
            EditMethodName = string.Empty;
            EditTarget = string.Empty;
            EditTargetType = QuerySourceType.Text;
        }
        EditParameters.ResumeListening();
        RunValidation();
        RaiseNotifyPropertyChangedEvent(nameof(EditMethodName));
        RaiseNotifyPropertyChangedEvent(nameof(EditTarget));
        RaiseNotifyPropertyChangedEvent(nameof(IsWorkspaceVisible));
        RaiseNotifyPropertyChangedEvent(nameof(IsDeleteEnabled));
    }
    private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e) => RunValidation();
    private Task AddQueryAsync(object? parameter, CancellationToken ct) {
        CurrentIndex = int.MaxValue;
        return Task.CompletedTask;
    }

    private Task UpdateQueryAsync(object? parameter, CancellationToken ct) {
        RunValidation();
        if (!IsValid)
            return Task.CompletedTask;

        QuerySetting activeItem;
        bool isNewItem = CurrentIndex == int.MaxValue;
        var targetParameters = EditParameters.Select(p => new ParameterOverride {
            Name = p.Name,
            Type = string.IsNullOrWhiteSpace(p.Type) ? null : p.Type,
            IsNullable = p.IsNullable
        }).ToList();

        if (isNewItem) {
            activeItem = new QuerySetting {
                MethodName = EditMethodName,
                Target = EditTarget,
                SourceType = EditTargetType,
                Parameters = targetParameters
            };
            _settings.Queries.Add(activeItem);
        }
        else {
            activeItem = FilteredQueries[CurrentIndex].Value;
            activeItem.MethodName = EditMethodName;
            activeItem.Target = EditTarget;
            activeItem.SourceType = EditTargetType;
            activeItem.Parameters = targetParameters;
        }

        if (isNewItem) {
            if (MatchesFilter(activeItem)) {
                FilteredQueries.Add(new DisplayItem<QuerySetting>(activeItem, activeItem.MethodName));
                CurrentIndex = _currentIndex = FilteredQueries.Count - 1;
            }
            else {
                CurrentIndex = _currentIndex = -1;
            }
        }
        else {
            if (MatchesFilter(activeItem)) {
                FilteredQueries[CurrentIndex] = new DisplayItem<QuerySetting>(activeItem, activeItem.MethodName);
            }
            else {
                FilteredQueries.RemoveAt(CurrentIndex);
                CurrentIndex = _currentIndex = -1;
            }
        }
        RaiseNotifyPropertyChangedEvent(nameof(IsWorkspaceVisible));
        RaiseNotifyPropertyChangedEvent(nameof(IsDeleteEnabled));

        return Task.CompletedTask;
    }

    private Task CancelQueryAsync(object? parameter, CancellationToken ct) {
        CurrentIndex = -1;
        return Task.CompletedTask;
    }

    private async Task DeleteQueryAsync(object? parameter, CancellationToken ct) {
        if (CurrentIndex < 0 || CurrentIndex >= FilteredQueries.Count)
            return;

        bool confirmDelete = await _extensibility.Shell().ShowPromptAsync(
            $"Are you sure you want to delete the query '{EditMethodName}'?", PromptOptions.OKCancel, ct);

        if (!confirmDelete)
            return;

        var itemToRemove = FilteredQueries[CurrentIndex];
        _settings.Queries.Remove(itemToRemove.Value);
        FilteredQueries.RemoveAt(CurrentIndex);

        CurrentIndex = -1;
    }
    private Task AddParameterAsync(object? parameter, CancellationToken ct) {
        var newParam = new EditParameterData { Name = "@NewParam", Type = null, IsNullable = null };
        newParam.PropertyChanged += OnParameterPropertyChanged;
        EditParameters.Add(newParam);
        RunValidation();
        return Task.CompletedTask;
    }

    private Task DeleteParameterAsync(object? parameter, CancellationToken ct) {
        if (parameter is EditParameterData targetParam) {
            targetParam.PropertyChanged -= OnParameterPropertyChanged;
            EditParameters.Remove(targetParam);
            RunValidation();
        }
        return Task.CompletedTask;
    }

    private void RunValidation() {
        bool methodOk = !string.IsNullOrWhiteSpace(EditMethodName) && (char.IsLetter(EditMethodName[0]) || EditMethodName[0] == '_');
        HasMethodNameError = !methodOk;

        bool targetOk = !string.IsNullOrWhiteSpace(EditTarget);
        HasTargetError = !targetOk;
        bool paramsOk = EditParameters.All(p => !string.IsNullOrWhiteSpace(p.Name));

        IsValid = methodOk && targetOk && paramsOk && IsDirty;
        RaiseNotifyPropertyChangedEvent(nameof(IsDirty));
    }

    #endregion
}
[DataContract]
public class EditParameterData : NotifyPropertyChangedObject {
    private string _name = string.Empty;
    private string? _type;
    private bool? _isNullable;

    [DataMember]
    public string Name {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [DataMember]
    public string? Type {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [DataMember]
    public bool? IsNullable {
        get => _isNullable;
        set => SetProperty(ref _isNullable, value);
    }
}