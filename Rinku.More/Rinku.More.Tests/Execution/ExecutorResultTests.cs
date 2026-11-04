using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorResultTests
{
    [Fact]
    public void One_recording_can_implement_void_and_value_returning_signatures()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Then(out var result, () => input.Val + 1).Result(result);
        var configuration = new ExecutorBuildConfiguration().Support<IExecutor<int>>().Support<ILongResult>();

        var executor = plan.Build<IIntResult>(configuration);

        Assert.Equal(8, executor.Execute(7));
        Assert.Equal(8L, ((ILongResult)executor).Execute(7));
        ((IExecutor<int>)executor).Execute(7);
    }

    [Fact]
    public void A_returned_scope_resolves_its_result_without_a_holder()
    {
        var child = ExecutorBuilder.Start().Reference<int>("input", out var value).Result(value);
        var plan = ExecutorBuilder.Start<int>(out _, "input").Merge(out Variable<int> result, child.ToScope()).Result(result);

        Assert.Equal(13, plan.Build<IIntResult>().Execute(13));
    }

    [Fact]
    public void Known_null_is_accepted_only_by_nullable_result_contracts()
    {
        var plan = ExecutorBuilder.Start().Call<string>(out var value, "MissingText", [], requirement: ExecutorRequirement.Nullable).Result(value);

        Assert.Null(plan.Build<INullableText>().Execute());
        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IRequiredText>());

        Assert.Equal(ExecutorBindingFailure.NullNotAccepted, error.Failure);
    }

    [Fact]
    public void A_value_result_is_converted_using_the_normal_implicit_conversion_rules()
    {
        var plan = ExecutorBuilder.Start<int>(out var value).Result(value);

        Assert.Equal(42L, plan.Build<ILongResult>().Execute(42));
        Assert.Throws<ExecutorBindingException>(() => plan.Build<ITextFromInt>());
    }

    [Fact]
    public void A_missing_result_is_rejected_during_compilation()
    {
        var plan = ExecutorBuilder.Start<int>(out _);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IIntResult>());
    }

    [Fact]
    public void An_early_return_requires_the_result_to_be_available_at_that_point()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<int> result);
        plan.If(() => input.Val == 0, branch => branch.Return()).Then(result, 2).Result(result);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IIntResult>());
    }

    [Fact]
    public void An_initialized_result_can_be_returned_early_or_changed_later()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<int> result).Then(result, 1);
        plan.If(() => input.Val == 0, branch => branch.Return()).Then(result, 2).Result(result);

        var executor = plan.Build<IIntResult>();

        Assert.Equal(1, executor.Execute(0));
        Assert.Equal(2, executor.Execute(1));
    }

    [Fact]
    public void Shared_signatures_validate_early_returns_against_every_nullability_contract()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<string?> result).Then(result, (string?)null);
        plan.If(() => input.Val == 0, branch => branch.Return()).Then(result, "Present").Result(result);
        var config = new ExecutorBuildConfiguration().Support<IConditionalRequiredText>();

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IConditionalNullableText>(config));
    }

    public interface IConditionalNullableText : IExecutor
    {
        string? Execute(int value);
    }
    public interface IConditionalRequiredText : IExecutor
    {
        string Execute(int value);
    }

    [Fact]
    public void A_known_null_nullable_struct_still_has_an_addressable_value()
    {
        var plan = ExecutorBuilder.Start<int>(out _).Call<int>(out var missing, "Missing", [], requirement: ExecutorRequirement.Nullable);
        plan.Then(out var result, () => missing.Val.GetHashCode()).Result(result);

        Assert.Equal(0, plan.Build<IIntResult>().Execute(1));
    }

    [Fact]
    public void Coalescing_evaluates_the_fallback_only_when_needed()
    {
        var probe = new Probe();
        var plan = ExecutorBuilder.Start<string?>(out var input).Then(out var result, () => input.Val ?? probe.Fallback()).Result(result);
        var executor = plan.Build<ITextResult>();

        Assert.Equal("supplied", executor.Execute("supplied"));
        Assert.Equal(0, probe.Calls);
        Assert.Equal("fallback", executor.Execute(null));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void Coalescing_a_nullable_value_uses_the_existing_conditional_path()
    {
        var plan = ExecutorBuilder.Start<int?>(out var input).Then(out var result, () => input.Val ?? 17).Result(result);
        var executor = plan.Build<INullableNumber>();

        Assert.Equal(17, executor.Execute(null));
        Assert.Equal(5, executor.Execute(5));
    }

    public class Probe
    {
        public int Calls
        {
            get; private set;
        }
        public string Fallback()
        {
            Calls++;
            return "fallback";
        }
    }

    public interface ITextResult : IExecutor
    {
        string Execute(string? input);
    }

    public interface INullableNumber : IExecutor
    {
        int Execute(int? input);
    }

    public interface IIntResult : IExecutor
    {
        int Execute(int value);
    }

    public interface ILongResult : IExecutor
    {
        long Execute(int value);
    }

    public interface ITextFromInt : IExecutor
    {
        string Execute(int value);
    }

    public interface INullableText : IExecutor
    {
        string? Execute();
    }

    public interface IRequiredText : IExecutor
    {
        string Execute();
    }
}
