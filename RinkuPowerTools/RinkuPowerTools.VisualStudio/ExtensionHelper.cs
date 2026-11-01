using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace RinkuPowerTools.VisualStudio;

public static class ExtensionHelper {
    internal static async Task UpdateCommandsAsync(this VisualStudioExtensibility extensibility, IClientContext context, bool configure, CancellationToken ct) {
        var (project, directory) = await context.GetProjectAndDirectoryAsync(ct);
        if (project is null || directory is null) {
            await extensibility.ShowPromptAsync("No active project found. Please select a project or a file inside a project.", ct);
            return;
        }
        string? fileName = (await context.GetSelectedPathAsync(ct)).GetFileNameWithTrueCasing();
        if (!fileName.TryExtractClassName(out string? className)) return;
        ExtensionSettings? settings = configure
            ? await SettingsProvider.ConfigureSettingsAsync(extensibility, directory, className, ct)
            : await SettingsProvider.GetSettingsAsync(extensibility, directory, className, ct);
        if (settings is not null) await extensibility.GenerateAndOpenCommandsFileAsync(project, settings, ct);
    }

    /// <summary>
    /// Safely retrieves the active project and its directory.
    /// </summary>
    public static async Task<(IProjectSnapshot? Project, string? Directory)> GetProjectAndDirectoryAsync(this IClientContext context, CancellationToken ct) {
        var project = await context.GetActiveProjectAsync(ct);
        var directory = project != null ? Path.GetDirectoryName(project.Path) : null;
        return (project, directory);
    }

    /// <summary>
    /// Queries the workspace for the default namespace of a given project.
    /// </summary>
    public static async Task<string?> GetBaseNamespaceAsync(this VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, CancellationToken ct) {
        var projectResults = await extensibility.Workspaces().QueryProjectsAsync(
            query => query
                .Where(p => p.Path == projectSnapshot.Path)
                .With(p => p.DefaultNamespace)
                .With(p => p.Name),
            ct);

        var project = projectResults.FirstOrDefault();
        return project?.DefaultNamespace ?? project?.Name;
    }

    /// <summary>
    /// Displays a standard OK prompt for errors or warnings.
    /// </summary>
    public static Task ShowPromptAsync(this VisualStudioExtensibility extensibility, string message, CancellationToken ct) 
        => extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, ct);
    /// <summary>
    /// Derives the generated class name based on the JSON file's naming convention.
    /// </summary>
    public static bool TryExtractClassName(this string? fileName, [MaybeNullWhen(false)] out string className) {
        var match = SharedRegex.ConfigFileName().Match(fileName ?? string.Empty);
        if (!match.Success) {
            className = null;
            return false;
        }
        className = match.Groups[1].Value.TrimStart('.');
        return true;
    }

    public static Task<bool> GenerateAndOpenCommandsFileAsync(this VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, ExtensionSettings settings, CancellationToken ct) => GenerateAndOpenCommandsFileAsync(extensibility, projectSnapshot, settings, true, ct);
    public static async Task<bool> GenerateAndOpenCommandsFileAsync(this VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, ExtensionSettings settings, bool openAfter, CancellationToken ct) {
        var baseNamespace = await extensibility.GetBaseNamespaceAsync(projectSnapshot, ct);
        if (baseNamespace is null)
            return false;
        try {
            var fullPath = await MainClassGenerator.GenerateClassAsync(settings, baseNamespace, ct);
            await ApplySqlFileProjectMetadataAsync(projectSnapshot, settings, ct);
            if (openAfter)
                await extensibility.Documents().OpenDocumentAsync(new Uri(fullPath), ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.ShowPromptAsync($"Failed to write class file to disk: {ex.Message}", ct);
            return false;
        }
    }

    private static async Task ApplySqlFileProjectMetadataAsync(IProjectSnapshot projectSnapshot, ExtensionSettings settings, CancellationToken ct) {
        HashSet<string> handled = new(StringComparer.OrdinalIgnoreCase);
        foreach (var query in settings.Queries) {
            if (query.SourceType != QuerySourceType.FromFile || Path.IsPathRooted(query.Target) || !handled.Add(query.Target))
                continue;

            string fullPath = settings.ResolveSqlFilePath(query.Target);
            if (!File.Exists(fullPath))
                continue;

            IFileSnapshot? file = await FindProjectFileAsync(projectSnapshot, fullPath, ct);
            if (file is null) {
                await projectSnapshot.AsUpdatable().AddFile(fullPath, "None").ExecuteAsync(ct);
                file = await FindProjectFileAsync(projectSnapshot, fullPath, ct)
                    ?? throw new InvalidOperationException($"Unable to add SQL file '{query.Target}' to the project.");
            }

            await file.AsUpdatable()
                .SetPropertyValue("CopyToOutputDirectory", "PreserveNewest")
                .SetPropertyValue("CopyToPublishDirectory", "PreserveNewest")
                .ExecuteAsync(ct);
        }
    }
    private static async Task<IFileSnapshot?> FindProjectFileAsync(IProjectSnapshot projectSnapshot, string fullPath, CancellationToken ct) {
        await foreach (var result in projectSnapshot.Files.With(file => file.Path).QueryAsync(ct)) {
            if (string.Equals(Path.GetFullPath(result.Value.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                return result.Value;
        }
        return null;
    }
    public static bool IsValidCSharpName(this string name) {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (char.IsDigit(name[0]))
            return false;
        foreach (var c in name)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }
}
