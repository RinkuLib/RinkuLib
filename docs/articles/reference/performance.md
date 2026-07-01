# Performance

*Benchmarks against Dapper, the numbers, for you to draw your own conclusion.*

Measured with BenchmarkDotNet against a real database. In short, comparable time and consistently lower allocations. Rinku builds the `DbCommand` directly and reuses compiled, schema-keyed mappers and pooled buffers. For the call-by-call API equivalents, see [coming from Dapper](migrating-from-dapper.md).

## Benchmarks vs. Dapper

Lower is better. `Ratio` and `Alloc Ratio` are relative to the Dapper baseline in each group.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---|---|---|
| **1. Query one Sync** | | | | |
| Dapper_QueryFirst | 526.8 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryT | 508.6 us | 0.97 | 3.07 KB | 0.84 |
| **2. Query one or default Sync** | | | | |
| Dapper_QueryFirstOrDefault | 521.3 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QueryOptionalT | 521.3 us | 1.00 | 3.07 KB | 0.84 |
| **3. Query one single Sync** | | | | |
| Dapper_QuerySingle | 510.0 us | 1.00 | 3.66 KB | 1.00 |
| Rinku_QuerySingleT | 512.0 us | 1.00 | 3.07 KB | 0.84 |
| **4. Query one Async** | | | | |
| Dapper_QueryFirstAsync | 559.8 us | 1.00 | 5.71 KB | 1.00 |
| Rinku_QueryTAsync | 540.4 us | 0.97 | 4.91 KB | 0.86 |
| **7. Query Sync (Stream)** | | | | |
| Dapper_QueryUnbuffered | 602.6 us | 1.00 | 20.84 KB | 1.00 |
| Rinku_QueryIEnumerable | 601.1 us | 1.00 | 15.46 KB | 0.74 |
| **13. Execute Sync** | | | | |
| Dapper_Execute | 1,476.8 us | 1.00 | 2.33 KB | 1.00 |
| Rinku_Execute | 1,443.6 us | 0.98 | 1.76 KB | 0.76 |
| **15. IN Clause** | | | | |
| Dapper_InClause | 604.3 us | 1.00 | 8.01 KB | 1.00 |
| Rinku_InClause | 592.1 us | 0.98 | 6.63 KB | 0.83 |

The full 15-group table is in the project README. The pattern holds across every category. Comparable time, consistently lower allocations.

## How to read the numbers

The cost of a query is dominated by the database round trip, so the `Mean` columns are close. The meaningful difference is **allocations** (the `Allocated` column), where Rinku's reuse of compiled mappers and pooled buffers shows up as 15 to 26 percent less garbage per call.

## Reproducing

The benchmarks live in `RinkuLib.Tests.Benchmark` (BenchmarkDotNet, targeting `net10.0`). Run them in Release.

```bash
dotnet run -c Release --project RinkuLib.Tests.Benchmark
```

`BaseBenchmark` is decorated with `[MemoryDiagnoser]` (which produces the `Allocated` and `Alloc Ratio` columns) and groups results by category. It runs every method against a real SQL Server spun up through the test-container fixture, so each measurement includes an actual database round trip. That is why the `Mean` columns track each other so closely and the allocation columns are the differentiator.

BenchmarkDotNet writes a full environment block (CPU, OS, runtime, and the chosen job) into its `BenchmarkDotNet.Artifacts/` output at run time. Reproduce on your own hardware and compare the ratios rather than the absolute microseconds, since wall-clock time depends on the machine and the database.
