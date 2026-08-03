# Performance

BenchmarkDotNet against a real SQL Server started in a test container. Every measurement includes a database round trip. Lower is better. Ratios are relative to the Dapper baseline in each group.

These are the latest results from the benchmark project. The numbers depend on the machine and database, so compare the ratio and allocation columns rather than the absolute time.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| **Query one, sync** | | | | |
| Dapper_QueryFirst | 565.4 us | 1.00 | 15.87 KB | 1.00 |
| Rinku_QueryT | 559.8 us | 0.99 | 14.69 KB | 0.93 |
| Rinku2_QueryT | 565.3 us | 1.00 | 14.69 KB | 0.93 |
| **Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 571.5 us | 1.00 | 15.87 KB | 1.00 |
| Rinku_QueryOptionalT | 570.7 us | 1.00 | 14.69 KB | 0.93 |
| Rinku2_QueryOptionalT | 569.4 us | 1.00 | 14.69 KB | 0.93 |
| **Query one, single, sync** | | | | |
| Dapper_QuerySingle | 573.1 us | 1.00 | 15.87 KB | 1.00 |
| Rinku_QuerySingleT | 564.4 us | 0.99 | 14.69 KB | 0.93 |
| Rinku2_QuerySingleT | 564.4 us | 0.99 | 14.69 KB | 0.93 |
| **Query one, async** | | | | |
| Dapper_QueryFirstAsync | 631.6 us | 1.00 | 22.65 KB | 1.00 |
| Rinku_QueryTAsync | 623.8 us | 0.99 | 21.32 KB | 0.94 |
| Rinku2_QueryTAsync | 598.9 us | 0.95 | 21.32 KB | 0.94 |
| **Query one or default, async** | | | | |
| Dapper_QueryFirstOrDefaultAsync | 621.6 us | 1.00 | 22.65 KB | 1.00 |
| Rinku_QueryOptionalTAsync | 617.8 us | 0.99 | 21.32 KB | 0.94 |
| Rinku2_QueryOptionalTAsync | 617.7 us | 0.99 | 21.32 KB | 0.94 |
| **Query one, single, async** | | | | |
| Dapper_QuerySingleAsync | 624.7 us | 1.00 | 22.79 KB | 1.00 |
| Rinku_QuerySingleTAsync | 618.7 us | 0.99 | 21.46 KB | 0.94 |
| Rinku2_QuerySingleTAsync | 615.3 us | 0.99 | 21.46 KB | 0.94 |
| **Query stream, sync** (5000 rows) | | | | |
| Dapper_QueryUnbuffered | 63.62 ms | 1.00 | 29.76 MB | 1.00 |
| Rinku_QueryIEnumerable | 62.92 ms | 0.99 | 29.42 MB | 0.99 |
| Rinku2_QueryIEnumerable | 62.95 ms | 0.99 | 29.42 MB | 0.99 |
| **Query buffered, sync** (5000 rows) | | | | |
| Dapper_QueryBuffered | 61.26 ms | 1.00 | 29.89 MB | 1.00 |
| Rinku_QueryList | 61.17 ms | 1.00 | 29.54 MB | 0.99 |
| Rinku2_QueryList | 61.62 ms | 1.01 | 29.54 MB | 0.99 |
| **Query stream, async** (5000 rows) | | | | |
| Dapper_QueryUnbufferedAsync | 63.34 ms | 1.00 | 29.77 MB | 1.00 |
| Rinku_StreamQueryAsync | 64.24 ms | 1.01 | 29.43 MB | 0.99 |
| Rinku2_StreamQueryAsync | 63.26 ms | 1.00 | 29.43 MB | 0.99 |
| **Query buffered, async** (5000 rows) | | | | |
| Dapper_QueryAsyncBuffered | 62.65 ms | 1.00 | 29.90 MB | 1.00 |
| Rinku_QueryAsyncList | 61.05 ms | 0.98 | 29.55 MB | 0.99 |
| Rinku2_QueryAsyncList | 61.36 ms | 0.98 | 29.55 MB | 0.99 |
| **Dynamic results** | | | | |
| Dapper_QueryAsyncDynamic | 621.7 us | 1.00 | 22.71 KB | 1.00 |
| Rinku_QueryAsyncDynaObject | 607.4 us | 0.98 | 21.38 KB | 0.94 |
| Rinku2_QueryAsyncDynaObject | 613.4 us | 0.99 | 21.38 KB | 0.94 |
| **Complex mapping** | | | | |
| Dapper_Complex | 593.7 us | 1.00 | 15.64 KB | 1.00 |
| Rinku_Complex | 581.9 us | 0.98 | 14.33 KB | 0.92 |
| Rinku2_Complex | 595.4 us | 1.00 | 14.33 KB | 0.92 |
| **Scalar, async** | | | | |
| Dapper_Scalar | 1.414 ms | 1.00 | 13.52 KB | 1.00 |
| Rinku_Scalar | 1.387 ms | 0.98 | 10.85 KB | 0.80 |
| Rinku2_Scalar | 1.416 ms | 1.00 | 13.01 KB | 0.96 |
| **Scalar sequence, async** (5000 rows) | | | | |
| Dapper_ScalarSequence | 2.070 ms | 1.00 | 202.21 KB | 1.00 |
| Rinku_ScalarSequence | 1.931 ms | 0.93 | 84.01 KB | 0.42 |
| Rinku2_ScalarSequence | 1.934 ms | 0.93 | 84.01 KB | 0.42 |
| **Manually added parameters** | | | | |
| Dapper_DynamicParameters | 581.4 us | 1.00 | 16.32 KB | 1.00 |
| Rinku_BuilderCommand | 571.0 us | 0.98 | 14.70 KB | 0.90 |
| **Conditional SQL without parameter** | | | | |
| Rinku_FixedCount | 1.416 ms | 1.00 | 10.85 KB | 1.00 |
| Rinku_ConditionalCountWithoutId | 1.398 ms | 0.99 | 11.20 KB | 1.03 |
| **Conditional SQL with parameter** | | | | |
| Rinku_FixedCountById | 564.8 us | 1.00 | 11.91 KB | 1.00 |
| Rinku_ConditionalCountWithId | 554.5 us | 0.98 | 11.91 KB | 1.00 |
| **Literal replacement** | | | | |
| Dapper_LiteralReplacement | 537.6 us | 1.00 | 6.42 KB | 1.00 |
| Rinku_NumericLiteral | 513.7 us | 0.96 | 4.79 KB | 0.75 |
| Rinku_ParameterizedCount | 518.5 us | 0.97 | 5.35 KB | 0.83 |
| **IN clause** | | | | |
| Dapper_InClause | 774.8 us | 1.00 | 51.74 KB | 1.00 |
| Rinku_InClause | 776.6 us | 1.00 | 47.75 KB | 0.92 |
| Rinku2_InClause | 772.5 us | 1.00 | 47.75 KB | 0.92 |
| **Execute, sync** | | | | |
| Dapper_Execute | 1.513 ms | 1.00 | 6.13 KB | 1.00 |
| Rinku_Execute | 1.485 ms | 0.98 | 5.02 KB | 0.82 |
| Rinku2_Execute | 1.471 ms | 0.97 | 5.02 KB | 0.82 |
| **Execute, async** | | | | |
| Dapper_ExecuteAsync | 1.576 ms | 1.00 | 10.99 KB | 1.00 |
| Rinku_ExecuteAsync | 1.574 ms | 1.00 | 9.87 KB | 0.90 |
| Rinku2_ExecuteAsync | 1.575 ms | 1.00 | 9.87 KB | 0.90 |
| **Batch execution** | | | | |
| Dapper_BatchExecute | 103.00 ms | 1.00 | 225.22 KB | 1.00 |
| Rinku_BatchUseWith | 95.87 ms | 0.93 | 266.00 KB | 1.18 |
| **Multiple result sets** | | | | |
| Dapper_MultiResultSet | 675.9 us | 1.00 | 33.68 KB | 1.00 |
| Rinku_MultiResultSet | 654.0 us | 0.97 | 34.59 KB | 1.03 |
| Rinku2_MultiResultSet | 659.7 us | 0.98 | 34.59 KB | 1.03 |
| **One-to-many fold** | | | | |
| Dapper_OneToManyMultiMap | 11.37 ms | 1.00 | 3.30 MB | 1.00 |
| Rinku_OneToManyTuples | 10.95 ms | 0.96 | 2.61 MB | 0.79 |
| Rinku_OneToManyNative | 10.54 ms | 0.93 | 2.15 MB | 0.65 |
| **One-to-many separate result sets** | | | | |
| Dapper_OneToManySeparateResultSets | 11.75 ms | 1.00 | 2.85 MB | 1.00 |
| Rinku_OneToManySeparateResultSets | 10.08 ms | 0.86 | 2.10 MB | 0.74 |
| **One-to-many separate result sets, ordered** | | | | |
| Dapper_OneToManySeparateResultSetsOrdered | 8.530 ms | 1.00 | 2.69 MB | 1.00 |
| Rinku_OneToManySeparateResultSetsOrdered | 8.068 ms | 0.95 | 1.94 MB | 0.72 |

## Methodology

The benchmark project uses one SQL Server test container, one reused connection, and real round trips. It seeds 5000 posts and three comments per post. The comparison validates the complete returned shapes before timing, and runs validation twice to cover both cold and warm mapper paths.

The Dapper and Rinku comparisons use the same SQL whenever the feature allows it. For one-to-many mapping, the shared joined SQL is used for Dapper multi-map, Rinku tuple mapping, and Rinku native grouping. The separate-result-set benchmarks also share their SQL; the ordered variant uses sequential folding instead of a dictionary.

## Reproducing

The benchmarks live in `RinkuLib.Benchmarks` and target `net10.0`. Docker must be running.

Run every benchmark:

```bash
dotnet run -c Release --project RinkuLib.Benchmarks
```

Run only one category:

```bash
dotnet run -c Release --project RinkuLib.Benchmarks -- --anyCategories "One-to-many fold"
```

Compare ratios rather than absolute microseconds. Wall-clock time depends on the machine and database.
