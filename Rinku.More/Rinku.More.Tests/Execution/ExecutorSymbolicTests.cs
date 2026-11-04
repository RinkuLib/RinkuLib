using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorSymbolicTests
{
    [Theory]
    [InlineData(0, 3000000001L)]
    [InlineData(1, -3000000000L)]
    [InlineData(2, 6000000000L)]
    public void Arithmetic_uses_the_bound_result_type_instead_of_narrowing_to_the_authoring_type(int operation, long expected)
    {
        var plan = ExecutorBuilder.Start<AuthoredNumber>(out var input).Then(out var number, () => input.Val.Number);
        if (operation == 0)
        {
            plan.Then(out var result, () => number.Val + 1).Then(() => input.Val.Result, result);
        }
        else if (operation == 1)
        {
            plan.Then(out var result, () => -number.Val).Then(() => input.Val.Result, result);
        }
        else
        {
            plan.Then(out var result, () => number.Val << 1).Then(() => input.Val.Result, result);
        }
        var instance = new BoundNumber { Number = 3000000000L };

        plan.Build<IExecutor<BoundNumber>>().Execute(instance);

        Assert.Equal(expected, instance.Result);
    }

    [Fact]
    public void Comparisons_use_the_bound_operand_types()
    {
        var plan = ExecutorBuilder.Start<AuthoredNumber>(out var input).Then(out var number, () => input.Val.Number)
            .If(() => number.Val > 0, branch => branch.Then(() => input.Val.Result, 1));
        var instance = new BoundNumber { Number = 3000000000L };

        plan.Build<IExecutor<BoundNumber>>().Execute(instance);

        Assert.Equal(1, instance.Result);
    }

    public class AuthoredNumber
    {
        public int Number
        {
            get; set;
        }
        public int Result
        {
            get; set;
        }
    }

    public class BoundNumber
    {
        public long Number
        {
            get; set;
        }
        public long Result
        {
            get; set;
        }
    }

    [Fact]
    public void An_unrelated_target_type_drives_chained_calls_and_members()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Then(out var column, () => grid.Val.CreateColumn())
            .Then(() => column.Val.Caption, "Name");
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Equal("Name", tree.Column.Caption);
    }

    [Fact]
    public void Fixed_receivers_contribute_their_actual_instance_overloads()
    {
        BaseHelper helper = new ActualHelper();
        var plan = ExecutorBuilder.Start<Grid>(out var grid).Then(() => helper.Apply(grid.Val));
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Equal("actual helper", tree.Column.Caption);
    }

    [Fact]
    public void Fixed_values_stored_as_variables_retain_their_actual_type()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid).Then<BaseHelper>(out var helper, new ActualHelper())
            .Then(() => helper.Val.Apply(grid.Val));
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Equal("actual helper", tree.Column.Caption);
    }

    [Fact]
    public void Fixed_arguments_contribute_methods_without_being_a_receiver()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Call(nameof(ActualHelper.FromArgument), [grid, new ActualHelper()]);
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Equal("argument provider", tree.Column.Caption);
    }

    [Fact]
    public void One_generated_type_implements_independently_bound_execute_signatures()
    {
        var configuration = new ExecutorBuildConfiguration().Support<IExecutor<Tree>>();
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Then(out var column, () => grid.Val.CreateColumn())
            .Then(() => column.Val.Caption, "shared plan");
        var executor = plan.Build<IExecutor<Grid>>(configuration);
        var first = new Grid();
        var second = new Tree();

        executor.Execute(first);
        Assert.IsAssignableFrom<IExecutor<Tree>>(executor).Execute(second);

        Assert.Equal("shared plan", first.Column.Caption);
        Assert.Equal("shared plan", second.Column.Caption);
        Assert.Equal(2, executor.GetType().GetMethods().Count(method => method.Name == nameof(IExecutor<Grid>.Execute)));
    }

    [Fact]
    public void A_composite_interface_can_request_multiple_signatures()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Then(out var column, () => grid.Val.CreateColumn())
            .Then(() => column.Val.Caption, "combined");
        var executor = plan.Build<IBoth>();
        var first = new Grid();
        var second = new Tree();

        executor.Execute(first);
        executor.Execute(second);

        Assert.Equal("combined", first.Column.Caption);
        Assert.Equal("combined", second.Column.Caption);
    }

    [Fact]
    public void A_runtime_interface_type_can_be_requested_without_a_generic_build_call()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid).Call(nameof(Grid.CreateColumn), [grid]);
        var built = plan.Build(new ExecutorBuildConfiguration().Support(typeof(IExecutor<Tree>)));
        Type type = built.GetType();
        var executor = Assert.IsAssignableFrom<IExecutor<Tree>>(built);

        executor.Execute(new Tree());

        Assert.Single(type.GetInterfaces(), contract => contract.IsGenericType);
    }

    [Fact]
    public void Symbolic_calls_survive_clone_merge_and_json_without_associations()
    {
        var source = ExecutorBuilder.Start<Grid>(out var grid)
            .Call<GridColumn>(out var column, nameof(Grid.CreateColumn), [grid])
            .Then(() => column.Val.Caption, "serialized");
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(source.Clone()));
        var plan = ExecutorBuilder.Start<Grid>(out var target).Merge(restored.ToScope());
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Equal("serialized", tree.Column.Caption);
        Assert.Equal("ExecutorScope", plan.Nodes.First().Kind);
        Assert.Equal(nameof(Grid.CreateColumn), restored.Nodes.First().Name);
    }

    [Fact]
    public void Repeated_authored_input_types_bind_independently_by_position()
    {
        var plan = ExecutorBuilder.Start<Grid, Grid>(out var left, out var right, "left", "right")
            .Then(out var first, () => left.Val.CreateColumn(), "first")
            .Then(out var second, () => right.Val.CreateColumn(), "second")
            .Then(() => first.Val.Caption, "tree")
            .Then(() => second.Val.Caption, "grid");
        var tree = new Tree();
        var grid = new Grid();

        plan.Build<IExecutor<Tree, Grid>>().Execute(tree, grid);

        Assert.Equal("tree", tree.Column.Caption);
        Assert.Equal("grid", grid.Column.Caption);
    }

    [Fact]
    public void Equivalent_requested_signatures_share_one_method_body()
    {
        var configuration = new ExecutorBuildConfiguration().Support<IExecutor<Grid>>().Support<IExecutor<Grid>>();
        var plan = ExecutorBuilder.Start<Grid>(out var grid).Call(nameof(Grid.CreateColumn), [grid]);

        var executor = plan.Build<IAlias>(configuration);

        Assert.Single(executor.GetType().GetMethods(), method => method.Name == nameof(IExecutor<Grid>.Execute));
        Assert.IsAssignableFrom<IExecutor<Grid>>(executor);
    }

    [Fact]
    public void Generated_callbacks_bind_independently_for_each_execute_signature()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Then(() => CallbackValues.Prepare(grid.Val));
        var executor = plan.Build<IExecutor<Grid>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(CallbackValues)).Support<IExecutor<Tree>>());
        var first = new Grid();
        var second = new Tree();
        CallbackValues.Values.Clear();

        executor.Execute(first);
        ((IExecutor<Tree>)executor).Execute(second);
        first.Raise(1);
        second.Raise(2);

        Assert.Equal([1, 2], CallbackValues.Values);
    }

    [Fact]
    public void Named_out_calls_create_variables_from_the_selected_output_type()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid);
        plan.Declare(out Variable<GridColumn> column, "column")
            .Call(nameof(OutMethods.Create), [grid, column], [ExecutorArgumentPassing.Value, ExecutorArgumentPassing.Out])
            .Then(() => column.Val.Caption, "created")
            .Call(nameof(OutMethods.Use), [grid, column]);
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(OutMethods))).Execute(tree);

        Assert.Equal("created", tree.Column.Caption);
    }

    public static class OutMethods
    {
        public static void Create(Tree target, out TreeColumn column) => column = new();
        public static void Use(Tree target, TreeColumn column) => target.Column.Caption = column.Caption;
    }

    [Fact]
    public void Literal_null_assignments_resolve_nullable_members_on_the_bound_type()
    {
        var plan = ExecutorBuilder.Start<Grid>(out var grid)
            .Then(out var column, () => grid.Val.CreateColumn())
            .Then(() => column.Val.OptionalCaption, (string?)null);
        var tree = new Tree();

        plan.Build<IExecutor<Tree>>().Execute(tree);

        Assert.Null(tree.Column.OptionalCaption);
    }

    public interface IAlias : IExecutor<Grid>;

    public class GridArgs : EventArgs
    {
        public int Value
        {
            get; set;
        }
    }

    public class TreeArgs : EventArgs
    {
        public int Value
        {
            get; set;
        }
    }

    public static class CallbackValues
    {
        public static ExecutorScope Prepare(Grid target)
        {
            EventHandler<GridArgs> handler = (_, args) => Add(args.Value);
            return ExecutorBuilder.Start<Grid>(out var input).Call(nameof(Grid.Apply), [input, handler]).ToScope();
        }
        public static ExecutorScope Prepare(Tree target)
        {
            EventHandler<TreeArgs> handler = (_, args) => Add(args.Value);
            return ExecutorBuilder.Start<Tree>(out var input).Call(nameof(Grid.Apply), [input, handler]).ToScope();
        }

        public static List<int> Values { get; } = [];
        public static void Add(int value) => Values.Add(value);
    }

    public interface IBoth : IExecutor<Grid>, IExecutor<Tree>;

    public class Grid
    {
        public event EventHandler<GridArgs>? Changed;
        public void Apply(EventHandler<GridArgs> handler)
        {
            Changed -= handler;
            Changed += handler;
        }
        public void Raise(int value) => Changed?.Invoke(this, new() { Value = value });
        public GridColumn Column { get; } = new();
        public GridColumn CreateColumn() => Column;
    }

    public class Tree
    {
        public event EventHandler<TreeArgs>? Changed;
        public void Apply(EventHandler<TreeArgs> handler)
        {
            Changed -= handler;
            Changed += handler;
        }
        public void Raise(int value) => Changed?.Invoke(this, new() { Value = value });
        public TreeColumn Column { get; } = new();
        public TreeColumn CreateColumn() => Column;
    }

    public class GridColumn
    {
        public string Caption { get; set; } = "";
        public string? OptionalCaption { get; set; } = "existing";
    }

    public class TreeColumn
    {
        public string Caption { get; set; } = "";
        public string? OptionalCaption { get; set; } = "existing";
    }

    public class BaseHelper
    {
        public void Apply(Grid grid) => grid.Column.Caption = "base helper";
    }

    public class ActualHelper : BaseHelper
    {
        public void Apply(Tree tree) => tree.Column.Caption = "actual helper";
        public static void FromArgument(Tree tree, ActualHelper helper) => tree.Column.Caption = "argument provider";
    }
}
