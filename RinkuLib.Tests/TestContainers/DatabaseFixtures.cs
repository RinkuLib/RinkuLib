using System.Data;
using System.Reflection;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.MariaDb;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace RinkuLib.Tests.TestContainers;
#pragma warning disable CS0618
public delegate T CnnMaker<T>(string connectionString) where T : IDbConnection;
public static class Connections {
    public static MsSqlContainer GetMSSQLContainer()
        => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public static PostgreSqlContainer GetPostgreContainer()
        => new PostgreSqlBuilder("postgres:16-alpine").Build();
    public static MySqlContainer GetMySQLContainer()
        => new MySqlBuilder("mysql:8.0").Build();
    public static MariaDbContainer GetMariaDBContainer()
        => new MariaDbBuilder("mariadb:11").Build();
    public static OracleContainer GetOracleContainer()
        => new OracleBuilder("gvenzl/oracle-free:latest").Build();

    public const string DbFileName = "TestDB.db";
    public static string GetSQLiteConnectionString()
        => $"Data Source={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DbFileName)}";
    public static CnnMaker<T> GetFor<T>() where T : IDbConnection {
        if (typeof(T) == typeof(SqlConnection))
            return (CnnMaker<T>)(object)new CnnMaker<SqlConnection>(s => new SqlConnection(s));
        if (typeof(T) == typeof(System.Data.SqlClient.SqlConnection))
            return (CnnMaker<T>)(object)new CnnMaker<System.Data.SqlClient.SqlConnection>(s => new System.Data.SqlClient.SqlConnection(s));
        if (typeof(T) == typeof(NpgsqlConnection))
            return (CnnMaker<T>)(object)new CnnMaker<NpgsqlConnection>(s => new NpgsqlConnection(s));
        if (typeof(T) == typeof(MySqlConnection))
            return (CnnMaker<T>)(object)new CnnMaker<MySqlConnection>(s => new MySqlConnection(s));
        if (typeof(T) == typeof(MySql.Data.MySqlClient.MySqlConnection))
            return (CnnMaker<T>)(object)new CnnMaker<MySql.Data.MySqlClient.MySqlConnection>(s => new MySql.Data.MySqlClient.MySqlConnection(s));
        if (typeof(T) == typeof(SqliteConnection))
            return (CnnMaker<T>)(object)new CnnMaker<SqliteConnection>(s => new SqliteConnection(s));
        if (typeof(T) == typeof(System.Data.SQLite.SQLiteConnection))
            return (CnnMaker<T>)(object)new CnnMaker<System.Data.SQLite.SQLiteConnection>(s => new System.Data.SQLite.SQLiteConnection(s));
        if (typeof(T) == typeof(OracleConnection))
            return (CnnMaker<T>)(object)new CnnMaker<OracleConnection>(s => new OracleConnection(s));
        throw new NotSupportedException($"The connection type {typeof(T).FullName} is not registered in the Connections helper.");
    }
    public static (DockerContainer?, Func<string>, CnnMaker<T>) GetAllFor<T>() where T : IDbConnection {
        if (typeof(T) == typeof(SqlConnection)) { 
            var c = GetMSSQLContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<SqlConnection>(s => new SqlConnection(s)));
        }
        if (typeof(T) == typeof(System.Data.SqlClient.SqlConnection)) {
            var c = GetMSSQLContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<System.Data.SqlClient.SqlConnection>(s => new System.Data.SqlClient.SqlConnection(s)));
        }
        if (typeof(T) == typeof(NpgsqlConnection)) {
            var c = GetPostgreContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<NpgsqlConnection>(s => new NpgsqlConnection(s)));
        }
        if (typeof(T) == typeof(MySqlConnection)) {
            var c = GetMySQLContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<MySqlConnection>(s => new MySqlConnection(s)));
        }
        if (typeof(T) == typeof(MySql.Data.MySqlClient.MySqlConnection)) {
            var c = GetMySQLContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<MySql.Data.MySqlClient.MySqlConnection>(s => new MySql.Data.MySqlClient.MySqlConnection(s)));
        }
        if (typeof(T) == typeof(SqliteConnection)) {
            return (null, GetSQLiteConnectionString, (CnnMaker<T>)(object)new CnnMaker<SqliteConnection>(s => new SqliteConnection(s)));
        }
        if (typeof(T) == typeof(System.Data.SQLite.SQLiteConnection)) {
            return (null, GetSQLiteConnectionString, (CnnMaker<T>)(object)new CnnMaker<System.Data.SQLite.SQLiteConnection>(s => new System.Data.SQLite.SQLiteConnection(s)));
        }
        if (typeof(T) == typeof(OracleConnection)) {
            var c = GetOracleContainer();
            return (c, c.GetConnectionString, (CnnMaker<T>)(object)new CnnMaker<OracleConnection>(s => new OracleConnection(s)));
        }
        throw new NotSupportedException($"The connection type {typeof(T).FullName} is not registered in the Connections helper.");
    }
}
#pragma warning restore CS0618
public class DBFixture<T> : IAsyncLifetime where T : IDbConnection {
    private readonly DockerContainer? Container;
    public string ConnectionString;
    public Func<string> CnnStrGetter;
    public CnnMaker<T> CnnMaker;
    public DBFixture() {
        (Container, CnnStrGetter, CnnMaker) = Connections.GetAllFor<T>();
        ConnectionString = string.Empty;
    }
    public T GetConnection() => CnnMaker(ConnectionString);
    public async ValueTask InitializeAsync() {
        if (Container is not null)
            await Container.StartAsync();
        ConnectionString = CnnStrGetter();
    }
    public async ValueTask DisposeAsync() { if (Container is not null) await Container.DisposeAsync(); }
}
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RepeatAttribute(int count) : DataAttribute {
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker) {
        var results = new List<ITheoryDataRow>();
        for (int i = 1; i <= count; i++) 
            results.Add(new TheoryDataRow(i));
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(results);
    }

    public override bool SupportsDiscoveryEnumeration() => true;
}