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
public sealed class ConfigManagerData : ListManagementShell<ConfigFile> {
    private readonly string _projectDirectory;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly List<ConfigFile> _items = [];

    private string _editName = string.Empty;
    private string _editTarget = string.Empty;
    private string? _editExtractionPath;
    private string _editOutputPath = string.Empty;
    private string? _editNamespace;

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
        ShowConnectionStringCommand = new AsyncCommand(ShowConnectionStringAsync);
        TestConnectionCommand = new AsyncCommand(TestConnectionAsync);
        _ = LoadAsync();
    }

    public new ConfigFile? GetWorkspaceItem() => base.GetWorkspaceItem();

    #region Properties

    [DataMember] public List<DisplayItem<ConnectionSourceMetadata>> AvailableSources { get; } = [..Sources.Select(s => new DisplayItem<ConnectionSourceMetadata>(s, s.DisplayName))];

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
        }
        else {
            EditName = item.Name;
            EditTarget = item.ConnectionTarget;
            EditExtractionPath = item.ConnectionExtractionPath;
            SelectedSource = AvailableSources.FirstOrDefault(s => s.Value.SourceType == item.ConnectionSourceType).Value ?? AvailableSources[0].Value;
            EditOutputPath = item.OutputPath;
            EditNamespace = item.Namespace;
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
                || !string.IsNullOrWhiteSpace(EditNamespace);
        return item.ConnectionSourceType != (SelectedSource?.SourceType ?? 0)
            || item.ConnectionTarget != EditTarget
            || item.ConnectionExtractionPath != EditExtractionPath
            || item.OutputPath != EditOutputPath
            || item.Namespace != EditNamespace;
    }

    protected override bool CanCommit() => !HasNameError && !HasTargetError && !HasExtractionPathError && HasUnsavedChanges();

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
                var node = JsonNode.Parse(json) as JsonObject ?? [];

                ConnectionSourceType? detectedType = null;
                string? detectedTarget = null;

                foreach (var pair in node) {
                    if (Enum.TryParse<ConnectionSourceType>(pair.Key, true, out var type)) {
                        detectedType = type;
                        detectedTarget = pair.Value?.ToString();
                        break;
                    }
                }

                _items.Add(new ConfigFile {
                    FilePath = file,
                    ConnectionSourceType = detectedType ?? ConnectionSourceType.RawConnectionString,
                    ConnectionTarget = detectedTarget ?? string.Empty,
                    ConnectionExtractionPath = node[nameof(ExtensionSettings.ConnectionExtractionPath)]?.ToString(),
                    OutputPath = node[nameof(ExtensionSettings.OutputPath)]?.ToString() ?? string.Empty,
                    Namespace = node[nameof(ExtensionSettings.Namespace)]?.ToString()
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

        json[nameof(ExtensionSettings.ConnectionExtractionPath)] = file.ConnectionExtractionPath;
        json[nameof(ExtensionSettings.OutputPath)] = file.OutputPath;
        json[nameof(ExtensionSettings.Namespace)] = file.Namespace;

        await using var output = File.Create(file.FilePath);
        await JsonSerializer.SerializeAsync(output, json, SettingsProvider.PrettyIndent, ct);
    }

    private bool RunValidation() {
        if (_workspaceIndex == NewWorkspaceIndex)
            HasNameError = !CheckNameIsValid();
        HasTargetError = string.IsNullOrWhiteSpace(EditTarget);
        HasExtractionPathError = (SelectedSource?.RequiresExtractionPath ?? false) && string.IsNullOrWhiteSpace(EditExtractionPath);
        RaiseNotifyPropertyChangedEvent(nameof(IsCommitEnabled));
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
                ConnectionSourceType = SelectedSource.SourceType,
                ConnectionTarget = EditTarget,
                ConnectionExtractionPath = EditExtractionPath,
                OutputPath = EditOutputPath,
                Namespace = EditNamespace
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