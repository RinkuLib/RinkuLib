using System.IO;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

[DataContract]
public class SyncDBOptionsControlData : NotifyPropertyChangedObject {
    private CancellationTokenSource? _validationCts;
    private readonly VisualStudioExtensibility _extensibility;
    private readonly string _projectDirectory;
    private readonly ExtensionSettings _settings;

    private bool _hasConnError;
    private bool _hasClassError;

    public SyncDBOptionsControlData(VisualStudioExtensibility extensibility, string projectDirectory, ExtensionSettings settings) {
        _extensibility = extensibility;
        _projectDirectory = projectDirectory;
        _settings = settings;
        BrowseFolderCommand = new AsyncCommand(BrowseFolderAsync);

        RunValidation();
    }

    [DataMember]
    public IAsyncCommand BrowseFolderCommand { get; }

    [DataMember]
    public string ConnectionString {
        get => _settings.ConnectionString ?? string.Empty;
        set {
            if (_settings.ConnectionString != value) {
                _settings.ConnectionString = value;
                DebounceValidation();
            }
        }
    }

    [DataMember]
    public string ClassName {
        get => _settings.ClassName ?? "DB";
        set {
            if (_settings.ClassName != value) {
                _settings.ClassName = value;
                DebounceValidation();
            }
        }
    }

    [DataMember]
    public string OutputPath {
        get => _settings.OutputPath ?? string.Empty;
        set {
            if (_settings.OutputPath != value) {
                _settings.OutputPath = value;
                RaiseNotifyPropertyChangedEvent(nameof(OutputPath));
            }
        }
    }

    [DataMember]
    public string? Namespace {
        get => _settings.Namespace;
        set {
            if (_settings.Namespace != value) {
                _settings.Namespace = value;
                RaiseNotifyPropertyChangedEvent(nameof(Namespace));
            }
        }
    }

    private void DebounceValidation() {
        _validationCts?.Cancel();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        _ = Task.Delay(300, token).ContinueWith(t => {
            if (!t.IsCanceled) {
                RaiseNotifyPropertyChangedEvent(nameof(ConnectionString));
                RaiseNotifyPropertyChangedEvent(nameof(ClassName));
                RunValidation();
            }
        }, TaskScheduler.Default);
    }

    private void RunValidation() {
        HasConnError = string.IsNullOrWhiteSpace(ConnectionString);
        HasClassError = string.IsNullOrWhiteSpace(ClassName);

        RaiseNotifyPropertyChangedEvent(nameof(IsValid));
    }

    [DataMember] public bool HasConnError { get => _hasConnError; set => SetProperty(ref _hasConnError, value); }
    [DataMember] public bool HasClassError { get => _hasClassError; set => SetProperty(ref _hasClassError, value); }

    public bool IsValid => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(ClassName);

    private async Task BrowseFolderAsync(object? parameter, CancellationToken ct) {
        FolderDialogOptions options = new() {
            Title = "Select Output Target Directory",
            InitialDirectory = _projectDirectory
        };
        string? selectedFolderPath = await _extensibility.Shell().ShowOpenFolderDialogAsync(options, ct);

        if (!string.IsNullOrWhiteSpace(selectedFolderPath)) {
            string relativePath = Path.GetRelativePath(_projectDirectory, selectedFolderPath);
            OutputPath = relativePath == "." || relativePath == ".." || string.IsNullOrWhiteSpace(relativePath)
                ? string.Empty : relativePath.Replace('\\', '/');
        }
    }

    public void TriggerValidate() => RunValidation();
}