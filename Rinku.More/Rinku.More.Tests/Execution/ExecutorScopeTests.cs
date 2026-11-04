using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorScopeTests
{
    private static ExecutorBuildConfiguration Configuration => new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations));

    [Fact]
    public void Loop_variables_are_runtime_values_during_preparation()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .Then(out var number, 1)
            .While(() => number.Val < 3, body => body
                .Then(() => Operations.PrepareNumber(target.Val, number.Val))
                .Then(number, () => number.Val + 1));
        var instance = new Target();

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(0, instance.Number);
    }

    [Fact]
    public void Compilation_requires_explicit_execution_signatures()
    {
        var plan = ExecutorBuilder.Start<Authored>(out var target).Call(nameof(Target.Touch), [target]);
        Assert.Throws<ArgumentException>(() => plan.Build());
        var executor = plan.Build(new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations)).Support<IExecutor<Target>>());
        var instance = new Target();
        Assert.IsAssignableFrom<IExecutor<Target>>(executor).Execute(instance);
        Assert.True(instance.Touched);
        Assert.IsNotAssignableFrom<IExecutor<Authored>>(executor);
    }

    [Fact]
    public void A_returned_scope_keeps_a_stable_handler_and_runtime_operands()
    {
        int prepared = 0;
        int called = 0;
        var helper = new Helpers(() => prepared++);
        Action action = () => called++;
        var plan = ExecutorBuilder.Start<Target>(out var target).Then(() => helper.AddClick(target.Val, action));
        var executor = plan.Build<IExecutor<Target>>(Configuration);
        var first = new Target();
        var second = new Target();
        executor.Execute(first);
        executor.Execute(first);
        executor.Execute(second);
        first.Raise();
        second.Raise();
        Assert.Equal(1, prepared);
        Assert.Equal(2, called);
        Assert.Same(first.Handler, second.Handler);
    }

    [Fact]
    public void Nested_scopes_find_enclosing_constants_and_runtime_inputs()
    {
        var child = ExecutorBuilder.Start<Target>(out var childTarget)
            .Reference<object>("shared", out var shared)
            .Reference<int>("number", out var number)
            .Then(() => childTarget.Val.Record(shared.Val, number.Val));
        var parent = ExecutorBuilder.Start<Target, int>(out var target, out _, name2: "number").Then<object>(out _, new object(), name: "shared")
            .Merge(child.ToScope());
        var first = new Target();
        var second = new Target();
        var executor = parent.Build<IExecutor<Target, int>>(Configuration);
        executor.Execute(first, 12);
        executor.Execute(second, 43);
        Assert.NotNull(first.Value);
        Assert.Same(first.Value, second.Value);
        Assert.Equal(12, first.Number);
        Assert.Equal(43, second.Number);
    }

    [Fact]
    public void Scope_names_shadow_ancestors_and_do_not_leak_to_siblings()
    {
        var read = ExecutorBuilder.Start<Target>(out var child)
            .Reference<int>("number", out var number)
            .Then(() => child.Val.SetNumber(number.Val)).Then(() => child.Val.Numbers.Add(number.Val));
        var nested = ExecutorBuilder.Start<Target>(out var inner).Then<int>(out _, 2, name: "number").Merge(read.ToScope());
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 1, name: "number").Merge(nested.ToScope()).Merge(read.ToScope());
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal([2, 1], instance.Numbers);
    }

    [Fact]
    public void Separate_compilation_signatures_create_separate_scope_values()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.AddValue(target.Val));
        var executor = plan.Build(new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations)).Support<IExecutor<Target>>().Support<IExecutor<OtherTarget>>());
        var first = new Target();
        var second = new OtherTarget();
        ((IExecutor<Target>)executor).Execute(first);
        ((IExecutor<OtherTarget>)executor).Execute(second);
        Assert.NotNull(first.Value);
        Assert.NotNull(second.Value);
        Assert.NotSame(first.Value, second.Value);
    }

    [Fact]
    public void Rejected_optional_scopes_leave_outer_names_and_values_intact()
    {
        var read = ExecutorBuilder.Start<Target>(out var child).Reference<int>("number", out var number).Then(() => child.Val.SetNumber(number.Val));
        var rejected = ExecutorBuilder.Start<Target>(out var childTarget).Then<int>(out _, 99, name: "number")
            .Then(() => childTarget.Val.Touch())
            .Call("MissingOperation", [childTarget]);
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 7, name: "number")
            .Merge(rejected.ToScope(), requirement: ExecutorRequirement.Optional)
            .Merge(read.ToScope());
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.False(instance.Touched);
        Assert.Equal(7, instance.Number);
    }

    [Fact]
    public void Missing_names_require_an_explicit_default_and_defaults_keep_their_type()
    {
        var child = ExecutorBuilder.Start<Target>(out var target).Reference<string>("missing", out var missing, true)
            .Call(nameof(Target.SelectMissing), [target, missing]);
        var root = ExecutorBuilder.Start<Target>(out var instance).Merge(child.ToScope());
        var value = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(value);
        Assert.Equal(1, value.Number);
        var required = ExecutorBuilder.Start<Target>(out var requiredTarget).Reference<int>("missing", out var number)
            .Then(() => requiredTarget.Val.SetNumber(number.Val));
        Assert.Throws<ExecutorBindingException>(() => required.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void A_sibling_cannot_observe_another_scopes_names()
    {
        var first = ExecutorBuilder.Start<Target>(out var firstTarget).Then<object>(out _, new object(), name: "private").Then(() => firstTarget.Val.Touch());
        var second = ExecutorBuilder.Start<Target>(out var secondTarget).Reference<object>("private", out var value, true)
            .Then(() => secondTarget.Val.Record(value.Val, 0));
        var root = ExecutorBuilder.Start<Target>(out var target).Merge(first.ToScope()).Merge(second.ToScope());
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Null(instance.Value);
    }

    [Fact]
    public void Named_references_are_aliases_including_ref_writes()
    {
        var child = ExecutorBuilder.Start().Reference<int>("number", out var innerNumber).Then(() => Operations.Increment(ref innerNumber.Val));
        var root = ExecutorBuilder.Start<Target>(out var target).Then(out Variable<int> number, 10, "number")
            .Merge(child.ToScope()).Then(() => target.Val.SetNumber(number.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(11, instance.Number);
    }

    [Fact]
    public void Incompatible_outer_names_fail_instead_of_using_a_default()
    {
        var child = ExecutorBuilder.Start().Reference<int>("value", out var value, true).Then(() => Operations.Accept(value.Val));
        var root = ExecutorBuilder.Start<Target>(out _).Then<string>(out _, "text", name: "value").Merge(child.ToScope());
        Assert.Throws<ExecutorBindingException>(() => root.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void Preparation_requires_a_fixed_receiver_and_fixed_inputs_are_available()
    {
        var runtime = ExecutorBuilder.Start<Helpers>(out var helper).Then(() => helper.Val.Empty());
        Assert.Throws<InvalidOperationException>(() => runtime.Build<IExecutor<Helpers>>(Configuration));
        int prepared = 0;
        Action action = Operations.Accept;
        var root = ExecutorBuilder.Start<Target>(out var target).Then<Helpers>(out var fixedHelper, new Helpers(() => prepared++))
            .Then(() => fixedHelper.Val.AddClick(target.Val, action));
        root.Build<IExecutor<Target>>(Configuration).Execute(new());
        Assert.Equal(1, prepared);
    }

    [Fact]
    public void Scope_results_specialize_downstream_calls()
    {
        var root = ExecutorBuilder.Start<Target>(out var target).Then(out var result, () => Operations.MakeResult(target.Val))
            .Call(nameof(Result.Record), [target, result]);
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.True(instance.Touched);
    }

    [Fact]
    public void Manual_composition_can_expose_a_result_and_accept_fixed_values()
    {
        var child = ExecutorBuilder.Start<int>(out var number).Then(out var result, () => Operations.Double(number.Val)).Result(result);
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 21).Merge<int>(out var output, child.ToScope()).Then(() => target.Val.SetNumber(output.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(42, instance.Number);
    }

    [Fact]
    public void Only_selected_factories_execute_and_defaults_do_not_run_struct_constructors()
    {
        Operations.SpecificCalls = 0;
        var root = ExecutorBuilder.Start<Target, ConstructedDefault>(out var target, out var value).Then(() => Operations.PrepareDefault(target.Val, value.Val));
        var instance = new Target();
        root.Build<IExecutor<Target, ConstructedDefault>>(Configuration).Execute(instance, new());
        Assert.Equal(1, Operations.SpecificCalls);
        Assert.Equal(0, instance.Number);
    }

    [Fact]
    public void Factory_errors_are_unwrapped_and_recursive_scopes_are_rejected()
    {
        var failing = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.Fail(target.Val));
        Assert.Throws<NotSupportedException>(() => failing.Build<IExecutor<Target>>(Configuration));
        var recursive = ExecutorBuilder.Start<Target>(out var other).Then(() => Operations.Recurse(other.Val));
        Assert.Throws<InvalidOperationException>(() => recursive.Build<IExecutor<Target>>(Configuration));
        var cycle = ExecutorBuilder.Start<Target>(out var input);
        cycle.Merge(cycle.ToScope());
        Assert.Throws<InvalidOperationException>(() => cycle.Build<IExecutor<Target>>(Configuration));
        Assert.Throws<NotSupportedException>(() => ExecutorJson.Serialize(cycle));
    }

    [Fact]
    public void Out_operands_are_runtime_aliases_and_preparation_does_not_assign_them()
    {
        var root = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number)
            .Call(nameof(Operations.PrepareOut), [target, number], [ExecutorArgumentPassing.Value, ExecutorArgumentPassing.Out])
            .Then(() => target.Val.SetNumber(number.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations))).Execute(instance);
        Assert.Equal(73, instance.Number);
    }

    [Fact]
    public void Clone_and_json_preserve_scope_structure_names_and_results()
    {
        var child = ExecutorBuilder.Start<Target>(out var input).Reference<int>("number", out var number).Then(() => input.Val.SetNumber(number.Val));
        var source = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 42, name: "number").Merge(child.ToScope());
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(source.Clone()));
        var instance = new Target();
        restored.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(42, instance.Number);
        Assert.Equal(4, source.Nodes.Count());
    }

    [Fact]
    public void Preparation_can_nest_scopes_and_read_its_own_fixed_values_through_parent_lookup()
    {
        var root = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.AddValue(target.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.NotNull(instance.Value);
        Assert.Equal(37, instance.Number);
    }

    [Fact]
    public void Building_does_not_expand_or_mutate_the_authored_plan()
    {
        var source = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.AddValue(target.Val));
        var json = ExecutorJson.Serialize(source);
        Parallel.For(0, 4, _ => source.Build<IExecutor<Target>>(Configuration).Execute(new()));
        Assert.Equal(json, ExecutorJson.Serialize(source));
    }

    [Fact]
    public void Private_fixed_values_can_be_held_through_a_public_contract()
    {
        var source = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.PreparePrivate(target.Val));
        var instance = new Target();
        source.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(19, instance.Number);
    }

    [Fact]
    public void Prepared_execution_allocates_nothing()
    {
        var source = ExecutorBuilder.Start<Target>(out var target).Then(() => Operations.PrepareNumber(target.Val, 42));
        var executor = source.Build<IExecutor<Target>>(Configuration);
        var instance = new Target();
        for (int index = 0; index < 1000; index++)
        {
            executor.Execute(instance);
        }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10000; index++)
        {
            executor.Execute(instance);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.Equal(42, instance.Number);
    }

    [Fact]
    public void Inline_scopes_resolve_the_same_names_as_composed_scopes()
    {
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 17, name: "number")
            .Scope(scope => scope.Reference<int>("number", out var number).Then(() => target.Val.SetNumber(number.Val)));
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(root));
        var instance = new Target();
        restored.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(17, instance.Number);
    }

    [Fact]
    public void Inline_scope_names_can_shadow_without_changing_the_outer_binding()
    {
        var child = ExecutorBuilder.Start<Target>(out var input).Reference<int>("number", out var value).Then(() => input.Val.Numbers.Add(value.Val));
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 1, name: "number")
            .Scope(scope => scope.Then<int>(out _, 2, name: "number").Merge(child.ToScope())).Merge(child.ToScope());
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal([2, 1], instance.Numbers);
    }

    [Fact]
    public void A_name_reference_created_in_a_loop_condition_remains_an_alias()
    {
        var root = ExecutorBuilder.Start<Target>(out var target).Then<bool>(out var running, true, "running")
            .While(condition =>
            {
                condition.Reference<bool>("running", out var reference);
                return reference;
            }, body => body.Then(() => target.Val.Touch()).Then(running, false));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.True(instance.Touched);
    }

    [Fact]
    public void A_missing_default_cannot_supply_a_ref_address()
    {
        var child = ExecutorBuilder.Start().Reference<int>("missing", out var value, true).Then(() => Operations.Increment(ref value.Val));
        var root = ExecutorBuilder.Start<Target>(out _).Merge(child.ToScope());
        Assert.Throws<ExecutorBindingException>(() => root.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void Nested_builders_remain_editable_and_clones_copy_the_composed_graph()
    {
        var child = ExecutorBuilder.Start<Target>(out var input).Then(() => input.Val.Touch());
        var root = ExecutorBuilder.Start<Target>(out var target).Merge(child.ToScope());
        var clone = root.Clone();
        clone.Remove(clone.Nodes.Single(node => node.Name == nameof(Target.Touch)));
        var original = new Target();
        var changed = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(original);
        clone.Build<IExecutor<Target>>(Configuration).Execute(changed);
        Assert.True(original.Touched);
        Assert.False(changed.Touched);
        child.Then(() => input.Val.SetNumber(23));
        root.Build<IExecutor<Target>>(Configuration).Execute(original);
        Assert.Equal(23, original.Number);
    }

    [Fact]
    public void A_composed_input_can_alias_and_assign_an_enclosing_local()
    {
        var child = ExecutorBuilder.Start<int>(out var input).Then(input, 51).Result(input);
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out var number, 0)
            .Merge<int>(out var result, child.ToScope()).Then(() => target.Val.SetNumber(result.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(51, instance.Number);
    }

    [Fact]
    public void A_scope_result_can_be_a_missing_typed_default()
    {
        var child = ExecutorBuilder.Start().Reference<string>("missing", out var missing, true).Result(missing);
        var root = ExecutorBuilder.Start<Target>(out var target).Merge<string>(out var result, child.ToScope()).Call(nameof(Target.SelectMissing), [target, result]);
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.Equal(1, instance.Number);
    }

    [Fact]
    public void Name_references_follow_the_enclosing_specialization()
    {
        var child = ExecutorBuilder.Start().Reference<Authored>("target", out var reference).Call(nameof(Target.Touch), [reference]);
        var root = ExecutorBuilder.Start<Authored>(out _, "target").Merge(child.ToScope());
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.True(instance.Touched);
    }

    [Fact]
    public void Json_preserves_composition_results_and_scope_constants()
    {
        var resultScope = ExecutorBuilder.Start<int>(out var input).Then(out var output, () => Operations.Double(input.Val)).Result(output);
        var callback = ExecutorBuilder.Start<Target>(out var callbackTarget).Then(() => callbackTarget.Val.Touch()).ToScope();
        var root = ExecutorBuilder.Start<Target>(out var target).Then<int>(out _, 12).Merge<int>(out var result, resultScope.ToScope())
            .Then(() => target.Val.SetNumber(result.Val)).Then(() => Operations.ApplyBody(target.Val, callback));
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(root));
        var instance = new Target();
        restored.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.True(instance.Touched);
        Assert.Equal(24, instance.Number);
    }

    [Fact]
    public void Returned_scopes_can_use_enclosing_names_without_declaring_inputs()
    {
        var root = ExecutorBuilder.Start<Target>(out var target, "target").Then(() => Operations.ConfigureByName(target.Val));
        var instance = new Target();
        root.Build<IExecutor<Target>>(Configuration).Execute(instance);
        Assert.True(instance.Touched);
    }

    [Fact]
    public void A_named_scope_result_can_be_shared_with_later_sibling_scopes()
    {
        var read = ExecutorBuilder.Start<Target>(out var input).Reference<object>("shared", out var value).Then(() => input.Val.Record(value.Val, 0));
        var root = ExecutorBuilder.Start<Target>(out var target).Then(out _, () => Operations.CreateShared(), "shared").Merge(read.ToScope());
        var first = new Target();
        var second = new Target();
        var executor = root.Build<IExecutor<Target>>(Configuration);
        executor.Execute(first);
        executor.Execute(second);
        Assert.NotNull(first.Value);
        Assert.Same(first.Value, second.Value);
    }

    public class Authored;
    public class OtherTarget : Target;
    public class Target
    {
        private event EventHandler? Click;
        public EventHandler? Handler
        {
            get; set;
        }
        public object? Value
        {
            get; set;
        }
        public int Number
        {
            get; set;
        }
        public bool Touched
        {
            get; set;
        }
        public List<int> Numbers { get; } = [];
        public void Touch() => Touched = true;
        public void Raise() => Click?.Invoke(this, EventArgs.Empty);
        public void ApplyClick(EventHandler handler)
        {
            Click -= handler;
            Click += handler;
            Handler = handler;
        }
        public void Record(object? value, int number) => (Value, Number) = (value, number);
        public void SetNumber(int value) => Number = value;
        public void SetStoredValue(IStoredValue value) => Number = value.Number;
        public void SelectMissing(string? value) => Number = 1;
        public void SelectMissing(int? value) => Number = 2;
        public Result CreateResult() => new();
    }
    public class Result
    {
        public static void Record(Target target, Result result) => target.Touch();
    }
    public interface IStoredValue
    {
        int Number
        {
            get;
        }
    }
    private class PrivateValue : IStoredValue
    {
        public int Number => 19;
    }
    public readonly struct ConstructedDefault
    {
        public int Value
        {
            get;
        }
        public ConstructedDefault() => Value = 99;
    }
    public class Helpers(Action prepared)
    {
        public ExecutorScope AddClick(Target target, Action action)
        {
            Assert.Null(target);
            prepared();
            EventHandler handler = (_, _) => action();
            return ExecutorBuilder.Start().Reference<Target>(out var input).Then(() => input.Val.ApplyClick(handler)).ToScope();
        }
        public ExecutorScope Empty() => ExecutorBuilder.Start().ToScope();
    }
    public static class Operations
    {
        public static int SpecificCalls;
        public static ExecutorScope AddValue(Target target)
        {
            var child = ExecutorBuilder.Start<Target>(out var inner).Reference<object>("shared", out var shared)
                .Reference<int>("number", out var number).Then(() => inner.Val.Record(shared.Val, number.Val));
            return ExecutorBuilder.Start().Reference<Target>(out var input).Then<object>(out _, new object(), name: "shared").Then<int>(out _, 37, name: "number").Merge(child.ToScope()).ToScope();
        }
        public static ExecutorScope MakeResult(Target target)
        {
            var scope = ExecutorBuilder.Start().Reference<Target>(out var input).Then(out var result, () => input.Val.CreateResult());
            return scope.Result(result).ToScope();
        }
        public static ExecutorScope PrepareNumber(Target target, int number) => ExecutorBuilder.Start().Reference<Target>(out var input).Then(() => input.Val.SetNumber(number)).ToScope();
        public static ExecutorScope PrepareDefault(Target target, ConstructedDefault value)
        {
            SpecificCalls++;
            return ExecutorBuilder.Start().Reference<Target>(out var input).Then(() => input.Val.SetNumber(value.Value)).ToScope();
        }
        public static ExecutorScope PrepareDefault(object target, ConstructedDefault value) => throw new NotSupportedException();
        public static ExecutorScope Fail(Target target) => throw new NotSupportedException();
        public static ExecutorScope Recurse(Target target) => ExecutorBuilder.Start().Reference<Target>(out var input).Then(() => Recurse(input.Val)).ToScope();
        public static ExecutorScope PreparePrivate(Target target) => ExecutorBuilder.Start().Reference<Target>(out var input).Then<IStoredValue>(out var value, new PrivateValue()).Then(() => input.Val.SetStoredValue(value.Val)).ToScope();
        public static ExecutorScope PrepareOut(Target target, out int value)
        {
            value = -1;
            return ExecutorBuilder.Start().Reference<Target>(out var input).Reference<int>(out var output).Then(() => WriteOut(input.Val, out output.Val)).ToScope();
        }
        public static void WriteOut(Target target, out int value) => value = 73;
        public static void Increment(ref int value) => value++;
        public static int Double(int value) => value * 2;
        public static ExecutorScope ApplyBody(Target target, ExecutorScope body) => ExecutorBuilder.Start().Reference<Target>(out var input).Merge(body).ToScope();
        public static ExecutorScope ConfigureByName(Target target) => ExecutorBuilder.Start().Reference<Target>("target", out var input).Then(() => input.Val.Touch()).ToScope();
        public static ExecutorScope CreateShared() => ExecutorBuilder.Start().Then<object>(out var value, new object()).Result(value).ToScope();
        public static void Accept()
        {
        }
        public static void Accept(int value)
        {
        }
    }
}
