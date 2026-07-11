using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using RinkuPowerTools.VisualStudio.Content.Config;
using RinkuPowerTools.VisualStudio.Content.Query;

namespace RinkuPowerTools.VisualStudio; 
public static class SettingsProvider {
    public static readonly JsonSerializerOptions PrettyIndent = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public static readonly JsonSerializerOptions Deserialize = new() { PropertyNameCaseInsensitive = true };
    private static async Task<ExtensionSettings?> GetSettingsAsync(VisualStudioExtensibility extensibility, string projectDirectory, bool showDialogs, string? settingsName, CancellationToken ct = default) {
        try {
            ExtensionSettings? res;
            if (!showDialogs && settingsName is not null) {
                res = await ParseSettingsAsync(projectDirectory, settingsName, ct);
                if (res is not null)
                    return res;
            }

            var data = new ConfigManagerData(extensibility, projectDirectory, settingsName ?? string.Empty);
            DialogResult result;
            using (var control = new ConfigManager(data)) {
                result = await extensibility.Shell().ShowDialogAsync(control, "Manage configs", DialogOption.OKCancel, ct);
            }

            if (result != DialogResult.OK)
                return null;
            if (data.IsCommitEnabled)
                await data.CommitAsync(DialogResult.OK, ct);

            var selected = data.GetWorkspaceItem();

            if (selected is null) {
                await extensibility.Shell().ShowPromptAsync("No file were selected before continuing", PromptOptions.OK, ct);
                return null;
            }

            settingsName = selected._name ?? string.Empty;

            res = await ParseSettingsAsync(projectDirectory, settingsName, ct);
            if (res is null) {
                await extensibility.Shell().ShowPromptAsync($"The settings file was not created successfully after dialog: rinkupt.{settingsName}.json", PromptOptions.OK, ct);
                return null;
            }
            if (!await TestConnectionAsync(extensibility, res, ct))
                return null;
            var queryData = new QueryManagerData(extensibility, projectDirectory, res);
            using (var control = new QueryManager(queryData)) {
                result = await extensibility.Shell().ShowDialogAsync(control, "Manage queries", DialogOption.OKCancel, ct);
            }

            if (result != DialogResult.OK)
                return null;
            if (queryData.IsCommitEnabled)
                await queryData.CommitAsync(DialogResult.OK, ct);
            if (!await SaveSettingsAsync(extensibility, res, selected.FilePath, ct)) {
                await extensibility.Shell().ShowPromptAsync($"Unable to save the file", PromptOptions.OK, ct);
                return null;
            }

            return res;
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync(ex.Message, PromptOptions.OK, ct);
            return null;
        }
    }

    private static async Task<ExtensionSettings?> ParseSettingsAsync(string projectDirectory, string settingsName, CancellationToken ct) {
        var filePath = Path.Combine(projectDirectory, string.IsNullOrWhiteSpace(settingsName) ? "rinkupt.json" : $"rinkupt.{settingsName}.json");

        if (!File.Exists(filePath))
            return null;

        using var stream = File.OpenRead(filePath);
        var res = await JsonSerializer.DeserializeAsync<ExtensionSettings>(stream, Deserialize, ct);
        if (res is not null) {
            res.ClassName = settingsName;
            res.SetProjectDirectory(projectDirectory);
            await res.ResolveConnectionStringAsync(ct);
        }
        return res;
    }
    public static Task<ExtensionSettings?> GetSettingsAsync(VisualStudioExtensibility extensibility, string projectDirectory, string? settingsName = null, CancellationToken ct = default)
        => GetSettingsAsync(extensibility, projectDirectory, false, settingsName, ct);
    public static Task<ExtensionSettings?> ConfigureSettingsAsync(VisualStudioExtensibility extensibility, string projectDirectory, string? settingsName = null, CancellationToken ct = default)
        => GetSettingsAsync(extensibility, projectDirectory, true, settingsName, ct);

    public static async Task<bool> TestConnectionAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, CancellationToken ct) {
        try {
            using var cnn = settings.GetConnection();
            await cnn.OpenAsync(ct);

            using var cmd = cnn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            cmd.CommandType = System.Data.CommandType.Text;

            await cmd.ExecuteScalarAsync(ct);
        }
        catch (Exception ex) {
            string message = $"Could not connect to the database with the provided settings.\n\n" +
                             $"Error: {ex.Message}\n\n" +
                             $"Please check your Connection String and ensure the database server is accessible before continuing.";

            await extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, ct);

            return false;
        }

        return true;
    }
    private static async Task<bool> SaveSettingsAsync(VisualStudioExtensibility extensibility, ExtensionSettings settings, string configFilePath, CancellationToken ct) {
        try {
            string targetJsonOutput = JsonSerializer.Serialize(settings, PrettyIndent);
            await File.WriteAllTextAsync(configFilePath, targetJsonOutput, ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.Shell().ShowPromptAsync($"Failed to save rptOptions.json: {ex.Message}", PromptOptions.OK, ct);
            return false;
        }
    }
    public static IEnumerable<string> GetConfigFiles(string projectDirectory) {
        if (!Directory.Exists(projectDirectory))
            return [];
        return Directory.EnumerateFiles(projectDirectory, "*.json", SearchOption.AllDirectories)
            .Where(f => SharedRegex.ConfigFileName().IsMatch(Path.GetFileName(f)));
    }
}
