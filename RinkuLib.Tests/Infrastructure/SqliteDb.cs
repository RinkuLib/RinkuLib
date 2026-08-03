using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// A private SQLite database file for one test class, created and seeded at fixture start and deleted
/// at the end. Connections are handed out closed so the execution paths that open and close on demand
/// stay exercised.
/// </summary>
public sealed class SqliteDb : IDisposable {
    private readonly string _path;
    public readonly string ConnectionString;

    static SqliteDb() => SQLitePCL.Batteries.Init();

    public SqliteDb() {
        var dir = Path.Combine(Path.GetTempPath(), "rinku-tests");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={_path}";
        using var cnn = Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Users (
                ID INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Email TEXT NULL,
                IsActive INTEGER NOT NULL,
                Salary REAL NULL
            );
            INSERT INTO Users (ID, Name, Email, IsActive, Salary) VALUES
                (1, 'John', NULL, 1, 10.5),
                (2, 'Victor', 'victor@corp.com', 0, 20.0),
                (3, 'Alice', 'alice@corp.com', 1, NULL);
            CREATE TABLE Scratch (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                Val INTEGER NULL,
                Txt TEXT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A closed connection to the database.</summary>
    public SqliteConnection GetConnection() => new(ConnectionString);

    /// <summary>An open connection to the database.</summary>
    public SqliteConnection Open() {
        var cnn = GetConnection();
        cnn.Open();
        return cnn;
    }

    public int CountScratchRows() {
        using var cnn = Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Scratch";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Releases this database's pooled handles so its file can go, and only this one's. Clearing every pool
    /// in the process would reach the fixtures of the other classes running beside this one, which is a way
    /// for one class finishing to disturb another mid-test.
    /// </summary>
    public void Dispose() {
        using (var cnn = GetConnection())
            SqliteConnection.ClearPool(cnn);
        try {
            File.Delete(_path);
        }
        catch (IOException) {
        }
    }
}
