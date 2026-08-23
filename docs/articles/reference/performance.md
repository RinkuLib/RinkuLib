# Performance

## Latest verified results

These measurements include the database round trip. Dapper and Rinku use the same SQL, database, provider, and result shape in each benchmark.

The results below were recorded on 2026-08-22 on Windows 11 with .NET SDK 10.0.303 and .NET 10.0.11, using x64 RyuJIT x86-64-v3.

The benchmark cases and setup are defined in the [`RinkuLib.Benchmarks` project](https://github.com/RinkuLib/RinkuLib/tree/alpha/RinkuLib.Benchmarks).

| Benchmark | Route | Dapper mean | Rinku mean | Time ratio | Dapper memory | Rinku memory | Memory ratio |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Complex mapping | Command | 546.9 us | 550.4 us | 1.01 | 15.63 KB | 14.33 KB | 0.92 |
| Complex mapping | SQL string | 546.9 us | 539.3 us | 0.99 | 15.63 KB | 14.33 KB | 0.92 |
| Direct reader async | Command | 632.7 us | 633.2 us | 1.00 | 12.13 KB | 11.54 KB | 0.95 |
| Direct reader sync | Command | 582.1 us | 586.2 us | 1.01 | 5.33 KB | 4.73 KB | 0.89 |
| Output and return parameters | Command | 531.7 us | 527.7 us | 0.99 | 13.98 KB | 12.57 KB | 0.90 |

The ratios use Dapper as `1.00`.

## Run the suite

Run the benchmark project in Release mode without a debugger.

```text
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release
```

Focused runs keep the same benchmark setup.

```text
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*Complex*"
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*DirectReader*"
dotnet run --project .\RinkuLib.Benchmarks\RinkuLib.Benchmarks.csproj -c Release -- --filter "*OutputParameter*"
```
