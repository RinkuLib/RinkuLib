# Performance

BenchmarkDotNet against a real SQL Server (started in a test container), compared with Dapper. Every measurement includes a genuine round trip. Lower is better, ratios are relative to the Dapper baseline in each group.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| **1. Query one, sync** | | | | |
| Dapper_QueryFirst | 571.0 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QueryT | 522.8 us | 0.92 | 11.33 KB | 0.95 |
| **2. Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 531.2 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QueryOptionalT | 526.0 us | 0.99 | 11.33 KB | 0.95 |
| **3. Query one single, sync** | | | | |
| Dapper_QuerySingle | 532.1 us | 1.00 | 11.94 KB | 1.00 |
| Rinku_QuerySingleT | 524.1 us | 0.99 | 11.33 KB | 0.95 |
| **4. Query one, async** | | | | |
| Dapper_QueryFirstAsync | 576.9 us | 1.00 | 13.80 KB | 1.00 |
| Rinku_QueryTAsync | 578.3 us | 1.00 | 12.93 KB | 0.94 |
| **5. Query one or default, async** | | | | |
| Dapper_QueryFirstOrDefaultAsync | 583.4 us | 1.00 | 13.80 KB | 1.00 |
| Rinku_QueryOptionalTAsync | 571.5 us | 0.98 | 12.93 KB | 0.94 |
| **6. Query one single, async** | | | | |
| Dapper_QuerySingleAsync | 594.2 us | 1.00 | 13.90 KB | 1.00 |
| Rinku_QuerySingleTAsync | 577.8 us | 0.97 | 13.02 KB | 0.94 |
| **7. Query stream, sync** (5000 rows) | | | | |
| Dapper_QueryUnbuffered | 59.68 ms | 1.00 | 29.76 MB | 1.00 |
| Rinku_QueryIEnumerable | 58.13 ms | 0.98 | 29.42 MB | 0.99 |
| **8. Query buffered, sync** (5000 rows) | | | | |
| Dapper_QueryBuffered | 64.72 ms | 1.00 | 29.89 MB | 1.00 |
| Rinku_QueryList | 66.28 ms | 1.02 | 29.55 MB | 0.99 |
| **9. Query stream, async** (5000 rows) | | | | |
| Dapper_QueryUnbufferedAsync | 57.62 ms | 1.00 | 29.76 MB | 1.00 |
| Rinku_StreamQueryAsync | 58.03 ms | 1.01 | 29.42 MB | 0.99 |
| **10. Query buffered, async** (5000 rows) | | | | |
| Dapper_QueryAsyncBuffered | 65.70 ms | 1.00 | 29.89 MB | 1.00 |
| Rinku_QueryAsyncList | 66.55 ms | 1.01 | 29.55 MB | 0.99 |
| **11. Dynamic, async** | | | | |
| Dapper_QueryAsyncDynamic | 591.7 us | 1.00 | 13.87 KB | 1.00 |
| Rinku_QueryAsyncDynaObject | 570.6 us | 0.96 | 12.99 KB | 0.94 |
| **12. Complex mapping** | | | | |
| Dapper_Complex | 569.5 us | 1.00 | 5.92 KB | 1.00 |
| Rinku_Complex | 531.8 us | 0.93 | 5.03 KB | 0.85 |
| **13. Execute, sync** | | | | |
| Dapper_Execute | 1,452.2 us | 1.00 | 2.05 KB | 1.00 |
| Rinku_Execute | 1,449.3 us | 1.00 | 1.51 KB | 0.74 |
| **14. Execute, async** | | | | |
| Dapper_ExecuteAsync | 1,515.2 us | 1.00 | 3.48 KB | 1.00 |
| Rinku_ExecuteAsync | 1,474.5 us | 0.97 | 2.93 KB | 0.84 |
| **15. IN clause** | | | | |
| Dapper_InClause | 721.6 us | 1.00 | 40.69 KB | 1.00 |
| Rinku_InClause | 713.8 us | 0.99 | 39.08 KB | 0.96 |
| **16. Scalar, async** | | | | |
| Dapper_Scalar | 1,357.6 us | 1.00 | 2.91 KB | 1.00 |
| Rinku_Scalar | 1,308.3 us | 0.96 | 2.50 KB | 0.86 |
| **17. Scalar sequence, async** (5000 rows) | | | | |
| Dapper_ScalarSequence | 1,855.6 us | 1.00 | 193.43 KB | 1.00 |
| Rinku_ScalarSequence | 1,828.0 us | 0.99 | 75.65 KB | 0.39 |
| **18. Multiple result sets, async** | | | | |
| Dapper_MultiResultSet | 723.1 us | 1.00 | 24.37 KB | 1.00 |
| Rinku_MultiResultSet | 693.9 us | 0.96 | 23.52 KB | 0.97 |

Indicative figures from a ShortRun (`--job short`); the bulk categories carry wider error.

## Reading the numbers

A query's cost is dominated by the database round trip, so on the single-row categories the `Mean` columns track each other and the steady difference is `Allocated`. Reusing the built command and the compiled, schema-keyed mappers means less garbage per call, most of all where the mapper runs once per row: scalar sequences allocate about 40 percent of Dapper's, and the execute paths about three quarters.

## Methodology

The setup mirrors the established ORM benchmark suites so the numbers line up with their published results. The row is the wide `Post` from the Dapper suite, thirteen columns with a `varchar(max)` text field and nine nullable ints, so materialization is a real cost. Five thousand rows are seeded and the queried id rotates every call, so no single hot row skews the cache. One connection is opened in setup and reused, so the run measures mapping rather than pool rent and return. A setup pass asserts Dapper and Rinku return identical results for all eighteen categories before any timing runs.

The shapes and the fairness rules come from three suites: [DapperLib/Dapper](https://github.com/DapperLib/Dapper/tree/main/benchmarks/Dapper.Tests.Performance) for the row and the rotating id, [FransBouma/RawDataAccessBencher](https://github.com/FransBouma/RawDataAccessBencher) for equal connection handling across libraries, and [InfoTechBridge/OrmBenchmark](https://github.com/InfoTechBridge/OrmBenchmark) for the single-row-repeated and bulk-set-fetch shapes.

## Reproducing

The benchmarks live in `RinkuLib.Benchmarks` (BenchmarkDotNet, `net10.0`). They spin up a real SQL Server through a test container, so each measurement includes an actual round trip. Docker must be running.

```bash
dotnet run -c Release --project RinkuLib.Benchmarks
```

Compare the ratios rather than absolute microseconds. Wall-clock time depends on the machine and the database.
