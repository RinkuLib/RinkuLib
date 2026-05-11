using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace RinkuPowerTools;

[DataContract]
public class ProcLinkInfoControlData : INotifyPropertyChanged {
    public ProcLinkInfoControlData(string? selectedDb, List<string> databases, IEnumerable<string> procedures) {
        Databases = databases;
        selectedDatabase = selectedDb;

        foreach (var proc in procedures)
            Procedures.Add(proc);
    }

    [DataMember]
    public List<string> Databases { get; }

    [DataMember]
    public ObservableCollection<string> Procedures { get; } = [];

    private string? selectedDatabase;
    [DataMember]
    public string? SelectedDatabase {
        get => selectedDatabase;
        set { selectedDatabase = value; OnPropertyChanged(); }
    }

    private string? selectedProcedure;
    [DataMember]
    public string? SelectedProcedure {
        get => selectedProcedure;
        set { selectedProcedure = value; OnPropertyChanged(); }
    }

    private string className = string.Empty;

    [DataMember]
    public string ClassName {
        get => className;
        set { className = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); 
    public string? GetValidationError() {
        if (string.IsNullOrWhiteSpace(ClassName) || ClassName.Length < 3)
            return "Class Name must be at least 3 characters.";

        if (string.IsNullOrEmpty(SelectedDatabase))
            return "Please select a database.";

        if (string.IsNullOrEmpty(SelectedProcedure))
            return "Please select a stored procedure.";

        return null;
    }
}