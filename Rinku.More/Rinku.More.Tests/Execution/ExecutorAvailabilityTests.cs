using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorAvailabilityTests
{
    private static ExecutorBuildConfiguration Configuration => new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations));

    private static MethodInfo Method(string name) => typeof(ILogical).GetMethod(name)!;

    [Fact]
    public void A_required_operation_reports_a_missing_implementation_during_build()
    {
        var plan = ExecutorBuilder.Start<Trace>(out _).Then<string>(out _, Method(nameof(ILogical.Text)), []);

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.MissingImplementation, error.Failure);
    }

    [Fact]
    public void Optional_dependencies_each_apply_their_own_requirement()
    {
        Variable<string> missing = null!;
        Variable<int> length = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out missing, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Optional)
            .Then(out length, () => missing.Val.Length, requirement: ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, length.Val), requirement: ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, 42));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal([42], target.Numbers);
    }

    [Fact]
    public void A_required_consumer_of_an_optional_missing_result_fails()
    {
        Variable<string> missing = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out missing, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, missing.Val));

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.UnavailableInput, error.Failure);
    }

    [Fact]
    public void A_dependent_nullable_operation_materializes_null_after_an_unavailable_input()
    {
        Variable<string> missing = null!;
        Variable<string> fallback = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out missing, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Optional)
            .Then(out fallback, () => Operations.CopyTextValue(missing.Val), "fallback", requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.Record(trace.Val, fallback.Val));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal([null], target.Texts);
    }

    [Fact]
    public void Nullable_fallback_lifts_a_struct_and_accepts_a_nullable_consumer()
    {
        Variable<int> number = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out number, Method(nameof(ILogical.Number)), [], requirement: ExecutorRequirement.Nullable)
            .Then(typeof(Operations).GetMethod(nameof(Operations.RecordNullable))!, [trace, number]);
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal([null], target.NullableNumbers);
    }

    [Fact]
    public void Known_null_does_not_bind_to_an_unannotated_reference_parameter()
    {
        Variable<string> missing = null!;
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Then(out missing, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.RequireText(missing.Val));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));
    }

    [Fact]
    public void An_optional_scope_emits_nothing_when_a_later_required_operation_fails()
    {
        Variable<string> missing = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then(() => Operations.Record(trace.Val, 1)).Then(out missing, Method(nameof(ILogical.Text)), []), ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, 2));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal([2], target.Numbers);
    }

    [Fact]
    public void Nullable_scope_rolls_back_earlier_values_and_materializes_its_outputs_as_null()
    {
        Variable<string> first = null!;
        Variable<string> missing = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then(out first, () => Operations.CopyTextValue("value")).Then(out missing, Method(nameof(ILogical.Text)), [], "missing"), ExecutorRequirement.Nullable)
            .Then(() => Operations.Record(trace.Val, first.Val))
            .Then(() => Operations.Record(trace.Val, missing.Val));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal([null, null], target.Texts);
    }

    [Fact]
    public void A_successful_virtual_scope_can_produce_a_value_used_after_the_scope()
    {
        Variable<string> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then(out text, () => Operations.CopyTextValue("value")))
            .Then(() => Operations.Record(trace.Val, text.Val));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal(["value"], target.Texts);
    }

    [Fact]
    public void NotNull_on_ref_restores_availability_after_nullable_fallback()
    {
        Variable<string?> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.EnsureText(ref text.Val))
            .Then(() => Operations.RequireText(text.Val!))
            .Then(() => Operations.Record(trace.Val, text.Val));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal(["restored"], target.Texts);
    }

    [Fact]
    public void NotNullWhen_applies_only_to_the_matching_branch()
    {
        Variable<string?> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(out var success, () => Operations.TryEnsureText(ref text.Val))
            .If(() => success.Val, branch => branch.Then(() => Operations.RequireText(text.Val!)).Then(() => Operations.Record(trace.Val, text.Val)));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal(["restored"], target.Texts);
    }

    [Fact]
    public void Ignoring_an_optional_ref_call_preserves_the_previous_value()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out var text, () => Operations.CopyTextValue("original"))
            .Then(Method(nameof(ILogical.Edit)), [text], requirement: ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, text.Val));
        var target = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(target);

        Assert.Equal(["original"], target.Texts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Requirements_and_scopes_survive_cloning_merging_and_json(int representation)
    {
        Variable<string> text = null!;
        var source = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then(() => Operations.Record(trace.Val, 1)).Then(out text, Method(nameof(ILogical.Text)), []), ExecutorRequirement.Nullable)
            .Then(() => Operations.Record(trace.Val, text.Val));
        var plan = representation switch
        {
            0 => source.Clone(),
            1 => ExecutorJson.Deserialize(ExecutorJson.Serialize(source)),
            _ => ExecutorBuilder.Start<Trace>(out var target).Merge(source.ToScope())
        };
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Empty(result.Numbers);
        Assert.Equal([null], result.Texts);
        Assert.Equal(ExecutorRequirement.Nullable, source.Nodes.First().Requirement);
    }

    [Fact]
    public void Requirements_can_be_edited_without_mutating_the_source_plan()
    {
        var source = ExecutorBuilder.Start<Trace>(out _).Then<string>(out _, Method(nameof(ILogical.Text)), []);
        var clone = source.Clone();
        clone.Nodes.Single().Requirement = ExecutorRequirement.Optional;

        clone.Build<IExecutor<Trace>>(Configuration).Execute(new Trace());

        Assert.Throws<ExecutorBindingException>(() => source.Build<IExecutor<Trace>>(Configuration));
    }

    [Fact]
    public void Null_instance_access_fails_during_binding()
    {
        Variable<string> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then<int>(out _, () => text.Val.Length);

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.NullNotAccepted, error.Failure);
    }

    [Fact]
    public void Null_instance_access_can_be_omitted_by_its_own_requirement()
    {
        Variable<string> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then<int>(out _, () => text.Val.Length, requirement: ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, 3));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([3], result.Numbers);
    }

    [Fact]
    public void AllowNull_accepts_a_known_null_on_a_nonnullable_reference_parameter()
    {
        Variable<string> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.RecordAllowed(trace.Val, text.Val));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([null], result.Texts);
    }

    [Fact]
    public void DisallowNull_rejects_a_known_null_on_a_nullable_reference_parameter()
    {
        Variable<string> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.RequireAnnotatedText(text.Val));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));
    }

    [Fact]
    public void NotNullWhen_does_not_apply_to_the_other_branch()
    {
        Variable<string?> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(out var success, () => Operations.HasText(text.Val))
            .If(() => !success.Val, branch => branch.Then(() => Operations.RequireText(text.Val!)));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));
    }

    [Fact]
    public void Conditional_postcondition_is_available_after_the_other_branch_returns()
    {
        Variable<string?> text = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out text, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Nullable)
            .Then(out var success, () => Operations.TryEnsureText(ref text.Val))
            .If(() => !success.Val, branch => branch.Return())
            .Then(() => Operations.RequireText(text.Val!))
            .Then(() => Operations.Record(trace.Val, text.Val));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal(["restored"], result.Texts);
    }

    [Fact]
    public void A_short_circuited_out_assignment_is_not_definitely_available()
    {
        var plan = ExecutorBuilder.Start<Trace>(out _);
        plan.Declare(out Variable<string> text, "text");
        plan.Then<bool>(out var left, false);
        plan.Then(out var combined, () => left.Val && Operations.SetText(out text.Val));
        plan.Then(() => Operations.RequireText(text.Val));

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.UnavailableInput, error.Failure);
    }

    [Fact]
    public void Nullable_fallback_initializes_out_parameters_without_reading_them()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace);
        plan.Declare(out Variable<string> text, "text");
        plan.Then(Method(nameof(ILogical.Output)), [text], requirement: ExecutorRequirement.Nullable)
            .Then(() => Operations.Record(trace.Val, text.Val));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([null], result.Texts);
    }

    [Fact]
    public void An_omitted_scope_preserves_existing_local_values()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace);
        plan.Declare(out Variable<int> number, "number").Then(number, 5);
        plan.Scope(group => group.Then(number, 7).Then<string>(out _, Method(nameof(ILogical.Text)), []), ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, number.Val));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([5], result.Numbers);
    }

    [Fact]
    public void An_optional_child_keeps_its_requirement_inside_an_optional_scope()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then(() => Operations.Record(trace.Val, 1))
                .Then<string>(out _, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Optional), ExecutorRequirement.Optional)
            .Then(() => Operations.Record(trace.Val, 2));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([1, 2], result.Numbers);
    }

    [Fact]
    public void An_optional_child_keeps_its_requirement_inside_required_nested_scopes()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(outer => outer.Scope(inner => inner.Then<string>(out _, Method(nameof(ILogical.Text)), [], requirement: ExecutorRequirement.Optional))
                .Then(() => Operations.Record(trace.Val, 1)))
            .Then(() => Operations.Record(trace.Val, 2));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(Configuration).Execute(result);

        Assert.Equal([1, 2], result.Numbers);
    }

    [Fact]
    public void A_missing_required_check_inside_nested_required_scopes_fails_the_build()
    {
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Scope(outer => outer.Scope(inner => inner.Then<bool>(out _, Method(nameof(ILogical.Condition)), [])));

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.MissingImplementation, error.Failure);
    }

    [Fact]
    public async Task Building_one_plan_concurrently_keeps_state_local_to_each_build()
    {
        Variable<int> number = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out number, Method(nameof(ILogical.Number)), [], requirement: ExecutorRequirement.Nullable)
            .Then(typeof(Operations).GetMethod(nameof(Operations.RecordNullable))!, [trace, number]);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var result = new Trace();
            plan.Build<IExecutor<Trace>>(Configuration).Execute(result);
            return result;
        })));

        Assert.All(results, result => Assert.Equal([null], result.NullableNumbers));
    }

    [Fact]
    public void Known_null_cannot_be_unwrapped_by_an_explicit_value_conversion()
    {
        Variable<int> number = null!;
        var plan = ExecutorBuilder.Start<Trace>(out _)
            .Then(out number, Method(nameof(ILogical.Number)), [], requirement: ExecutorRequirement.Nullable).Then(out _, () => (long)(number).Val);

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.NullNotAccepted, error.Failure);
    }

    [Fact]
    public void Nullable_boolean_fallback_cannot_be_used_as_a_required_condition()
    {
        Variable<bool> condition = null!;
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(out condition, Method(nameof(ILogical.Condition)), [], requirement: ExecutorRequirement.Nullable)
            .If(() => condition.Val, branch => branch.Then(() => Operations.Record(trace.Val, 1)));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));
    }

    [Fact]
    public void An_optional_operation_does_not_hide_ambiguous_provider_candidates()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(AmbiguousOperations));
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Then(Method(nameof(ILogical.Ambiguous)), [trace, trace], requirement: ExecutorRequirement.Optional);

        var error = Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<Trace>>(configuration));

        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_out_producer_binds_its_new_variable_to_the_selected_output_type()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(Operations)).RegisterProvider(typeof(ConcreteOutput));
        var plan = ExecutorBuilder.Start<Trace>(out var trace);
        plan.Declare(out Variable<IValue> value, "value");
        plan.Then(typeof(IOutput).GetMethod(nameof(IOutput.Create))!, [value])
            .Then(() => Operations.Record(trace.Val, value.Val.Number));
        var result = new Trace();

        plan.Build<IExecutor<Trace>>(configuration).Execute(result);

        Assert.Equal([17], result.Numbers);
    }

    public interface IValue
    {
        int Number
        {
            get;
        }
    }

    public class ConcreteValue : IValue
    {
        public int Number => 17;
    }

    public interface IOutput
    {
        static abstract void Create(out IValue value);
    }

    public static class ConcreteOutput
    {
        public static void Create(out ConcreteValue value) => value = new();
    }

    [Fact]
    public void A_rejected_scope_does_not_contribute_methods_from_its_discarded_result_types()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var trace)
            .Scope(group => group.Then<ScopedProvider>(out _, () => new ScopedProvider())
                .Then<string>(out _, Method(nameof(ILogical.Text)), []), ExecutorRequirement.Optional)
            .Then(Method(nameof(ILogical.OnlyFromScope)), [trace]);

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Trace>>(Configuration));

        Assert.Equal(ExecutorBindingFailure.MissingImplementation, error.Failure);
    }

    public class ScopedProvider
    {
        public static void OnlyFromScope(Trace target) => target.Numbers.Add(1);
    }

    public static class AmbiguousOperations
    {
        public static void Ambiguous(Trace first, object second)
        {
        }
        public static void Ambiguous(object first, Trace second)
        {
        }
    }

    public class Trace
    {
        public List<int> Numbers { get; } = [];
        public List<int?> NullableNumbers { get; } = [];
        public List<string?> Texts { get; } = [];
    }

    public interface ILogical
    {
        static abstract string Text();
        static abstract int Number();
        static abstract bool Condition();
        static abstract void OnlyFromScope(Trace target);
        static abstract void Ambiguous(Trace first, Trace second);
        static abstract void Edit(ref string value);
        static abstract void Output(out string value);
    }

    public static class Operations
    {
        public static void RecordAllowed(Trace target, [AllowNull] string value) => target.Texts.Add(value);
        public static void RequireAnnotatedText([DisallowNull] string? value) => ArgumentNullException.ThrowIfNull(value);
        public static bool SetText(out string value)
        {
            value = "set";
            return true;
        }
        public static string CopyTextValue(string value) => value;
        public static void Record(Trace target, int value) => target.Numbers.Add(value);
        public static void Record(Trace target, string? value) => target.Texts.Add(value);
        public static void RecordNullable(Trace target, int? value) => target.NullableNumbers.Add(value);
        public static void RequireText(string value) => ArgumentNullException.ThrowIfNull(value);
        public static void EnsureText([NotNull] ref string? value) => value ??= "restored";
        public static bool HasText([NotNullWhen(true)] string? value) => value is not null;
        public static bool TryEnsureText([NotNullWhen(true)] ref string? value)
        {
            value ??= "restored";
            return true;
        }
    }
}
