namespace RinkuPowerTools.Cli;

internal static class RefreshService {
    public static Task<int> RunAsync(ProjectContext project, CliArguments args, CancellationToken ct) =>
        RunAsync(project, args, static async (settings, baseNamespace, token) => {
            await settings.ResolveConnectionStringAsync(token);
            return await MainClassGenerator.GenerateClassAsync(settings, baseNamespace, token);
        }, ct);

    internal static async Task<int> RunAsync(ProjectContext project, CliArguments args, Func<ExtensionSettings, string, CancellationToken, Task<string>> generate, CancellationToken ct) {
        bool useDefault = args.Has("default");
        string? name = args.Positionals.Count > 1 ? args.Positionals[1] : null;

        if (args.Positionals.Count > 2)
            throw new CliException("Refresh accepts at most one configuration name.", 2);
        if (useDefault && name is not null)
            throw new CliException("Use either 'rinku refresh <name>' or 'rinku refresh --default'.", 2);

        if (useDefault) {
            await RefreshOneAsync(project, null, explicitSelection: true, generate, ct);
            return 0;
        }

        if (name is not null) {
            await RefreshOneAsync(project, name, explicitSelection: true, generate, ct);
            return 0;
        }

        IReadOnlyList<string> configs = ConfigStore.List(project);
        if (configs.Count == 0)
            return 0;

        foreach (string path in configs) {
            await RefreshOneAsync(project, path, explicitSelection: false, generate, ct);
        }

        return 0;
    }

    private static async Task RefreshOneAsync(ProjectContext project, string? config, bool explicitSelection, Func<ExtensionSettings, string, CancellationToken, Task<string>> generate, CancellationToken ct) {
        if (!ConfigStore.Exists(project, config)) {
            if (!explicitSelection)
                return;
            throw new CliException($"Configuration not found: {ConfigStore.GetPath(project, config)}", 2);
        }

        ExtensionSettings settings = await ConfigStore.LoadAsync(project, config, ct);
        string output = await generate(settings, project.BaseNamespace, ct);
        Console.WriteLine(output);
    }
}
