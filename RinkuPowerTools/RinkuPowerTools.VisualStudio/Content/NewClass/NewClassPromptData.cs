using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools.VisualStudio.Content.NewClass;

[DataContract]
public sealed class ResultSetEntry {
    [DataMember]
    public required string DisplayName { get; init; }

    public required ExtensionSettings Settings { get; init; }

    public required QuerySetting Query { get; init; }
}

[DataContract]
public sealed class NewClassPromptData : NotifyPropertyChangedObject {
    private string _editName = string.Empty;
    private ResultSetEntry? _selectedResultSet;
    private bool _hasNameError;

    public NewClassPromptData(string projectDirectory) {
        foreach (var file in SettingsProvider.GetConfigFiles(projectDirectory)) {
            ExtensionSettings? settings;
            try {
                settings = JsonSerializer.Deserialize<ExtensionSettings>(File.ReadAllText(file));
            }
            catch {
                continue;
            }

            if (settings is null)
                continue;

            settings.SetProjectDirectory(projectDirectory);

            if (Path.GetFileName(file).TryExtractClassName(out var className))
                settings.ClassName = className;

            foreach (var query in settings.Queries) {
                var resultSetName = query.ResultSetName ?? $"{query.MethodName}Result";
                ResultSets.Add(new ResultSetEntry {
                    DisplayName = $"{settings.ClassName} - {resultSetName}",
                    Settings = settings,
                    Query = query
                });
            }
        }
        SelectedResultSet = ResultSets.FirstOrDefault();
    }

    [DataMember]
    public string EditName {
        get => _editName;
        set {
            if (SetProperty(ref _editName, value))
                RunValidation();
        }
    }

    [DataMember]
    public List<ResultSetEntry> ResultSets { get; } = [];

    [DataMember]
    public ResultSetEntry? SelectedResultSet {
        get => _selectedResultSet;
        set => SetProperty(ref _selectedResultSet, value);
    }

    public ExtensionSettings? SelectedSettings =>
        SelectedResultSet?.Settings;

    public QuerySetting? SelectedQuery =>
        SelectedResultSet?.Query;

    [DataMember]
    public string? NameError => _hasNameError ? "Name must be unique and valid c# class name" : null;
    public bool HasNameError {
        get => _hasNameError;
        set {
            if (!SetProperty(ref _hasNameError, value))
                return;
            RaiseNotifyPropertyChangedEvent(nameof(NameError));
        }
    }
    private void RunValidation()
        => HasNameError = !EditName.IsValidCSharpName();
}