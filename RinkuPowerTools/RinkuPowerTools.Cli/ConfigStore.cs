using System.Text.Json;

namespace RinkuPowerTools.Cli;

internal static class ConfigStore {
    private static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetPath(ProjectContext project, string? config) {
        if (string.IsNullOrWhiteSpace(config))
            return Path.Combine(project.ProjectDirectory, "rinkupt.json");

        if (config.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || config.Contains(Path.DirectorySeparatorChar) || config.Contains(Path.AltDirectorySeparatorChar))
            return Path.IsPathRooted(config) ? Path.GetFullPath(config) : Path.GetFullPath(Path.Combine(project.ProjectDirectory, config));

        string path = Path.Combine(project.ProjectDirectory, $"rinkupt.{config}.json");
        string alternate = Path.Combine(project.ProjectDirectory, $"rinku.{config}.json");
        if (File.Exists(path) && File.Exists(alternate))
            throw new CliException($"Two configurations have the name '{config}'. Select a file path explicitly.", 2);
        return File.Exists(alternate) ? alternate : path;
    }

    public static IReadOnlyList<string> List(ProjectContext project) => [.. Directory
        .EnumerateFiles(project.ProjectDirectory, "rinku*.json", SearchOption.TopDirectoryOnly)
        .Where(static path => {
            string fileName = Path.GetFileName(path);
            return string.Equals(fileName, "rinkupt.json", StringComparison.OrdinalIgnoreCase)
                || ((fileName.StartsWith("rinkupt.", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("rinku.", StringComparison.OrdinalIgnoreCase))
                    && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && fileName.Length > fileName.IndexOf('.') + 6);
        })
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];

    public static bool Exists(ProjectContext project, string? config) => File.Exists(GetPath(project, config));

    public static string? GetName(string path) {
        string fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "rinkupt.json", StringComparison.OrdinalIgnoreCase))
            return null;
        if ((!fileName.StartsWith("rinkupt.", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("rinku.", StringComparison.OrdinalIgnoreCase)) || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new CliException($"Not a Rinku Power Tools configuration: {path}", 2);

        string name = fileName[(fileName.IndexOf('.') + 1)..^5];
        if (string.IsNullOrWhiteSpace(name))
            throw new CliException($"Not a Rinku Power Tools configuration: {path}", 2);
        return name;
    }

    public static async Task<ExtensionSettings> LoadAsync(ProjectContext project, string? config, CancellationToken ct) {
        string path = GetPath(project, config);
        if (!File.Exists(path))
            throw new CliException($"Configuration not found: {path}", 2);

        // The VS extension and CLI can touch the same config. A save can briefly
        // replace the file while a build starts, so tolerate that small window.
        for (int attempt = 0; ; attempt++) {
            try {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                ExtensionSettings? settings = await JsonSerializer.DeserializeAsync<ExtensionSettings>(stream, Options, ct) ?? throw new CliException($"Configuration is empty: {path}", 2);
                settings.SetProjectDirectory(project.ProjectDirectory);
                settings.ClassName = GetName(path);
                return settings;
            }
            catch (Exception ex) when (attempt < 3 && ex is IOException or JsonException) {
                await Task.Delay(20, ct);
            }
        }
    }

    public static async Task SaveAsync(ProjectContext project, string? config, ExtensionSettings settings, CancellationToken ct) {
        string path = GetPath(project, config);
        settings.SetProjectDirectory(project.ProjectDirectory);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tempPath, path, true);
        }
        finally {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        Console.WriteLine(path);
    }
}
