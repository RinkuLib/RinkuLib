using BenchmarkDotNet.Running;
using RinkuLib.Benchmarks;

internal static class Program {
    public static async Task Main(string[] args) {
        if (args.Contains("--validate")) {
            await using var benchmark = new BaseBenchmark();
            await benchmark.Setup();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
