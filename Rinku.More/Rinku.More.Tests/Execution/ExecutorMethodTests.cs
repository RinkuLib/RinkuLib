using Rinku;

namespace Rinku.More.Tests;

public sealed class ExecutorMethodTests
{
    [Fact]
    public void Named_methods_share_fields_without_aliasing_equal_signatures()
    {
        var plan = ExecutorBuilder.Start().Field<int>(out var value, "counter");
        plan.Then(() => Counter.Increment(ref value.Val));
        var reset = plan.DefineMethod<ICounter>(nameof(ICounter.Reset));
        reset.Field<int>(out var resetting, "counter");
        reset.Then(() => Counter.Reset(ref resetting.Val));
        var read = plan.DefineMethod<ICounter>(nameof(ICounter.Read));
        read.Field<int>(out var reading, "counter");
        read.Result(reading);
        var executor = plan.Build<ICounter>(new ExecutorBuildConfiguration().RegisterProvider(typeof(Counter)));
        executor.Execute();
        executor.Execute();
        Assert.Equal(2, executor.Read());
        executor.Reset();
        Assert.Equal(0, executor.Read());
    }

    public interface ICounter : IExecutor
    {
        void Execute();
        void Reset();
        int Read();
    }

    public static class Counter
    {
        public static void Increment(ref int value) => value++;
        public static void Reset(ref int value) => value = 0;
    }

    [Fact]
    public void Callback_captures_change_at_the_recorded_operation_not_method_entry()
    {
        var holder = new Holder();
        var body = ExecutorBuilder.Start<Source, Target>(out var captured, out var destination)
            .Then(() => destination.Val.SetValue(captured.Val.Value));
        var plan = ExecutorBuilder.Start<Source, Target>(out var source, out var target)
            .Then(() => holder.Invoke())
            .Callback<Action>(out var callback, body.ToScope(), source, target)
            .Then(() => holder.Set(callback.Val));
        var executor = plan.Build<IExecutor<Source, Target>>();
        var first = new Source { Value = 7 };
        var second = new Source { Value = 11 };
        var left = new Target();
        var right = new Target();
        executor.Execute(first, left);
        Action retained = holder.Value!;
        first.Value = 31;
        executor.Execute(second, right);
        Assert.Equal(31, left.Value);
        Assert.Equal(0, right.Value);
        Assert.Same(retained, holder.Value);
        holder.Invoke();
        Assert.Equal(11, right.Value);
    }

    [Fact]
    public void Cloning_and_merging_keep_additional_method_plans()
    {
        var original = ExecutorBuilder.Start().Field<int>(out var value, "counter")
            .Then(() => Counter.Increment(ref value.Val));
        original.DefineMethod<ICounter>(nameof(ICounter.Read)).Field<int>(out var reading, "counter").Result(reading);
        original.DefineMethod<ICounter>(nameof(ICounter.Reset)).Field<int>(out var resetting, "counter").Then(() => Counter.Reset(ref resetting.Val));
        var merged = ExecutorBuilder.Start().Merge(original.Clone().ToScope());
        var factory = merged.Compile<ICounter>(new ExecutorBuildConfiguration().RegisterProvider(typeof(Counter)));
        var left = factory();
        var right = factory();
        left.Execute();
        Assert.Equal(1, left.Read());
        Assert.Equal(0, right.Read());
        left.Reset();
        Assert.Equal(0, left.Read());
    }

    public sealed class Source { public int Value { get; set; } }
    public sealed class Target
    {
        public int Value { get; private set; }
        public void SetValue(int value) => Value = value;
    }
    public sealed class Holder
    {
        public Action? Value { get; private set; }
        public void Set(Action value) => Value = value;
        public void Invoke() => Value?.Invoke();
    }
}
