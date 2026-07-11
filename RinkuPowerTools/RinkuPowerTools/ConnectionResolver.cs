using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;

namespace RinkuPowerTools;

public static class ConnectionResolver {
    public static async Task<string> ResolveAsync(ConnectionSourceType sourceType, string target, string? extractionPath, string projectDirectory, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Connection target cannot be empty.");

        string GetAbsoluteFilePath() => Path.Combine(projectDirectory, target);

        return sourceType switch {
            ConnectionSourceType.RawConnectionString => target,
            ConnectionSourceType.EnvironmentVariable => Environment.GetEnvironmentVariable(target)
                ?? throw new Exception($"Environment variable '{target}' not found."),
            ConnectionSourceType.JsonFile => await ParseJsonAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.XmlFile => await ParseXmlAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.DotEnvFile => await ParseDotEnvAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.IniFile => await ParseIniFileAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.MsBuildProject => await ParseMsBuildProjectAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.NetUserSecrets => await ParseNetUserSecretsAsync(GetAbsoluteFilePath(), extractionPath, ct),
            ConnectionSourceType.LaunchSettings => await ParseLaunchSettingsAsync(projectDirectory, extractionPath, ct),
            ConnectionSourceType.VsDataConnection => await ParseVsDataConnectionAsync(target, ct),
            ConnectionSourceType.CloudSecret => await ParseCloudSecretAsync(target, extractionPath, ct),
            _ => throw new NotImplementedException($"Resolution for {sourceType} is not fully implemented.")
        };
    }

    private static async Task<string> ParseJsonAsync(string filePath, string? jsonPath, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(jsonPath))
            throw new ArgumentException("JSON extraction requires a path.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("JSON file not found.", filePath);

        using var stream = File.OpenRead(filePath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var element = doc.RootElement;
        foreach (var segment in jsonPath.Split(':')) {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var next))
                element = next;
            else
                throw new Exception($"JSON path '{jsonPath}' could not be resolved in {filePath}.");
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
    }

    private static async Task<string> ParseXmlAsync(string filePath, string? xPath, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(xPath))
            throw new ArgumentException("XML extraction requires an XPath.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("XML file not found.", filePath);

        using var stream = File.OpenRead(filePath);
        var xDoc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        var result = xDoc.XPathEvaluate(xPath);

        if (result is System.Collections.IEnumerable enumerable) {
            var first = enumerable.Cast<object>().FirstOrDefault();
            if (first is XAttribute attr)
                return attr.Value;
            if (first is XElement elem)
                return elem.Value;
        }

        throw new Exception($"XPath '{xPath}' did not yield a valid connection string in {filePath}.");
    }

    private static async Task<string> ParseDotEnvAsync(string filePath, string? variableName, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(variableName))
            throw new ArgumentException(".env extraction requires a variable name.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(".env file not found.", filePath);

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        foreach (var line in lines) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var parts = trimmed.Split('=', 2);
            if (string.Equals(parts[0].Trim(), variableName, StringComparison.OrdinalIgnoreCase))
                return parts[1].Trim().Trim('"', '\'');
        }

        throw new Exception($"Variable '{variableName}' not found in {filePath}.");
    }

    private static async Task<string> ParseIniFileAsync(string filePath, string? keyPath, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("INI extraction requires a key path (e.g., '[Section]Key').");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("INI file not found.", filePath);

        string targetSection = string.Empty;
        string targetKey = keyPath;

        if (keyPath.StartsWith('[') && keyPath.Contains(']')) {
            var parts = keyPath.Split(']', 2);
            targetSection = parts[0].TrimStart('[').Trim();
            targetKey = parts[1].Trim();
        }

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        string currentSection = string.Empty;

        foreach (var line in lines) {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) {
                currentSection = trimmed.Trim('[', ']');
                continue;
            }

            if (string.Equals(currentSection, targetSection, StringComparison.OrdinalIgnoreCase)) {
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && string.Equals(parts[0].Trim(), targetKey, StringComparison.OrdinalIgnoreCase)) {
                    return parts[1].Trim().Trim('"', '\'');
                }
            }
        }

        throw new Exception($"Key '{keyPath}' not found in {filePath}.");
    }

    private static async Task<string> ParseMsBuildProjectAsync(string filePath, string? propertyName, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("MSBuild extraction requires a Property Name.");
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found.", filePath);

        using var stream = File.OpenRead(filePath);
        var xDoc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        var element = xDoc.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase));

        return element?.Value ?? throw new Exception($"Property '{propertyName}' not found in {filePath}.");
    }

    private static async Task<string> ParseNetUserSecretsAsync(string csprojPath, string? jsonPath, CancellationToken ct) {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException("Project file not found.", csprojPath);

        using var stream = File.OpenRead(csprojPath);
        var xDoc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        var secretId = xDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UserSecretsId")?.Value;

        if (string.IsNullOrWhiteSpace(secretId))
            throw new Exception($"No <UserSecretsId> property found in {csprojPath}.");

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string secretsPath = isWindows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", secretId, "secrets.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", secretId, "secrets.json");

        return await ParseJsonAsync(secretsPath, jsonPath, ct);
    }

    private static async Task<string> ParseLaunchSettingsAsync(string projectDirectory, string? profileAndVar, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(profileAndVar))
            throw new ArgumentException("Launch settings requires extraction path format: 'ProfileName:VariableName'.");

        string launchPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!File.Exists(launchPath))
            throw new FileNotFoundException("launchSettings.json not found.", launchPath);

        var parts = profileAndVar.Split(':', 2);
        string profile = parts[0].Trim();
        string varName = parts.Length > 1 ? parts[1].Trim() : throw new ArgumentException("Invalid extraction path. Use 'ProfileName:VariableName'.");

        using var stream = File.OpenRead(launchPath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("profiles", out var profiles) &&
            profiles.TryGetProperty(profile, out var profileData) &&
            profileData.TryGetProperty("environmentVariables", out var envVars) &&
            envVars.TryGetProperty(varName, out var value)) {
            return value.GetString() ?? string.Empty;
        }

        throw new Exception($"Environment variable '{varName}' not found in profile '{profile}'.");
    }

    private static Task<string> ParseVsDataConnectionAsync(string connectionName, CancellationToken ct) {
        throw new NotImplementedException("Retrieving connections from Visual Studio Server Explorer out-of-process requires IVsDataConnectionManager via BrokeredServices.");
    }

    private static Task<string> ParseCloudSecretAsync(string vaultUri, string? secretName, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Cloud Vault requires a secret name.");

        throw new NotImplementedException("Cloud Key Vault resolution requires Azure.Identity package integration.");
    }
}