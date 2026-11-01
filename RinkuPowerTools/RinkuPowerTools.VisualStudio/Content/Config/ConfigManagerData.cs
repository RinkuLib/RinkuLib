using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.DataContracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using RinkuPowerTools.VisualStudio.Shell;

namespace RinkuPowerTools.VisualStudio.Content.Config;

[DataContract]
public class ConnectionSourceMetadata(ConnectionSourceType SourceType, string DisplayName, string TargetLabel, string? FilePattern, bool RequiresExtractionPath, string? ExtractionPathLabel = null, string? ErrorMessage = null) {
    [DataMember] public ConnectionSourceType SourceType { get; } = SourceType;
    [DataMember] public string DisplayName { get; } = DisplayName;
    [DataMember] public string TargetLabel { get; } = TargetLabel;
    [DataMember] public string? FilePattern { get; } = FilePattern;
    [DataMember] public bool RequiresExtractionPath { get; } = RequiresExtractionPath;
    [DataMember] public string? ExtractionPathLabel { get; } = ExtractionPathLabel;
    [DataMember] public string? ErrorMessage { get; } = ErrorMessage;
}
[DataContract]
public sealed class DatabaseSelection(DatabaseType? Database, string DisplayName) {
    [DataMember] public DatabaseType? Database { get; } = Database;
    [DataMember] public string DisplayName { get; } = DisplayName;
}

[DataContract]
public sealed class ConfigManagerData : ListManagementShell<ConfigFile> {
    private readonly string _projectDirectory;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly List<ConfigFile> _items = [];

    private string _editName = string.Empty;
    private string _editTarget = string.Empty;
    private string? _editExtractionPath;
    private string _editOutputPath = string.Empty;
    private string? _editNamespace;
    private DatabaseType? _editDatabase;
    private bool _editIsInternal;
    private QuerySourceType? _editConstantsType;
    private string _editConstantsTarget = string.Empty;

    private bool _hasNameError;
    private bool _hasTargetError;
    private bool _hasExtractionPathError;

    public ConfigManagerData(VisualStudioExtensibility extensibility, string projectDirectory, string? openingConfigName = null) {
        _extensibility = extensibility;
        _projectDirectory = projectDirectory;
        if (openingConfigName is not null)
            _editName = openingConfigName;

        BrowseCommand = new AsyncCommand(BrowseAsync);
        BrowseFolderCommand = new AsyncCommand(BrowseFolderAsync);
        BrowseConstantsCommand = new AsyncCommand(BrowseConstantsAsync);
        ShowConnectionStringCommand = new AsyncCommand(ShowConnectionStringAsync);
        TestConnectionCommand = new AsyncCommand(TestConnectionAsync);
        _ = LoadAsync();
    }

    public new ConfigFile? GetWorkspaceItem() => base.GetWorkspaceItem();

    #region Properties

    [DataMember] public List<DisplayItem<ConnectionSourceMetadata>> AvailableSources { get; } = [..Sources.Select(s => new DisplayItem<ConnectionSourceMetadata>(s, s.DisplayName))];
    [DataMember] public List<DatabaseSelection> AvailableDatabases { get; } = [
        new(null, "Auto detect"),
        new(DatabaseType.SqlServer, "SQL Server"),
        new(DatabaseType.PostgreSql, "PostgreSQL"),
        new(DatabaseType.Sqlite, "SQLite")
    ];

    public readonly static ConnectionSourceMetadata[] Sources = [
        new(ConnectionSourceType.RawConnectionString, "Raw Connection String", "Connection String", null, false),
        new(ConnectionSourceType.EnvironmentVariable, "Environment Variable", "Variable Name", null, false),
        new(ConnectionSourceType.JsonFile, "JSON Configuration File", "Relative File Path", "*.json", true, "JSON Path (e.g., ConnectionStrings:Default)"),
        new(ConnectionSourceType.XmlFile, "XML / Config File", "Relative File Path", "*.xml;*.config", true, "XPath (e.g., //add[@name='Default']/@connectionString)"),
        new(ConnectionSourceType.DotEnvFile, ".env File", "Relative File Path", ".env", true, "Variable Name"),
        new(ConnectionSourceType.IniFile, "INI File", "Relative File Path", "*.ini", true, "Key (e.g., [Section]Key)"),
        new(ConnectionSourceType.MsBuildProject, "MSBuild Project File", "Relative Project File Path", "*.csproj;*.vbproj", true, "Property Name"),
        new(ConnectionSourceType.NetUserSecrets, ".NET User Secrets", "Relative Project File Path", "*.csproj", true, "JSON Path (e.g., ConnectionStrings:Default)"),
        new(ConnectionSourceType.LaunchSettings, "Launch Settings", "Relative Project File Path", "launchSettings.json", true, "Profile Name : Env Var Name")
    //new(ConnectionSourceType.VsDataConnection, "Visual Studio Server Explorer", "Connection Name", false, false),
    //new(ConnectionSourceType.CloudSecret, "Cloud Key Vault", "Vault URI", false, true, "Secret Name")
    ];

