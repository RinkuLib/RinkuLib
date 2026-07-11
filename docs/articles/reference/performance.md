# Performance

BenchmarkDotNet against a real SQL Server (started in a test container), compared with Dapper. Every measurement includes a genuine round trip. Lower is better, ratios are relative to the Dapper baseline in each group.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| **Query one, sync** | | | | |
| Dapper_QueryFirst | 576.6 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QueryT | 530.3 us | 0.92 | 11.33 KB | 0.95 |
| **Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 538.8 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QueryOptionalT | 531.7 us | 0.99 | 11.33 KB | 0.95 |
| **Query one single, sync** | | | | |
| Dapper_QuerySingle | 541.7 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QuerySingleT | 527.3 us | 0.97 | 11.33 KB | 0.95 |
| **Query one, async** | | | | |
| Dapper_QueryFirstAsync | 589.4 us | 1.00 | 13.80 KB | 1.00 |
| Rinku_QueryTAsync | 581.6 us | 0.99 | 12.93 KB | 0.94 |
| **Query one or default, async** | | | | |
| Dapper_QueryFirstOrDefaultAsync | 586.9 us | 1.00 | 13.80 KB | 1.00 |
| Rinku_QueryOptionalTAsync | 577.8 us | 0.98 | 12.93 KB | 0.94 |
| **Query one single, async** | | | | |
| Dapper_QuerySingleAsync | 577.5 us | 1.00 | 13.90 KB | 1.00 |
| Rinku_QuerySingleTAsync | 570.0 us | 0.99 | 13.02 KB | 0.94 |
| **Query stream, sync** (5000 rows) | | | | |
| Dapper_QueryUnbuffered | 57.96 ms | 1.00 | 29.76 MB | 1.00 |
| Rinku_QueryIEnumerable | 58.20 ms | 1.00 | 29.42 MB | 0.99 |
| **Query buffered, sync** (5000 rows) | | | | |
| Dapper_QueryBuffered | 64.15 ms | 1.00 | 29.89 MB | 1.00 |
| Rinku_QueryList | 66.38 ms | 1.04 | 29.55 MB | 0.99 |
| **Query stream, async** (5000 rows) | | | | |
| Dapper_QueryUnbufferedAsync | 57.11 ms | 1.00 | 29.76 MB | 1.00 |
| Rinku_StreamQueryAsync | 57.84 ms | 1.01 | 29.42 MB | 0.99 |
| **Query buffered, async** (5000 rows) | | | | |
| Dapper_QueryAsyncBuffered | 64.39 ms | 1.00 | 29.89 MB | 1.00 |
| Rinku_QueryAsyncList | 66.17 ms | 1.03 | 29.55 MB | 0.99 |
| **Dynamic, async** | | | | |
| Dapper_QueryAsyncDynamic | 584.8 us | 1.00 | 13.87 KB | 1.00 |
| Rinku_QueryAsyncDynaObject | 580.9 us | 0.99 | 12.99 KB | 0.94 |
| **Complex mapping** | | | | |
| Dapper_Complex | 616.8 us | 1.00 | 5.92 KB | 1.00 |
| Rinku_Complex | 606.4 us | 0.98 | 5.03 KB | 0.85 |
| **Execute, sync** | | | | |
| Dapper_Execute | 1,703.5 us | 1.00 | 2.05 KB | 1.00 |
| Rinku_Execute | 1,677.9 us | 0.99 | 1.51 KB | 0.74 |
| **Execute, async** | | | | |
| Dapper_ExecuteAsync | 1,733.0 us | 1.00 | 3.48 KB | 1.00 |
| Rinku_ExecuteAsync | 1,698.2 us | 0.98 | 2.93 KB | 0.84 |
| **IN clause** | | | | |
| Dapper_InClause | 798.5 us | 1.00 | 40.70 KB | 1.00 |
| Rinku_InClause | 785.3 us | 0.98 | 39.08 KB | 0.96 |
| **Scalar, async** | | | | |
| Dapper_Scalar | 1,365.9 us | 1.00 | 2.91 KB | 1.00 |
| Rinku_Scalar | 1,371.9 us | 1.00 | 2.50 KB | 0.86 |
| **Scalar sequence, async** (5000 rows) | | | | |
| Dapper_ScalarSequence | 1,907.8 us | 1.00 | 193.43 KB | 1.00 |
| Rinku_ScalarSequence | 1,896.7 us | 0.99 | 75.65 KB | 0.39 |
| **Multiple result sets, async** | | | | |
| Dapper_MultiResultSet | 690.4 us | 1.00 | 24.37 KB | 1.00 |
| Rinku_MultiResultSet | 674.4 us | 0.98 | 23.49 KB | 0.96 |

## Methodology

The setup mirrors the established ORM benchmark suites so the numbers line up with their published results. The row is the wide `Post` from the Dapper suite, thirteen columns with a `varchar(max)` text field and nine nullable ints, so materialization is a real cost. Five thousand rows are seeded and the queried id rotates every call, so no single hot row skews the cache. One connection is opened in setup and reused, so the run measures mapping rather than pool rent and return. A setup pass asserts Dapper and Rinku return identical results for all eighteen categories before any timing runs.

The shapes and the fairness rules come from three suites: [DapperLib/Dapper](https://github.com/DapperLib/Dapper/tree/main/benchmarks/Dapper.Tests.Performance) for the row and the rotating id, [FransBouma/RawDataAccessBencher](https://github.com/FransBouma/RawDataAccessBencher) for equal connection handling across libraries, and [InfoTechBridge/OrmBenchmark](https://github.com/InfoTechBridge/OrmBenchmark) for the single-row-repeated and bulk-set-fetch shapes.

## Reproducing

The benchmarks live in `RinkuLib.Benchmarks` (BenchmarkDotNet, `net10.0`). They spin up a real SQL Server through a test container, so each measurement includes an actual round trip. Docker must be running.

```bash
dotnet run -c Release --project RinkuLib.Benchmarks
```

Compare the ratios rather than absolute microseconds. Wall-clock time depends on the machine and the database.
