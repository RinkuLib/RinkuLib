using System.Linq.Expressions;
using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorRecordingTests
{
    private static ExecutorBuildConfiguration Configuration => new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations));

    [Fact]
    public void Delegate_invocation_records_without_invoking_and_executes_once_per_call()
    {
        int calls = 0;
        Func<int, int> transform = value => { calls++; return value * 2; };
        var plan = ExecutorBuilder.Start<Target, Func<int, int>>(out var target, out var callback)
            .Then(() => target.Val.Value, () => callback.Val(7));
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(plan));
        var executor = restored.Build<IExecutor<Target, Func<int, int>>>(Configuration);
        var instance = new Target();

        Assert.Equal(0, calls);
        executor.Execute(instance, transform);
        Assert.Equal(14, instance.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Single_operations_store_the_requirement_without_a_scope()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .Then(() => target.Val.Touch(), ExecutorRequirement.Optional)
            .Then(() => target.Val.Value, 4, ExecutorRequirement.Nullable)
            .Call("Missing", [target], requirement: ExecutorRequirement.Optional);

        Assert.Equal(3, plan.Nodes.Count());
        Assert.DoesNotContain(plan.Nodes, node => node.Kind == "ScopeNode");
        Assert.Equal([ExecutorRequirement.Optional, ExecutorRequirement.Nullable, ExecutorRequirement.Optional], plan.Nodes.Select(node => node.Requirement));
        var instance = new Target();
        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(1, instance.Calls);
        Assert.Equal(4, instance.Value);
    }

    [Fact]
    public void One_expression_with_nested_calls_is_one_atomic_scope()
    {
        var plan = ExecutorBuilder.Start<Authored>(out var target)
            .Then(() => target.Val.Set(Operations.Read(target.Val), Operations.Missing(target.Val)), ExecutorRequirement.Optional);
        var instance = new Target();

        Assert.Equal("ScopeNode", plan.Nodes.First().Kind);
        Assert.Single(plan.Nodes, node => node.Kind == "ScopeNode");
        Assert.Equal(ExecutorRequirement.Optional, plan.Nodes.First().Requirement);
        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(0, instance.Calls);
    }

    [Fact]
    public void A_block_expression_does_not_commit_its_first_call_if_a_later_call_cannot_bind()
    {
        var plan = ExecutorBuilder.Start<Authored>(out var target);
        Expression<Action> first = () => target.Val.Touch();
        Expression<Action> second = () => Operations.Missing(target.Val);
        var block = Expression.Lambda<Action>(Expression.Block(first.Body, second.Body));
        plan.Then(block, ExecutorRequirement.Optional);
        var instance = new Target();

        Assert.Single(plan.Nodes, node => node.Kind == "ScopeNode");
        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(0, instance.Calls);
    }

    [Fact]
    public void A_compound_result_is_available_by_name_in_the_parent_scope()
    {
        var child = ExecutorBuilder.Start<Target>(out var input).Reference<int>("result", out var value).Then(() => input.Val.Value, value);
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .Then(out _, () => Operations.Double(Operations.Read(target.Val)), "result")
            .Merge(child.ToScope());
        var instance = new Target();

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(8, instance.Value);
        Assert.Equal(1, instance.Calls);
    }

    [Fact]
    public void Nullable_compound_result_exports_its_fallback_without_running_earlier_calls()
    {
        var child = ExecutorBuilder.Start<Target>(out var input).Reference<string?>("result", out var value).Then(() => input.Val.Text, value);
        var plan = ExecutorBuilder.Start<Authored>(out var target)
            .Then(out _, () => Operations.Combine(Operations.Read(target.Val), Operations.Missing(target.Val)), "result", ExecutorRequirement.Nullable)
            .Merge(child.ToScope());
        var instance = new Target();

        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(plan.Clone()));
        restored.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Null(instance.Text);
        Assert.Equal(0, instance.Calls);
    }

    [Fact]
    public void Assigning_an_existing_local_keeps_the_entire_expression_atomic()
    {
        var plan = ExecutorBuilder.Start<Authored>(out var target).Then(out var number, 9)
            .Then(number, () => Operations.Double(Operations.Read(target.Val) + Operations.Missing(target.Val)), ExecutorRequirement.Optional)
            .Then(() => target.Val.Value, number);
        var instance = new Target();

        Assert.Single(plan.Nodes, node => node.Kind == "ScopeNode");
        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(9, instance.Value);
        Assert.Equal(0, instance.Calls);
    }

    [Fact]
    public void Invalid_requirements_leave_the_plan_unchanged()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Then(() => target.Val.Touch(), (ExecutorRequirement)255));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Call(nameof(Target.Touch), [target], requirement: (ExecutorRequirement)255));
        Assert.Empty(plan.Nodes);
    }

    [Fact]
    public void Public_recording_api_has_no_callback_requirement_or_type_only_build()
    {
        var methods = typeof(ExecutorBuilder).GetMethods();
        Assert.DoesNotContain(methods, method => method.Name == "WithRequirement");
        Assert.DoesNotContain(methods, method => method.Name == "BuildType");
    }

    public class Authored : Target;

    public class Target
    {
        public int Calls
        {
            get; set;
        }
        public int Value
        {
            get; set;
        }
        public string? Text { get; set; } = "original";
        public void Touch() => Calls++;
        public void Set(int first, int second) => Value = first + second;
    }

    public static class Operations
    {
        public static int Read(Target target)
        {
            target.Touch();
            return 4;
        }
        public static int Double(int value) => value * 2;
        public static int Missing(Authored target) => throw new InvalidOperationException();
        public static string Combine(int first, int second) => (first + second).ToString();
    }
}