    private ConnectionSourceMetadata? _selectedSource;
    [DataMember]
    public ConnectionSourceMetadata? SelectedSource {
        get => _selectedSource;
        set {
            if (SetProperty(ref _selectedSource, value)) {
                RaiseNotifyPropertyChangedEvent(nameof(SelectedSource));
                RaiseNotifyPropertyChangedEvent(nameof(IsBrowseVisible));
                RaiseNotifyPropertyChangedEvent(nameof(IsExtractionPathVisible));
                RunValidation();
            }
        }
    }
    [DataMember] public Visibility IsBrowseVisible => SelectedSource?.FilePattern != null ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public Visibility IsExtractionPathVisible => SelectedSource?.RequiresExtractionPath ?? false ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public string EditName { get => _editName; set { if (SetProperty(ref _editName, value)) RunValidation(); } }
    [DataMember] public string EditTarget { get => _editTarget; set { if (SetProperty(ref _editTarget, value)) RunValidation(); } }
    [DataMember] public string? EditExtractionPath { get => _editExtractionPath; set { if (SetProperty(ref _editExtractionPath, value)) RunValidation(); } }
    [DataMember] public string EditOutputPath { get => _editOutputPath; set { if (SetProperty(ref _editOutputPath, value)) RunValidation(); } }
    [DataMember] public string? EditNamespace { get => _editNamespace; set { if (SetProperty(ref _editNamespace, value)) RunValidation(); } }
    [DataMember] public DatabaseType? EditDatabase { get => _editDatabase; set { if (SetProperty(ref _editDatabase, value)) RunValidation(); } }
    [DataMember] public bool EditIsInternal { get => _editIsInternal; set { if (SetProperty(ref _editIsInternal, value)) RunValidation(); } }
    [DataMember] public List<DisplayItem<QuerySourceType?>> ConstantsTypes { get; } = [new(null, "None"), new(QuerySourceType.FromFile, "SQL file"), new(QuerySourceType.Text, "Raw SQL"), new(QuerySourceType.StoredProcedure, "Stored procedure")];
    [DataMember] public QuerySourceType? EditConstantsType {
        get => _editConstantsType;
        set { if (SetProperty(ref _editConstantsType, value)) { RaiseNotifyPropertyChangedEvent(nameof(ConstantsVisibility)); RaiseNotifyPropertyChangedEvent(nameof(ConstantsFileVisibility)); RaiseNotifyPropertyChangedEvent(nameof(ConstantsTextVisibility)); RaiseNotifyPropertyChangedEvent(nameof(ConstantsProcedureVisibility)); RunValidation(); } }
    }
    [DataMember] public string EditConstantsTarget { get => _editConstantsTarget; set { if (SetProperty(ref _editConstantsTarget, value)) RunValidation(); } }
    [DataMember] public Visibility ConstantsVisibility => EditConstantsType is null ? Visibility.Collapsed : Visibility.Visible;
    [DataMember] public Visibility ConstantsFileVisibility => EditConstantsType == QuerySourceType.FromFile ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public Visibility ConstantsTextVisibility => EditConstantsType == QuerySourceType.Text ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public Visibility ConstantsProcedureVisibility => EditConstantsType == QuerySourceType.StoredProcedure ? Visibility.Visible : Visibility.Collapsed;
    [DataMember] public string? ConstantsError => EditConstantsType is not null && string.IsNullOrWhiteSpace(EditConstantsTarget) ? "A constants/enums source is required, or select None." : EditConstantsType == QuerySourceType.StoredProcedure && EditDatabase == DatabaseType.Sqlite ? "SQLite does not support stored procedures." : null;
    private ConstantsSource? EditedConstants => EditConstantsType is { } type ? new ConstantsSource(type, EditConstantsTarget) : null;

