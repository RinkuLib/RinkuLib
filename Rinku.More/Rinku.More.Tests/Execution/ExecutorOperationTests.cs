using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_external_operation_can_change_method_name_argument_order_and_quantity(bool selectCandidate)
    {
        var plan = ExecutorBuilder.Start<Target, int, string>(out var target, out var number, out var text).Declare(out Variable<string> result);
        plan.Add(new CombineOperation(target, number, text, result, selectCandidate), outputs: [result]).Result(result);
        var first = plan.Build<ICombinedResult>();
        var second = plan.Clone().Build<ICombinedResult>();

        Assert.Equal("prefix:text:7", first.Execute(new(), 7, "text"));
        Assert.Equal("prefix:other:9", second.Execute(new(), 9, "other"));
    }

    [Fact]
    public void A_custom_operation_uses_the_same_nullable_fallback_and_preserves_the_original_plan()
    {
        var plan = ExecutorBuilder.Start<Target, int, string>(out var target, out var number, out var text).Declare(out Variable<string> result);
        plan.Add(new CombineOperation(target, number, text, result), ExecutorRequirement.Nullable, result).Result(result);

        Assert.Null(plan.Build<IMissingCombinedResult>().Execute(new(), 7, "text"));
        Assert.Equal("prefix:text:7", plan.Build<ICombinedResult>().Execute(new(), 7, "text"));
    }

    [Fact]
    public void A_custom_selected_candidate_can_return_a_prepared_scope()
    {
        Values.Prepared = 0;
        var plan = ExecutorBuilder.Start<Target>(out var target).Add(new PreparedOperation(target));
        var executor = plan.Build<IExecutor<Target>>();
        Target instance = new();

        Assert.Equal(1, Values.Prepared);
        executor.Execute(instance);
        executor.Execute(instance);
        Assert.Equal(1, Values.Prepared);
        Assert.Equal("prepared", instance.Caption);
    }

    [Fact]
    public void An_external_operation_can_bind_and_emit_without_a_builtin_operation()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<int> result);
        plan.Add(new IncrementOperation(input, result)).Result(result);

        Assert.Equal(8, plan.Build<INumberResult>().Execute(7));
        Assert.Equal(10, plan.Clone().Build<INumberResult>().Execute(9));
    }

    [Fact]
    public void Capturing_a_value_and_reading_a_static_field_have_distinct_execution_semantics()
    {
        Values.Number = 3;
        var capturedPlan = ExecutorBuilder.Start<int>(out _).Declare(out Variable<int> captured);
        capturedPlan.Add(new ConstantOperation<int>(Values.Number, captured)).Result(captured);
        var readPlan = ExecutorBuilder.Start<int>(out _).Declare(out Variable<int> read);
        readPlan.Add(new StaticFieldReadOperation<int>(typeof(Values).GetField(nameof(Values.Number))!, read)).Result(read);
        var capturedExecutor = capturedPlan.Build<INumberResult>();
        var readExecutor = readPlan.Build<INumberResult>();
        Values.Number = 8;

        Assert.Equal(3, capturedExecutor.Execute(0));
        Assert.Equal(8, readExecutor.Execute(0));
        Values.Number = 12;
        Assert.Equal(12, readExecutor.Execute(0));
    }

    [Fact]
    public void A_captured_variable_object_is_a_value_rather_than_an_argument_reference()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<Variable<int>> result);
        plan.Add(new ConstantOperation<Variable<int>>(input, result)).Result(result);

        Assert.Same(input, plan.Build<IVariableResult>().Execute(42));
    }

    [Fact]
    public void Alternative_names_do_not_impose_a_priority()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).Call([nameof(Target.Caption), nameof(Target.Title)], [target, "Name"]);

        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<Target>>());
    }

    [Fact]
    public void Instance_and_static_methods_use_the_same_argument_sequence()
    {
        var plan = ExecutorBuilder.Start<object, int>(out var target, out var input).Call<int>(out var result, nameof(Target.Double), [target, input]).Result(result);

        Assert.Equal(14, plan.Build<IInstanceResult>().Execute(new(), 7));
        Assert.Equal(21, plan.Build<IStaticResult>(new ExecutorBuildConfiguration().RegisterProvider(typeof(StaticMethods))).Execute(new(), 7));
    }

    [Fact]
    public void Emitted_custom_operations_allocate_nothing()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<int> output).Add(new IncrementOperation(input, output)).Result(output);
        var executor = plan.Build<INumberResult>();
        executor.Execute(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int last = 0;
        for (int index = 0; index < 10000; index++)
        {
            last = executor.Execute(index);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10000, last);
    }

    [Fact]
    public void Expression_inspection_does_not_execute_getters_or_calls()
    {
        Values.Reads = 0;
        var source = new ReadSource();
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .If(() => source.Value is Target, branch => branch.Then(() => target.Val.Caption, "matched"));
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(ReadSource));

        Assert.Equal(0, Values.Reads);
        var executor = plan.Build<IExecutor<Target>>(configuration);
        Assert.Equal(0, Values.Reads);
        Target instance = new();
        executor.Execute(instance);
        Assert.Equal(1, Values.Reads);
        Assert.Equal("matched", instance.Caption);
    }

    [Fact]
    public void Authoring_helpers_are_not_implicitly_registered()
    {
        var plan = ExecutorBuilder.Start<Target, int>(out var target, out var number).Then(() => Authoring.Hidden(target.Val, number.Val));

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Target, int>>());
        plan.Build<IExecutor<Target, int>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(Authoring))).Execute(new(), 1);
    }

    [Fact]
    public void Fixed_arguments_contribute_their_actual_instance_type()
    {
        object source = new ReadSource();
        var plan = ExecutorBuilder.Start<Target>(out var target).Call(nameof(ReadSource.Copy), [source, target]);
        Target instance = new();

        plan.Build<IExecutor<Target>>().Execute(instance);

        Assert.Equal("copied", instance.Caption);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Ordered_comparisons_include_equality_without_reversing_the_direction(int leftValue, int rightValue)
    {
        var lessPlan = ExecutorBuilder.Start<int, int>(out var left, out var right).Then(out var less, () => left.Val <= right.Val).Result(less);
        var greaterPlan = ExecutorBuilder.Start<int, int>(out var otherLeft, out var otherRight).Then(out var greater, () => otherLeft.Val >= otherRight.Val).Result(greater);

        Assert.Equal(leftValue <= rightValue, lessPlan.Build<IComparisonResult>().Execute(leftValue, rightValue));
        Assert.Equal(leftValue >= rightValue, greaterPlan.Build<IComparisonResult>().Execute(leftValue, rightValue));
    }

    [Fact]
    public void Nullable_floating_comparisons_preserve_unordered_and_missing_values()
    {
        var plan = ExecutorBuilder.Start<double?, double?>(out var left, out var right).Then(out var result, () => left.Val <= right.Val).Result(result);
        var executor = plan.Build<INullableComparisonResult>();

        Assert.False(executor.Execute(double.NaN, 1));
        Assert.False(executor.Execute(1, double.NaN));
        Assert.False(executor.Execute(null, 1));
        Assert.True(executor.Execute(1, 1));
        Assert.True(executor.Execute(1, 2));
        Assert.False(executor.Execute(2, 1));
    }

    public interface ICombinedResult : IExecutor
    {
        string Execute(Target target, int number, string text);
    }
    public interface IMissingCombinedResult : IExecutor
    {
        string? Execute(StaticTarget target, int number, string text);
    }
    public interface INumberResult : IExecutor
    {
        int Execute(int number);
    }
    public interface IVariableResult : IExecutor
    {
        Variable<int> Execute(int number);
    }
    public interface IInstanceResult : IExecutor
    {
        int Execute(Target target, int number);
    }
    public interface IStaticResult : IExecutor
    {
        int Execute(StaticTarget target, int number);
    }
    public interface IComparisonResult : IExecutor
    {
        bool Execute(int left, int right);
    }
    public interface INullableComparisonResult : IExecutor
    {
        bool Execute(double? left, double? right);
    }

    public class Target
    {
        public string Caption { get; set; } = "";
        public string Title { get; set; } = "";
        public string Combine(string prefix, string text, int number) => $"{prefix}:{text}:{number}";
        public int Double(int number) => number * 2;
    }
    public class StaticTarget;
    public static class StaticMethods
    {
        public static int Double(StaticTarget target, int number) => number * 3;
    }
    public static class Values
    {
        public static int Number;
        public static int Reads;
        public static int Prepared;
    }

    public static class Authoring
    {
        public static void Hidden(Target target, int number) => target.Caption = number.ToString();
        public static ExecutorScope Prepare(Target target)
        {
            Values.Prepared++;
            return ExecutorBuilder.Start<Target>(out var instance).Then(() => instance.Val.Caption, "prepared").ToScope();
        }
    }

    public class ReadSource
    {
        public object Value
        {
            get
            {
                Values.Reads++;
                return new Target();
            }
        }

        public void Copy(Target target) => target.Caption = "copied";
    }

    private sealed class CombineOperation(Variable<Target> target, Variable<int> number, Variable<string> text, Variable<string> output, bool selectCandidate = false) : Operation
    {
        public override IPlanNode Copy(OperationCopyContext context) => new CombineOperation(context.Copy(target), context.Copy(number), context.Copy(text), context.Copy(output), selectCandidate);
        public override IPlanNode? Resolve(OperationBindingContext context)
        {
            OperationArgument[] arguments = [target, OperationArgument.Constant("prefix"), text, number];
            return selectCandidate ? context.Bind(context.Find(context.TypeOf(target)).Single(candidate => candidate.Member.Name == nameof(Target.Combine)), arguments, output) : context.Call(nameof(Target.Combine), arguments, output);
        }
        public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException();
    }

    private sealed class PreparedOperation(Variable<Target> target) : Operation
    {
        public override IPlanNode Copy(OperationCopyContext context) => new PreparedOperation(context.Copy(target));
        public override IPlanNode? Resolve(OperationBindingContext context) => context.Bind(new(typeof(Authoring).GetMethod(nameof(Authoring.Prepare))!), [target]);
        public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException();
    }

    private sealed class IncrementOperation(Variable<int> input, Variable<int> output) : IOperation
    {
        public IPlanNode Copy(OperationCopyContext context) => new IncrementOperation(context.Copy(input), context.Copy(output));
        public IPlanNode? Resolve(OperationBindingContext context)
        {
            context.Require(input);
            context.Define(output, typeof(int));
            return this;
        }
        public void Compile(OperationEmissionContext context)
        {
            context.Load(input);
            context.IL.Emit(OpCodes.Ldc_I4_1);
            context.IL.Emit(OpCodes.Add);
            context.Store(output);
        }
    }
}
