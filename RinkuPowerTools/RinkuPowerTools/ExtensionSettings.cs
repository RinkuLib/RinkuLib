using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RinkuPowerTools;

public enum QuerySourceType {
    Text,
    StoredProcedure,
    FromFile
}
public enum ConnectionSourceType {
    JsonFile,
    XmlFile,
    MsBuildProject,
    DotEnvFile,
    IniFile,
    NetUserSecrets,
    LaunchSettings,
    RawConnectionString,
    EnvironmentVariable,
    VsDataConnection,
    CloudSecret
}
[JsonConverter(typeof(ExtensionSettingsConverter))]
public class ExtensionSettings {
    [JsonIgnore]
    public string? ClassName { get; set; }
    [JsonIgnore]
    private string? ProjectDirectory;
    public void SetProjectDirectory(string projectDirectory) => ProjectDirectory = projectDirectory;
    public string GetFullPath(string relativePath) => Path.Combine(ProjectDirectory ?? throw new Exception("The project directory was not set"), relativePath);
    public string GetRelativePath(string relativePath) => Path.GetRelativePath(ProjectDirectory ?? throw new Exception("The project directory was not set"), relativePath);
    public async Task<string> GetSqlTextAsync(QuerySetting query, CancellationToken ct) =>
        query.SourceType == QuerySourceType.FromFile
            ? await File.ReadAllTextAsync(Path.IsPathRooted(query.Target) ? query.Target : GetFullPath(query.Target), ct)
            : query.Target;
    public DbConnection GetConnection() => new SqlConnection(ConnectionString ?? throw new Exception("The connection string was not set"));
    public string? ConnectionString { get; set; }
    public ConnectionSourceType ConnectionSourceType { get; set; }
    public required string ConnectionTarget { get; set; }
    public string? ConnectionExtractionPath { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public List<QuerySetting> Queries { get; set; } = [];
    public async Task ResolveConnectionStringAsync(CancellationToken ct) {
        ConnectionString = await ConnectionResolver.ResolveAsync(ConnectionSourceType, ConnectionTarget, ConnectionExtractionPath, ProjectDirectory ?? throw new Exception("The project directory was not set"), ct);
    }

}
public class ExtensionSettingsConverter : JsonConverter<ExtensionSettings> {
    public override ExtensionSettings? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected JSON object for ExtensionSettings.");

        ConnectionSourceType? connectionSourceType = null;
        string? connectionTarget = null;
        string? connectionExtractionPath = null;
        string outputPath = string.Empty;
        string? @namespace = null;
        List<QuerySetting> queries = [];

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName) {
                string propertyName = reader.GetString() ?? string.Empty;
                reader.Read();

                if (string.Equals(propertyName, nameof(ExtensionSettings.ConnectionExtractionPath), StringComparison.OrdinalIgnoreCase))
                    connectionExtractionPath = reader.GetString();
                else if (string.Equals(propertyName, nameof(ExtensionSettings.OutputPath), StringComparison.OrdinalIgnoreCase))
                    outputPath = reader.GetString() ?? string.Empty;
                else if (string.Equals(propertyName, nameof(ExtensionSettings.Namespace), StringComparison.OrdinalIgnoreCase))
                    @namespace = reader.GetString();
                else if (string.Equals(propertyName, nameof(ExtensionSettings.Queries), StringComparison.OrdinalIgnoreCase))
                    queries = JsonSerializer.Deserialize<List<QuerySetting>>(ref reader, options) ?? [];
                else if (Enum.TryParse<ConnectionSourceType>(propertyName, true, out var parsedType)) {
                    connectionSourceType = parsedType;
                    connectionTarget = reader.GetString();
                }
                else {
                    reader.Skip();
                }
            }
        }

        if (connectionSourceType == null || string.IsNullOrWhiteSpace(connectionTarget))
            throw new JsonException("Extension settings are missing the connection source type identifier or target value.");

        return new ExtensionSettings {
            ConnectionSourceType = connectionSourceType.Value,
            ConnectionTarget = connectionTarget,
            ConnectionExtractionPath = connectionExtractionPath,
            OutputPath = outputPath,
            Namespace = @namespace,
            Queries = queries
        };
    }

    public override void Write(Utf8JsonWriter writer, ExtensionSettings value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        writer.WriteString(value.ConnectionSourceType.ToString(), value.ConnectionTarget);

        if (!string.IsNullOrWhiteSpace(value.ConnectionExtractionPath))
            writer.WriteString(nameof(ExtensionSettings.ConnectionExtractionPath), value.ConnectionExtractionPath);

        if (!string.IsNullOrEmpty(value.OutputPath))
            writer.WriteString(nameof(ExtensionSettings.OutputPath), value.OutputPath);

        if (!string.IsNullOrEmpty(value.Namespace)) 
            writer.WriteString(nameof(ExtensionSettings.Namespace), value.Namespace);

        if (value.Queries != null && value.Queries.Count > 0) {
            writer.WritePropertyName(nameof(ExtensionSettings.Queries));
            JsonSerializer.Serialize(writer, value.Queries, options);
        }

        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(QuerySettingConverter))]
