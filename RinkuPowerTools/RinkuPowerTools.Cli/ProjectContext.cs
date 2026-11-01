using System.Xml.Linq;

namespace RinkuPowerTools.Cli;

internal sealed record ProjectContext(string ProjectFile, string ProjectDirectory, string BaseNamespace) {
    public static ProjectContext Resolve(string? requestedProject) {
        string projectFile;

        if (!string.IsNullOrWhiteSpace(requestedProject)) {
            string path = Path.GetFullPath(requestedProject);
            if (Directory.Exists(path))
                projectFile = FindProject(path, walkParents: false);
            else if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
                projectFile = path;
            else
                throw new CliException($"Project not found: {requestedProject}", 2);
        }
        else {
            projectFile = FindProject(Environment.CurrentDirectory, walkParents: true);
        }

        string directory = Path.GetDirectoryName(projectFile)
            ?? throw new CliException($"Could not determine the project directory for {projectFile}.", 2);
        return new(projectFile, directory, ReadBaseNamespace(projectFile));
    }

    private static string FindProject(string startDirectory, bool walkParents) {
        for (DirectoryInfo? dir = new(Path.GetFullPath(startDirectory)); dir is not null; dir = walkParents ? dir.Parent : null) {
            string[] projects = Directory.GetFiles(dir.FullName, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projects.Length == 1)
                return projects[0];
            if (projects.Length > 1)
                throw new CliException($"Several projects found in {dir.FullName}. Use --project.", 2);
        }

        throw new CliException("No .csproj found. Use --project.", 2);
    }

    private static string ReadBaseNamespace(string projectFile) {
        XDocument document = XDocument.Load(projectFile);
        string? rootNamespace = FindProperty(document, "RootNamespace");
        if (!string.IsNullOrWhiteSpace(rootNamespace))
            return rootNamespace;

        string? assemblyName = FindProperty(document, "AssemblyName");
        if (!string.IsNullOrWhiteSpace(assemblyName))
            return assemblyName;

        return Path.GetFileNameWithoutExtension(projectFile);
    }

    private static string? FindProperty(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.Ordinal))?.Value.Trim();
}
