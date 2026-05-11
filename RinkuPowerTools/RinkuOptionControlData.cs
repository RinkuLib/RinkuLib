using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace RinkuPowerTools;

[DataContract]
public class RinkuOptionControlData : INotifyPropertyChanged {
    private string connectionString = string.Empty;

    [DataMember]
    public string ConnectionString {
        get => this.connectionString;
        set {
            if (this.connectionString != value) {
                this.connectionString = value;
                this.OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string? GetValidationError() {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "Please enter a connection string";

        return null;
    }
    public RinkuOption ToOptions() => new(this.ConnectionString);
}