using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Rinku;
using Rinku.Querying;
using Xunit;

namespace RinkuLib.Tests.Documentation;

public class DocumentationSqlRuntimeTests {
    static readonly string[] CaseNames = [
        "named-marker-active",
        "named-marker-inactive",
        "parameter-marker-inactive",
        "parameter-marker-active",
        "parenthesized-marker",
        "conditional-join-inactive",
        "conditional-join-active",
        "repeated-section-marker",
        "negated-marker-inactive",
        "negated-marker-active",
        "merged-footprint-partial",
        "merged-footprint-complete",
        "footprint-boundary",
        "preserved-comment",
        "numeric-pagination",
        "invariant-numbers",
        "numeric-boolean",
        "quoted-value-escaping",
        "trusted-raw-value",
        "collection-expansion",
        "optional-handler-value",
        "custom-variable-character"
    ];
    static readonly AsyncLocal<int> TargetCase = new();
    static readonly AsyncLocal<int> CurrentCase = new();

    public static IEnumerable<object?[]> SqlCases()
        => CaseNames.Select((name, index) => new object?[] { index, name });

    [Theory]
    [MemberData(nameof(SqlCases))]
    public void Conditional_SQL_example_matches_the_generated_command(
        int index,
        string name) {
        Assert.Equal(CaseNames[index], name);
        TargetCase.Value = index;
        CurrentCase.Value = 0;
        VerifySelectedCase();
        Assert.Equal(CaseNames.Length, CurrentCase.Value);
    }

    static void VerifySelectedCase() {
        AssertSql(
            "SELECT AlbumId AS Id, Title, /*IncludeYear*/ReleaseYear FROM albums",
            values => values.Use("IncludeYear"),
            "SELECT AlbumId AS Id, Title, ReleaseYear FROM albums");
        AssertSql(
            "SELECT AlbumId AS Id, Title, /*IncludeYear*/ReleaseYear FROM albums",
            null,
            "SELECT AlbumId AS Id, Title FROM albums");
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums WHERE /*@artistId*/ArtistId = @artistId",
            null,
            "SELECT AlbumId AS Id, Title FROM albums");
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums WHERE /*@artistId*/ArtistId = @artistId",
            values => values.Use("@artistId", 7),
            "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
        AssertSql(
            "SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min AND (Country = @country OR /*@city*/City = @city)",
            values => {
                values.Use("@min", 100m);
                values.Use("@country", "Canada");
            },
            "SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min AND (Country = @country)");
        AssertSql(
            "SELECT i.InvoiceId AS Id FROM invoices i /*@country*/JOIN customers c ON c.CustomerId = i.CustomerId WHERE c.Country = ?@country",
            null,
            "SELECT i.InvoiceId AS Id FROM invoices i");
        AssertSql(
            "SELECT i.InvoiceId AS Id FROM invoices i /*@country*/JOIN customers c ON c.CustomerId = i.CustomerId WHERE c.Country = ?@country",
            values => values.Use("@country", "Canada"),
            "SELECT i.InvoiceId AS Id FROM invoices i JOIN customers c ON c.CustomerId = i.CustomerId WHERE c.Country = @country");
        AssertSql(
            "SELECT Country, COUNT(*) AS Total FROM customers /*Grouped*/GROUP BY Country /*Grouped*/HAVING COUNT(*) > 1",
            values => values.Use("Grouped"),
            "SELECT Country, COUNT(*) AS Total FROM customers GROUP BY Country HAVING COUNT(*) > 1");
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums WHERE /*!All*/IsArchived = 0",
            null,
            "SELECT AlbumId AS Id, Title FROM albums WHERE IsArchived = 0");
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums WHERE /*!All*/IsArchived = 0",
            values => values.Use("All"),
            "SELECT AlbumId AS Id, Title FROM albums");
        AssertSql(
            "SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceDate >= ?@from &AND InvoiceDate < ?@until",
            values => values.Use("@from", new DateTime(2020, 1, 1)),
            "SELECT InvoiceId AS Id, Total FROM invoices");
        AssertSql(
            "SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceDate >= ?@from &AND InvoiceDate < ?@until",
            values => {
                values.Use("@from", new DateTime(2020, 1, 1));
                values.Use("@until", new DateTime(2021, 1, 1));
            },
            "SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceDate >= @from AND InvoiceDate < @until");
        AssertSql(
            "SELECT DISTINCT??? /*ShowId*/AlbumId AS Id, Title FROM albums",
            null,
            "SELECT DISTINCT Title FROM albums");
        AssertSql(
            "/*~ application note */SELECT AlbumId AS Id, Title FROM albums",
            null,
            "/* application note */SELECT AlbumId AS Id, Title FROM albums");

        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET @skip_N ROWS FETCH NEXT @take_N ROWS ONLY",
            values => {
                values.Use("@skip", 20);
                values.Use("@take", 10);
            },
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");
        AssertSql(
            "SELECT @integer_N AS IntegerValue, @fraction_N AS FractionValue",
            values => {
                values.Use("@integer", 46u);
                values.Use("@fraction", 1.5m);
            },
            "SELECT 46 AS IntegerValue, 1.5 AS FractionValue");
        AssertSql(
            "SELECT @enabled_N AS Enabled",
            values => values.Use("@enabled", true),
            "SELECT 1 AS Enabled");
        AssertSql(
            "SELECT ArtistId AS Id, Name FROM artists WHERE Name = @name_S",
            values => values.Use("@name", "O'Brien"),
            "SELECT ArtistId AS Id, Name FROM artists WHERE Name = 'O''Brien'");
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY @orderBy_R",
            values => values.Use("@orderBy", "Title DESC"),
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC");
        AssertSql(
            "SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_X) OR ParentGenreId IN (@ids_X)",
            values => values.Use("@ids", new[] { 2, 5 }),
            "SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_1, @ids_2) OR ParentGenreId IN (@ids_1, @ids_2)",
            ["@ids_1", "@ids_2"]);
        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET ?@skip_N ROWS",
            null,
            "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");

        AssertSql(
            "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = :albumId AND Title = ?:title",
            values => values.Use(":albumId", 12),
            "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = :albumId",
            [":albumId"],
            ':');
    }

    static void AssertSql(
        string template,
        Action<QueryBuilder>? configure,
        string expectedSql,
        string[]? expectedParameters = null,
        char variableCharacter = '@') {
        int current = CurrentCase.Value;
        CurrentCase.Value = current + 1;
        if (current != TargetCase.Value)
            return;

        var connection = new RecordingDbConnection();
        var query = new QueryCommand(template, variableCharacter);
        QueryBuilder values = query.StartBuilder();
        configure?.Invoke(values);
        values.Execute(connection);

        RecordingDbCommand command = connection.LastCommand
            ?? throw new InvalidOperationException("The example did not execute a command.");
        if (command.CommandText != expectedSql) {
            throw new InvalidOperationException(
                $"Generated SQL did not match. Expected '{expectedSql}', got '{command.CommandText}'.");
        }

        if (expectedParameters is null)
            return;

        string[] actual = command.ExecutedParameterNames;
        if (!actual.SequenceEqual(expectedParameters)) {
            throw new InvalidOperationException(
                $"Generated parameters did not match. Expected '{string.Join(", ", expectedParameters)}', got '{string.Join(", ", actual)}'.");
        }
    }
}

