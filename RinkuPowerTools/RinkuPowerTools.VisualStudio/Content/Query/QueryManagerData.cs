using System.Data;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.DataContracts;
using System.Windows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using RinkuPowerTools.VisualStudio.Shell;

namespace RinkuPowerTools.VisualStudio.Content.Query;

[DataContract]
public class QueryManagerData : ListManagementShell<QuerySetting> {
    private readonly string _projectDirectory;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly ExtensionSettings _settings;
    private readonly DatabaseProvider _databaseProvider;

    [DataMember] public List<DisplayItem<string>> StoredProcedureSuggestions { get; } = [];
    [DataMember] public List<DisplayItem<QuerySourceType>> TargetTypeValues { get; } = [];
    [DataMember] public List<string> ParameterTypeSuggestions { get; } = [];
    [DataMember] public BulkObservableCollection<EditParameterData> EditParameters { get; } = [];

    private string _editMethodName = string.Empty;
    private string _editTarget = string.Empty;
    private string? _editResultSetName = string.Empty;
    private QuerySourceType _editTargetType = QuerySourceType.Text;

    private bool _hasMethodNameError;
    private bool _hasTargetError;

    public QueryManagerData(VisualStudioExtensibility extensibility, string projectDirectory, ExtensionSettings settings) {
        _extensibility = extensibility;
        _projectDirectory = projectDirectory;
        _settings = settings;
        _databaseProvider = settings.GetDatabaseProvider();

        TargetTypeValues.Add(new(QuerySourceType.Text, "SQL Query"));
        if (_databaseProvider.Supports(DatabaseCapabilities.StoredProcedures))
            TargetTypeValues.Add(new(QuerySourceType.StoredProcedure, "Stored procedure"));
        TargetTypeValues.Add(new(QuerySourceType.FromFile, "SQL File"));
        ParameterTypeSuggestions.AddRange(_databaseProvider.ParameterTypeSuggestions);

        if (_databaseProvider.Supports(DatabaseCapabilities.StoredProcedures))
            _ = LoadStoredProceduresAsync();

        EditParameters.CollectionChanged += (_, _) => RunValidation();
        Init(settings.Queries);
    }

    #region Properties

    [DataMember]
    public string EditMethodName {
        get => _editMethodName;
        set {
            if (SetProperty(ref _editMethodName, value))
                RunValidation();
        }
    }

    [DataMember]
    public string? EditResultSetName {
        get => _editResultSetName;
        set {
            if (SetProperty(ref _editResultSetName, value))
                RunValidation();
        }
    }

    [DataMember]
    public string EditTarget {
        get => _editTarget;
        set {
            if (SetProperty(ref _editTarget, value))
                RunValidation();
        }
    }

    [DataMember]
    public QuerySourceType EditTargetType {
        get => _editTargetType;
        set {
            if (!SetProperty(ref _editTargetType, value))
                return;
            RunValidation();
            RaiseNotifyPropertyChangedEvent(nameof(TextModeVisibility));
            RaiseNotifyPropertyChangedEvent(nameof(StoredProcedureModeVisibility));
            RaiseNotifyPropertyChangedEvent(nameof(FileModeVisibility));
            RaiseNotifyPropertyChangedEvent(nameof(TargetLabel));
            RaiseNotifyPropertyChangedEvent(nameof(TargetError));
        }
    }
    [DataMember] public Visibility TextModeVisibility => EditTargetType == QuerySourceType.Text ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public Visibility StoredProcedureModeVisibility => EditTargetType == QuerySourceType.StoredProcedure ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public Visibility FileModeVisibility => EditTargetType == QuerySourceType.FromFile ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public string TargetLabel =>
    EditTargetType switch {
        QuerySourceType.StoredProcedure => "Stored Procedure Name",
        QuerySourceType.FromFile => "SQL File path",
        _ => "Raw SQL Query"
    };
    [DataMember] public string? TargetError =>
    _hasTargetError ? EditTargetType switch {
        QuerySourceType.StoredProcedure => "Stored Procedure Name is required",
        QuerySourceType.FromFile => "SQL Script File path is required",
        _ => "SQL Query is required"
    } : null;

    [DataMember]
    public string? MethodNameError => _hasMethodNameError ? "The method name is required" : null;
    public bool HasMethodNameError {
        get => _hasMethodNameError;
        set { if (!SetProperty(ref _hasMethodNameError, value)) return;
            RaiseNotifyPropertyChangedEvent(nameof(MethodNameError));
            RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
        }
    }
    public bool HasTargetError {
        get => _hasTargetError;
        set {
            if (!SetProperty(ref _hasTargetError, value)) return;
            RaiseNotifyPropertyChangedEvent(nameof(TargetError));
            RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
        }
    }

    #endregion

    #region Commands

    [DataMember]
    public IAsyncCommand AddParameterCommand => new AsyncCommand(AddParameterAsync);

    private Task AddParameterAsync(object? arg, CancellationToken ct) {
        EditParameters.Add(new EditParameterData {
            Name = string.Empty,
            Type = _databaseProvider.DefaultParameterType,
            IsNullable = false
        });

        RunValidation();
        return Task.CompletedTask;
    }

    [DataMember]
    public IAsyncCommand DeleteParameterCommand => new AsyncCommand(DeleteParameterAsync);

    private Task DeleteParameterAsync(object? arg, CancellationToken ct) {
        if (arg is EditParameterData param) {
            EditParameters.Remove(param);
            RunValidation();
        }

        return Task.CompletedTask;
    }

    [DataMember]
    public IAsyncCommand BrowseSQLFileCommand => new AsyncCommand(BrowseSQLFileAsync);

