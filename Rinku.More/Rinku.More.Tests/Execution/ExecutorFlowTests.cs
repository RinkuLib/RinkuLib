using System.Diagnostics.CodeAnalysis;
using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorFlowTests
{
    private static ExecutorBuildConfiguration Configuration => new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations));

    [Fact]
    public void A_break_before_assignment_does_not_make_the_local_available()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .DoWhile(() => false, loop => loop.If(() => target.Val.Stop, branch => branch.Break()).Then(number, 42))
            .Then(() => target.Val.Value, number);

        Assert.Equal(ExecutorBindingFailure.UnavailableInput, Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration)).Failure);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void Continue_and_fallthrough_states_both_reach_the_do_while_condition(bool stop, int expected)
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .DoWhile(() => Operations.Positive(number.Val), loop => loop.If(() => target.Val.Stop, branch => branch.Then(number, -1).Continue(), branch => branch.Then(number, -2)))
            .Then(() => target.Val.Value, () => -number.Val);
        var instance = new Target { Stop = stop };

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(expected, instance.Value);
    }

    [Fact]
    public void Continue_before_assignment_rejects_the_do_while_condition_read()
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Declare(out Variable<int> number, "number")
            .DoWhile(() => Operations.Positive(number.Val), loop => loop.Continue().Then(number, 42));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void Continue_before_assignment_rejects_the_for_increment_read()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .For(() => target.Val.Stop, increment => increment.Then(() => target.Val.Value, number), loop => loop.Continue().Then(number, 42));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void A_for_increment_does_not_require_values_from_a_breaking_path()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .For(() => target.Val.Stop, increment => increment.Then(() => target.Val.Value, number), loop => loop.Break());
        var instance = new Target { Stop = true };

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(0, instance.Value);
    }

    [Fact]
    public void A_nested_loop_consumes_its_own_break()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .DoWhile(() => false, outer => outer.DoWhile(() => false, inner => inner.Break()).Then(number, 42))
            .Then(() => target.Val.Value, number);
        var instance = new Target();

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(42, instance.Value);
    }

    [Fact]
    public void Rejected_optional_scopes_do_not_leave_loop_exit_states_behind()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .DoWhile(() => false, loop => loop.Scope(group => group.If(() => target.Val.Stop, branch => branch.Break()).Call("Missing", []), ExecutorRequirement.Optional).Then(number, 42))
            .Then(() => target.Val.Value, number);
        var instance = new Target { Stop = true };

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(42, instance.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_returns_do_not_contribute_an_unassigned_state_to_the_surviving_path(bool stop)
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .If(() => target.Val.Stop, branch => branch.If(() => target.Val.Other, inner => inner.Return(), inner => inner.Return()), branch => branch.Then(number, 42))
            .Then(() => target.Val.Value, number);
        var instance = new Target { Stop = stop };

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal(stop ? 0 : 42, instance.Value);
    }

    [Fact]
    public void Locals_used_only_in_a_returning_branch_still_have_emitted_storage()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .If(() => target.Val.Stop, branch => branch.Then(out var local, 7).Then(() => target.Val.Value, local).Return())
            .Then(() => target.Val.Value, 42);
        var executor = plan.Build<IExecutor<Target>>(Configuration);
        var instance = new Target { Stop = true };

        executor.Execute(instance);

        Assert.Equal(7, instance.Value);
    }

    [Fact]
    public void Unreachable_operations_after_return_are_not_resolved()
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Return().Call("Missing", []);

        plan.Build<IExecutor<Target>>(Configuration).Execute(new Target());
    }

    [Fact]
    public void A_while_loop_does_not_guarantee_an_iteration()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<int> number, "number")
            .While(() => target.Val.Stop, loop => loop.Then(number, 42).Break())
            .Then(() => target.Val.Value, number);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacing_a_contract_subject_invalidates_direct_and_copied_boolean_facts(bool copy)
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Declare(out Variable<string?> text, "text").Then(text, (string?)null)
            .Then(out var result, () => Operations.TryFill(ref text.Val));
        var condition = result;
        if (copy)
        {
            plan.Then(out condition, () => result.Val);
        }
        plan.Then(text, (string?)null).If(() => condition.Val, branch => branch.Then(() => Operations.Require(text.Val!)));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void Replacing_the_boolean_result_discards_its_conditional_contract()
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Declare(out Variable<string?> text, "text").Then(text, (string?)null)
            .Then(out var success, () => Operations.HasText(text.Val)).Then(success, true)
            .If(() => success.Val, branch => branch.Then(() => Operations.Require(text.Val!)));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    [Fact]
    public void Writes_in_returning_branches_do_not_destroy_facts_on_the_surviving_path()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<string?> text, "text").Then(text, (string?)null)
            .Then(out var success, () => Operations.HasText(text.Val))
            .If(() => target.Val.Stop, branch => branch.Then(text, "changed").Return())
            .If(() => success.Val, branch => branch.Then(() => Operations.Require(text.Val!)));

        plan.Build<IExecutor<Target>>(Configuration).Execute(new Target());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writable_ref_and_out_parameters_can_replace_a_known_null_without_annotations(bool output)
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Declare(out Variable<string?> text, "text").Then(text, (string?)null);
        if (output)
        {
            plan.Then(() => Operations.Output(out text.Val));
        }
        else
        {
            plan.Then(() => Operations.Fill(ref text.Val));
        }
        plan.Then(() => Operations.Require(text.Val!)).Then(() => target.Val.Text, text);
        var instance = new Target();

        plan.Build<IExecutor<Target>>(Configuration).Execute(instance);

        Assert.Equal("filled", instance.Text);
    }

    [Fact]
    public void Readonly_ref_parameters_preserve_the_known_null_state()
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Declare(out Variable<string?> text, "text").Then(text, (string?)null)
            .Then(() => Operations.ReadOnly(in text.Val)).Then(() => Operations.Require(text.Val!));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target>>(Configuration));
    }

    public class Target
    {
        public bool Stop
        {
            get; set;
        }
        public bool Other
        {
            get; set;
        }
        public int Value
        {
            get; set;
        }
        public string? Text
        {
            get; set;
        }
    }

    public static class Operations
    {
        public static bool Positive(int value) => value > 0;
        public static bool HasText([NotNullWhen(true)] string? value) => value is not null;
        public static bool TryFill([NotNullWhen(true)] ref string? value)
        {
            value = "filled";
            return true;
        }
        public static void Fill(ref string? value) => value = "filled";
        public static void Output(out string? value) => value = "filled";
        public static void ReadOnly(in string? value)
        {
        }
        public static void Require(string value) => ArgumentNullException.ThrowIfNull(value);
    }
}