internal sealed class RecordingDbConnection : DbConnection {
    ConnectionState state;

    public RecordingDbCommand? LastCommand { get; private set; }
    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "Documentation";
    public override string DataSource => "Documentation";
    public override string ServerVersion => "1";
    public override ConnectionState State => state;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => state = ConnectionState.Closed;
    public override void Open() => state = ConnectionState.Open;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand()
        => LastCommand = new RecordingDbCommand(this);
}

internal sealed class RecordingDbCommand(RecordingDbConnection connection) : DbCommand {
    readonly RecordingDbParameterCollection parameters = new();
    public string[] ExecutedParameterNames { get; private set; } = [];

    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    [AllowNull]
    protected override DbConnection DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override int ExecuteNonQuery() {
        ExecutedParameterNames = parameters
            .Cast<DbParameter>()
            .Select(parameter => parameter.ParameterName)
            .ToArray();
        return 1;
    }
    public override object ExecuteScalar() => 1;
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new RecordingDbParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => throw new NotSupportedException();
}

internal sealed class RecordingDbParameter : DbParameter {
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = "";
    [AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }
    public override void ResetDbType() { }
}

internal sealed class RecordingDbParameterCollection : DbParameterCollection {
    readonly List<DbParameter> values = [];

    public override int Count => values.Count;
    public override object SyncRoot => ((ICollection)values).SyncRoot;
    public override int Add(object value) {
        values.Add((DbParameter)value);
        return values.Count - 1;
    }
    public override void AddRange(Array values) {
        foreach (object value in values)
            Add(value);
    }
    public override void Clear() => values.Clear();
    public override bool Contains(object value) => values.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)values).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => values.GetEnumerator();
    public override int IndexOf(object value) => values.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName)
        => values.FindIndex(parameter => parameter.ParameterName == parameterName);
    public override void Insert(int index, object value) => values.Insert(index, (DbParameter)value);
    public override void Remove(object value) => values.Remove((DbParameter)value);
    public override void RemoveAt(int index) => values.RemoveAt(index);
    public override void RemoveAt(string parameterName) => values.RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => values[index];
    protected override DbParameter GetParameter(string parameterName) => values[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => values[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) {
        int index = IndexOf(parameterName);
        if (index < 0)
            values.Add(value);
        else
            values[index] = value;
    }
}
