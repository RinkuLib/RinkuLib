using System.ComponentModel;
using System.Data.SqlClient;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.UI;

namespace RinkuPowerTools;

public partial class ProcLinkInfoControl : RemoteUserControl {
    private readonly VisualStudioExtensibility extensibility;
    private readonly string connectionString;
    private readonly ProcLinkInfoControlData data;

    public ProcLinkInfoControl(VisualStudioExtensibility extensibility, ProcLinkInfoControlData dataContext, string connectionString)
        : base(dataContext) {
        this.extensibility = extensibility;
        this.data = dataContext;
        this.connectionString = connectionString;

        this.data.PropertyChanged += OnDataContextPropertyChanged;
    }

    private void OnDataContextPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(ProcLinkInfoControlData.SelectedDatabase)) {
            _ = SafeLoadProceduresAsync();
        }
    }

    private async Task SafeLoadProceduresAsync() {
        if (string.IsNullOrEmpty(data.SelectedDatabase))
            return;

        data.Procedures.Clear();

        try {
            var builder = new SqlConnectionStringBuilder(connectionString) {
                InitialCatalog = data.SelectedDatabase
            };

            using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME",
                conn);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                data.Procedures.Add(reader.GetString(0));
            }
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync($"Error loading procedures for {data.SelectedDatabase}: {ex.Message}", PromptOptions.OK, CancellationToken.None);
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            this.data.PropertyChanged -= OnDataContextPropertyChanged;
        }
        base.Dispose(disposing);
    }
}