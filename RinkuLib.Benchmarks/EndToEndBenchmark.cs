using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Rinku;
using Rinku.Mapping;
using Rinku.Mapping.Defaults;
using Rinku.Querying;
using Rinku.Querying.Defaults;
using Rinku.Querying.Parameters;
using RinkuLib.Tests.TestContainers;
using Rinku.Mapping.Parsers;

namespace RinkuLib.Benchmarks;

/// <summary>
/// End-to-end examples over a real SQL Server connection. Each measured method binds its parameters,
/// executes its command, reads the provider result and produces the value a caller consumes. The first
/// kind compares Dapper with equivalent Rinku public APIs, the second compares Rinku's public command and
/// SQL-string routes, and the third measures complete Rinku-specific scenarios without a useful Dapper
/// equivalent. The Dapper comparisons mirror established ORM benchmark suites where practical:
/// <list type="bullet">
/// <item>DapperLib/Dapper (benchmarks/Dapper.Tests.Performance) for the wide <c>Post</c> row and the rotating id.</item>
/// <item>FransBouma/RawDataAccessBencher for equal connection handling across libraries.</item>
/// <item>InfoTechBridge/OrmBenchmark for the single-row-repeated and bulk-set-fetch shapes.</item>
/// </list>
/// Fairness rests on four choices, applied identically to both libraries:
/// <list type="bullet">
/// <item>A wide 13-column row (varchar(max) text plus nine nullable ints) so materialization is a real cost.</item>
/// <item>5000 seeded rows with the queried id rotating each call, so no single hot row skews the cache.</item>
/// <item>One connection opened in setup and reused, so the run measures mapping, not pool rent/return.</item>
/// <item>A setup pass asserting Dapper and Rinku return identical results for every category.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EndToEndBenchmark : IAsyncDisposable {
    private const int RowCount = 5000;

    private DBFixture<SqlConnection> _fixture = null!;
    private SqlConnection cnn = null!;
    private BatchUpdateArgs[] _batchItems = [];
    private DataTable _tableIds = null!;
    private QueryCommand _outputProcCmd = null!;

    private int _i;
    private bool _fixId;
    private int NextId() {
        if (_fixId)
            return 1;
        _i++;
        if (_i > RowCount)
            _i = 1;
        return _i;
    }

    private const string SelectPostSql = "SELECT * FROM Posts WHERE Id = @id";
    private const string SelectNullableValueSql = "SELECT Counter2 FROM Posts WHERE Id = @id";
    private const string SelectNullableReferenceSql = "SELECT CAST(Counter2 AS VARCHAR(20)) FROM Posts WHERE Id = @id";
    private const string SelectPostsSql = "SELECT TOP (@take) * FROM Posts ORDER BY Id";
    private const string SelectComplexSql = "SELECT p.Id, p.Name, c.Id, c.Name, c.Description FROM Products p INNER JOIN Categories c ON p.CategoryId = c.Id WHERE p.Id = @id";
    private const string UpdateSql = "UPDATE Posts SET Counter1 = @val WHERE Id = @id";
    private const string InClauseSql = "SELECT * FROM Posts WHERE Id IN @ids";
    private const string InClauseSqlRinku = "SELECT * FROM Posts WHERE Id IN (@ids_X)";
    private const string CountSql = "SELECT COUNT(*) FROM Posts";
    private const string CountByIdSql = "SELECT COUNT(*) FROM Posts WHERE Id = @id";
    private const string ConditionalCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = ?@id";
    private const string SelectIdsSql = "SELECT Id FROM Posts";
    private const string SelectCustomPostSql = "SELECT Id, Text FROM Posts WHERE Id = @id";
    private const string DynamicProjectionSql = "?SELECT Id!, Text&, CreationDate FROM Posts WHERE Id = @id";
    private const string RawProjectionSql = "SELECT @Cols_R FROM Posts WHERE Id = @id";
    private const string ReadPostsSql = "SELECT TOP (50) Id, LEN(Text) FROM Posts ORDER BY Id";
    private const string DbStringSql = "SELECT COUNT(*) FROM BenchmarkCodes WHERE Value = @value";
    private const string TableValuedParameterSql = "SELECT COUNT(*) FROM @ids";
    private const string ShapeRowsSql = @"
        SELECT 'circle' AS Kind, 2 AS Radius, CAST(NULL AS INT) AS Side
        UNION ALL SELECT 'square', NULL, 3
        UNION ALL SELECT 'circle', 4, NULL
        UNION ALL SELECT 'square', NULL, 5";
    private const string GetPostProcedure = "dbo.GetBenchmarkPost";
    private const string OutputProcedure = "dbo.GetBenchmarkOutput";
    private const string DapperLiteralCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = {=id}";
    private const string RinkuLiteralCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = @id_N";
    private const string SelectPostCommentsSql = @"
        SELECT p.Id, CAST(p.Id AS NVARCHAR(20)) AS Name,
               c.Id, c.Text AS Value
        FROM Posts p
        LEFT JOIN PostComments c ON c.PostId = p.Id
        ORDER BY p.Id, c.Id";
    private const string SelectPostCommentSetsSql = @"
        SELECT Id, CAST(Id AS NVARCHAR(20)) AS Name
        FROM Posts
        ORDER BY Id;
        SELECT PostId, Id, Text AS Title
        FROM PostComments
        ORDER BY PostId, Id";
    private const string MultiSql = "SELECT * FROM Posts WHERE Id = @a; SELECT * FROM Posts WHERE Id = @b";

    private static readonly QueryCommand QueryPostCmd = new(SelectPostSql);
    private static readonly QueryCommand QueryNullableValueCmd = new(SelectNullableValueSql);
    private static readonly QueryCommand QueryNullableReferenceCmd = new(SelectNullableReferenceSql);
    private static readonly QueryCommand QueryPostsCmd = new(SelectPostsSql);
    private static readonly QueryCommand QueryComplexCmd = new(SelectComplexSql);
    private static readonly QueryCommand ExecuteUpdateCmd = new(UpdateSql);
    private static readonly QueryCommand InClauseCmd = new(InClauseSqlRinku);
    private static readonly QueryCommand CountCmd = new(CountSql);
    private static readonly QueryCommand CountByIdCmd = new(CountByIdSql);
    private static readonly QueryCommand ConditionalCountCmd = new(ConditionalCountSql);
    private static readonly QueryCommand SelectIdsCmd = new(SelectIdsSql);
    private static readonly QueryCommand SelectCustomPostCmd = new(SelectCustomPostSql);
    private static readonly QueryCommand DynamicProjectionCmd = new(DynamicProjectionSql);
    private static readonly QueryCommand RawProjectionCmd = new(RawProjectionSql);
    private static readonly QueryCommand ReadPostsCmd = new(ReadPostsSql);
    private static readonly QueryCommand DbStringCmd = new(DbStringSql);
    private static readonly QueryCommand TableValuedParameterCmd = new(TableValuedParameterSql);
    private static readonly QueryCommand ShapeRowsCmd = new(ShapeRowsSql);
    private static readonly QueryCommand GetPostProcCmd = new(GetPostProcedure, ["id"]);
    private static readonly QueryCommand SelectPostCommentsCmd = new(SelectPostCommentsSql);
    private static readonly QueryCommand SelectPostCommentSetsCmd = new(SelectPostCommentSetsSql);
    private static readonly QueryCommand RinkuLiteralCountCmd = new(RinkuLiteralCountSql);
    private static readonly QueryCommand MultiCmd = new(MultiSql);

    static EndToEndBenchmark()
        => TypeParsingInfo.GetOrAdd<BenchmarkShape>()
            .AddPossibleConstruction(typeof(BenchmarkShape).GetMethod(nameof(BenchmarkShape.FromRow))!);

    [GlobalSetup]
    public Task Setup() => Setup(true);
    public async Task Setup(bool withValidate) {
        SqlMapper.AddTypeHandler(StrongPostIdDapperHandler.Instance);
        TypeParsingInfo.AddOrSet(typeof(StrongPostId), StrongPostIdTypeInfo.Instance);
        SelectCustomPostCmd.UpdateParamCache("@id", StrongPostIdParamInfo.Instance);
        DbStringCmd.UpdateParamCache("@value", SizedDbParamCache.Get(DbType.AnsiString, 50));
        TableValuedParameterCmd.UpdateParamCache("@ids", SqlServerTableParamInfo.Instance);

        _fixture = new DBFixture<SqlConnection>();
        await _fixture.InitializeAsync();

        await using (var seed = _fixture.GetConnection()) {
            await seed.OpenAsync();

            async Task Exec(string sql) {
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            await Exec(@"
        CREATE TABLE Posts (
            Id INT IDENTITY PRIMARY KEY,
            Text VARCHAR(MAX) NOT NULL,
            CreationDate DATETIME NOT NULL,
            LastChangeDate DATETIME NOT NULL,
            Counter1 INT, Counter2 INT, Counter3 INT, Counter4 INT, Counter5 INT,
            Counter6 INT, Counter7 INT, Counter8 INT, Counter9 INT
        );

        CREATE TABLE Categories (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100),
            Description NVARCHAR(MAX)
        );

        CREATE TABLE Products (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100),
            CategoryId INT REFERENCES Categories(Id)
        );

        CREATE TABLE PostComments (
            Id INT NOT NULL PRIMARY KEY,
            PostId INT NOT NULL REFERENCES Posts(Id),
            Text NVARCHAR(100) NOT NULL
        );

        CREATE TABLE BenchmarkCodes (
            Id INT NOT NULL PRIMARY KEY,
            Value VARCHAR(50) NOT NULL UNIQUE
        );");

            await Exec($@"
        SET NOCOUNT ON;
        DECLARE @i INT = 0;
        WHILE @i < {RowCount} BEGIN
            INSERT INTO Posts (Text, CreationDate, LastChangeDate)
            VALUES (REPLICATE('x', 2000), GETDATE(), GETDATE());
            INSERT INTO BenchmarkCodes (Id, Value)
            VALUES (@i + 1, CONCAT('code-', @i + 1));
            SET @i += 1;
        END");

            await Exec("INSERT INTO Categories (Id, Name, Description) VALUES (1, 'Electronics', 'Gadgets and stuff')");
            await Exec("INSERT INTO Products (Id, Name, CategoryId) VALUES (1, 'Laptop', 1)");
            await Exec($@"
        DECLARE @postId INT = 1;
        WHILE @postId <= {RowCount} BEGIN
            INSERT INTO PostComments (Id, PostId, Text)
            VALUES (@postId * 10 + 1, @postId, 'first'),
                   (@postId * 10 + 2, @postId, 'second'),
                   (@postId * 10 + 3, @postId, 'third');
            SET @postId += 1;
        END");

            await Exec(@"
        CREATE PROCEDURE dbo.GetBenchmarkPost @id INT AS
        BEGIN
            SET NOCOUNT ON;
            SELECT * FROM Posts WHERE Id = @id;
        END");

            await Exec(@"
        CREATE PROCEDURE dbo.GetBenchmarkOutput @input INT, @doubled INT OUTPUT AS
        BEGIN
            SET NOCOUNT ON;
            SET @doubled = @input * 2;
            SELECT @doubled;
            RETURN (@input + 1);
        END");

            await Exec("CREATE TYPE dbo.BenchmarkIds AS TABLE (Id INT NOT NULL)");
        }

        cnn = _fixture.GetConnection();
        await cnn.OpenAsync();
        _outputProcCmd = QueryCommand.FromProc(OutputProcedure, cnn);
        _batchItems = [.. Enumerable.Range(1, 64).Select(id => new BatchUpdateArgs(1, id))];
        _tableIds = new DataTable();
        _tableIds.Columns.Add("Id", typeof(int));
        foreach (var id in Enumerable.Range(1, 50))
            _tableIds.Rows.Add(id);
        if (!withValidate)
            return;
        Console.WriteLine("--- Starting Full Equivalence Validation ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All comparison categories match ---");

        Console.WriteLine("--- Starting Full Equivalence Validation (second pass) ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All comparison categories match (second pass) ---");
    }

    private async Task Validate() {
        _fixId = true;
        try {
            var dapperFirstSync = Dapper_QueryFirst();
            var commandFirstSync = RinkuCommand_QueryT();
            var sqlFirstSync = RinkuSql_QueryT();
            if (dapperFirstSync != commandFirstSync || dapperFirstSync != sqlFirstSync)
                throw new Exception("Query one Sync: Results differ.");

            var dapperOptionalSync = Dapper_QueryFirstOrDefault();
            var commandOptionalSync = RinkuCommand_QueryOptionalT();
            var sqlOptionalSync = RinkuSql_QueryOptionalT();
            var commandOptionalNullableSync = RinkuCommand_QueryOptionalNullableT();
            var sqlOptionalNullableSync = RinkuSql_QueryOptionalNullableT();
            if (dapperOptionalSync != commandOptionalSync || dapperOptionalSync != sqlOptionalSync || dapperOptionalSync != commandOptionalNullableSync || dapperOptionalSync != sqlOptionalNullableSync)
                throw new Exception("Query one (or default) Sync: Results differ.");

            if (Dapper_QueryFirstNullableValue() is not null || RinkuCommand_QueryNullableValue() is not null || RinkuSql_QueryNullableValue() is not null)
                throw new Exception("Query nullable value Sync: Expected the nullable column to be NULL.");

            if (Dapper_QueryFirstNullableReference() is not null || RinkuCommand_QueryMaybeNullReference() is not null || RinkuSql_QueryMaybeNullReference() is not null)
                throw new Exception("Query nullable reference Sync: Expected the nullable column to be NULL.");

            if (Dapper_QueryFirstOrDefaultNullableReference() is not null || RinkuCommand_QueryOptionalNullableReference() is not null || RinkuSql_QueryOptionalNullableReference() is not null)
                throw new Exception("Query nullable reference (or default) Sync: Expected the nullable column to be NULL.");

            var dapperSingleSync = Dapper_QuerySingle();
            var commandSingleSync = RinkuCommand_QuerySingleT();
            var sqlSingleSync = RinkuSql_QuerySingleT();
            if (dapperSingleSync != commandSingleSync || dapperSingleSync != sqlSingleSync)
                throw new Exception("Query one (single) Sync: Results differ.");

            var dapperSingleOrDefaultSync = Dapper_QuerySingleOrDefault();
            var commandSingleOrDefaultSync = RinkuCommand_QuerySingleOrDefaultT();
            var sqlSingleOrDefaultSync = RinkuSql_QuerySingleOrDefaultT();
            if (dapperSingleOrDefaultSync != commandSingleOrDefaultSync || dapperSingleOrDefaultSync != sqlSingleOrDefaultSync)
                throw new Exception("Query one (single or default) Sync: Results differ.");

            var dapperFirstAsync = await Dapper_QueryFirstAsync();
            var commandFirstAsync = await RinkuCommand_QueryTAsync();
            var sqlFirstAsync = await RinkuSql_QueryTAsync();
            if (dapperFirstAsync != commandFirstAsync || dapperFirstAsync != sqlFirstAsync)
                throw new Exception("Query one Async: Results differ.");

            var dapperOptionalAsync = await Dapper_QueryFirstOrDefaultAsync();
            var commandOptionalAsync = await RinkuCommand_QueryOptionalTAsync();
            var sqlOptionalAsync = await RinkuSql_QueryOptionalTAsync();
            var commandOptionalNullableAsync = await RinkuCommand_QueryOptionalNullableTAsync();
            var sqlOptionalNullableAsync = await RinkuSql_QueryOptionalNullableTAsync();
            if (dapperOptionalAsync != commandOptionalAsync || dapperOptionalAsync != sqlOptionalAsync || dapperOptionalAsync != commandOptionalNullableAsync || dapperOptionalAsync != sqlOptionalNullableAsync)
                throw new Exception("Query one (or default) Async: Results differ.");

            if (await Dapper_QueryFirstNullableValueAsync() is not null || await RinkuCommand_QueryNullableValueAsync() is not null || await RinkuSql_QueryNullableValueAsync() is not null)
                throw new Exception("Query nullable value Async: Expected the nullable column to be NULL.");

            if (await Dapper_QueryFirstNullableReferenceAsync() is not null || await RinkuCommand_QueryMaybeNullReferenceAsync() is not null || await RinkuSql_QueryMaybeNullReferenceAsync() is not null)
                throw new Exception("Query nullable reference Async: Expected the nullable column to be NULL.");

            if (await Dapper_QueryFirstOrDefaultNullableReferenceAsync() is not null || await RinkuCommand_QueryOptionalNullableReferenceAsync() is not null || await RinkuSql_QueryOptionalNullableReferenceAsync() is not null)
                throw new Exception("Query nullable reference (or default) Async: Expected the nullable column to be NULL.");

            var dapperSingleAsync = await Dapper_QuerySingleAsync();
            var commandSingleAsync = await RinkuCommand_QuerySingleTAsync();
            var sqlSingleAsync = await RinkuSql_QuerySingleTAsync();
            if (dapperSingleAsync != commandSingleAsync || dapperSingleAsync != sqlSingleAsync)
                throw new Exception("Query one (single) Async: Results differ.");

            var dapperSingleOrDefaultAsync = await Dapper_QuerySingleOrDefaultAsync();
            var commandSingleOrDefaultAsync = await RinkuCommand_QuerySingleOrDefaultTAsync();
            var sqlSingleOrDefaultAsync = await RinkuSql_QuerySingleOrDefaultTAsync();
            if (dapperSingleOrDefaultAsync != commandSingleOrDefaultAsync || dapperSingleOrDefaultAsync != sqlSingleOrDefaultAsync)
                throw new Exception("Query one (single or default) Async: Results differ.");

            var dapperLifecycle = await Dapper_ConnectionLifecycleAsync();
            var commandLifecycle = await RinkuCommand_ConnectionLifecycleAsync();
            var sqlLifecycle = await RinkuSql_ConnectionLifecycleAsync();
            if (dapperLifecycle != commandLifecycle || dapperLifecycle != sqlLifecycle)
                throw new Exception("Connection lifecycle Async: Results differ.");

            foreach (int rowCount in new[] { 50, RowCount }) {
                var dapperStreamSync = Dapper_QueryUnbuffered(rowCount);
                var commandStreamSync = RinkuCommand_QueryIEnumerable(rowCount);
                var sqlStreamSync = RinkuSql_QueryIEnumerable(rowCount);
                if (dapperStreamSync != commandStreamSync || dapperStreamSync != sqlStreamSync)
                    throw new Exception($"Query Sync (Stream): sums differ for {rowCount} rows.");

                var dapperListSync = Dapper_QueryBuffered(rowCount);
                var commandListSync = RinkuCommand_QueryList(rowCount);
                var sqlListSync = RinkuSql_QueryList(rowCount);
                if (dapperListSync.Count != commandListSync.Count || dapperListSync.Count != sqlListSync.Count)
                    throw new Exception($"Query Buffered Sync: collections differ for {rowCount} rows.");
                for (var i = 0; i < dapperListSync.Count; i++)
                    if (dapperListSync[i] != commandListSync[i] || dapperListSync[i] != sqlListSync[i])
                        throw new Exception($"Query Buffered Sync: collections differ for {rowCount} rows.");

                var dapperStreamAsync = await Dapper_QueryUnbufferedAsync(rowCount);
                var commandStreamAsync = await RinkuCommand_StreamQueryAsync(rowCount);
                var sqlStreamAsync = await RinkuSql_StreamQueryAsync(rowCount);
                if (dapperStreamAsync != commandStreamAsync || dapperStreamAsync != sqlStreamAsync)
                    throw new Exception($"Query Async (Stream): sums differ for {rowCount} rows.");

                var dapperListAsync = await Dapper_QueryAsyncBuffered(rowCount);
                var commandListAsync = await RinkuCommand_QueryAsyncList(rowCount);
                var sqlListAsync = await RinkuSql_QueryAsyncList(rowCount);
                if (dapperListAsync.Count != commandListAsync.Count || dapperListAsync.Count != sqlListAsync.Count)
                    throw new Exception($"Query Buffered Async: collections differ for {rowCount} rows.");
                for (var i = 0; i < dapperListAsync.Count; i++)
                    if (dapperListAsync[i] != commandListAsync[i] || dapperListAsync[i] != sqlListAsync[i])
                        throw new Exception($"Query Buffered Async: collections differ for {rowCount} rows.");
            }

            var dapperDynamic = await Dapper_QueryAsyncDynamic();
            var commandDynamic = await RinkuCommand_QueryAsyncDynaObject();
            var sqlDynamic = await RinkuSql_QueryAsyncDynaObject();
            if (dapperDynamic != commandDynamic || dapperDynamic != sqlDynamic)
                throw new Exception("Dynamic Async: Values differ.");

            foreach (bool includeDetails in new[] { false, true }) {
                var dapperProjection = await Dapper_DynamicProjection(includeDetails);
                var rinkuProjection = await RinkuCommand_DynamicProjection(includeDetails);
                if (!SameProjection(dapperProjection, rinkuProjection)
                    || dapperProjection.Id != 1
                    || (includeDetails
                        ? dapperProjection.Text?.Length != 2000 || !dapperProjection.CreationDate.HasValue
                        : dapperProjection.Text is not null || dapperProjection.CreationDate.HasValue))
                    throw new Exception($"Dynamic projection: Results differ when includeDetails={includeDetails}.");

                var dapperDynamicProjection = await Dapper_DynamicProjectionDynamic(includeDetails);
                var rinkuDynamicProjection = await RinkuCommand_DynaObjectProjection(includeDetails);
                var dapperDynamicValues = (IDictionary<string, object>)dapperDynamicProjection;
                var hasDapperText = dapperDynamicValues.TryGetValue("Text", out var dapperText);
                var hasRinkuText = rinkuDynamicProjection.ContainsKey("Text");
                if ((int)dapperDynamicValues["Id"] != rinkuDynamicProjection.Get<int>("Id")
                    || (includeDetails
                        ? !hasDapperText || !hasRinkuText || !Equals(dapperText, rinkuDynamicProjection.Get<string>("Text"))
                        : hasDapperText || hasRinkuText))
                    throw new Exception($"Dynamic projection: Results differ when includeDetails={includeDetails}.");

                var dapperRawProjection = await Dapper_RawDictionaryProjection(includeDetails);
                var rinkuRawProjection = await RinkuCommand_RawDictionaryProjection(includeDetails);
                if (!SameRawProjection(dapperRawProjection, rinkuRawProjection))
                    throw new Exception($"Raw dictionary projection: Results differ when includeDetails={includeDetails}. Dapper={DescribeDictionary(dapperRawProjection)}; Rinku={DescribeDictionary(rinkuRawProjection)}.");
            }

            var dapperCustomType = await Dapper_CustomDatabaseType();
            var rinkuCustomType = await RinkuCommand_CustomDatabaseType();
            if (dapperCustomType != rinkuCustomType || dapperCustomType.Id != new StrongPostId(1) || dapperCustomType.Text.Length != 2000)
                throw new Exception("Custom database type: Results differ.");

            var dapperComplex = await Dapper_Complex();
            var commandComplex = await RinkuCommand_Complex();
            var sqlComplex = await RinkuSql_Complex();
            if (dapperComplex.Count != commandComplex.Count || dapperComplex.Count != sqlComplex.Count)
                throw new Exception("Complex Mapping: Results differ.");
            for (var i = 0; i < dapperComplex.Count; i++)
                if (dapperComplex[i] != commandComplex[i] || dapperComplex[i] != sqlComplex[i])
                    throw new Exception($"Complex Mapping: Results differ. Dapper={dapperComplex[i].Id}/{dapperComplex[i].Name}/{dapperComplex[i].Category}; Rinku={commandComplex[i].Id}/{commandComplex[i].Name}/{commandComplex[i].Category}");

            var dapperExecuteSync = Dapper_Execute();
            var commandExecuteSync = RinkuCommand_Execute();
            var sqlExecuteSync = RinkuSql_Execute();
            if (dapperExecuteSync != commandExecuteSync || dapperExecuteSync != sqlExecuteSync)
                throw new Exception("Execute Sync: Row counts differ.");

            var dapperExecuteAsync = await Dapper_ExecuteAsync();
            var commandExecuteAsync = await RinkuCommand_ExecuteAsync();
            var sqlExecuteAsync = await RinkuSql_ExecuteAsync();
            if (dapperExecuteAsync != commandExecuteAsync || dapperExecuteAsync != sqlExecuteAsync)
                throw new Exception("Execute Async: Row counts differ.");

            var dapperInClause = await Dapper_InClause();
            var commandInClause = await RinkuCommand_InClause();
            var sqlInClause = await RinkuSql_InClause();
            if (dapperInClause != commandInClause || dapperInClause != sqlInClause)
                throw new Exception("IN Clause: Results differ.");

            var dapperScalar = await Dapper_Scalar();
            var commandScalar = await RinkuCommand_Scalar();
            var sqlScalar = await RinkuSql_Scalar();
            if (dapperScalar != RowCount || dapperScalar != commandScalar || dapperScalar != sqlScalar)
                throw new Exception("Scalar: Values differ.");

            var dapperExecuteScalar = await Dapper_ExecuteScalar();
            var commandExecuteScalar = await RinkuCommand_ExecuteScalar();
            var sqlExecuteScalar = await RinkuSql_ExecuteScalar();
            if (dapperExecuteScalar != RowCount || dapperExecuteScalar != commandExecuteScalar || dapperExecuteScalar != sqlExecuteScalar)
                throw new Exception("ExecuteScalar: Values differ.");

            var dapperScalarSequence = await Dapper_ScalarSequence();
            var commandScalarSequence = await RinkuCommand_ScalarSequence();
            var sqlScalarSequence = await RinkuSql_ScalarSequence();
            if (dapperScalarSequence.Count != commandScalarSequence.Count || dapperScalarSequence.Count != sqlScalarSequence.Count)
                throw new Exception("Scalar Sequence: Collections differ.");
            for (var i = 0; i < dapperScalarSequence.Count; i++)
                if (dapperScalarSequence[i] != commandScalarSequence[i] || dapperScalarSequence[i] != sqlScalarSequence[i])
                    throw new Exception("Scalar Sequence: Collections differ.");

            var dapperMultiResult = await Dapper_MultiResultSet();
            var commandMultiResult = await RinkuCommand_MultiResultSet();
            var sqlMultiResult = await RinkuSql_MultiResultSet();
            if (dapperMultiResult != commandMultiResult || dapperMultiResult != sqlMultiResult)
                throw new Exception("Multiple Result Sets: Sums differ.");

            var dapperMultiResultSync = Dapper_MultiResultSetSync();
            var commandMultiResultSync = RinkuCommand_MultiResultSetSync();
            var sqlMultiResultSync = RinkuSql_MultiResultSetSync();
            if (dapperMultiResultSync != commandMultiResultSync || dapperMultiResultSync != sqlMultiResultSync)
                throw new Exception("Multiple Result Sets Sync: Sums differ.");

            if (await Dapper_FixedCount() != RowCount
                || await RinkuCommand_FixedCount() != RowCount
                || await RinkuCommand_ConditionalCountWithoutId() != RowCount
                || await Dapper_FixedCountById() != 1
                || await RinkuCommand_FixedCountById() != 1
                || await RinkuCommand_ConditionalCountWithId() != 1)
                throw new Exception("Conditional SQL: Results differ.");

            var dapperProcedure = await Dapper_StoredProcedure();
            var rinkuProcedure = await RinkuCommand_StoredProcedure();
            if (dapperProcedure != rinkuProcedure || dapperProcedure.Id != 1 || dapperProcedure.Text?.Length != 2000)
                throw new Exception("Stored procedure: Results differ.");

            if (await Dapper_OutputParameter() != 106 || await RinkuCommand_OutputParameter() != 106)
                throw new Exception("Output parameter: Results differ.");

            const long expectedReaderChecksum = 101275;
            if (Dapper_DirectReader() != expectedReaderChecksum || RinkuCommand_DirectReader() != expectedReaderChecksum)
                throw new Exception("Direct reader: Results differ.");
            if (await Dapper_DirectReaderAsync() != expectedReaderChecksum || await RinkuCommand_DirectReaderAsync() != expectedReaderChecksum)
                throw new Exception("Direct reader Async: Results differ.");

            if (Dapper_Transaction() != 1 || RinkuCommand_Transaction() != 1)
                throw new Exception("Transaction: Results differ.");

            if (await Dapper_DbString() != 1 || await RinkuCommand_DbString() != 1)
                throw new Exception("Explicit string metadata: Results differ.");

            if (await Dapper_TableValuedParameter() != 50 || await RinkuCommand_TableValuedParameter() != 50)
                throw new Exception("Table-valued parameter: Results differ.");

            if (Dapper_RowParserSelection() != 54 || RinkuCommand_RowParserSelection() != 54 || RinkuCommand_InterfaceFactory() != 54)
                throw new Exception("Per-row parser selection: Results differ.");

            var dapperContext = await Dapper_ExecutionContext();
            var rinkuContext = await RinkuCommand_ExecutionContext();
            if (dapperContext.Count != 50 || !dapperContext.SequenceEqual(rinkuContext))
                throw new Exception("Timeout and cancellation context: Results differ.");

            var groups = await SelectPostCommentsCmd.QueryAsync<List<PostGroup>>(cnn);
            if (groups.Count != RowCount || groups[0].Children.Count != 3 || groups[^1].Children.Count != 3)
                throw new Exception("Multi-row validation failed.");

            var expected = RinkuCommand_OneToManyNative();
            var tuples = RinkuCommand_OneToManyTuples();
            var dapper = Dapper_OneToManyMultiMap();
            var separate = await RinkuCommand_OneToManySeparateResultSets();
            var separateDapper = await Dapper_OneToManySeparateResultSets();
            var ordered = await RinkuCommand_OneToManySeparateResultSetsOrdered();
            var orderedDapper = await Dapper_OneToManySeparateResultSetsOrdered();
            if (!SameGroups(expected, tuples) || !SameGroups(expected, dapper))
                throw new Exception("Multi-row route validation failed.");
            var expectedTotal = GroupTotal(expected);
            if (expectedTotal != separate || expectedTotal != separateDapper || expectedTotal != ordered || expectedTotal != orderedDapper)
                throw new Exception("Separate result-set route validation failed.");

            if (Dapper_LiteralReplacement() != 1 || RinkuCommand_NumericLiteral() != 1 || RinkuCommand_ParameterizedCount() != 1)
                throw new Exception("Literal benchmark validation failed.");

            if (Dapper_BatchExecute() != _batchItems.Length || RinkuCommand_BatchUseWith() != _batchItems.Length)
                throw new Exception("Batch execution validation failed.");

            var wasFixed = _fixId;
            _fixId = true;
            Post dapperParameters;
            Post builderParameters;
            try {
                dapperParameters = Dapper_DynamicParameters();
                builderParameters = RinkuCommand_BuilderCommand();
            }
            finally {
                _fixId = wasFixed;
            }
            if (dapperParameters != builderParameters)
                throw new Exception("Dynamic parameter benchmark validation failed.");
        }
        finally {
            _fixId = false;
        }
    }

    private static int GroupTotal(IReadOnlyList<PostGroup> groups)
        => groups.Sum(group => group.Id + group.Children.Count);

    private static bool SameProjection(ProjectionRow left, ProjectionRow right)
        => left.Id == right.Id && left.Text == right.Text && left.CreationDate == right.CreationDate;

    private static bool SameRawProjection(IEnumerable<KeyValuePair<string, object>> left, IEnumerable<KeyValuePair<string, object>> right) {
        var leftValues = left.ToArray();
        var rightValues = right.ToArray();
        if (leftValues.Length != rightValues.Length)
            return false;
        foreach (var (key, value) in leftValues) {
            var found = false;
            foreach (var pair in rightValues) {
                if (!StringComparer.OrdinalIgnoreCase.Equals(pair.Key, key) || !EquivalentValue(pair.Value, value))
                    continue;
                found = true;
                break;
            }
            if (!found)
                return false;
        }
        return true;
    }

    private static bool EquivalentValue(object? left, object? right) {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (Equals(left, right))
            return true;
        if (left is not IConvertible || right is not IConvertible)
            return false;
        try {
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
        }
        catch (FormatException) { return false; }
        catch (InvalidCastException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static long ReadChecksum(IDataReader reader) {
        long sum = 0;
        while (reader.Read())
            sum += reader.GetInt32(0) + reader.GetInt64(1);
        return sum;
    }

    private static async Task<long> ReadChecksumAsync(DbDataReader reader) {
        long sum = 0;
        while (await reader.ReadAsync())
            sum += reader.GetInt32(0) + reader.GetInt64(1);
        return sum;
    }

    private static bool SameGroups(List<PostGroup> left, List<PostGroup> right) {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++) {
            var leftGroup = left[i];
            var rightGroup = right[i];
            if (leftGroup.Id != rightGroup.Id || leftGroup.Name != rightGroup.Name || leftGroup.Children.Count != rightGroup.Children.Count)
                return false;
            for (var j = 0; j < leftGroup.Children.Count; j++) {
                var leftChild = leftGroup.Children[j];
                var rightChild = rightGroup.Children[j];
                if (leftChild.Id != rightChild.Id || leftChild.Value != rightChild.Value)
                    return false;
            }
        }
        return true;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Query one Sync")]
    public Post Dapper_QueryFirst() => cnn.QueryFirst<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Sync")]
    public Post RinkuCommand_QueryT() => QueryPostCmd.Query<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Sync")]
    public Post RinkuSql_QueryT() => cnn.Query<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (or default) Sync")]
    public Post? Dapper_QueryFirstOrDefault() => cnn.QueryFirstOrDefault<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? RinkuCommand_QueryOptionalT() => QueryPostCmd.Query<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? RinkuSql_QueryOptionalT() => cnn.Query<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? RinkuCommand_QueryOptionalNullableT() => QueryPostCmd.Query<OptionalNullable<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? RinkuSql_QueryOptionalNullableT() => cnn.Query<OptionalNullable<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable value Sync")]
    public int? Dapper_QueryFirstNullableValue() => cnn.QueryFirst<int?>(SelectNullableValueSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable value Sync")]
    public int? RinkuCommand_QueryNullableValue() => QueryNullableValueCmd.Query<int?>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable value Sync")]
    public int? RinkuSql_QueryNullableValue() => cnn.Query<int?>(SelectNullableValueSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable reference Sync")]
    public string? Dapper_QueryFirstNullableReference() => cnn.QueryFirst<string?>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference Sync")]
    public string? RinkuCommand_QueryMaybeNullReference() => QueryNullableReferenceCmd.Query<MaybeNull<string>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference Sync")]
    public string? RinkuSql_QueryMaybeNullReference() => cnn.Query<MaybeNull<string>>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable reference (or default) Sync")]
    public string? Dapper_QueryFirstOrDefaultNullableReference() => cnn.QueryFirstOrDefault<string?>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference (or default) Sync")]
    public string? RinkuCommand_QueryOptionalNullableReference() => QueryNullableReferenceCmd.Query<OptionalNullable<string>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference (or default) Sync")]
    public string? RinkuSql_QueryOptionalNullableReference() => cnn.Query<OptionalNullable<string>>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single) Sync")]
    public Post Dapper_QuerySingle() => cnn.QuerySingle<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Sync")]
    public Post RinkuCommand_QuerySingleT() => QueryPostCmd.Query<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Sync")]
    public Post RinkuSql_QuerySingleT() => cnn.Query<Single<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single or default) Sync")]
    public Post? Dapper_QuerySingleOrDefault() => cnn.QuerySingleOrDefault<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single or default) Sync")]
    public Post? RinkuCommand_QuerySingleOrDefaultT() => QueryPostCmd.Query<SingleOrDefault<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single or default) Sync")]
    public Post? RinkuSql_QuerySingleOrDefaultT() => cnn.Query<SingleOrDefault<Post>>(SelectPostSql, new { id = NextId() });


    [Benchmark(Baseline = true), BenchmarkCategory("Query one Async")]
    public async Task<Post> Dapper_QueryFirstAsync() => await cnn.QueryFirstAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Async")]
    public async Task<Post> RinkuCommand_QueryTAsync() => await QueryPostCmd.QueryAsync<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Async")]
    public async Task<Post> RinkuSql_QueryTAsync() => await cnn.QueryAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> Dapper_QueryFirstOrDefaultAsync() => await cnn.QueryFirstOrDefaultAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> RinkuCommand_QueryOptionalTAsync() => await QueryPostCmd.QueryAsync<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> RinkuSql_QueryOptionalTAsync() => await cnn.QueryAsync<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> RinkuCommand_QueryOptionalNullableTAsync() => await QueryPostCmd.QueryAsync<OptionalNullable<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> RinkuSql_QueryOptionalNullableTAsync() => await cnn.QueryAsync<OptionalNullable<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable value Async")]
    public async Task<int?> Dapper_QueryFirstNullableValueAsync() => await cnn.QueryFirstAsync<int?>(SelectNullableValueSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable value Async")]
    public async Task<int?> RinkuCommand_QueryNullableValueAsync() => await QueryNullableValueCmd.QueryAsync<int?>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable value Async")]
    public async Task<int?> RinkuSql_QueryNullableValueAsync() => await cnn.QueryAsync<int?>(SelectNullableValueSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable reference Async")]
    public async Task<string?> Dapper_QueryFirstNullableReferenceAsync() => await cnn.QueryFirstAsync<string?>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference Async")]
    public async Task<string?> RinkuCommand_QueryMaybeNullReferenceAsync() => await QueryNullableReferenceCmd.QueryAsync<MaybeNull<string>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference Async")]
    public async Task<string?> RinkuSql_QueryMaybeNullReferenceAsync() => await cnn.QueryAsync<MaybeNull<string>>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query nullable reference (or default) Async")]
    public async Task<string?> Dapper_QueryFirstOrDefaultNullableReferenceAsync() => await cnn.QueryFirstOrDefaultAsync<string?>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference (or default) Async")]
    public async Task<string?> RinkuCommand_QueryOptionalNullableReferenceAsync() => await QueryNullableReferenceCmd.QueryAsync<OptionalNullable<string>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query nullable reference (or default) Async")]
    public async Task<string?> RinkuSql_QueryOptionalNullableReferenceAsync() => await cnn.QueryAsync<OptionalNullable<string>>(SelectNullableReferenceSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> Dapper_QuerySingleAsync() => await cnn.QuerySingleAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> RinkuCommand_QuerySingleTAsync() => await QueryPostCmd.QueryAsync<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> RinkuSql_QuerySingleTAsync() => await cnn.QueryAsync<Single<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single or default) Async")]
    public async Task<Post?> Dapper_QuerySingleOrDefaultAsync() => await cnn.QuerySingleOrDefaultAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single or default) Async")]
    public async Task<Post?> RinkuCommand_QuerySingleOrDefaultTAsync() => await QueryPostCmd.QueryAsync<SingleOrDefault<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single or default) Async")]
    public async Task<Post?> RinkuSql_QuerySingleOrDefaultTAsync() => await cnn.QueryAsync<SingleOrDefault<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Connection lifecycle Async")]
    public async Task<Post> Dapper_ConnectionLifecycleAsync() {
        await using var connection = _fixture.GetConnection();
        return await connection.QueryFirstAsync<Post>(SelectPostSql, new { id = NextId() });
    }

    [Benchmark, BenchmarkCategory("Connection lifecycle Async")]
    public async Task<Post> RinkuCommand_ConnectionLifecycleAsync() {
        await using var connection = _fixture.GetConnection();
        return await QueryPostCmd.QueryAsync<Post>(connection, new { id = NextId() });
    }

    [Benchmark, BenchmarkCategory("Connection lifecycle Async")]
    public async Task<Post> RinkuSql_ConnectionLifecycleAsync() {
        await using var connection = _fixture.GetConnection();
        return await connection.QueryAsync<Post>(SelectPostSql, new { id = NextId() });
    }


    [Benchmark(Baseline = true), BenchmarkCategory("Query Sync (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public int Dapper_QueryUnbuffered(int rowCount) {
        var items = cnn.Query<Post>(SelectPostsSql, new { take = rowCount }, buffered: false);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Sync (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public int RinkuCommand_QueryIEnumerable(int rowCount) {
        var items = QueryPostsCmd.Query<IEnumerable<Post>>(cnn, new { take = rowCount });
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }
    [Benchmark, BenchmarkCategory("Query Sync (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public int RinkuSql_QueryIEnumerable(int rowCount) {
        var items = cnn.Query<IEnumerable<Post>>(SelectPostsSql, new { take = rowCount });
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("Query Buffered Sync")]
    [Arguments(50), Arguments(RowCount)]
    public List<Post> Dapper_QueryBuffered(int rowCount) => cnn.Query<Post>(SelectPostsSql, new { take = rowCount }, buffered: true).AsList();

    [Benchmark, BenchmarkCategory("Query Buffered Sync")]
    [Arguments(50), Arguments(RowCount)]
    public List<Post> RinkuCommand_QueryList(int rowCount) => QueryPostsCmd.Query<List<Post>>(cnn, new { take = rowCount });

    [Benchmark, BenchmarkCategory("Query Buffered Sync")]
    [Arguments(50), Arguments(RowCount)]
    public List<Post> RinkuSql_QueryList(int rowCount) => cnn.Query<List<Post>>(SelectPostsSql, new { take = rowCount });


    [Benchmark(Baseline = true), BenchmarkCategory("Query Async (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public async Task<int> Dapper_QueryUnbufferedAsync(int rowCount) {
        var items = cnn.QueryUnbufferedAsync<Post>(SelectPostsSql, new { take = rowCount });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Async (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public async Task<int> RinkuCommand_StreamQueryAsync(int rowCount) {
        var items = QueryPostsCmd.StreamQueryAsync<Post>(cnn, new { take = rowCount });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Async (Stream)")]
    [Arguments(50), Arguments(RowCount)]
    public async Task<int> RinkuSql_StreamQueryAsync(int rowCount) {
        var items = cnn.StreamQueryAsync<Post>(SelectPostsSql, new { take = rowCount });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("Query Buffered Async")]
    [Arguments(50), Arguments(RowCount)]
    public async Task<List<Post>> Dapper_QueryAsyncBuffered(int rowCount) => (await SqlMapper.QueryAsync<Post>(cnn, SelectPostsSql, new { take = rowCount })).AsList();

    [Benchmark, BenchmarkCategory("Query Buffered Async")]
    [Arguments(50), Arguments(RowCount)]
    public Task<List<Post>> RinkuCommand_QueryAsyncList(int rowCount) => QueryPostsCmd.QueryAsync<List<Post>>(cnn, new { take = rowCount });

    [Benchmark, BenchmarkCategory("Query Buffered Async")]
    [Arguments(50), Arguments(RowCount)]
    public Task<List<Post>> RinkuSql_QueryAsyncList(int rowCount) => cnn.QueryAsync<List<Post>>(SelectPostsSql, new { take = rowCount });


    [Benchmark(Baseline = true), BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Dapper_QueryAsyncDynamic() {
        var row = await cnn.QueryFirstAsync(SelectPostSql, new { id = NextId() });
        return ((int)row.Id, (string?)row.Text, (DateTime)row.CreationDate, (int?)row.Counter1);
    }

    [Benchmark, BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> RinkuCommand_QueryAsyncDynaObject() {
        var row = await QueryPostCmd.QueryAsync<DynaObject>(cnn, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }
    [Benchmark, BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> RinkuSql_QueryAsyncDynaObject() {
        var row = await cnn.QueryAsync<DynaObject>(SelectPostSql, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Typed projection Async")]
    [Arguments(false), Arguments(true)]
    public Task<ProjectionRow> Dapper_DynamicProjection(bool includeDetails) {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate("SELECT /**select**/ FROM Posts WHERE Id = @id");
        builder.Select("Id");
        if (includeDetails)
            builder.Select("Text").Select("CreationDate");
        return cnn.QueryFirstAsync<ProjectionRow>(template.RawSql, new { id = NextId() });
    }

    [Benchmark, BenchmarkCategory("Typed projection Async")]
    [Arguments(false), Arguments(true)]
    public Task<ProjectionRow> RinkuCommand_DynamicProjection(bool includeDetails)
        => DynamicProjectionCmd.QueryAsync<ProjectionRow>(cnn, includeDetails ? new ProjectionArgsWithDetails(NextId()) : new ProjectionArgs(NextId()));

    [Benchmark(Baseline = true), BenchmarkCategory("Dynamic projection Async")]
    [Arguments(false), Arguments(true)]
    public async Task<dynamic> Dapper_DynamicProjectionDynamic(bool includeDetails) {
        var builder = new SqlBuilder();
        var template = builder.AddTemplate("SELECT /**select**/ FROM Posts WHERE Id = @id");
        builder.Select("Id");
        if (includeDetails)
            builder.Select("Text").Select("CreationDate");
        return await cnn.QueryFirstAsync(template.RawSql, new { id = NextId() });
    }

    [Benchmark, BenchmarkCategory("Dynamic projection Async")]
    [Arguments(false), Arguments(true)]
    public async Task<IDictionary<string, object>> Dapper_RawDictionaryProjection(bool includeDetails) {
        var columns = includeDetails ? "Id, Text, CreationDate" : "Id";
        var row = await cnn.QueryFirstAsync($"SELECT {columns} FROM Posts WHERE Id = @id", new { id = NextId() });
        return (IDictionary<string, object>)row;
    }

    [Benchmark, BenchmarkCategory("Dynamic projection Async")]
    [Arguments(false), Arguments(true)]
    public Task<DynaObject> RinkuCommand_DynaObjectProjection(bool includeDetails)
        => DynamicProjectionCmd.QueryAsync<DynaObject>(cnn, includeDetails ? new ProjectionArgsWithDetails(NextId()) : new ProjectionArgs(NextId()));

    [Benchmark, BenchmarkCategory("Dynamic projection Async")]
    [Arguments(false), Arguments(true)]
    public Task<Dictionary<string, object>> RinkuCommand_RawDictionaryProjection(bool includeDetails)
        => RawProjectionCmd.QueryAsync<Dictionary<string, object>>(cnn, new {
            Cols = includeDetails ? "Id, Text, CreationDate" : "Id",
            id = NextId()
        });

    [Benchmark(Baseline = true), BenchmarkCategory("Custom database type Async")]
    public Task<CustomPost> Dapper_CustomDatabaseType()
        => cnn.QueryFirstAsync<CustomPost>(SelectCustomPostSql, new { id = new StrongPostId(NextId()) });

    [Benchmark, BenchmarkCategory("Custom database type Async")]
    public Task<CustomPost> RinkuCommand_CustomDatabaseType()
        => SelectCustomPostCmd.QueryAsync<CustomPost>(cnn, new { id = new StrongPostId(NextId()) });

    [Benchmark(Baseline = true), BenchmarkCategory("Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await cnn.QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("Complex Mapping")]
    public Task<List<Product>> RinkuCommand_Complex() => QueryComplexCmd.QueryAsync<List<Product>>(cnn, new { id = 1 });

    [Benchmark, BenchmarkCategory("Complex Mapping")]
    public Task<List<Product>> RinkuSql_Complex() => cnn.QueryAsync<List<Product>>(SelectComplexSql, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("Execute Sync")]
    public int Dapper_Execute() => cnn.Execute(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Sync")]
    public int RinkuCommand_Execute() => ExecuteUpdateCmd.Execute(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Sync")]
    public int RinkuSql_Execute() => cnn.Execute(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Async")]
    public Task<int> RinkuCommand_ExecuteAsync() => ExecuteUpdateCmd.ExecuteAsync(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Async")]
    public Task<int> RinkuSql_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("IN Clause")]
    public async Task<int> Dapper_InClause() {
        var items = cnn.QueryUnbufferedAsync<Post>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("IN Clause")]
    public async Task<int> RinkuCommand_InClause() {
        var items = InClauseCmd.StreamQueryAsync<Post>(cnn, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("IN Clause")]
    public async Task<int> RinkuSql_InClause() {
        var items = cnn.StreamQueryAsync<Post>(InClauseSqlRinku, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar query Async")]
    public Task<int> Dapper_Scalar() => cnn.QuerySingleAsync<int>(CountSql, param: null);

    [Benchmark, BenchmarkCategory("Scalar query Async")]
    public Task<int> RinkuCommand_Scalar() => CountCmd.QueryAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("Scalar query Async")]
    public Task<int> RinkuSql_Scalar() => cnn.QueryAsync<int>(CountSql);

    [Benchmark(Baseline = true), BenchmarkCategory("ExecuteScalar Async")]
    public Task<int> Dapper_ExecuteScalar() => cnn.ExecuteScalarAsync<int>(CountSql, param: null);

    [Benchmark, BenchmarkCategory("ExecuteScalar Async")]
    public Task<int> RinkuCommand_ExecuteScalar() => CountCmd.ExecuteScalarAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("ExecuteScalar Async")]
    public Task<int> RinkuSql_ExecuteScalar() => cnn.ExecuteScalarAsync<int>(CountSql);

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar Sequence Async")]
    public async Task<List<int>> Dapper_ScalarSequence() => (await cnn.QueryAsync<int>(SelectIdsSql, param: null)).AsList();

    [Benchmark, BenchmarkCategory("Scalar Sequence Async")]
    public Task<List<int>> RinkuCommand_ScalarSequence() => SelectIdsCmd.QueryAsync<List<int>>(cnn);

    [Benchmark, BenchmarkCategory("Scalar Sequence Async")]
    public Task<List<int>> RinkuSql_ScalarSequence() => cnn.QueryAsync<List<int>>(SelectIdsSql);
    
    [Benchmark(Baseline = true), BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> Dapper_MultiResultSet() {
        using var grid = await cnn.QueryMultipleAsync(MultiSql, new { a = NextId(), b = NextId() });
        var p1 = await grid.ReadFirstAsync<Post>();
        var p2 = await grid.ReadFirstAsync<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> RinkuCommand_MultiResultSet() {
        using var multi = await MultiCmd.ExecuteMultiReaderAsync(cnn, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> RinkuSql_MultiResultSet() {
        using var multi = await cnn.ExecuteMultiReaderAsync(MultiSql, out _, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Multiple Result Sets Sync")]
    public int Dapper_MultiResultSetSync() {
        using var grid = cnn.QueryMultiple(MultiSql, new { a = NextId(), b = NextId() });
        var p1 = grid.ReadFirst<Post>();
        var p2 = grid.ReadFirst<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Sync")]
    public int RinkuCommand_MultiResultSetSync() {
        using var multi = MultiCmd.ExecuteMultiReader(cnn, new { a = NextId(), b = NextId() });
        var p1 = multi.Query<Post>();
        var p2 = multi.Query<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Sync")]
    public int RinkuSql_MultiResultSetSync() {
        using var multi = cnn.ExecuteMultiReader(MultiSql, out _, new { a = NextId(), b = NextId() });
        var p1 = multi.Query<Post>();
        var p2 = multi.Query<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar count: fixed path and reusable conditional path without parameter Async")]
    public Task<int> Dapper_FixedCount() => cnn.QuerySingleAsync<int>(CountSql);

    [Benchmark, BenchmarkCategory("Scalar count: fixed path and reusable conditional path without parameter Async")]
    public Task<int> RinkuCommand_FixedCount() => CountCmd.QueryAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("Scalar count: fixed path and reusable conditional path without parameter Async")]
    public Task<int> RinkuCommand_ConditionalCountWithoutId()
        => ConditionalCountCmd.QueryAsync<int>(cnn, new { id = (int?)null });

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar count: fixed path and reusable conditional path with parameter Async")]
    public Task<int> Dapper_FixedCountById() => cnn.QuerySingleAsync<int>(CountByIdSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Scalar count: fixed path and reusable conditional path with parameter Async")]
    public Task<int> RinkuCommand_FixedCountById() => CountByIdCmd.QueryAsync<int>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Scalar count: fixed path and reusable conditional path with parameter Async")]
    public Task<int> RinkuCommand_ConditionalCountWithId()
        => ConditionalCountCmd.QueryAsync<int>(cnn, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Stored procedure Async")]
    public Task<Post> Dapper_StoredProcedure()
        => cnn.QueryFirstAsync<Post>(GetPostProcedure, new { id = NextId() }, commandType: CommandType.StoredProcedure);

    [Benchmark, BenchmarkCategory("Stored procedure Async")]
    public Task<Post> RinkuCommand_StoredProcedure()
        => GetPostProcCmd.QueryAsync<Post>(cnn, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Output and return parameters Async")]
    public async Task<int> Dapper_OutputParameter() {
        var parameters = new DynamicParameters();
        parameters.Add("input", 21);
        parameters.Add("doubled", dbType: DbType.Int32, direction: ParameterDirection.Output);
        parameters.Add("returnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
        var selected = await cnn.QuerySingleAsync<int>(OutputProcedure, parameters, commandType: CommandType.StoredProcedure);
        return selected + parameters.Get<int>("doubled") + parameters.Get<int>("returnValue");
    }

    [Benchmark, BenchmarkCategory("Output and return parameters Async")]
    public async Task<int> RinkuCommand_OutputParameter() {
        var task = _outputProcCmd.QueryAsync<int>(cnn, out var command, new { RETURN_VALUE = 0, input = 21, doubled = 0 });
        try {
            var selected = await task;
            return selected + command.GetOutputValue<int>("@doubled") + command.GetReturnValue<int>();
        }
        finally {
            await command.DisposeAsync();
        }
    }

    private static string DescribeDictionary(IEnumerable<KeyValuePair<string, object>> values)
        => string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value ?? "<null>"} ({pair.Value?.GetType().FullName ?? "null"})"));

    [Benchmark(Baseline = true), BenchmarkCategory("Direct reader Sync")]
    public long Dapper_DirectReader() {
        using var reader = SqlMapper.ExecuteReader(cnn, ReadPostsSql);
        return ReadChecksum(reader);
    }

    [Benchmark, BenchmarkCategory("Direct reader Sync")]
    public long RinkuCommand_DirectReader() {
        var reader = ReadPostsCmd.ExecuteReader(cnn, out var command);
        try {
            using (reader)
                return ReadChecksum(reader);
        }
        finally {
            command.Dispose();
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Direct reader Async")]
    public async Task<long> Dapper_DirectReaderAsync() {
        await using var reader = await SqlMapper.ExecuteReaderAsync(cnn, ReadPostsSql);
        return await ReadChecksumAsync(reader);
    }

    [Benchmark, BenchmarkCategory("Direct reader Async")]
    public async Task<long> RinkuCommand_DirectReaderAsync() {
        var reader = await ReadPostsCmd.ExecuteReaderAsync(cnn, out var command);
        try {
            await using (reader)
                return await ReadChecksumAsync(reader);
        }
        finally {
            await command.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Transaction Sync")]
    public int Dapper_Transaction() {
        using var transaction = cnn.BeginTransaction();
        try {
            return cnn.Execute(UpdateSql, new { val = 2, id = NextId() }, transaction);
        }
        finally {
            transaction.Rollback();
        }
    }

    [Benchmark, BenchmarkCategory("Transaction Sync")]
    public int RinkuCommand_Transaction() {
        using var transaction = cnn.BeginTransaction();
        try {
            return ExecuteUpdateCmd.Execute(cnn, new { val = 2, id = NextId() }, transaction: transaction);
        }
        finally {
            transaction.Rollback();
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Explicit string metadata Async")]
    public Task<int> Dapper_DbString() => cnn.QuerySingleAsync<int>(DbStringSql, new { value = new DbString { Value = "code-2500", IsAnsi = true, Length = 50 } });

    [Benchmark, BenchmarkCategory("Explicit string metadata Async")]
    public Task<int> RinkuCommand_DbString()
        => DbStringCmd.QueryAsync<int>(cnn, new { value = "code-2500" });

    [Benchmark(Baseline = true), BenchmarkCategory("Table-valued parameter Async")]
    public Task<int> Dapper_TableValuedParameter()
        => cnn.QuerySingleAsync<int>(TableValuedParameterSql, new { ids = _tableIds.AsTableValuedParameter("dbo.BenchmarkIds") });

    [Benchmark, BenchmarkCategory("Table-valued parameter Async")]
    public Task<int> RinkuCommand_TableValuedParameter()
        => TableValuedParameterCmd.QueryAsync<int>(cnn, new { ids = _tableIds });

    [Benchmark(Baseline = true), BenchmarkCategory("Polymorphic row mapping Sync")]
    public int Dapper_RowParserSelection() {
        using var reader = SqlMapper.ExecuteReader(cnn, ShapeRowsSql);
        var circle = reader.GetRowParser<BenchmarkShape>(typeof(CircleBenchmarkShape));
        var square = reader.GetRowParser<BenchmarkShape>(typeof(SquareBenchmarkShape));
        var total = 0;
        while (reader.Read())
            total += (reader.GetString(0) == "circle" ? circle(reader) : square(reader)).Measure();
        return total;
    }

    [Benchmark, BenchmarkCategory("Polymorphic row mapping Sync")]
    public int RinkuCommand_RowParserSelection() {
        using var reader = ShapeRowsCmd.ExecuteMultiReader(cnn);
        var circle = (ISimpleParser<CircleBenchmarkShape>)reader.GetCurrentSetParser<CircleBenchmarkShape>();
        var square = (ISimpleParser<SquareBenchmarkShape>)reader.GetCurrentSetParser<SquareBenchmarkShape>();
        var total = 0;
        while (reader.Read()) {
            BenchmarkShape shape = reader.GetString(0) == "circle" ? circle.RowParser(reader) : square.RowParser(reader);
            total += shape.Measure();
        }
        return total;
    }

    [Benchmark, BenchmarkCategory("Polymorphic row mapping Sync")]
    public int RinkuCommand_InterfaceFactory() {
        using var reader = ShapeRowsCmd.ExecuteMultiReader(cnn);
        var parser = (ISimpleParser<BenchmarkShape>)reader.GetCurrentSetParser<BenchmarkShape>();
        var total = 0;
        while (reader.Read())
            total += parser.RowParser(reader).Measure();
        return total;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Timeout and cancellation context Async")]
    public async Task<List<Post>> Dapper_ExecutionContext() {
        var command = new CommandDefinition(SelectPostsSql, new { take = 50 }, commandTimeout: 30, cancellationToken: CancellationToken.None);
        return (await cnn.QueryAsync<Post>(command)).AsList();
    }

    [Benchmark, BenchmarkCategory("Timeout and cancellation context Async")]
    public Task<List<Post>> RinkuCommand_ExecutionContext()
        => QueryPostsCmd.QueryAsync<List<Post>>(cnn, new { take = 50 }, timeout: 30, ct: CancellationToken.None);

    [Benchmark(Baseline = true), BenchmarkCategory("Literal replacement")]
    public int Dapper_LiteralReplacement() => cnn.QueryFirst<int>(DapperLiteralCountSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Literal replacement")]
    public int RinkuCommand_NumericLiteral() => RinkuLiteralCountCmd.Query<int>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Literal replacement")]
    public int RinkuCommand_ParameterizedCount() => CountByIdCmd.Query<int>(cnn, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Manually added parameters")]
    public Post Dapper_DynamicParameters() {
        var parameters = new DynamicParameters();
        parameters.Add("id", NextId());
        return cnn.QueryFirst<Post>(SelectPostSql, parameters);
    }

    [Benchmark, BenchmarkCategory("Manually added parameters")]
    public Post RinkuCommand_BuilderCommand() {
        var builder = QueryPostCmd.StartBuilder();
        builder.Use("@id", NextId());
        return builder.Query<Post>(cnn);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Batch execution")]
    public int Dapper_BatchExecute() => SqlMapper.Execute(cnn, UpdateSql, _batchItems);

    [Benchmark, BenchmarkCategory("Batch execution")]
    public int RinkuCommand_BatchUseWith() {
        using var command = cnn.CreateCommand();
        var builder = ExecuteUpdateCmd.StartBuilder(command);
        var affected = 0;
        foreach (var item in _batchItems) {
            builder.UseWith(item);
            affected += builder.Execute();
        }
        return affected;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> Dapper_OneToManyMultiMap() {
        var groups = new List<PostGroup>();
        PostGroup? current = null;
        var rows = cnn.Query<PostMultiMapParent, PostMultiMapChild, PostMultiMapParent>(
            SelectPostCommentsSql,
            (parent, child) => {
                if (current is null || current.Id != parent.Id) {
                    current = new PostGroup(parent.Id, parent.Name, []);
                    groups.Add(current);
                }
                current.Children.Add(child);
                return parent;
            },
            splitOn: "Id",
            buffered: false);
        foreach (var _ in rows) { }
        return groups;
    }

    [Benchmark, BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> RinkuCommand_OneToManyTuples() {
        var rows = SelectPostCommentsCmd.Query<IEnumerable<(PostMultiMapParent Parent, PostMultiMapChild Child)>>(cnn);
        var groups = new List<PostGroup>();
        PostGroup? current = null;
        foreach (var (Parent, Child) in rows) {
            if (current is null || current.Id != Parent.Id) {
                current = new PostGroup(Parent.Id, Parent.Name, []);
                groups.Add(current);
            }
            current.Children.Add(Child);
        }
        return groups;
    }

    [Benchmark, BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> RinkuCommand_OneToManyNative() {
        return SelectPostCommentsCmd.Query<List<PostGroup>>(cnn);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many separate result sets")]
    public async Task<int> Dapper_OneToManySeparateResultSets() {
        using var multi = await cnn.QueryMultipleAsync(SelectPostCommentSetsSql);
        var parents = (await multi.ReadAsync<PostSetParent>()).AsList();
        var children = multi.ReadUnbufferedAsync<PostSetChild>();
        var byId = parents.ToDictionary(parent => parent.Id);
        await foreach (var child in children)
            byId[child.PostId].Children.Add(child);
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark, BenchmarkCategory("One-to-many separate result sets")]
    public async Task<int> RinkuCommand_OneToManySeparateResultSets() {
        using var multi = await SelectPostCommentSetsCmd.ExecuteMultiReaderAsync(cnn);
        var parents = await multi.QueryAsync<List<PostSetParent>>();
        var children = await multi.QueryAsync<IEnumerable<PostSetChild>>();
        var byId = parents.ToDictionary(parent => parent.Id);
        foreach (var child in children)
            byId[child.PostId].Children.Add(child);
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many separate result sets (ordered)")]
    public async Task<int> Dapper_OneToManySeparateResultSetsOrdered() {
        using var multi = await cnn.QueryMultipleAsync(SelectPostCommentSetsSql);
        var parents = (await multi.ReadAsync<PostSetParent>()).AsList();
        await using var children = multi.ReadUnbufferedAsync<PostSetChild>().GetAsyncEnumerator();
        var more = await children.MoveNextAsync();
        foreach (var parent in parents) {
            while (more && children.Current.PostId == parent.Id) {
                parent.Children.Add(children.Current);
                more = await children.MoveNextAsync();
            }
        }
        if (more)
            throw new Exception("Ordered Dapper children were not fully consumed.");
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark, BenchmarkCategory("One-to-many separate result sets (ordered)")]
    public async Task<int> RinkuCommand_OneToManySeparateResultSetsOrdered() {
        using var multi = await SelectPostCommentSetsCmd.ExecuteMultiReaderAsync(cnn);
        var parents = await multi.QueryAsync<List<PostSetParent>>();
        var children = await multi.QueryAsync<IEnumerable<PostSetChild>>();
        using var enumerator = children.GetEnumerator();
        var more = enumerator.MoveNext();
        foreach (var parent in parents) {
            while (more && enumerator.Current.PostId == parent.Id) {
                parent.Children.Add(enumerator.Current);
                more = enumerator.MoveNext();
            }
        }
        if (more)
            throw new Exception("Ordered Rinku children were not fully consumed.");
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [GlobalCleanup]
    public async ValueTask Cleanup() => await DisposeAsync();

    public async ValueTask DisposeAsync() {
        _outputProcCmd?.Dispose();
        if (cnn is not null)
            await cnn.DisposeAsync();
        if (_fixture is not null)
            await _fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
[UsesBoolConds("CreationDate")]
public sealed record ProjectionArgsWithDetails(int Id);
public sealed record ProjectionArgs(int Id);

public sealed class ProjectionRow {
    public int Id { get; set; }
    public string? Text { get; set; }
    public DateTime? CreationDate { get; set; }
}

public abstract class BenchmarkShape {
    public string Kind { get; set; } = null!;
    public abstract int Measure();

    public static BenchmarkShape FromRow(string kind, int? radius, int? side)
        => kind == "circle"
            ? new CircleBenchmarkShape { Kind = kind, Radius = radius.GetValueOrDefault() }
            : new SquareBenchmarkShape { Kind = kind, Side = side.GetValueOrDefault() };
}

public sealed class CircleBenchmarkShape : BenchmarkShape {
    public int Radius { get; set; }
    public override int Measure() => Radius * Radius;
}

public sealed class SquareBenchmarkShape : BenchmarkShape {
    public int Side { get; set; }
    public override int Measure() => Side * Side;
}

public readonly record struct StrongPostId(int Value);

public sealed record CustomPost(StrongPostId Id, string Text) : IDbReadable;

internal sealed class StrongPostIdDapperHandler : SqlMapper.TypeHandler<StrongPostId> {
    internal static readonly StrongPostIdDapperHandler Instance = new();

    public override StrongPostId Parse(object value) => new(Convert.ToInt32(value));

    public override void SetValue(IDbDataParameter parameter, StrongPostId value) {
        parameter.DbType = DbType.Int32;
        parameter.Value = value.Value;
    }
}

internal sealed class StrongPostIdParamInfo : ConvertedDbParamInfo<StrongPostId> {
    internal static readonly StrongPostIdParamInfo Instance = new();

    protected override object ConvertValue(StrongPostId value) => value.Value;

    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.Int32;
}

internal sealed class SqlServerTableParamInfo : ConvertedDbParamInfo<DataTable> {
    internal static readonly SqlServerTableParamInfo Instance = new();

    protected override object ConvertValue(DataTable value) => value;

    protected override void ConfigureParameter(IDbDataParameter parameter) {
        var sqlParameter = (SqlParameter)parameter;
        sqlParameter.SqlDbType = SqlDbType.Structured;
        sqlParameter.TypeName = "dbo.BenchmarkIds";
    }
}

internal sealed class StrongPostIdTypeInfo : ScalarTypeParsingInfo<StrongPostId> {
    internal static readonly StrongPostIdTypeInfo Instance = new();
    private static readonly MethodInfo FromInt32Method = typeof(StrongPostIdTypeInfo).GetMethod(nameof(FromInt32), BindingFlags.Static | BindingFlags.NonPublic)!;

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal)
        => column.Type == typeof(int)
            ? new ConvertedScalarPlan(parentType, new MethodCallConverter(FromInt32Method), parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal)
            : null;

    private static StrongPostId FromInt32(int value) => new(value);
}

public sealed record BatchUpdateArgs(int Val, int Id);

public sealed record PostGroup([GroupKey] int Id, string Name, List<PostMultiMapChild> Children) : IDbReadable;

public sealed record PostMultiMapChild([AltSkippingSegments("Id", 2), AbortOnNull] int Id, [AltSkippingSegments("Value", 2)] string Value) : IDbReadable;

public sealed class PostMultiMapParent : IDbReadable {
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}

public sealed class PostSetParent : IDbReadable {
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<PostSetChild> Children { get; } = [];
}

public sealed record PostSetChild([AbortOnNull] int PostId, [AbortOnNull] int Id, string Title) : IDbReadable;

/// <summary>
/// The wide row the single-row and bulk benchmarks map, matching the Dapper suite's Post: a
/// varchar(max) text column and nine nullable ints, so materialization exercises varied types and
/// the null path. A positional record, so value equality drives the setup validation for free.
/// </summary>
public record Post(
    int Id,
    string? Text,
    DateTime CreationDate,
    DateTime LastChangeDate,
    int? Counter1,
    int? Counter2,
    int? Counter3,
    int? Counter4,
    int? Counter5,
    int? Counter6,
    int? Counter7,
    int? Counter8,
    int? Counter9) {
    public int Sum() => Id + (Text?.Length ?? 0)
        + (Counter1 ?? 0) + (Counter2 ?? 0) + (Counter3 ?? 0) + (Counter4 ?? 0) + (Counter5 ?? 0)
        + (Counter6 ?? 0) + (Counter7 ?? 0) + (Counter8 ?? 0) + (Counter9 ?? 0);
}

public class Product {
    public int Id { get; set; }
    public string? Name { get; set; }
    public Category? Category { get; set; }
    public static bool operator ==(Product? p1, Product? p2) {
        if (ReferenceEquals(p1, p2))
            return true;
        if (p1 is null || p2 is null)
            return false;

        return p1.Id == p2.Id &&
               p1.Name == p2.Name &&
               p1.Category == p2.Category;
    }

    public static bool operator !=(Product? p1, Product? p2) => !(p1 == p2);

    public override bool Equals(object? obj) => obj is Product other && this == other;

    public override int GetHashCode() => HashCode.Combine(Id, Name, Category);
}

public record class Category(
    [AltSkippingSegments("Id", 2)] int Id,
    [AltSkippingSegments("Name", 2)] string? Name,
    [AltSkippingSegments("Description", 2)] string? Description) : IDbReadable;