    [DataMember] public string? NameError => _hasNameError ? "Name must be unique and a valid C# identifier." : null;
    public bool HasNameError {
        get => _hasNameError;
        set {
            if (SetProperty(ref _hasNameError, value)) {
                RaiseNotifyPropertyChangedEvent(nameof(NameError));
                RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
            }
        }
    }

    [DataMember] public string? TargetError => _hasTargetError ? (_selectedSource?.ErrorMessage ?? $"{_selectedSource?.TargetLabel ?? "Target"} is required.") : null;
    public bool HasTargetError {
        get => _hasTargetError;
        set {
            if (SetProperty(ref _hasTargetError, value)) {
                RaiseNotifyPropertyChangedEvent(nameof(TargetError));
                RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
            }
        }
    }

    [DataMember] public string? ExtractionPathError => _hasExtractionPathError ? $"{_selectedSource?.ExtractionPathLabel ?? "Value"} is required." : null;
    public bool HasExtractionPathError {
        get => _hasExtractionPathError;
        set {
            if (SetProperty(ref _hasExtractionPathError, value)) {
                RaiseNotifyPropertyChangedEvent(nameof(ExtractionPathError));
                RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
            }
        }
    }

    #endregion

    #region Shell Overrides

    protected override void LoadWorkspace(ConfigFile? item) {
        if (item is null) {
            EditName = string.Empty;
            SelectedSource = AvailableSources[0].Value;
            EditTarget = string.Empty;
            EditExtractionPath = null;
            EditOutputPath = string.Empty;
            EditNamespace = null;
            EditDatabase = null;
            EditIsInternal = false;
            EditConstantsType = null;
            EditConstantsTarget = string.Empty;
        }
        else {
            EditName = item.Name;
            EditTarget = item.ConnectionTarget;
            EditExtractionPath = item.ConnectionExtractionPath;
            SelectedSource = AvailableSources.FirstOrDefault(s => s.Value.SourceType == item.ConnectionSourceType).Value ?? AvailableSources[0].Value;
            EditOutputPath = item.OutputPath;
            EditNamespace = item.Namespace;
            EditDatabase = item.Database;
            EditIsInternal = item.IsInternal;
            var constants = item.Constants;
            EditConstantsType = constants?.SourceType;
            EditConstantsTarget = constants?.Target ?? string.Empty;
        }
        RunValidation();
    }
    protected override bool HasUnsavedChanges() {
        var item = GetWorkspaceItem();
        if (item is null)
            return !string.IsNullOrWhiteSpace(EditName)
                || !string.IsNullOrWhiteSpace(EditExtractionPath)
                || !string.IsNullOrWhiteSpace(EditTarget)
                || !string.IsNullOrWhiteSpace(EditOutputPath)
                || !string.IsNullOrWhiteSpace(EditNamespace)
                || EditDatabase is not null || EditConstantsType is not null;
        return item.ConnectionSourceType != (SelectedSource?.SourceType ?? 0)
            || item.ConnectionTarget != EditTarget
            || item.ConnectionExtractionPath != EditExtractionPath
            || item.OutputPath != EditOutputPath
            || item.Namespace != EditNamespace
            || item.Database != EditDatabase
            || item.IsInternal != EditIsInternal
            || item.Constants?.SourceType != EditConstantsType
            || (item.Constants?.Target ?? string.Empty) != (EditConstantsType is null ? string.Empty : EditConstantsTarget);
    }

    protected override bool CanCommit() => !HasNameError && !HasTargetError && !HasExtractionPathError && ConstantsError is null && HasUnsavedChanges();

    public override async Task CommitAsync(object? arg, CancellationToken ct) {
        if (!CanCommit())
            return;
        var isFromOutside = arg is DialogResult d && d == DialogResult.OK;
        var item = GetWorkspaceItem();
        var isNew = item is null;
        item ??= new ConfigFile { FilePath = CreateFilePath(EditName) };

        item.ConnectionSourceType = SelectedSource?.SourceType ?? ConnectionSourceType.RawConnectionString;
        item.ConnectionTarget = EditTarget;
        item.ConnectionExtractionPath = EditExtractionPath;
        item.OutputPath = EditOutputPath;
        item.Namespace = string.IsNullOrWhiteSpace(EditNamespace) ? null : EditNamespace;
        item.Database = EditDatabase;
        item.IsInternal = EditIsInternal;
        item.Constants = EditedConstants;

        await WriteFileAsync(item, ct);
        if (isNew) {
            _workspaceIndex = _items.Count;
            _items.Add(item);
            if (!isFromOutside) { RefreshShellState(); ApplyFilter(); }
        }
        if (!isFromOutside)
            RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
    }

