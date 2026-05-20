using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

[DataContract]
public class QuerySelectorControlData : NotifyPropertyChangedObject {
    private readonly ExtensionSettings _settings;
    private string _targetClassName = string.Empty;
    private string _queryInputText = string.Empty;
    private bool _hasClassError;
    private bool _hasQueryError;

    public QuerySelectorControlData(ExtensionSettings settings) {
        _settings = settings;

        // Harvest known MethodNames from the current context state
        var names = _settings.Queries?.Select(q => q.MethodName) ?? Enumerable.Empty<string>();
        AvailableQueries = new ObservableCollection<string>(names);

        RunValidation();
    }

    [DataMember]
    public ObservableCollection<string> AvailableQueries { get; }

    [DataMember]
    public string TargetClassName {
        get => _targetClassName;
        set {
            if (_targetClassName != value) {
                _targetClassName = value;
                RaiseNotifyPropertyChangedEvent(nameof(TargetClassName));
                RunValidation();
            }
        }
    }

    [DataMember]
    public string QueryInputText {
        get => _queryInputText;
        set {
            if (_queryInputText != value) {
                _queryInputText = value;
                RaiseNotifyPropertyChangedEvent(nameof(QueryInputText));
                RunValidation();
            }
        }
    }

    [DataMember]
    public bool HasClassError {
        get => _hasClassError;
        set => SetProperty(ref _hasClassError, value);
    }

    [DataMember]
    public bool HasQueryError {
        get => _hasQueryError;
        set => SetProperty(ref _hasQueryError, value);
    }

    public bool IsValid => !HasClassError && !HasQueryError;

    private void RunValidation() {
        HasClassError = string.IsNullOrWhiteSpace(TargetClassName);

        // Ensure it is not empty AND exists exactly inside our options roster
        HasQueryError = string.IsNullOrWhiteSpace(QueryInputText) ||
                        !AvailableQueries.Contains(QueryInputText, StringComparer.OrdinalIgnoreCase);
    }

    public void TriggerValidate() => RunValidation();
}