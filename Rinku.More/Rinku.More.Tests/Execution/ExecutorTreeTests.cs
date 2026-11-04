using System.Reflection.Emit;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorTreeTests
{
    [Fact]
    public void Operations_are_leaf_nodes_with_no_tree_or_recording_metadata_contract()
    {
        Assert.Contains(typeof(IPlanNode), typeof(IOperation).GetInterfaces());
        Assert.Empty(typeof(IOperation).GetProperties());
        Assert.Empty(typeof(IPlanNode).GetProperties());
        Assert.Equal([nameof(IPlanNode.Compile), nameof(IPlanNode.Copy), nameof(IPlanNode.Resolve)], typeof(IPlanNode).GetMethods().Select(method => method.Name).Order().ToArray());
    }

    [Fact]
    public void An_operation_cannot_also_be_a_group()
    {
        Assert.Throws<ArgumentException>(() => ExecutorBuilder.Start().Add(new InvalidLeafGroup()));
    }

    [Fact]
    public void Conditions_and_loops_own_sequences_without_extra_scope_nodes()
    {
        var plan = ExecutorBuilder.Start<Target>(out var target)
            .If(() => target.Val.Enabled, branch => branch.Then(() => target.Val.Value, 1))
            .While(() => target.Val.Enabled, body => body.Break());
        int groups = 0;
        foreach (PlanInstruction instruction in plan.ToScope().Instructions.Items)
        {
            if (instruction.Node is not IPlanGroup group)
            {
                Assert.IsAssignableFrom<IOperation>(instruction.Node);
                continue;
            }
            groups++;
            Assert.IsNotAssignableFrom<IOperation>(group);
            foreach (PlanSequence sequence in group.Sequences)
            {
                foreach (PlanInstruction nested in sequence.Items)
                {
                    Assert.IsAssignableFrom<IOperation>(nested.Node);
                    Assert.IsNotAssignableFrom<IPlanGroup>(nested.Node);
                }
            }
        }
        Assert.Equal(2, groups);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void Conditional_branches_have_independent_names_without_recorded_scope_wrappers(bool enabled, int expected)
    {
        var plan = ExecutorBuilder.Start<Target>(out var target).If(() => target.Val.Enabled,
            branch => branch.Then(out var value, 2, "value").Then(() => target.Val.Value, value),
            branch => branch.Then(out var value, 3, "value").Then(() => target.Val.Value, value));
        Target instance = new()
        {
            Enabled = enabled
        };

        plan.Build<IExecutor<Target>>().Execute(instance);

        Assert.Equal(expected, instance.Value);
    }

    [Fact]
    public void Loop_names_do_not_replace_enclosing_names()
    {
        var useOuter = ExecutorBuilder.Start().Reference<Target>(out var target).Reference<int>("value", out var value).Then(() => target.Val.Value, value).ToScope();
        var plan = ExecutorBuilder.Start<Target>(out var input).Then(out _, 1, "value")
            .While(() => input.Val.Enabled, body => body.Then(out _, 2, "value").Then(() => input.Val.Enabled, false))
            .Merge(useOuter);
        Target instance = new()
        {
            Enabled = true
        };

        plan.Build<IExecutor<Target>>().Execute(instance);

        Assert.Equal(1, instance.Value);
    }

    [Fact]
    public void A_returned_scope_is_group_data_without_an_execution_builder()
    {
        var builder = ExecutorBuilder.Start<Target>(out var target).Then(() => target.Val.Value, 1);
        ExecutorScope scope = builder.ToScope();
        Assert.Same(scope, builder.ToScope());
        Assert.IsAssignableFrom<IPlanGroup>(scope);
        Assert.IsNotAssignableFrom<IOperation>(scope);
        Assert.Null(typeof(ExecutorScope).GetMethod(nameof(ExecutorBuilder.Build)));
        new ExecutorBuilder(scope).Then(() => target.Val.Value, 2);
        var plan = ExecutorBuilder.Start<Target>(out _).Add(scope);
        Target instance = new();

        plan.Build<IExecutor<Target>>().Execute(instance);

        Assert.Equal(2, instance.Value);
    }

    [Fact]
    public void Custom_operation_failures_use_the_recorded_requirement_and_results()
    {
        var plan = ExecutorBuilder.Start<Target>(out _).Declare(out Variable<int> result)
            .Add(new MissingOperation(), ExecutorRequirement.Nullable, result).Result(result);

        Assert.Null(plan.Build<INullableResult>().Execute(new()));
        plan.Nodes.Last().Requirement = ExecutorRequirement.Required;
        Assert.Equal(ExecutorBindingFailure.MissingImplementation, Assert.Throws<ExecutorBindingException>(() => plan.Build<INullableResult>()).Failure);
    }

    [Fact]
    public void A_custom_scope_can_use_the_public_copy_binding_and_emission_helpers()
    {
        var plan = ExecutorBuilder.Start<int>(out var input).Declare(out Variable<int> output);
        PlanSequence body = new();
        body.Add(new IncrementOperation(input, output), outputs: [output]);
        plan.Add(new TestScope(body)).Result(output);

        Assert.Equal(8, plan.Build<INumberResult>().Execute(7));
        Assert.Equal(10, plan.Clone().Build<INumberResult>().Execute(9));
    }

    [Fact]
    public void Inspection_rejects_recursive_groups()
    {
        var plan = ExecutorBuilder.Start();
        plan.Add(plan.ToScope());

        Assert.Throws<InvalidOperationException>(() => plan.Nodes.ToArray());
        Assert.Throws<InvalidOperationException>(() => plan.Build<IEmptyExecutor>());
    }

    public class Target
    {
        public bool Enabled
        {
            get; set;
        }
        public int Value
        {
            get; set;
        }
    }
    public interface INullableResult : IExecutor
    {
        int? Execute(Target target);
    }
    public interface INumberResult : IExecutor
    {
        int Execute(int value);
    }
    public interface IEmptyExecutor : IExecutor
    {
        void Execute();
    }

    private sealed class MissingOperation : Operation
    {
        public override IPlanNode Copy(OperationCopyContext context) => new MissingOperation();
        public override IPlanNode? Resolve(OperationBindingContext context) => throw new ExecutorBindingException(ExecutorBindingFailure.MissingImplementation, nameof(MissingOperation));
        public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException();
    }

    private sealed class InvalidLeafGroup : Operation, IPlanGroup
    {
        public IEnumerable<PlanSequence> Sequences => [];
        public override IPlanNode Copy(OperationCopyContext context) => new InvalidLeafGroup();
        public override IPlanNode? Resolve(OperationBindingContext context) => this;
        public override void Compile(OperationEmissionContext context)
        {
        }
    }

    private sealed class TestScope(PlanSequence body) : PlanGroup
    {
        public override IEnumerable<PlanSequence> Sequences => [body];
        public override IPlanNode Copy(OperationCopyContext context) => new TestScope(context.Sequence(body));
        public override IPlanNode? Resolve(OperationBindingContext context) => new TestScope(context.Resolve(body));
        public override void Compile(OperationEmissionContext context) => context.Compile(body);
    }

    private sealed class IncrementOperation(Variable<int> input, Variable<int> output) : Operation
    {
        public override IPlanNode Copy(OperationCopyContext context) => new IncrementOperation(context.Copy(input), context.Copy(output));
        public override IPlanNode? Resolve(OperationBindingContext context)
        {
            context.Require(input);
            context.Define(output, typeof(int));
            return this;
        }
        public override void Compile(OperationEmissionContext context)
        {
            context.Load(input);
            context.IL.Emit(OpCodes.Ldc_I4_1);
            context.IL.Emit(OpCodes.Add);
            context.Store(output);
        }
    }
}
