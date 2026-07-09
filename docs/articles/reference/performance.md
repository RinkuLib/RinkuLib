# Performance

BenchmarkDotNet against a real SQL Server (started in a test container), compared with Dapper. Every measurement includes a genuine round trip. Lower is better, ratios are relative to the Dapper baseline in each group.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| **1. Query one, sync** | | | | |
| Dapper_QueryFirst | 526.8 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryT | 508.6 us | 0.97 | 3.07 KB | 0.84 |
| **2. Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 521.3 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryOptionalT | 521.3 us | 1.00 | 3.07 KB | 0.84 |
| **3. Query one single, sync** | | | | |
| Dapper_QuerySingle | 510.0 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QuerySingleT | 512.0 us | 1.00 | 3.07 KB | 0.84 |
| **4. Query one, async** | | | | |
| Dapper_QueryFirstAsync | 559.8 us | 1.00 | 5.71 KB | 1.00 |
| Rinku_QueryTAsync | 540.4 us | 0.97 | 4.91 KB | 0.86 |
| **5. Query one or default, async** | | | | |
| Dapper_QueryFirstOrDefaultAsync | 554.5 us | 1.00 | 5.71 KB | 1.00 |
| Rinku_QueryOptionalTAsync | 546.8 us | 0.99 | 4.91 KB | 0.86 |
| **6. Query one single, async** | | | | |
| Dapper_QuerySingleAsync | 555.9 us | 1.00 | 5.8 KB | 1.00 |
| Rinku_QuerySingleTAsync | 544.4 us | 0.98 | 5.01 KB | 0.86 |
| **7. Query stream, sync** | | | | |
| Dapper_QueryUnbuffered | 602.6 us | 1.00 | 20.84 KB | 1.00 |
| Rinku_QueryIEnumerable | 601.1 us | 1.00 | 15.46 KB | 0.74 |
| **8. Query buffered, sync** | | | | |
| Dapper_QueryBuffered | 603.0 us | 1.00 | 22.98 KB | 1.00 |
| Rinku_QueryList | 601.1 us | 1.00 | 17.52 KB | 0.76 |
| **9. Query stream, async** | | | | |
| Dapper_QueryUnbufferedAsync | 654.7 us | 1.00 | 22.87 KB | 1.00 |
| Rinku_StreamQueryAsync | 643.4 us | 0.98 | 17.41 KB | 0.76 |
| **10. Query buffered, async** | | | | |
| Dapper_QueryAsyncBuffered | 653.4 us | 1.00 | 24.79 KB | 1.00 |
| Rinku_QueryAsyncList | 650.3 us | 1.00 | 19.36 KB | 0.78 |
| **11. Dynamic, async** | | | | |
| Dapper_QueryAsyncDynamic | 570.5 us | 1.00 | 5.77 KB | 1.00 |
| Rinku_QueryAsyncDynaObject | 570.0 us | 1.00 | 4.95 KB | 0.86 |
| **12. Complex mapping** | | | | |
| Dapper_Complex | 587.2 us | 1.00 | 6.25 KB | 1.00 |
| Rinku_Complex | 581.1 us | 0.99 | 5.41 KB | 0.87 |
| **13. Execute, sync** | | | | |
| Dapper_Execute | 1,476.8 us | 1.00 | 2.33 KB | 1.00 |
| Rinku_Execute | 1,443.6 us | 0.98 | 1.76 KB | 0.76 |
| **14. Execute, async** | | | | |
| Dapper_ExecuteAsync | 1,511.7 us | 1.00 | 3.95 KB | 1.00 |
| Rinku_ExecuteAsync | 1,485.1 us | 0.98 | 3.37 KB | 0.85 |
| **15. IN clause** | | | | |
| Dapper_InClause | 604.3 us | 1.00 | 8.01 KB | 1.00 |
| Rinku_InClause | 592.1 us | 0.98 | 6.63 KB | 0.83 |

## Reading the numbers

A query's cost is dominated by the database round trip, so the `Mean` columns track each other. The steady difference is `Allocated`: reusing the built command and the compiled, schema-keyed mappers means less garbage per call, most of all on multi-row reads, where the mapper runs once per row.

## Reproducing

The benchmarks live in `RinkuLib.Tests.Benchmark` (BenchmarkDotNet, `net10.0`). They spin up a real SQL Server through a test container, so each measurement includes an actual round trip.

```bash
dotnet run -c Release --project RinkuLib.Tests.Benchmark
```

Compare the ratios rather than absolute microseconds. Wall-clock time depends on the machine and the database.
