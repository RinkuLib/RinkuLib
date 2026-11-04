using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class EventSpecializationTests
{
    [Fact]
    public void Prepared_helpers_choose_event_names_and_delegate_types_outside_the_compiler()
    {
        EventProbe.Total = 0;
        var plan = ExecutorBuilder.Start<LogicalEventSource>(out var source).Then(() => EventHooks.Add(source.Val));
        var executor = plan.Build(new ExecutorBuildConfiguration().RegisterProvider(typeof(EventHooks)).Support<IExecutor<LogicalEventSource>>().Support<IExecutor<ConcreteEventSource>>().Support<IExecutor<SameNameConcreteEventSource>>());
        var logical = new LogicalEventSource();
        var renamed = new ConcreteEventSource();
        var sameName = new SameNameConcreteEventSource();

        ((IExecutor<LogicalEventSource>)executor).Execute(logical);
        ((IExecutor<ConcreteEventSource>)executor).Execute(renamed);
        ((IExecutor<ConcreteEventSource>)executor).Execute(renamed);
        ((IExecutor<SameNameConcreteEventSource>)executor).Execute(sameName);
        logical.Raise();
        renamed.Raise();
        sameName.Raise();

        Assert.Equal(5, EventProbe.Total);
    }

    [Fact]
    public void Prepared_callback_plans_survive_clone_merge_and_symbolic_serialization()
    {
        EventProbe.Total = 0;
        var source = ExecutorBuilder.Start<LogicalEventSource>(out var owner).Then(() => EventHooks.Add(owner.Val));
        var plan = ExecutorBuilder.Start<LogicalEventSource>(out var target).Merge(source.Clone().ToScope());
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(plan));
        var instance = new ConcreteEventSource();

        restored.Build<IExecutor<ConcreteEventSource>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(EventHooks))).Execute(instance);
        instance.Raise();

        Assert.Equal(2, EventProbe.Total);
    }

    public static class EventHooks
    {
        public static ExecutorScope Add(LogicalEventSource target) => Prepare<LogicalEventSource, LogicalEventArgs>();
        public static ExecutorScope Add(ConcreteEventSource target) => Prepare<ConcreteEventSource, ConcreteEventArgs>();
        public static ExecutorScope Add(SameNameConcreteEventSource target) => Prepare<SameNameConcreteEventSource, ConcreteEventArgs>();

        private static ExecutorScope Prepare<TSource, TArgs>() where TArgs : EventArgs
        {
            var plan = ExecutorBuilder.Start<LogicalEventSource, LogicalEventArgs>(out var source, out var args)
                .Then(() => source.Val.Record(args.Val.Level));
            var executor = plan.Build<IExecutor<TSource, TArgs>>();
            EventHandler<TArgs> handler = (sender, args) => executor.Execute((TSource)sender!, args);
            return ExecutorBuilder.Start<TSource>(out var target).Call(nameof(Apply), [target, handler]).ToScope();
        }

        public static void Apply(LogicalEventSource target, EventHandler<LogicalEventArgs> handler)
        {
            target.Changed -= handler;
            target.Changed += handler;
        }
        public static void Apply(ConcreteEventSource target, EventHandler<ConcreteEventArgs> handler)
        {
            target.Updated -= handler;
            target.Updated += handler;
        }
        public static void Apply(SameNameConcreteEventSource target, EventHandler<ConcreteEventArgs> handler)
        {
            target.Changed -= handler;
            target.Changed += handler;
        }
    }
}

public sealed class LogicalEventSource
{
    public event EventHandler<LogicalEventArgs>? Changed;
    public void Raise() => Changed?.Invoke(this, new LogicalEventArgs());
    public void Record(int level) => EventProbe.Mark(level);
}

public sealed class SameNameConcreteEventSource
{
    public event EventHandler<ConcreteEventArgs>? Changed;
    public void Raise() => Changed?.Invoke(this, new ConcreteEventArgs());
    public void Record(int level) => EventProbe.Mark(level);
}
public sealed class ConcreteEventSource
{
    public event EventHandler<ConcreteEventArgs>? Updated;
    public void Raise() => Updated?.Invoke(this, new ConcreteEventArgs());
    public void Record(int level) => EventProbe.Mark(level);
}

public sealed class LogicalEventArgs : EventArgs
{
    public int Level { get; } = 1;
}
public sealed class ConcreteEventArgs : EventArgs
{
    public int Level { get; } = 2;
}

public static class EventProbe
{
    public static int Total
    {
        get; set;
    }
    public static void Mark(int level) => Total += level;
}