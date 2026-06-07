using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools.VisualStudio.Content.Query;

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