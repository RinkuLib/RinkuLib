namespace RinkuPowerTools.Cli;

internal static class Program {
    public static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        try {
            return await Cli.RunAsync(args, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        catch (CliException ex) {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