    protected override string GetDisplayDescription(ConfigFile item) => item.Name;
    protected override bool MatchesFilter(ConfigFile item) => string.IsNullOrWhiteSpace(FilterText) || item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    protected override Task DeleteAsync(object? arg, CancellationToken ct)
        => Task.CompletedTask;
    protected override Task<bool> ConfirmDiscardAsync(CancellationToken ct)
        => _extensibility.Shell().ShowPromptAsync("You have unsaved changes. Discard them?", PromptOptions.OKCancel, ct);
    #endregion

    #region IO and Helpers
    private async Task LoadAsync() {
        var files = SettingsProvider.GetConfigFiles(_projectDirectory);
        _items.Clear();
        foreach (var file in files) {
            try {
                var json = await File.ReadAllTextAsync(file);
                var settings = JsonSerializer.Deserialize<ExtensionSettings>(json) ?? throw new JsonException("Expected connection settings.");
                _items.Add(new ConfigFile {
                    FilePath = file,
                    Database = settings.Database,
                    ConnectionSourceType = settings.ConnectionSourceType,
                    ConnectionTarget = settings.ConnectionTarget,
                    ConnectionExtractionPath = settings.ConnectionExtractionPath,
                    OutputPath = settings.OutputPath,
                    Namespace = settings.Namespace,
                    IsInternal = settings.IsInternal,
                    Constants = settings.Constants
                });
            }
            catch { }
        }
        Init(_items);
        if (_editName is not null) {
            for (int i = 0; i < FilteredItems.Count; i++) {
                if (FilteredItems[i].Value.EqualName(_editName)) {
                    DisplayIndex = i;
                    break;
                }
            }
        }
        if (DisplayIndex == -1 && FilteredItems.Count > 0)
            DisplayIndex = 0;
    }

    private static async Task WriteFileAsync(ConfigFile file, CancellationToken ct) {
        JsonObject json;
        if (File.Exists(file.FilePath)) {
            await using var stream = File.OpenRead(file.FilePath);
            json = await JsonNode.ParseAsync(stream, cancellationToken: ct) as JsonObject ?? [];
        }
        else {
            json = [];
        }

        foreach (var sourceType in Enum.GetNames<ConnectionSourceType>()) 
            json.Remove(sourceType);

        json[file.ConnectionSourceType.ToString()] = file.ConnectionTarget;

        if (file.Database is { } database)
            json[nameof(ExtensionSettings.Database)] = database.ToString();
        else
            json.Remove(nameof(ExtensionSettings.Database));

        SetOptional(nameof(ExtensionSettings.ConnectionExtractionPath), file.ConnectionExtractionPath);
        SetOptional(nameof(ExtensionSettings.OutputPath), file.OutputPath);
        SetOptional(nameof(ExtensionSettings.Namespace), file.Namespace);
        if (file.IsInternal) json[nameof(ExtensionSettings.IsInternal)] = true;
        else json.Remove(nameof(ExtensionSettings.IsInternal));
        foreach (string key in new[] { "ConstantsSQL", "ConstantsPath", "ConstantsProc" }) json.Remove(key);
        if (file.Constants is { } constants) json[constants.PropertyName] = constants.Target;

        void SetOptional(string name, string? value) {
            if (string.IsNullOrWhiteSpace(value)) json.Remove(name);
            else json[name] = value;
        }

        await using var output = File.Create(file.FilePath);
        await JsonSerializer.SerializeAsync(output, json, SettingsProvider.PrettyIndent, ct);
    }

