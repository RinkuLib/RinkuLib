# Performance

These measurements come from one BenchmarkDotNet run against SQL Server in a test container. Every measurement includes the database round trip. Lower values are better, and ratios compare each Rinku route with Dapper for the same operation.

The numbers are a recorded run, not a guarantee for another database, provider, machine, schema, or workload.

## First result

| Operation | Route | Sync mean | Sync allocated | Async mean | Async allocated |
| --- | --- | ---: | ---: | ---: | ---: |
| First result | Dapper | 538.6 us (1.00) | 15.87 KB (1.00) | 595.5 us (1.00) | 22.65 KB (1.00) |
| | Rinku command | 535.2 us (0.99) | 14.69 KB (0.93) | 584.3 us (0.98) | 21.31 KB (0.94) |
| | Rinku SQL string | 537.3 us (1.00) | 14.69 KB (0.93) | 584.1 us (0.98) | 21.31 KB (0.94) |
| Exactly one | Dapper | 527.0 us (1.00) | 15.87 KB (1.00) | 587.0 us (1.00) | 22.79 KB (1.00) |
| | Rinku command `Single<T>` | 525.2 us (1.00) | 14.69 KB (0.93) | 577.7 us (0.98) | 21.45 KB (0.94) |
| | Rinku SQL `Single<T>` | 519.6 us (0.99) | 14.69 KB (0.93) | 575.9 us (0.98) | 21.45 KB (0.94) |
| Nullable value | Dapper | 497.2 us (1.00) | 6.53 KB (1.00) | 545.6 us (1.00) | 13.36 KB (1.00) |
| | Rinku command | 488.3 us (0.98) | 5.39 KB (0.83) | 537.0 us (0.98) | 12.06 KB (0.90) |

## Many results

| Shape | Rows | Route | Sync mean | Sync allocated | Async mean | Async allocated |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| Stream | 50 | Dapper | 1.162 ms (1.00) | 315.85 KB (1.00) | 1.208 ms (1.00) | 323.87 KB (1.00) |
| | | Rinku command | 1.159 ms (1.00) | 309.95 KB (0.98) | 1.227 ms (1.02) | 316.70 KB (0.98) |
| | | Rinku SQL | 1.182 ms (1.02) | 309.95 KB (0.98) | 1.203 ms (1.00) | 316.70 KB (0.98) |
| Stream | 5000 | Dapper | 59.286 ms (1.00) | 29.77 MB (1.00) | 60.036 ms (1.00) | 29.77 MB (1.00) |
| | | Rinku command | 59.927 ms (1.01) | 29.42 MB (0.99) | 60.059 ms (1.00) | 29.43 MB (0.99) |
| Buffered | 50 | Dapper | 1.170 ms (1.00) | 316.97 KB (1.00) | 1.242 ms (1.00) | 323.61 KB (1.00) |
| | | Rinku command | 1.160 ms (0.99) | 311.00 KB (0.98) | 1.207 ms (0.97) | 317.71 KB (0.98) |
| Buffered | 5000 | Dapper | 68.565 ms (1.00) | 29.90 MB (1.00) | 70.685 ms (1.00) | 29.91 MB (1.00) |
| | | Rinku command | 70.106 ms (1.02) | 29.55 MB (0.99) | 70.171 ms (0.99) | 29.56 MB (0.99) |

## Command execution and scalar values

| Operation | Route | Sync mean | Sync allocated | Async mean | Async allocated |
| --- | --- | ---: | ---: | ---: | ---: |
| Execute | Dapper | 1.368 ms (1.00) | 6.13 KB (1.00) | 1.471 ms (1.00) | 10.98 KB (1.00) |
| | Rinku command | 1.365 ms (1.00) | 5.02 KB (0.82) | 1.456 ms (0.99) | 9.87 KB (0.90) |
| Execute scalar | Dapper | n/a | n/a | 1.292 ms (1.00) | 13.52 KB (1.00) |
| | Rinku command | n/a | n/a | 1.298 ms (1.01) | 13.01 KB (0.96) |
| Scalar query | Dapper | n/a | n/a | 1.315 ms (1.00) | 12.22 KB (1.00) |
| | Rinku command | n/a | n/a | 1.284 ms (0.98) | 10.84 KB (0.89) |
| Scalar sequence | Dapper | n/a | n/a | 1.838 ms (1.00) | 202.20 KB (1.00) |
| | Rinku command | n/a | n/a | 1.801 ms (0.98) | 84.02 KB (0.42) |

## Dynamic results

| Operation | Route | Mean | Allocated |
| --- | --- | ---: | ---: |
| All columns | Dapper dynamic | 597.5 us (1.00) | 22.71 KB (1.00) |
| | Rinku `DynaObject` | 590.1 us (0.99) | 21.38 KB (0.94) |
| Id projection | Dapper dynamic | 555.4 us (1.00) | 14.21 KB (1.00) |
| | Rinku `DynaObject` | 523.0 us (0.94) | 12.02 KB (0.85) |
| | Rinku dictionary | 521.1 us (0.94) | 12.26 KB (0.86) |
| Details projection | Dapper dynamic | 577.9 us (1.00) | 21.17 KB (1.00) |
| | Rinku `DynaObject` | 563.1 us (0.97) | 18.59 KB (0.88) |
| | Rinku dictionary | 572.7 us (0.99) | 18.97 KB (0.90) |

## Relationships and result sets

| Relationship | Route | Mean | Allocated |
| --- | --- | ---: | ---: |
| One-to-many fold | Dapper multi-map | 10.745 ms (1.00) | 3.30 MB (1.00) |
| | Rinku tuples | 10.556 ms (0.98) | 2.61 MB (0.79) |
| | Rinku native grouping | 10.254 ms (0.95) | 2.15 MB (0.65) |
| Separate result sets | Dapper | 11.133 ms (1.00) | 2.85 MB (1.00) |
| | Rinku command | 9.371 ms (0.84) | 2.09 MB (0.73) |
| Ordered result sets | Dapper | 8.299 ms (1.00) | 2.69 MB (1.00) |
| | Rinku command | 7.712 ms (0.93) | 1.94 MB (0.72) |

## Batch execution

| Route | Mean | Allocated |
| --- | ---: | ---: |
| Dapper | 99.174 ms (1.00) | 225.12 KB (1.00) |
| Rinku command with `UseWith` | 90.172 ms (0.91) | 163.88 KB (0.73) |

The benchmark validates returned values before timing and uses the same SQL wherever the compared features allow it. The complete setup and additional cases are stored in the `RinkuLib.Benchmarks` project.

```bash
dotnet run -c Release --project RinkuLib.Benchmarks
```
