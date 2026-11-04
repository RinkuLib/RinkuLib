using System.Globalization;
using Rinku.More.Globalization;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorConversionTests
{
    [Fact]
    public void Symbolic_calls_and_assignments_use_implicit_conversion_at_execution_time()
    {
        var plan = ExecutorBuilder.Start<Display>(out var display);
        var word = new Word();
        word.Add("en", "Name");
        word.Add("fr", "Nom");
        plan.Call(nameof(Display.Show), [display, word]);
        plan.Then(() => display.Val.Caption, word);
        var executor = plan.Build<IExecutor<Display>>();
        var target = new Display();
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
            executor.Execute(target);
            Assert.Equal("Name", target.Caption);
            Assert.Equal("Name", target.Shown);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr");
            executor.Execute(target);
            Assert.Equal("Nom", target.Caption);
            Assert.Equal("Nom", target.Shown);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Member_assignment_accepts_a_null_literal()
    {
        var plan = ExecutorBuilder.Start<Display>(out var display).Then(() => display.Val.Caption, null);
        var target = new NullableDisplay();
        plan.Build<IExecutor<NullableDisplay>>().Execute(target);
        Assert.Null(target.Caption);
        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Display>>());
    }

    [Fact]
    public void Member_assignment_resolves_against_the_compiled_target_type()
    {
        var plan = ExecutorBuilder.Start<Display>(out var display).Then(() => display.Val.Caption, 42);
        var target = new NumericDisplay();
        plan.Build<IExecutor<NumericDisplay>>().Execute(target);
        Assert.Equal(42, target.Caption);
        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Display>>());
    }

    [Fact]
    public void Member_assignment_accepts_a_differently_typed_plan_variable()
    {
        var plan = ExecutorBuilder.Start<Display, Number>(out var display, out var number).Then(() => display.Val.Caption, number);
        var target = new NumericDisplay();
        plan.Build<IExecutor<NumericDisplay, Number>>().Execute(target, new(42));
        Assert.Equal(42, target.Caption);
    }

    [Fact]
    public void Member_assignment_records_a_differently_typed_expression_without_executing_it()
    {
        var source = new NumberSource();
        var plan = ExecutorBuilder.Start<Display>(out var display).Then(() => display.Val.Caption, () => source.Read());
        Assert.Equal(0, source.Reads);
        var executor = plan.Build<IExecutor<NumericDisplay>>();
        Assert.Equal(0, source.Reads);
        var target = new NumericDisplay();
        executor.Execute(target);
        Assert.Equal(1, source.Reads);
        Assert.Equal(42, target.Caption);
    }

    public class NumericDisplay
    {
        public int Caption
        {
            get; set;
        }
    }
    public class NullableDisplay
    {
        public string? Caption
        {
            get; set;
        }
    }
    public class NumberSource
    {
        public int Reads
        {
            get; private set;
        }
        public Number Read()
        {
            Reads++;
            return new(42);
        }
    }

    [Fact]
    public void Expression_conversions_are_preserved()
    {
        var plan = ExecutorBuilder.Start<Display, Number>(out var display, out var number).Then(() => display.Val.ShowNumber(number.Val));
        var target = new Display();
        plan.Build<IExecutor<Display, Number>>().Execute(target, new(42));
        Assert.Equal(42L, target.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    public void Nullable_struct_conversions_are_lifted(int? input)
    {
        var plan = ExecutorBuilder.Start<Display, Number?>(out var display, out var number).Call(nameof(Display.ShowNullable), [display, number]);
        var target = new Display();
        plan.Build<IExecutor<Display, Number?>>().Execute(target, input.HasValue ? new Number(input.Value) : null);
        Assert.Equal(input, target.Nullable);
    }

    [Fact]
    public void Standard_conversions_may_surround_one_operator()
    {
        var plan = ExecutorBuilder.Start<Display, short>(out var display, out var number).Call(nameof(Display.ShowWrapped), [display, number]);
        var target = new Display();
        plan.Build<IExecutor<Display, short>>().Execute(target, 42);
        Assert.Equal(42, target.Number);
    }

    [Fact]
    public void A_nonnullable_source_uses_the_unlifted_operator_before_nullable_wrapping()
    {
        Number input = new(42);
        long? expected = input;
        var plan = ExecutorBuilder.Start<Display, Number>(out var display, out var number).Call(nameof(Display.ShowNullable), [display, number]);
        var target = new Display();
        plan.Build<IExecutor<Display, Number>>().Execute(target, input);
        Assert.Equal(expected, target.Nullable);
    }

    [Fact]
    public void Two_user_conversions_cannot_be_chained_implicitly()
    {
        var plan = ExecutorBuilder.Start<Display, Number>(out var display, out var number).Call(nameof(Display.ShowWrapped), [display, number]);
        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Display, Number>>());
    }

    [Fact]
    public void Ambiguous_operators_fail_at_compilation()
    {
        var plan = ExecutorBuilder.Start<Display, AmbiguousSource>(out var display, out var value).Call(nameof(Display.ShowAmbiguous), [display, value]);
        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Display, AmbiguousSource>>());
    }

    [Fact]
    public void An_ambiguous_conversion_does_not_hide_an_applicable_exact_overload()
    {
        var plan = ExecutorBuilder.Start<Display, AmbiguousSource>(out var display, out var value).Call(nameof(Display.ShowExact), [display, value]);
        var target = new Display();
        plan.Build<IExecutor<Display, AmbiguousSource>>().Execute(target, new());
        Assert.Equal("source", target.Shown);
    }

    public class Display
    {
        public string Caption { get; set; } = "";
        public string Shown { get; private set; } = "";
        public long Number
        {
            get; private set;
        }
        public long? Nullable
        {
            get; private set;
        }
        public void Show(string value) => Shown = value;
        public void ShowNumber(long value) => Number = value;
        public void ShowNullable(long? value) => Nullable = value;
        public void ShowWrapped(Wrapped value) => Number = value.Value;
        public void ShowAmbiguous(AmbiguousTarget value)
        {
        }
        public void ShowExact(AmbiguousSource value) => Shown = "source";
        public void ShowExact(AmbiguousTarget value) => Shown = "target";
    }

    public readonly record struct Number(int Value)
    {
        public static implicit operator int(Number value) => value.Value;
    }
    public readonly record struct Wrapped(long Value)
    {
        public static implicit operator Wrapped(long value) => new(value);
    }
    public class AmbiguousSource
    {
        public static implicit operator AmbiguousTarget(AmbiguousSource value) => new();
    }
    public class AmbiguousTarget
    {
        public static implicit operator AmbiguousTarget(AmbiguousSource value) => new();
    }
}

