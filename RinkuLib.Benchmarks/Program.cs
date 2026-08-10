using BenchmarkDotNet.Running;
using RinkuLib.Benchmarks;

internal static class Program {
    public static async Task Main(string[] args) {
        if (args.Contains("--validate")) {
            await using var benchmark = new EndToEndBenchmark();
            await benchmark.Setup();
            return;
        }

        BenchmarkRunner.Run<EndToEndBenchmark>(args: args);
    }
}
