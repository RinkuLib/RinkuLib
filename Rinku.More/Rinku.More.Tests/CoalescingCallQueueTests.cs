using Rinku.More;

namespace Rinku.More.Tests;

public class CoalescingCallQueueTests
{
    [Fact]
    public async Task A_burst_has_one_running_call_and_one_pending_call()
    {
        ICallQueue queue = new CoalescingCallQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        int active = 0;
        Func<Task> call = async () =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            if (Interlocked.Increment(ref calls) == 1)
            {
                await release.Task;
            }
            Interlocked.Decrement(ref active);
        };
        Task first = queue.EnqueueAsync(call);
        Task[] requests = new Task[100];
        await Task.WhenAll(Enumerable.Range(0, requests.Length).Select(index => Task.Run(() =>
        {
            requests[index] = queue.EnqueueAsync(call);
        })));
        Assert.All(requests, request => Assert.Same(first, request));
        Assert.Equal(1, calls);
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, calls);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Requests_during_the_second_call_leave_only_one_further_call()
    {
        var queue = new CoalescingCallQueue();
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Func<Task> call = async () =>
        {
            int current = ++calls;
            if (current == 1)
            {
                await firstRelease.Task;
            }
            else if (current == 2)
            {
                secondEntered.SetResult();
                await secondRelease.Task;
            }
        };
        Task batch = queue.EnqueueAsync(call);
        Assert.Same(batch, queue.EnqueueAsync(call));
        firstRelease.SetResult();
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (int request = 0; request < 100; request++)
        {
            Assert.Same(batch, queue.EnqueueAsync(call));
        }
        secondRelease.SetResult();
        await batch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Equal_method_delegates_reuse_the_same_entry()
    {
        var queue = new CoalescingCallQueue();
        var operation = new Operation();
        var firstDelegate = new Func<Task>(operation.RunAsync);
        var secondDelegate = new Func<Task>(operation.RunAsync);
        Assert.NotSame(firstDelegate, secondDelegate);
        Task first = queue.EnqueueAsync(firstDelegate);
        Assert.Same(first, queue.EnqueueAsync(secondDelegate));
        operation.Release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, operation.Calls);
        await queue.EnqueueAsync(secondDelegate);
        Assert.Equal(3, operation.Calls);
    }

    [Fact]
    public async Task Different_instances_run_independently()
    {
        var queue = new CoalescingCallQueue();
        var firstOperation = new Operation();
        var secondOperation = new Operation();
        Task first = queue.EnqueueAsync(firstOperation.RunAsync);
        Task second = queue.EnqueueAsync(secondOperation.RunAsync);
        Assert.Equal(1, firstOperation.Calls);
        Assert.Equal(1, secondOperation.Calls);
        firstOperation.Release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(second.IsCompleted);
        secondOperation.Release.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Pending_requests_allocate_nothing_and_share_the_completion()
    {
        var queue = new CoalescingCallQueue();
        var operation = new Operation();
        Func<Task> call = operation.RunAsync;
        Task batch = queue.EnqueueAsync(call);
        for (int request = 0; request < 100; request++)
        {
            _ = queue.EnqueueAsync(call);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool shared = true;
        for (int request = 0; request < 1000; request++)
        {
            shared &= ReferenceEquals(batch, queue.EnqueueAsync(call));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(shared);
        Assert.Equal(0, allocated);
        operation.Release.SetResult();
        await batch.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, operation.Calls);
    }

    [Fact]
    public async Task Failure_does_not_discard_a_pending_request_or_leave_the_entry_running()
    {
        var queue = new CoalescingCallQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new IOException();
        int calls = 0;
        Func<Task> call = async () =>
        {
            if (++calls == 1)
            {
                await release.Task;
                throw error;
            }
        };
        Task batch = queue.EnqueueAsync(call);
        Assert.Same(batch, queue.EnqueueAsync(call));
        release.SetResult();
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => batch));
        Assert.Equal(2, calls);
        await queue.EnqueueAsync(call);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Cancellation_and_synchronous_exceptions_leave_the_entry_reusable()
    {
        var queue = new CoalescingCallQueue();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        Func<Task> call = () => ++calls switch
        {
            1 => throw new IOException(),
            2 => Task.FromCanceled(cancellation.Token),
            _ => Task.CompletedTask
        };
        await Assert.ThrowsAsync<IOException>(() => queue.EnqueueAsync(call));
        Task canceled = queue.EnqueueAsync(call);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(canceled.IsCanceled);
        await queue.EnqueueAsync(call);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Error_callback_reports_each_failed_invocation_once_and_preserves_pending_work()
    {
        var reported = new List<Exception>();
        var queue = new CoalescingCallQueue(reported.Add);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstError = new IOException();
        var secondError = new InvalidOperationException();
        int calls = 0;
        Func<Task> call = async () =>
        {
            switch (++calls)
            {
                case 1:
                    await release.Task;
                    throw firstError;
                case 2:
                    throw secondError;
            }
        };
        Task batch = queue.EnqueueAsync(call);
        for (int request = 0; request < 100; request++)
        {
            Assert.Same(batch, queue.EnqueueAsync(call));
        }
        release.SetResult();
        Assert.Same(firstError, await Assert.ThrowsAsync<IOException>(() => batch));
        Assert.Equal([firstError, secondError], reported);
        await queue.EnqueueAsync(call);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Throwing_error_callback_cannot_strand_the_entry_or_discard_pending_work()
    {
        var operationError = new IOException();
        var reportingError = new InvalidOperationException();
        var queue = new CoalescingCallQueue(_ => throw reportingError);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Func<Task> call = async () =>
        {
            if (++calls == 1)
            {
                await release.Task;
                throw operationError;
            }
        };
        Task batch = queue.EnqueueAsync(call);
        Assert.Same(batch, queue.EnqueueAsync(call));
        release.SetResult();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => batch);
        Assert.Equal([operationError, reportingError], failure.InnerExceptions);
        Assert.Equal(2, calls);
        await queue.EnqueueAsync(call);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Error_callback_can_enqueue_pending_work_without_waiting_for_the_queue_lock()
    {
        ICallQueue? queue = null;
        Func<Task>? call = null;
        int calls = 0;
        queue = new CoalescingCallQueue(_ => queue!.EnqueueAsync(call!));
        call = () => ++calls == 1 ? throw new IOException() : Task.CompletedTask;
        Task batch = queue.EnqueueAsync(call);
        Assert.True(batch.IsFaulted);
        Assert.Equal(2, calls);
    }

    private class Operation
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task RunAsync()
        {
            if (++Calls == 1)
            {
                await Release.Task;
            }
        }
    }
}
