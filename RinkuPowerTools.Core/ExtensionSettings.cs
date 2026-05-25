using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RinkuPowerTools.Core;

public enum QuerySourceType {
    Text,
    StoredProcedure,
    FromFile
}

public class ExtensionSettings {
    public DbConnection GetConnection() => new SqlConnection(ConnectionString);
    public required string ConnectionString { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public List<QuerySetting> Queries { get; set; } = [];
}

public class ParameterOverride {
    public required string Name { get; set; }
    public string? Type { get; set; }
    public bool? IsNullable { get; set; }
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