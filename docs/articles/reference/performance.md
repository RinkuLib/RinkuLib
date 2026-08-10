# Performance

BenchmarkDotNet against a real SQL Server started in a test container. Every measurement shown below includes a database round trip. Lower is better. Ratios are relative to the Dapper baseline in each group.

This is a representative recorded run of the end-to-end benchmark project. The numbers depend on the machine and database, so compare the ratio and allocation columns rather than the absolute time. The benchmark source contains additional scenarios beyond this summary.

| Method | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|
| **Query one, sync** | | | | |
| Dapper_QueryFirst | 533.0 us | 1.00 | 15.87 KB | 1.00 |
| RinkuCommand_QueryT | 518.0 us | 0.97 | 14.69 KB | 0.93 |
| RinkuSql_QueryT | 521.9 us | 0.98 | 14.69 KB | 0.93 |
| **Query one or default, sync** | | | | |
| Dapper_QueryFirstOrDefault | 573.2 us | 1.00 | 15.87 KB | 1.00 |
| RinkuCommand_QueryOptionalT | 540.1 us | 0.94 | 14.69 KB | 0.93 |
| RinkuSql_QueryOptionalT | 534.9 us | 0.93 | 14.69 KB | 0.93 |
| RinkuCommand_QueryOptionalNullableT | 533.4 us | 0.93 | 14.69 KB | 0.93 |
| RinkuSql_QueryOptionalNullableT | 545.5 us | 0.95 | 14.69 KB | 0.93 |
| **Query one, single, sync** | | | | |
| Dapper_QuerySingle | 523.4 us | 1.00 | 15.87 KB | 1.00 |
| RinkuCommand_QuerySingleT | 513.9 us | 0.98 | 14.69 KB | 0.93 |
| RinkuSql_QuerySingleT | 507.5 us | 0.97 | 14.69 KB | 0.93 |
| **Query one, async** | | | | |
| Dapper_QueryFirstAsync | 566.9 us | 1.00 | 22.65 KB | 1.00 |
| RinkuCommand_QueryTAsync | 553.4 us | 0.98 | 21.32 KB | 0.94 |
| RinkuSql_QueryTAsync | 561.6 us | 0.99 | 21.32 KB | 0.94 |
| **Query one or default, async** | | | | |
| Dapper_QueryFirstOrDefaultAsync | 622.2 us | 1.00 | 22.65 KB | 1.00 |
| RinkuCommand_QueryOptionalTAsync | 596.4 us | 0.96 | 21.32 KB | 0.94 |
| RinkuSql_QueryOptionalTAsync | 596.8 us | 0.96 | 21.32 KB | 0.94 |
| RinkuCommand_QueryOptionalNullableTAsync | 597.2 us | 0.96 | 21.32 KB | 0.94 |
| RinkuSql_QueryOptionalNullableTAsync | 591.6 us | 0.95 | 21.32 KB | 0.94 |
| **Query one, single, async** | | | | |
| Dapper_QuerySingleAsync | 571.6 us | 1.00 | 22.79 KB | 1.00 |
| RinkuCommand_QuerySingleTAsync | 563.4 us | 0.99 | 21.46 KB | 0.94 |
| RinkuSql_QuerySingleTAsync | 557.6 us | 0.98 | 21.46 KB | 0.94 |
| **Nullable value, sync** | | | | |
| Dapper_QueryFirstNullableValue | 479.2 us | 1.00 | 6.53 KB | 1.00 |
| RinkuCommand_QueryNullableValue | 472.5 us | 0.99 | 5.39 KB | 0.83 |
| RinkuSql_QueryNullableValue | 469.9 us | 0.98 | 5.39 KB | 0.83 |
| **Nullable value, async** | | | | |
| Dapper_QueryFirstNullableValueAsync | 546.1 us | 1.00 | 13.36 KB | 1.00 |
| RinkuCommand_QueryNullableValueAsync | 535.1 us | 0.98 | 12.06 KB | 0.90 |
| RinkuSql_QueryNullableValueAsync | 497.4 us | 0.91 | 12.07 KB | 0.90 |
| **Nullable reference, sync** | | | | |
| Dapper_QueryFirstNullableReference | 481.8 us | 1.00 | 6.56 KB | 1.00 |
| RinkuCommand_QueryMaybeNullReference | 475.5 us | 0.99 | 5.45 KB | 0.83 |
| RinkuSql_QueryMaybeNullReference | 477.1 us | 0.99 | 5.45 KB | 0.83 |
| **Nullable reference, async** | | | | |
| Dapper_QueryFirstNullableReferenceAsync | 548.2 us | 1.00 | 13.50 KB | 1.00 |
| RinkuCommand_QueryMaybeNullReferenceAsync | 530.4 us | 0.97 | 12.24 KB | 0.91 |
| RinkuSql_QueryMaybeNullReferenceAsync | 538.8 us | 0.98 | 12.24 KB | 0.91 |
| **Nullable reference or default, sync** | | | | |
| Dapper_QueryFirstOrDefaultNullableReference | 523.6 us | 1.00 | 6.56 KB | 1.00 |
| RinkuCommand_QueryOptionalNullableReference | 497.8 us | 0.95 | 5.45 KB | 0.83 |
| RinkuSql_QueryOptionalNullableReference | 496.0 us | 0.95 | 5.45 KB | 0.83 |
| **Nullable reference or default, async** | | | | |
| Dapper_QueryFirstOrDefaultNullableReferenceAsync | 601.2 us | 1.00 | 13.50 KB | 1.00 |
| RinkuCommand_QueryOptionalNullableReferenceAsync | 539.7 us | 0.90 | 12.24 KB | 0.91 |
| RinkuSql_QueryOptionalNullableReferenceAsync | 532.1 us | 0.89 | 12.24 KB | 0.91 |
| **Query stream, sync (50 rows)** | | | | |
| Dapper_QueryUnbuffered | 1.203 ms | 1.00 | 315.85 KB | 1.00 |
| RinkuCommand_QueryIEnumerable | 1.189 ms | 0.99 | 309.95 KB | 0.98 |
| RinkuSql_QueryIEnumerable | 1.161 ms | 0.97 | 309.95 KB | 0.98 |
| **Query stream, sync (5000 rows)** | | | | |
| Dapper_QueryUnbuffered | 62.632 ms | 1.00 | 29.77 MB | 1.00 |
| RinkuCommand_QueryIEnumerable | 62.548 ms | 1.00 | 29.42 MB | 0.99 |
| RinkuSql_QueryIEnumerable | 62.423 ms | 1.00 | 29.42 MB | 0.99 |
| **Query buffered, sync (50 rows)** | | | | |
| Dapper_QueryBuffered | 1.231 ms | 1.00 | 316.97 KB | 1.00 |
| RinkuCommand_QueryList | 1.159 ms | 0.94 | 311.00 KB | 0.98 |
| RinkuSql_QueryList | 1.185 ms | 0.96 | 311.00 KB | 0.98 |
| **Query buffered, sync (5000 rows)** | | | | |
| Dapper_QueryBuffered | 70.290 ms | 1.00 | 29.90 MB | 1.00 |
| RinkuCommand_QueryList | 70.707 ms | 1.01 | 29.55 MB | 0.99 |
| RinkuSql_QueryList | 70.732 ms | 1.01 | 29.55 MB | 0.99 |
| **Query stream, async (50 rows)** | | | | |
| Dapper_QueryUnbufferedAsync | 1.282 ms | 1.00 | 323.87 KB | 1.00 |
| RinkuCommand_StreamQueryAsync | 1.203 ms | 0.94 | 316.70 KB | 0.98 |
| RinkuSql_StreamQueryAsync | 1.213 ms | 0.95 | 316.70 KB | 0.98 |
| **Query stream, async (5000 rows)** | | | | |
| Dapper_QueryUnbufferedAsync | 59.110 ms | 1.00 | 29.77 MB | 1.00 |
| RinkuCommand_StreamQueryAsync | 58.471 ms | 0.99 | 29.43 MB | 0.99 |
| RinkuSql_StreamQueryAsync | 57.851 ms | 0.98 | 29.43 MB | 0.99 |
| **Query buffered, async (50 rows)** | | | | |
| Dapper_QueryAsyncBuffered | 1.332 ms | 1.00 | 323.61 KB | 1.00 |
| RinkuCommand_QueryAsyncList | 1.262 ms | 0.95 | 317.71 KB | 0.98 |
| RinkuSql_QueryAsyncList | 1.247 ms | 0.94 | 317.71 KB | 0.98 |
| **Query buffered, async (5000 rows)** | | | | |
| Dapper_QueryAsyncBuffered | 70.348 ms | 1.00 | 29.90 MB | 1.00 |
| RinkuCommand_QueryAsyncList | 70.933 ms | 1.01 | 29.56 MB | 0.99 |
| RinkuSql_QueryAsyncList | 70.145 ms | 1.00 | 29.56 MB | 0.99 |
| **Dynamic results** | | | | |
| Dapper_QueryAsyncDynamic | 620.1 us | 1.00 | 22.71 KB | 1.00 |
| RinkuCommand_QueryAsyncDynaObject | 595.9 us | 0.96 | 21.38 KB | 0.94 |
| RinkuSql_QueryAsyncDynaObject | 598.0 us | 0.96 | 21.38 KB | 0.94 |
| **Typed projection, async** | | | | |
| Dapper_DynamicProjection (Id) | 575.8 us | 1.00 | 14.10 KB | 1.00 |
| RinkuCommand_DynamicProjection (Id) | 533.5 us | 0.93 | 12.16 KB | 0.86 |
| Dapper_DynamicProjection (details) | 590.8 us | 1.00 | 21.04 KB | 1.00 |
| RinkuCommand_DynamicProjection (details) | 584.4 us | 0.99 | 18.59 KB | 0.88 |
| **Dynamic projection, async** | | | | |
| Dapper_DynamicProjectionDynamic (Id) | 587.6 us | 1.00 | 14.21 KB | 1.00 |
| Dapper_RawDictionaryProjection (Id) | 555.9 us | 0.95 | 13.44 KB | 0.95 |
| RinkuCommand_DynaObjectProjection (Id) | 553.6 us | 0.94 | 12.14 KB | 0.85 |
| RinkuCommand_RawDictionaryProjection (Id) | 562.3 us | 0.96 | 12.39 KB | 0.87 |
| Dapper_DynamicProjectionDynamic (details) | 599.1 us | 1.00 | 21.17 KB | 1.00 |
| Dapper_RawDictionaryProjection (details) | 552.2 us | 0.92 | 20.17 KB | 0.95 |
| RinkuCommand_DynaObjectProjection (details) | 573.1 us | 0.96 | 18.59 KB | 0.88 |
| RinkuCommand_RawDictionaryProjection (details) | 584.8 us | 0.98 | 19.10 KB | 0.90 |
| **Complex mapping** | | | | |
| Dapper_Complex | 645.5 us | 1.00 | 15.64 KB | 1.00 |
| RinkuCommand_Complex | 634.3 us | 0.98 | 14.33 KB | 0.92 |
| RinkuSql_Complex | 621.7 us | 0.96 | 14.33 KB | 0.92 |
| **Scalar, async** | | | | |
| Dapper_Scalar | 1.303 ms | 1.00 | 12.22 KB | 1.00 |
| RinkuCommand_Scalar | 1.267 ms | 0.97 | 10.85 KB | 0.89 |
| RinkuSql_Scalar | 1.269 ms | 0.97 | 10.85 KB | 0.89 |
| **Scalar sequence, async** (5000 rows) | | | | |
| Dapper_ScalarSequence | 1.944 ms | 1.00 | 202.21 KB | 1.00 |
| RinkuCommand_ScalarSequence | 1.846 ms | 0.95 | 84.02 KB | 0.42 |
| RinkuSql_ScalarSequence | 1.829 ms | 0.94 | 84.01 KB | 0.42 |
| **Manually added parameters** | | | | |
| Dapper_DynamicParameters | 556.1 us | 1.00 | 16.32 KB | 1.00 |
| RinkuCommand_BuilderCommand | 540.6 us | 0.97 | 14.70 KB | 0.90 |
| **Scalar count: fixed path and reusable conditional path without parameter** | | | | |
| Dapper_FixedCount | 1.296 ms | 1.00 | 12.22 KB | 1.00 |
| RinkuCommand_FixedCount | 1.282 ms | 0.99 | 10.85 KB | 0.89 |
| RinkuCommand_ConditionalCountWithoutId | 1.283 ms | 0.99 | 11.20 KB | 0.92 |
| **Scalar count: fixed path and reusable conditional path with parameter** | | | | |
| Dapper_FixedCountById | 538.4 us | 1.00 | 13.35 KB | 1.00 |
| RinkuCommand_FixedCountById | 510.5 us | 0.95 | 11.91 KB | 0.89 |
| RinkuCommand_ConditionalCountWithId | 515.5 us | 0.96 | 11.91 KB | 0.89 |
| **Literal replacement** | | | | |
| Dapper_LiteralReplacement | 544.3 us | 1.00 | 6.42 KB | 1.00 |
| RinkuCommand_NumericLiteral | 536.6 us | 0.99 | 4.79 KB | 0.75 |
| RinkuCommand_ParameterizedCount | 529.2 us | 0.97 | 5.35 KB | 0.83 |
| **IN clause** | | | | |
| Dapper_InClause | 869.5 us | 1.00 | 51.74 KB | 1.00 |
| RinkuCommand_InClause | 736.7 us | 0.85 | 47.75 KB | 0.92 |
| RinkuSql_InClause | 730.7 us | 0.84 | 47.75 KB | 0.92 |
| **Execute, sync** | | | | |
| Dapper_Execute | 1.525 ms | 1.00 | 6.13 KB | 1.00 |
| RinkuCommand_Execute | 1.420 ms | 0.93 | 5.02 KB | 0.82 |
| RinkuSql_Execute | 1.402 ms | 0.92 | 5.02 KB | 0.82 |
| **Execute, async** | | | | |
| Dapper_ExecuteAsync | 1.574 ms | 1.00 | 10.99 KB | 1.00 |
| RinkuCommand_ExecuteAsync | 1.458 ms | 0.93 | 9.87 KB | 0.90 |
| RinkuSql_ExecuteAsync | 1.477 ms | 0.94 | 9.87 KB | 0.90 |
| **Batch execution** | | | | |
| Dapper_BatchExecute | 113.326 ms | 1.00 | 225.25 KB | 1.00 |
| RinkuCommand_BatchUseWith | 104.492 ms | 0.92 | 246.36 KB | 1.09 |
| **Multiple result sets** | | | | |
| Dapper_MultiResultSet | 693.8 us | 1.00 | 33.59 KB | 1.00 |
| RinkuCommand_MultiResultSet | 682.6 us | 0.98 | 33.25 KB | 0.99 |
| RinkuSql_MultiResultSet | 685.1 us | 0.99 | 33.25 KB | 0.99 |
| **One-to-many fold** | | | | |
| Dapper_OneToManyMultiMap | 10.777 ms | 1.00 | 3.30 MB | 1.00 |
| RinkuCommand_OneToManyTuples | 10.622 ms | 0.99 | 2.61 MB | 0.79 |
| RinkuCommand_OneToManyNative | 10.386 ms | 0.96 | 2.15 MB | 0.65 |
| **One-to-many separate result sets** | | | | |
| Dapper_OneToManySeparateResultSets | 11.362 ms | 1.00 | 2.85 MB | 1.00 |
| RinkuCommand_OneToManySeparateResultSets | 9.997 ms | 0.88 | 2.10 MB | 0.74 |
| **One-to-many separate result sets, ordered** | | | | |
| Dapper_OneToManySeparateResultSetsOrdered | 8.398 ms | 1.00 | 2.69 MB | 1.00 |
| RinkuCommand_OneToManySeparateResultSetsOrdered | 7.966 ms | 0.95 | 1.94 MB | 0.72 |

The comparisons use the same SQL whenever the feature allows it and validate the returned values before timing. See the [benchmark source](https://github.com/RinkuLib/RinkuLib/tree/main/RinkuLib.Benchmarks) for the complete setup and additional cases. To run it locally with Docker running:

```bash
dotnet run -c Release --project RinkuLib.Benchmarks
```
