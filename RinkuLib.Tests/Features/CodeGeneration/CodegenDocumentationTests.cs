using System.Data.Common;
using Rinku.Querying.Defaults;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS8765 // This test connection models a host-provided connection contract.
using System.Text.Json;
using RinkuPowerTools;
using Xunit;

namespace RinkuLib.Tests.Codegen;

public class CodegenDocumentationTests {
    [Fact]
    public void Configuration_example_deserializes_into_the_documented_settings() {
        const string json = """
        {
          "JsonFile": "appsettings.json",
          "ConnectionExtractionPath": "ConnectionStrings:Default",
          "OutputPath": "Data/Generated",
          "Namespace": "MyApp.Data",
          "IsInternal": false,
          "Queries": [
            {
              "MethodName": "GetTracksByAlbum",
              "SQLQuery": "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId >= @albumId"
            },
            {
              "MethodName": "ArchiveInvoices",
              "StoredProcName": "dbo.ArchiveInvoices",
              "ResultSetName": "ArchivedInvoice",
              "Parameters": [
                { "Name": "@cutoff", "Type": "datetime2", "IsNullable": false }
              ]
            }
          ]
        }
        """;

        var settings = JsonSerializer.Deserialize<ExtensionSettings>(json)!;

        Assert.Equal(ConnectionSourceType.JsonFile, settings.ConnectionSourceType);
        Assert.Equal("ConnectionStrings:Default", settings.ConnectionExtractionPath);
        Assert.Equal(2, settings.Queries.Count);
        Assert.Equal(QuerySourceType.StoredProcedure, settings.Queries[1].SourceType);
        Assert.Equal("datetime2", settings.Queries[1].Parameters[0].Type);
    }

    [Fact]
    public async Task Generated_code_example_contains_the_command_and_result_shape() {
        var settings = new ExtensionSettings {
            ClassName = "DbCommands",
            ConnectionTarget = "unused",
            Namespace = "MyApp.Data",
            Queries = [new QuerySetting {
                MethodName = "GetTracksByAlbum",
                Target = "SELECT TrackId AS Id, Name AS [Track Name], UnitPrice FROM tracks WHERE AlbumId >= @albumId",
                SourceType = QuerySourceType.Text
            }]
        };

        var code = await MainClassGenerator.GenerateClassCodeAsync(
            new FixedSchemaDiscoverer(),
            oldRoot: null,
            connection: new EmptyConnection(),
            settings,
            baseNamespace: "MyApp",
            CancellationToken.None);

        Assert.Contains("DbCommand GetTracksByAlbum(this DbConnection connection, int albumId)", code);
        Assert.Contains("command.Add(\"@albumId\", DbType.Int32, albumId)", code);
        Assert.Contains("public partial record GetTracksByAlbumResult(int Id, [TrueName(\"Track Name\")] string? Track_Name, decimal UnitPrice)", code);
    }

    private sealed class FixedSchemaDiscoverer : SchemaDiscoverer {
        public override Task<DiscoveredSchema> DiscoverSchemaAsync(ExtensionSettings settings, DbConnection connection, QuerySetting query, CancellationToken ct)
            => Task.FromResult(new DiscoveredSchema(
                query.Target,
                [new ParameterMetadata("@albumId", System.Data.DbType.Int32, false, 0, System.Data.ParameterDirection.Input, 0, 0)],
                [new ParameterMetadata("Id", System.Data.DbType.Int32, false, 0, System.Data.ParameterDirection.Input, 0, 0),
                 new ParameterMetadata("Track Name", System.Data.DbType.String, true, 0, System.Data.ParameterDirection.Input, 0, 0),
                 new ParameterMetadata("UnitPrice", System.Data.DbType.Decimal, false, 0, System.Data.ParameterDirection.Input, 0, 0)]));
    }

    private sealed class EmptyConnection : DbConnection {
        public override string ConnectionString { get; [param: AllowNull] set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
