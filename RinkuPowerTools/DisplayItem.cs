using System.Runtime.Serialization;

namespace RinkuPowerTools;

[DataContract]
public readonly struct DisplayItem<T>(T value, string description) {
    [DataMember]
    public T Value { get; init; } = value;

    [DataMember]
    public string Description { get; init; } = description;
}