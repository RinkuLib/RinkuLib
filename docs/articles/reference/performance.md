# Performance

Rinku benchmarks are compared with Dapper using the same SQL, database, provider, and result shape. Every measurement includes the database round trip.

The benchmark suite follows two invariants:

- Rinku allocates less memory than Dapper.
- Rinku is no slower than Dapper, allowing for normal benchmark error.

Ratios use Dapper as the baseline. The benchmark-suite criterion is lower allocation and timing at or below Dapper within normal measurement error.

Reported 2026-08-22 on Windows 11 with .NET SDK 10.0.303 and .NET 10.0.11, using x64 RyuJIT x86-64-v3.

## Latest verified results

| Benchmark | Route | Dapper mean | Rinku mean | Performance ratio | Dapper memory | Rinku memory | Memory ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Complex mapping | Command | 546.9 us | 550.4 us | 1.01 | 15.63 KB | 14.33 KB | 0.92 |
| | SQL string | 546.9 us | 539.3 us | 0.99 | 15.63 KB | 14.33 KB | 0.92 |
| Direct reader async | Command | 632.7 us | 633.2 us | 1.00 | 12.13 KB | 11.54 KB | 0.95 |
| Direct reader sync | Command | 582.1 us | 586.2 us | 1.01 | 5.33 KB | 4.73 KB | 0.89 |
| Output and return parameters | Command | 531.7 us | 527.7 us | 0.99 | 13.98 KB | 12.57 KB | 0.90 |

## Running the benchmarks

Run from a standalone Release terminal with Docker running and no debugger attached:

```text
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release
```

### Focused runs

```text
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*Complex*"
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*DirectReader*"
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*OutputParameter*"
```

Rerun any result that violates an invariant in isolation before treating it as a regression.
