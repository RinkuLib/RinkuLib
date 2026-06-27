using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace RinkuPowerTools.VisualStudio;

public static class ExtensionHelper {
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
        if (string.IsNullOrEmpty(fileName)) {
            className = null;
            return false;
        }
        int firstDot = fileName.IndexOf('.');
        if (firstDot == -1) {
            className = null;
            return false;
        }
        firstDot++;
        int secondDot = fileName.IndexOf('.', firstDot);
        if (secondDot == -1) {
            className = string.Empty;
            return true;
        }
        className = fileName[firstDot..secondDot];
        return true;
    }

    public static Task<bool> GenerateAndOpenCommandsFileAsync(this VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, ExtensionSettings settings, CancellationToken ct) => GenerateAndOpenCommandsFileAsync(extensibility, projectSnapshot, settings, true, ct);
    public static async Task<bool> GenerateAndOpenCommandsFileAsync(this VisualStudioExtensibility extensibility, IProjectSnapshot projectSnapshot, ExtensionSettings settings, bool openAfter, CancellationToken ct) {
        var baseNamespace = await extensibility.GetBaseNamespaceAsync(projectSnapshot, ct);
        if (baseNamespace is null)
            return false;
        try {
            var fullPath = await MainClassGenerator.GenerateClassAsync(settings, baseNamespace, ct);
            if (openAfter)
                await extensibility.Documents().OpenDocumentAsync(new Uri(fullPath), ct);
            return true;
        }
        catch (Exception ex) {
            await extensibility.ShowPromptAsync($"Failed to write class file to disk: {ex.Message}", ct);
            return false;
        }
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