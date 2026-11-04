using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorAmbientScopeTests
{
    private static ExecutorBuildConfiguration Configuration => new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations));

    [Fact]
    public void A_parameterless_factory_can_return_a_parameterless_scope_using_enclosing_values()
    {
        var plan = ExecutorBuilder.Start<Target>(out _, "target").Then<int>(out _, 42, name: "number").Then(() => Operations.Prepare());
        var value = new Target();

        plan.Build<IExecutor<Target>>(Configuration).Execute(value);

        Assert.Equal(42, value.Value);
    }

    [Fact]
    public void Returned_scope_inputs_resolve_by_type_instead_of_factory_parameter_position()
    {
        var plan = ExecutorBuilder.Start<Target, int>(out _, out _).Then(() => Operations.PrepareTyped("fixed"));
        var value = new Target();

        plan.Build<IExecutor<Target, int>>(Configuration).Execute(value, 31);

        Assert.Equal(31, value.Value);
        Assert.Equal("fixed", value.Text);
    }

    [Fact]
    public void Ordinary_merge_and_returned_scopes_use_the_same_enclosing_variable_resolution()
    {
        var child = ExecutorBuilder.Start<int, Target>(out var number, out var target).Then(() => target.Val.Value, number);
        var plan = ExecutorBuilder.Start<Target, int>(out _, out _).Merge(child.ToScope());
        var value = new Target();

        ExecutorJson.Deserialize(ExecutorJson.Serialize(plan.Clone())).Build<IExecutor<Target, int>>(Configuration).Execute(value, 17);

        Assert.Equal(17, value.Value);
    }

    [Fact]
    public void Named_requirements_select_the_named_enclosing_value()
    {
        var child = ExecutorBuilder.Start().Reference<Target>("right", out var right).Then(() => right.Val.Value, 24);
        var named = ExecutorBuilder.Start<Target, Target>(out _, out _, "left", "right").Merge(child.ToScope());
        var first = new Target();
        var second = new Target();

        named.Build<IExecutor<Target, Target>>(Configuration).Execute(first, second);

        Assert.Equal(0, first.Value);
        Assert.Equal(24, second.Value);
    }

    [Fact]
    public void Multiple_equally_matching_enclosing_values_are_ambiguous()
    {
        var child = ExecutorBuilder.Start().Reference<Target>(out var target).Then(() => target.Val.Value, 1);
        var plan = ExecutorBuilder.Start<Target, Target>(out _, out _).Merge(child.ToScope());

        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<Target, Target>>(Configuration));
    }

    [Fact]
    public void A_better_type_match_can_follow_multiple_broader_matches()
    {
        var child = ExecutorBuilder.Start().Reference<BaseTarget>(out var target).Then(() => target.Val.Value, 9);
        var plan = ExecutorBuilder.Start<DerivedTarget, DerivedTarget, BaseTarget>(out _, out _, out _).Merge(child.ToScope());
        var exact = new BaseTarget();

        plan.Build<IExecutor<DerivedTarget, DerivedTarget, BaseTarget>>(Configuration).Execute(new(), new(), exact);

        Assert.Equal(9, exact.Value);
    }

    [Fact]
    public void Missing_type_references_can_supply_an_explicit_typed_default()
    {
        var child = ExecutorBuilder.Start().Reference<Target>(out var target).Reference<string?>(out var text, true).Then(() => target.Val.Text, text);
        var plan = ExecutorBuilder.Start<Target>(out _).Merge(child.ToScope());
        var value = new Target { Text = "before" };

        ExecutorJson.Deserialize(ExecutorJson.Serialize(plan)).Build<IExecutor<Target>>(Configuration).Execute(value);

        Assert.Null(value.Text);
    }

    public class BaseTarget
    {
        public int Value
        {
            get; set;
        }
    }

    public class DerivedTarget : BaseTarget;

    public class Target : BaseTarget
    {
        public string? Text
        {
            get; set;
        }
    }

    public static class Operations
    {
        public static ExecutorScope Prepare() => ExecutorBuilder.Start().Reference<Target>("target", out var target).Reference<int>("number", out var number).Then(() => target.Val.Value, number).ToScope();
        public static ExecutorScope PrepareTyped(string text) => ExecutorBuilder.Start<int, Target>(out var number, out var target).Then(() => target.Val.Value, number).Then(() => target.Val.Text, text).ToScope();
    }
}
