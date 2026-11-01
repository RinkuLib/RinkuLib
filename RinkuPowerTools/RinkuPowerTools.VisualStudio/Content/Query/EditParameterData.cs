using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools.VisualStudio.Content.Query;

[DataContract]
public class EditParameterData(Action? changed = null) : NotifyPropertyChangedObject {
    private string _name = string.Empty;
    private string? _type;
    private bool? _isNullable;
    private string? _defaultValue;

    [DataMember]
    public string Name {
        get => _name;
        set {
            if (SetProperty(ref _name, value))
                changed?.Invoke();
        }
    }

    [DataMember]
    public string? Type {
        get => _type;
        set {
            if (SetProperty(ref _type, value))
                changed?.Invoke();
        }
    }

    [DataMember]
    public bool? IsNullable {
        get => _isNullable;
        set {
            if (SetProperty(ref _isNullable, value))
                changed?.Invoke();
        }
    }

    [DataMember]
    public string? DefaultValue {
        get => _defaultValue;
        set {
            if (SetProperty(ref _defaultValue, value))
                changed?.Invoke();
        }
    }
}