    private bool RunValidation() {
        if (_workspaceIndex == NewWorkspaceIndex)
            HasNameError = !CheckNameIsValid();
        HasTargetError = string.IsNullOrWhiteSpace(EditTarget);
        HasExtractionPathError = (SelectedSource?.RequiresExtractionPath ?? false) && string.IsNullOrWhiteSpace(EditExtractionPath);
        RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
        RaiseNotifyPropertyChangedEvent(nameof(ConstantsError));
        return !HasNameError && !HasTargetError && !HasExtractionPathError;
    }
    private bool CheckNameIsValid() {
        if (EditName.Length > 0 && !EditName.IsValidCSharpName())
            return false;
        for (int i = 0; i < _items.Count; i++)
            if (i != _workspaceIndex && _items[i].EqualName(EditName))
                return false;
        return true;
    }
    private string CreateFilePath(string name) => string.IsNullOrWhiteSpace(name) ? Path.Combine(_projectDirectory, $"rinkupt.json") : Path.Combine(_projectDirectory, $"rinkupt.{name}.json");
    #endregion

    #region Commands

    [DataMember] public IAsyncCommand BrowseConstantsCommand { get; }
    private async Task BrowseConstantsAsync(object? arg, CancellationToken ct) {
        var options = new FileDialogOptions { Title = "Select constants/enums SQL file", InitialDirectory = _projectDirectory, Filters = new DialogFilters([new("SQL files", "*.sql")]) };
        string? path = await _extensibility.Shell().ShowOpenFileDialogAsync(options, ct);
        if (!string.IsNullOrWhiteSpace(path)) {
            string relative = Path.GetRelativePath(_projectDirectory, path);
            EditConstantsTarget = Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : relative.Replace('\\', '/');
        }
    }

    [DataMember] public IAsyncCommand BrowseCommand { get; }
    private async Task BrowseAsync(object? arg, CancellationToken ct) {
        if (SelectedSource?.FilePattern == null)
            return;
        var options = new FileDialogOptions { Title = $"Select {SelectedSource.TargetLabel}", InitialDirectory = _projectDirectory, Filters = new DialogFilters([new("Files", SelectedSource.FilePattern)]) };
        string? path = await _extensibility.Shell().ShowOpenFileDialogAsync(options, ct);
        if (!string.IsNullOrWhiteSpace(path))
            EditTarget = Path.GetRelativePath(_projectDirectory, path).Replace('\\', '/');
    }

    [DataMember] public IAsyncCommand BrowseFolderCommand { get; }
    private async Task BrowseFolderAsync(object? parameter, CancellationToken ct) {
        var options = new FolderDialogOptions { Title = "Select Output Target Directory", InitialDirectory = _projectDirectory };
        string? path = await _extensibility.Shell().ShowOpenFolderDialogAsync(options, ct);
        if (!string.IsNullOrWhiteSpace(path)) {
            string relative = Path.GetRelativePath(_projectDirectory, path);
            EditOutputPath = (relative == "." || relative == "..") ? string.Empty : relative.Replace('\\', '/');
        }
    }
    [DataMember] public IAsyncCommand ShowConnectionStringCommand { get; }
    private async Task ShowConnectionStringAsync(object? arg, CancellationToken ct) {
        if (!RunValidation() || SelectedSource is null)
            return;

        try {
            string connectionString = await ConnectionResolver.ResolveAsync(SelectedSource.SourceType, EditTarget, EditExtractionPath, _projectDirectory, ct);

            await _extensibility.Shell().ShowPromptAsync(connectionString, PromptOptions.OK, ct);
        }
        catch (Exception ex) {
            await _extensibility.Shell().ShowPromptAsync(ex.Message, PromptOptions.OK, ct);
        }
    }

    [DataMember] public IAsyncCommand TestConnectionCommand { get; }
    private async Task TestConnectionAsync(object? arg, CancellationToken ct) {
        if (!RunValidation() || SelectedSource is null)
            return;

        try {
            var settings = new ExtensionSettings {
                Database = EditDatabase,
                ConnectionSourceType = SelectedSource.SourceType,
                ConnectionTarget = EditTarget,
                ConnectionExtractionPath = EditExtractionPath,
                OutputPath = EditOutputPath,
                Namespace = EditNamespace,
                IsInternal = EditIsInternal
            };

            settings.SetProjectDirectory(_projectDirectory);
            await settings.ResolveConnectionStringAsync(ct);

            if (await SettingsProvider.TestConnectionAsync(_extensibility, settings, ct))
                await _extensibility.Shell().ShowPromptAsync("Connection successful.", PromptOptions.OK, ct);
        }
        catch (Exception ex) {
            await _extensibility.Shell().ShowPromptAsync(ex.Message,  PromptOptions.OK, ct);
        }
    }
    #endregion
}
