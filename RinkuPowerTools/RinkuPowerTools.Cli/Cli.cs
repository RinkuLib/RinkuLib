using System.Data.Common;

namespace RinkuPowerTools.Cli;

internal static class Cli {
    public static async Task<int> RunAsync(string[] rawArgs, CancellationToken ct) {
        CliArguments args = CliArguments.Parse(rawArgs);
        if (args.Positionals.Count == 0 || args.Has("help") || args.Positionals[0] is "help" or "-h") {
            PrintHelp();
            return 0;
        }

        ProjectContext project = ProjectContext.Resolve(args.Get("project"));
        string command = args.Positionals[0].ToLowerInvariant();

        return command switch {
            "refresh" => await RefreshService.RunAsync(project, args, ct),
            "config" => await ConfigAsync(project, args, ct),
            "query" => await QueryAsync(project, args, ct),
            "connection" => await ConnectionAsync(project, args, ct),
            _ => throw new CliException($"Unknown command '{command}'. Run 'rinku help'.", 2)
        };
    }

    private static async Task<int> ConfigAsync(ProjectContext project, CliArguments args, CancellationToken ct) {
        string action = GetAction(args);
        string? config = args.Get("config");

        switch (action) {
            case "list":
                foreach (string path in ConfigStore.List(project))
                    Console.WriteLine(Path.GetFileName(path));
                return 0;

            case "show": {
                ExtensionSettings settings = await ConfigStore.LoadAsync(project, config, ct);
                PrintSettings(settings);
                return 0;
            }

            case "init": {
                string path = ConfigStore.GetPath(project, config);
                if (File.Exists(path) && !args.Has("force"))
                    throw new CliException($"Configuration already exists: {path}. Use --force to replace it.", 2);

                var settings = new ExtensionSettings {
                    ConnectionSourceType = ParseConnectionSource(args.Get("connection-source") ?? nameof(ConnectionSourceType.RawConnectionString)),
                    ConnectionTarget = args.Get("connection-target") ?? string.Empty,
                    ConnectionExtractionPath = args.Get("connection-path"),
                    Database = ParseDatabase(args.Get("database")),
                    OutputPath = args.Get("output") ?? string.Empty,
                    Namespace = args.Get("namespace"),
                    IsInternal = args.GetBoolean("internal", false)
                };
                ApplyConstantsOptions(settings, args);
                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            case "set": {
                ExtensionSettings settings = await ConfigStore.LoadAsync(project, config, ct);
                if (args.Has("connection-source")) settings.ConnectionSourceType = ParseConnectionSource(args.Require("connection-source"));
                if (args.Has("connection-target")) settings.ConnectionTarget = args.Require("connection-target");
                if (args.Has("connection-path")) settings.ConnectionExtractionPath = args.Get("connection-path");
                if (args.Has("output")) settings.OutputPath = args.Get("output") ?? string.Empty;
                if (args.Has("namespace")) settings.Namespace = args.Get("namespace");
                if (args.Has("internal")) settings.IsInternal = args.GetBoolean("internal", settings.IsInternal);
                if (args.Has("database")) settings.Database = ParseDatabase(args.Get("database"));
                ApplyConstantsOptions(settings, args);
                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            default:
                throw new CliException($"Unknown config action '{action}'.", 2);
        }
    }

    private static void ApplyConstantsOptions(ExtensionSettings settings, CliArguments args) {
        int count = new[] { "constants-sql", "constants-path", "constants-proc", "constants-none" }.Count(args.Has);
        if (count > 1) throw new CliException("Use only one of --constants-sql, --constants-path, --constants-proc or --constants-none.", 2);
        if (args.Has("constants-none")) settings.Constants = null;
        else if (args.Has("constants-sql")) settings.Constants = new(QuerySourceType.Text, args.Require("constants-sql"));
        else if (args.Has("constants-path")) settings.Constants = new(QuerySourceType.FromFile, args.Require("constants-path"));
        else if (args.Has("constants-proc")) settings.Constants = new(QuerySourceType.StoredProcedure, args.Require("constants-proc"));
    }

    private static async Task<int> QueryAsync(ProjectContext project, CliArguments args, CancellationToken ct) {
        string action = GetAction(args);
        string? config = args.Get("config");
        ExtensionSettings settings = await ConfigStore.LoadAsync(project, config, ct);

        switch (action) {
            case "list":
                foreach (QuerySetting query in settings.Queries)
                    Console.WriteLine($"{query.MethodName}\t{query.SourceType}\t{query.Target}");
                return 0;

            case "show": {
                QuerySetting query = FindQuery(settings, GetRequiredPositional(args, 2, "query name"));
                PrintQuery(query);
                return 0;
            }

            case "add": {
                string name = GetRequiredPositional(args, 2, "query name");
                if (settings.Queries.Any(x => string.Equals(x.MethodName, name, StringComparison.OrdinalIgnoreCase)))
                    throw new CliException($"Query '{name}' already exists.", 2);

                settings.Queries.Add(CreateQuery(name, args));
                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            case "set": {
                string name = GetRequiredPositional(args, 2, "query name");
                QuerySetting query = FindQuery(settings, name);
                ApplyQueryOptions(query, args, requireSource: false);
                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            case "remove": {
                string name = GetRequiredPositional(args, 2, "query name");
                int removed = settings.Queries.RemoveAll(x => string.Equals(x.MethodName, name, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    throw new CliException($"Query '{name}' was not found.", 2);
                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            case "param": {
                string name = GetRequiredPositional(args, 2, "query name");
                string parameterName = GetRequiredPositional(args, 3, "parameter name");
                QuerySetting query = FindQuery(settings, name);
                ParameterOverride? parameter = query.Parameters.FirstOrDefault(x => string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase));

                if (args.Has("remove")) {
                    if (parameter is not null)
                        query.Parameters.Remove(parameter);
                }
                else {
                    parameter ??= new ParameterOverride { Name = parameterName };
                    if (!query.Parameters.Contains(parameter))
                        query.Parameters.Add(parameter);
                    if (args.Has("type")) parameter.Type = args.Get("type");
                    if (args.Has("nullable")) parameter.IsNullable = ParseNullableBool(args.Get("nullable"));
                    if (args.Has("default")) {
                        string value = args.Require("default");
                        parameter.DefaultValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    }
                }

                await ConfigStore.SaveAsync(project, config, settings, ct);
                return 0;
            }

            default:
                throw new CliException($"Unknown query action '{action}'.", 2);
        }
    }

    private static async Task<int> ConnectionAsync(ProjectContext project, CliArguments args, CancellationToken ct) {
        string action = GetAction(args);
        if (action != "test")
            throw new CliException($"Unknown connection action '{action}'.", 2);

        ExtensionSettings settings = await ConfigStore.LoadAsync(project, args.Get("config"), ct);
        await settings.ResolveConnectionStringAsync(ct);
        await using DbConnection connection = settings.GetConnection();
        await connection.OpenAsync(ct);
        Console.WriteLine($"Connected: {connection.Database}");
        return 0;
    }

    private static QuerySetting CreateQuery(string name, CliArguments args) {
        var query = new QuerySetting { MethodName = name, Target = string.Empty, SourceType = QuerySourceType.Text };
        ApplyQueryOptions(query, args, requireSource: true);
        return query;
    }

    private static void ApplyQueryOptions(QuerySetting query, CliArguments args, bool requireSource) {
        int sources = (args.Has("sql") ? 1 : 0) + (args.Has("file") ? 1 : 0) + (args.Has("proc") ? 1 : 0);
        if (sources > 1)
            throw new CliException("Use only one of --sql, --file, or --proc.", 2);
        if (requireSource && sources == 0)
            throw new CliException("A query requires --sql, --file, or --proc.", 2);

        if (args.Has("sql")) {
            query.SourceType = QuerySourceType.Text;
            query.Target = args.Require("sql");
        }
        else if (args.Has("file")) {
            query.SourceType = QuerySourceType.FromFile;
            query.Target = args.Require("file");
        }
        else if (args.Has("proc")) {
            query.SourceType = QuerySourceType.StoredProcedure;
            query.Target = args.Require("proc");
        }

        if (args.Has("result")) query.ResultSetName = args.Get("result");
        if (args.Has("generate-hooks"))
        {
            query.GenerateHooks = args.GetBoolean("generate-hooks", true);
        }
    }

    private static QuerySetting FindQuery(ExtensionSettings settings, string name) =>
        settings.Queries.FirstOrDefault(x => string.Equals(x.MethodName, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new CliException($"Query '{name}' was not found.", 2);

    private static string GetAction(CliArguments args) => GetRequiredPositional(args, 1, "action").ToLowerInvariant();

    private static string GetRequiredPositional(CliArguments args, int index, string name) =>
        args.Positionals.Count > index ? args.Positionals[index] : throw new CliException($"Missing {name}.", 2);

    private static ConnectionSourceType ParseConnectionSource(string value) =>
        Enum.TryParse(value, true, out ConnectionSourceType parsed)
            ? parsed
            : throw new CliException($"Unknown connection source '{value}'.", 2);

    private static DatabaseType? ParseDatabase(string? value) {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            return null;
        if (DatabaseProviders.TryParseType(value, out DatabaseType parsed))
            return parsed;
        throw new CliException($"Unknown database '{value}'.", 2);
    }

    private static bool? ParseNullableBool(string? value) {
        if (value is null)
            return true;
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        if (bool.TryParse(value, out bool parsed))
            return parsed;
        throw new CliException("--nullable expects true, false, or auto.", 2);
    }

    private static void PrintSettings(ExtensionSettings settings) {
        Console.WriteLine($"Database:       {settings.Database?.ToString() ?? "auto"}");
        Console.WriteLine($"Connection:     {settings.ConnectionSourceType}");
        Console.WriteLine($"Target:         {settings.ConnectionTarget}");
        Console.WriteLine($"Extraction:     {settings.ConnectionExtractionPath ?? ""}");
        Console.WriteLine($"Output:         {settings.OutputPath}");
        Console.WriteLine($"Namespace:      {settings.Namespace ?? "auto"}");
        Console.WriteLine($"Internal:       {settings.IsInternal}");
        Console.WriteLine($"Queries:        {settings.Queries.Count}");
        if (settings.Constants?.ToQuery() is { } constants) Console.WriteLine($"Constants:    {constants.SourceType} {constants.Target}");
    }

    private static void PrintQuery(QuerySetting query) {
        Console.WriteLine($"Method:         {query.MethodName}");
        Console.WriteLine($"Source:         {query.SourceType}");
        Console.WriteLine($"Target:         {query.Target}");
        Console.WriteLine($"Result:         {query.ResultSetName ?? "auto"}");
        Console.WriteLine($"Generate hooks: {query.GenerateHooks}");
        foreach (ParameterOverride parameter in query.Parameters)
            Console.WriteLine($"Parameter:      {parameter.Name} type={parameter.Type ?? "auto"} nullable={parameter.IsNullable?.ToString() ?? "auto"} default={parameter.DefaultValue ?? "required"}");
    }

    private static void PrintHelp() => Console.WriteLine("""
Rinku Power Tools CLI

Usage:
  rinku <command> [action] [arguments] [options]

Global options:
  --project <path>           Project file or directory. Defaults to nearest .csproj.

Refresh:
  rinku refresh              Refresh every rinkupt*.json configuration. No configs is a successful no-op.
  rinku refresh <name>       Refresh rinkupt.<name>.json.
  rinku refresh --default    Refresh rinkupt.json.

Configuration:
  rinku config list
  rinku config show [--config <name|path>]
  rinku config init [--config <name|path>] [--connection-source <type>] [--connection-target <value>]
                    [--connection-path <path>] [--database <type|auto>] [--output <path>]
                    [--namespace <name>] [--internal [true|false]] [--force]
  rinku config set [--config <name|path>] [same options]
  Constants/enums source options for config init/set:
                    [--constants-path <path> | --constants-sql <sql> | --constants-proc <name> | --constants-none]

Queries:
  rinku query list [--config <name|path>]
  rinku query show <name> [--config <name|path>]
  rinku query add <name> (--sql <sql> | --file <path> | --proc <name>) [--result <type>] [--generate-hooks <true|false>] [--config <name|path>]
  rinku query set <name> [--sql <sql> | --file <path> | --proc <name>] [--result <type>] [--generate-hooks <true|false>] [--config <name|path>]
  rinku query remove <name> [--config <name|path>]
  rinku query param <query> <parameter> [--type <db-type>] [--nullable <true|false|auto>] [--default <C#-expression>] [--config <name|path>]
  rinku query param <query> <parameter> --remove [--config <name|path>]

Connection:
  rinku connection test [--config <name|path>]

Examples:
  rinku refresh
  rinku refresh dev
  rinku refresh --default
  rinku query add GetAlbums --file Sql/GetAlbums.sql
  rinku query add ArchiveInvoices --proc dbo.ArchiveInvoices --result ArchivedInvoice
""");
}
