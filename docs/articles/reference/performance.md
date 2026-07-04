# Performance

BenchmarkDotNet against a real SQL Server, compared with Dapper. Lower is better, ratios relative to the Dapper baseline in each group.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| **1. Query one, sync** | | | | |
| Dapper_QueryFirst | 526.8 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryT | 508.6 us | 0.97 | 3.07 KB | 0.84 |
| **2. Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 521.3 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryOptionalT | 521.3 us | 1.00 | 3.07 KB | 0.84 |
| **3. Query single, sync** | | | | |
| Dapper_QuerySingle | 510.0 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QuerySingleT | 512.0 us | 1.00 | 3.07 KB | 0.84 |
| **4. Query one, async** | | | | |
| Dapper_QueryFirstAsync | 559.8 us | 1.00 | 5.71 KB | 1.00 |
| Rinku_QueryTAsync | 540.4 us | 0.97 | 4.91 KB | 0.86 |
| **7. Query stream, sync** | | | | |
| Dapper_QueryUnbuffered | 602.6 us | 1.00 | 20.84 KB | 1.00 |
| Rinku_QueryIEnumerable | 601.1 us | 1.00 | 15.46 KB | 0.74 |
| **13. Execute, sync** | | | | |
| Dapper_Execute | 1,476.8 us | 1.00 | 2.33 KB | 1.00 |
| Rinku_Execute | 1,443.6 us | 0.98 | 1.76 KB | 0.76 |
| **15. IN clause** | | | | |
| Dapper_InClause | 604.3 us | 1.00 | 8.01 KB | 1.00 |
| Rinku_InClause | 592.1 us | 0.98 | 6.63 KB | 0.83 |

The table above is a subset of the 15 groups. The pattern holds across every category: comparable time, consistently lower allocations. Run the benchmarks below for the full set.

## Reading the numbers

The cost of a query is dominated by the database round trip, so the `Mean` columns track each other. The meaningful difference is `Allocated`, where reusing the built command and the compiled, schema-keyed mappers shows up as 15 to 26 percent less garbage per call.

## Reproducing

The benchmarks live in `RinkuLib.Tests.Benchmark` (BenchmarkDotNet, `net10.0`). They spin up a real SQL Server through a test container, so each measurement includes an actual round trip.

```bash
dotnet run -c Release --project RinkuLib.Tests.Benchmark
```

Compare ratios rather than absolute microseconds. Wall-clock time depends on the machine and the database.
