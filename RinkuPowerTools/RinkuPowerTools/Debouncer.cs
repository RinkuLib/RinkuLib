namespace RinkuPowerTools;
public sealed class Debouncer(int delayMs, Action action) {
    private readonly int _delayMs = delayMs;
    private readonly Action _action = action;
    private CancellationTokenSource? _cts;

    public void Invoke() {
        _cts?.Cancel();
        _cts?.Dispose();

        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = RunAsync(token);
    }

    private async Task RunAsync(CancellationToken token) {
        try {
            await Task.Delay(_delayMs, token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
                return;

            _action();
        }
        catch (OperationCanceledException) {
            // expected
        }
    }
}