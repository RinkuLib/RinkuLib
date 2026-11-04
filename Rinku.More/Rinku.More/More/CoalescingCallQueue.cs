namespace Rinku.More;

public interface ICallQueue
{
    Task EnqueueAsync(Func<Task> call);
}

public class CoalescingCallQueue(Action<Exception>? onError = null) : ICallQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<Func<Task>, Entry> _calls = [];

    public Task EnqueueAsync(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Entry entry;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (!_calls.TryGetValue(call, out entry!))
            {
                entry = new(call);
                _calls.Add(call, entry);
            }
            if (entry.Completion is not null)
            {
                entry.Pending = true;
                return entry.Completion.Task;
            }
            completion = entry.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _ = RunAsync(entry, completion);
        return completion.Task;
    }

    private async Task RunAsync(Entry entry, TaskCompletionSource completion)
    {
        Exception? failure = null;
        while (true)
        {
            try
            {
                await entry.Call().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure ??= error;
                try
                {
                    onError?.Invoke(error);
                }
                catch (Exception reportingError)
                {
                    failure = new AggregateException(failure, reportingError);
                }
            }
            lock (_gate)
            {
                if (entry.Pending)
                {
                    entry.Pending = false;
                    continue;
                }
                entry.Completion = null;
                if (failure is OperationCanceledException canceled)
                {
                    completion.SetCanceled(canceled.CancellationToken);
                }
                else if (failure is not null)
                {
                    completion.SetException(failure);
                    if (onError is not null)
                    {
                        _ = completion.Task.Exception;
                    }
                }
                else
                {
                    completion.SetResult();
                }
                return;
            }
        }
    }

    private class Entry(Func<Task> call)
    {
        public Func<Task> Call { get; } = call;
        public TaskCompletionSource? Completion;
        public bool Pending;
    }
}
