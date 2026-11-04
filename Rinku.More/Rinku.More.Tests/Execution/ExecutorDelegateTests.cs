using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorDelegateTests
{
    [Fact]
    public void An_existing_delegate_remains_the_same_fixed_value()
    {
        var destination = new CallbackTarget();
        Action callback = () => destination.Record(42);
        var plan = ExecutorBuilder.Start<CallbackTarget>(out var target);
        plan.Then(() => target.Val.Attach(callback));
        var create = plan.Compile<IExecutor<CallbackTarget>>();
        var first = create();
        var second = create();
        var left = new CallbackTarget();
        var right = new CallbackTarget();
        first.Execute(left);
        second.Execute(right);

        Assert.Same(callback, left.Callback);
        Assert.Same(callback, right.Callback);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            first.Execute(left);
            left.Callback!();
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(42, destination.Value);
    }

    [Fact]
    public void A_factory_can_use_the_signatures_supplied_by_its_configuration()
    {
        var plan = ExecutorBuilder.Start<int>(out _);
        var create = plan.Compile<IExecutor>(new ExecutorBuildConfiguration().Support<IExecutor<int>>());

        Assert.IsAssignableFrom<IExecutor<int>>(create());
    }

    [Fact]
    public void A_compiled_factory_reuses_the_type_and_isolates_instance_captures()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget, int>(out var target, out var value);
        plan.Then(() => target.Val.Attach(() => target.Val.Record(value.Val)));
        Func<IExecutor<CallbackTarget, int>> create = plan.Compile<IExecutor<CallbackTarget, int>>();
        var first = create();
        var second = create();
        var left = new CallbackTarget();
        var right = new CallbackTarget();
        first.Execute(left, 10);
        Action initial = left.Callback!;
        second.Execute(right, 20);
        first.Execute(left, 30);
        left.Callback!();
        right.Callback!();

        Assert.Equal(first.GetType(), second.GetType());
        Assert.Same(initial, left.Callback);
        Assert.NotSame(initial, right.Callback);
        Assert.Equal(30, left.Value);
        Assert.Equal(20, right.Value);
    }

    [Fact]
    public void Nested_delegate_arguments_and_results_use_normal_resolution()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget, int>(out var target, out var value);
        plan.Then(() => target.Val.Set((int argument) => value.Val + argument));
        var executor = plan.Build<IExecutor<CallbackTarget, int>>();
        var instance = new CallbackTarget();
        executor.Execute(instance, 6);

        Assert.Equal(10, instance.Calculate!(4));
    }

    [Fact]
    public void Captured_produced_values_specialize_before_the_delegate_body()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget>(out var target);
        plan.Call<object>(out var detail, nameof(CallbackTarget.Detail), [target]);
        plan.Then(() => target.Val.Attach(() => detail.Val.ToString()));
        var instance = new CallbackTarget();
        plan.Build<IExecutor<CallbackTarget>>().Execute(instance);

        instance.Callback!();
        Assert.Equal(1, instance.Child.Reads);
    }

    [Fact]
    public void Reexecuting_a_bound_delegate_does_not_allocate()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget, int>(out var target, out var value);
        plan.Then(() => target.Val.Attach(() => target.Val.Record(value.Val)));
        var executor = plan.Build<IExecutor<CallbackTarget, int>>();
        var instance = new CallbackTarget();
        executor.Execute(instance, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            executor.Execute(instance, index);
            instance.Callback!();
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(999, instance.Value);
    }

    [Fact]
    public void Delegate_recordings_survive_clone_and_merge()
    {
        var source = ExecutorBuilder.Start<CallbackTarget, int>(out var target, out var value);
        source.Then(() => target.Val.Attach(() => target.Val.Record(value.Val)));
        var plan = ExecutorBuilder.Start<CallbackTarget, int>(out _, out _).Merge(source.Clone().ToScope());
        var instance = new CallbackTarget();
        plan.Build<IExecutor<CallbackTarget, int>>().Execute(instance, 42);
        instance.Callback!();

        Assert.Equal(42, instance.Value);
    }

    [Fact]
    public void A_nested_delegate_can_capture_the_enclosing_delegate_parameter()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget>(out var target);
        plan.Then(() => target.Val.Set((int argument) => target.Val.Defer(() => argument)));
        var instance = new CallbackTarget();
        plan.Build<IExecutor<CallbackTarget>>().Execute(instance);

        Assert.Equal(7, instance.Calculate!(7));
        Func<int> original = instance.Deferred!;
        Assert.Equal(11, instance.Calculate!(11));
        Assert.Same(original, instance.Deferred);
        Assert.Equal(11, original());
    }

    [Fact]
    public void Captured_mutable_variables_are_shared_with_the_outer_execution()
    {
        var plan = ExecutorBuilder.Start<CallbackTarget>(out var target).Declare(out Variable<int> count).Then(count, 4);
        plan.Then(() => target.Val.Attach(() => target.Val.Record(count.Val)));
        plan.Then(count, () => count.Val + 1);
        var instance = new CallbackTarget();
        plan.Build<IExecutor<CallbackTarget>>().Execute(instance);
        instance.Callback!();

        Assert.Equal(5, instance.Value);
    }

    public sealed class CallbackTarget
    {
        public Action? Callback { get; private set; }
        public Func<int, int>? Calculate { get; private set; }
        public Func<int>? Deferred { get; private set; }
        public int Value { get; private set; }
        public CallbackDetail Child { get; } = new();
        public CallbackDetail Detail() => Child;
        public void Attach(Action action) => Callback = action;
        public void Set(Func<int, int> action) => Calculate = action;
        public int Defer(Func<int> value)
        {
            Deferred = value;
            return value();
        }
        public void Record(int value) => Value = value;
    }

    public sealed class CallbackDetail
    {
        public int Reads { get; private set; }
        public override string ToString()
        {
            Reads++;
            return Reads.ToString();
        }
    }
}