    private async Task BrowseSQLFileAsync(object? arg, CancellationToken ct) {
        var options = new FileDialogOptions {
            Title = "Select SQL Script File",
            InitialDirectory = _projectDirectory,
            Filters = new DialogFilters([new("SQL Files", "*.sql")])
        };

        var path = await _extensibility.Shell()
            .ShowOpenFileDialogAsync(options, ct);

        if (!string.IsNullOrWhiteSpace(path))
            EditTarget = _settings.GetSqlFileReference(path);
    }

    #endregion

    #region Shell overrides

    [DataMember] public override Visibility DeleteVisible => !IsNewWorkspace ? Visibility.Visible : Visibility.Collapsed;
    protected override bool CanCommit() => !_hasMethodNameError && !_hasTargetError && HasUnsavedChanges();

    protected override string GetDisplayDescription(QuerySetting item)
        => item.MethodName;

    protected override bool MatchesFilter(QuerySetting item) {
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        return item.MethodName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || item.Target.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    protected override void LoadWorkspace(QuerySetting? item) {
        EditParameters.PauseListening();
        EditParameters.Clear();

        if (item is not null) {
            EditMethodName = item.MethodName ?? string.Empty;
            EditTarget = item.Target ?? string.Empty;
            EditTargetType = item.SourceType;
            EditResultSetName = item.ResultSetName;

            foreach (var p in item.Parameters) {
                EditParameters.Add(new EditParameterData {
                    Name = p.Name,
                    Type = p.Type,
                    IsNullable = p.IsNullable
                });
            }
        }
        else {
            EditResultSetName = null;
            EditMethodName = string.Empty;
            EditTarget = string.Empty;
            EditTargetType = QuerySourceType.Text;
        }

        EditParameters.ResumeListening();
        RunValidation();
    }

    protected override bool HasUnsavedChanges() {
        var item = GetWorkspaceItem();

        if (item is null)
            return !string.IsNullOrWhiteSpace(EditMethodName)
                || !string.IsNullOrWhiteSpace(EditTarget);

        if (item.MethodName != EditMethodName ||
            item.ResultSetName != (string.IsNullOrEmpty(EditResultSetName) ? null : EditResultSetName) ||
            item.Target != EditTarget ||
            item.SourceType != EditTargetType)
            return true;

        if (item.Parameters.Count != EditParameters.Count)
            return true;

        for (int i = 0; i < item.Parameters.Count; i++) {
            var p = item.Parameters[i];
            var ep = EditParameters[i];

            if (p.Name != ep.Name ||
                p.Type != ep.Type ||
                p.IsNullable != ep.IsNullable)
                return true;
        }

        return false;
    }

    protected override Task<bool> ConfirmDiscardAsync(CancellationToken ct)
        => _extensibility.Shell().ShowPromptAsync("You have unsaved changes. Discard them?", PromptOptions.OKCancel, CancellationToken.None);

    public override Task CommitAsync(object? arg, CancellationToken ct) {
        if (!CanCommit())
            return Task.CompletedTask;

        var item = GetWorkspaceItem();
        var isFromOutside = arg is DialogResult d && d == DialogResult.OK;

        var parameters = EditParameters.Select(p => new ParameterOverride {
            Name = p.Name,
            Type = p.Type,
            IsNullable = p.IsNullable
        }).ToList();

        if (item is null) {
            _workspaceIndex = _settings.Queries.Count;
            _settings.Queries.Add(new QuerySetting {
                MethodName = EditMethodName,
                Target = EditTarget,
                SourceType = EditTargetType,
                ResultSetName = string.IsNullOrEmpty(EditResultSetName) ? null : EditResultSetName,
                Parameters = parameters
            });
            if (!isFromOutside) {
                RefreshShellState();
                ApplyFilter();
            }
        }
        else {
            item.ResultSetName = string.IsNullOrEmpty(EditResultSetName) ? null : EditResultSetName;
            item.MethodName = EditMethodName;
            item.Target = EditTarget;
            item.SourceType = EditTargetType;
            item.Parameters = parameters;
            if (!isFromOutside)
                RaiseNotifyPropertyChangedEvent(nameof(FilteredItems));
        }

        if (!isFromOutside)
            RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
        return Task.CompletedTask;
    }

    protected override Task DeleteAsync(object? arg, CancellationToken ct) {
        var item = GetWorkspaceItem();

        if (item is not null) {
            _settings.Queries.Remove(item);
            ApplyFilter();
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Validation

    private bool RunValidation() {
        HasMethodNameError = !CheckMethodNameIsValid();
        HasTargetError = string.IsNullOrWhiteSpace(EditTarget);
        RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
        return !_hasMethodNameError && !_hasTargetError;
    }
    private bool CheckMethodNameIsValid() {
        if (string.IsNullOrEmpty(EditMethodName))
            return false;
        if (char.IsDigit(EditMethodName[0]))
            return false;
        foreach (var c in EditMethodName)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }

    #endregion

        #region Loaders
    private async Task LoadStoredProceduresAsync() {
        try {
            using var cnn = _settings.GetConnection();
            IReadOnlyList<string> procedures = await _databaseProvider.GetStoredProceduresAsync(cnn, CancellationToken.None);

            StoredProcedureSuggestions.Clear();
            foreach (string procedure in procedures)
                StoredProcedureSuggestions.Add(new DisplayItem<string>(procedure, procedure));
            RaiseNotifyPropertyChangedEvent(nameof(StoredProcedureSuggestions));
        }
        catch {
            // Suggestions are optional. Generation and Test Connection surface provider errors explicitly.
        }
    }

    #endregion
}