public class QuerySetting {
    public required string MethodName { get; set; }
    public string? ResultSetName { get; set; }
    public required string Target { get; set; }
    public QuerySourceType SourceType { get; set; }
    public List<ParameterOverride> Parameters { get; set; } = [];

    public CommandType GetCommandType() => SourceType switch {
        QuerySourceType.StoredProcedure => CommandType.StoredProcedure,
        QuerySourceType.Text or QuerySourceType.FromFile => CommandType.Text,
        _ => throw new InvalidOperationException($"Unsupported source type: {SourceType}")
    };
}

public class ParameterOverride {
    public required string Name { get; set; }
    public string? Type { get; set; }
    public bool? IsNullable { get; set; }
}

public class QuerySettingConverter : JsonConverter<QuerySetting> {
    public override QuerySetting? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected JSON object for QuerySetting.");

        string? methodName = null;
        string? resultSetName = null;
        string? target = null;
        QuerySourceType sourceType = QuerySourceType.Text;
        List<ParameterOverride> parameters = [];

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName) {
                string propertyName = reader.GetString() ?? string.Empty;
                reader.Read();

                if (string.Equals(propertyName, nameof(QuerySetting.MethodName), StringComparison.OrdinalIgnoreCase)) {
                    methodName = reader.GetString();
                }
                if (string.Equals(propertyName, nameof(QuerySetting.ResultSetName), StringComparison.OrdinalIgnoreCase)) {
                    resultSetName = reader.GetString();
                }
                else if (string.Equals(propertyName, "StoredProcName", StringComparison.OrdinalIgnoreCase)) {
                    target = reader.GetString();
                    sourceType = QuerySourceType.StoredProcedure;
                }
                else if (string.Equals(propertyName, "SQLQuery", StringComparison.OrdinalIgnoreCase)) {
                    target = reader.GetString();
                    sourceType = QuerySourceType.Text;
                }
                else if (string.Equals(propertyName, "SQLFile", StringComparison.OrdinalIgnoreCase)) {
                    target = reader.GetString();
                    sourceType = QuerySourceType.FromFile;
                }
                else if (string.Equals(propertyName, nameof(QuerySetting.Parameters), StringComparison.OrdinalIgnoreCase)) {
                    parameters = JsonSerializer.Deserialize<List<ParameterOverride>>(ref reader, options) ?? [];
                }
                else {
                    reader.Skip();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(methodName))
            throw new JsonException("Query registration is missing the required 'MethodName' property.");
        if (string.IsNullOrWhiteSpace(target))
            throw new JsonException($"Query '{methodName}' is missing its execution target (StoredProcName, SQLQuery, or SQLFile).");

        return new QuerySetting {
            MethodName = methodName,
            Target = target,
            ResultSetName = resultSetName,
            SourceType = sourceType,
            Parameters = parameters
        };
    }

    public override void Write(Utf8JsonWriter writer, QuerySetting value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString(nameof(QuerySetting.MethodName), value.MethodName);

        switch (value.SourceType) {
            case QuerySourceType.StoredProcedure:
                writer.WriteString("StoredProcName", value.Target);
                break;
            case QuerySourceType.FromFile:
                writer.WriteString("SQLFile", value.Target);
                break;
            case QuerySourceType.Text:
            default:
                writer.WriteString("SQLQuery", value.Target);
                break;
        }

        if (value.ResultSetName != null) {
            writer.WritePropertyName(nameof(QuerySetting.ResultSetName));
            JsonSerializer.Serialize(writer, value.Parameters, options);
        }

        if (value.Parameters != null && value.Parameters.Count > 0) {
            writer.WritePropertyName(nameof(QuerySetting.Parameters));
            JsonSerializer.Serialize(writer, value.Parameters, options);
        }

        writer.WriteEndObject();
    }
}