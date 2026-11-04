using System.Linq.Expressions;
using System.Reflection;
using Rinku;
using Xunit;

namespace Rinku.More.Tests;
/// <summary>Defines the serialization boundary for the executor subsystem tests.</summary>
[CollectionDefinition("Executor subsystem", DisableParallelization = true)]
public sealed class ExecutorCollection;
/// <summary>Exercises executor authoring, binding and execution.</summary>
[Collection("Executor subsystem")]
public sealed class ExecutorSubsystemTests
{
    private static ExecutorBuildConfiguration Configuration { get; set; } = new();

    public ExecutorSubsystemTests() => Configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(TestActions)).RegisterMethod(typeof(ExecutorSubsystemTests).GetMethod(nameof(RecordGridMetadata))!);
    [Fact]
    public void Public_builder_surface_does_not_expose_IL_shaped_operations()
    {
        string[] methodNames = typeof(ExecutorBuilder).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(method => method.Name).Distinct().ToArray();
        Assert.DoesNotContain("New", methodNames);
        Assert.DoesNotContain("Get", methodNames);
        Assert.DoesNotContain("Load", methodNames);
        Assert.DoesNotContain("Convert", methodNames);
        Assert.DoesNotContain("TypeAs", methodNames);
        Assert.DoesNotContain("Compare", methodNames);
        Assert.DoesNotContain("Binary", methodNames);
        Assert.DoesNotContain("Unary", methodNames);
        Assert.DoesNotContain("Increment", methodNames);
        Assert.Contains("Call", methodNames);
        Assert.DoesNotContain("Subscribe", methodNames);
        Assert.DoesNotContain("Unsubscribe", methodNames);
        Assert.DoesNotContain("Method", methodNames);
        Assert.DoesNotContain("Assign", methodNames);
        Assert.DoesNotContain("Variable", methodNames);
        Assert.Contains("Then", methodNames);
    }

    [Fact]
    public void Specialization_is_selected_by_each_build_configuration()
    {
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var container).Then(() => container.Val.Enabled, true);
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration();
        var specialized = builder.Build<IExecutor<GridAdapter>>(configuration);
        var source = builder.Build<IExecutor<FakeGrid>>(configuration);
        var plain = builder.Build<IExecutor<IContainer>>();
        var reused = builder.Build<IExecutor<GridAdapter>>(configuration);
        var grid = new FakeGrid();
        source.Execute(grid);
        Assert.True(grid.Enabled);
        Assert.IsNotAssignableFrom<IExecutor<IContainer>>(specialized);
        Assert.IsNotAssignableFrom<IExecutor<FakeGrid>>(specialized);
        Assert.IsNotAssignableFrom<IExecutor<IContainer>>(source);
        Assert.IsNotAssignableFrom<IExecutor<FakeGrid>>(plain);
        Assert.IsNotAssignableFrom<IExecutor<FakeGrid>>(reused);
    }

    [Fact]
    public void Captured_expressions_cover_construction_chaining_assignment_and_scoped_loops()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out var repository, () => container.Val.GetOrAddColumn("Scoped").Repository)
            .Then(() => repository.Val.ReadOnly, true)
            .For(0, index => index < 3, (loop, index) => loop.Then(index, () => index.Val + 1), (loop, index) => loop.Then(() => TestActions.Observe(index.Val))).ForEach(() => container.Val.GetColumns(), (loop, column) => loop.Then(() => column.Val.SetWidth(column.Val.Width + 5)))
            .Build<IExecutor<IContainer>>(Configuration);
        FakeGrid grid = new();
        grid.AddColumn("Scoped").Width = 10;
        executor.Execute(new GridAdapter(grid));
        Assert.Equal([0, 1, 2], TestActions.Observed);
        Assert.True(grid.GetColumn("Scoped").Repository.ReadOnly);
        Assert.Equal(15, grid.GetColumn("Scoped").Width);
    }

    /// <summary>Verifies expression calls, chained values, promoted returns, assignments, and source entry points.</summary>
    [Fact]
    public void Expressions_build_chains_and_specialize_nested_types()
    {
        RegisterHelpers();
        TestActions.LastGenericType = null;
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(() => TestActions.SuspendLayout());
        builder.Then(out Variable<IColumn> column, () => input.Val.GetOrAddColumn("Name"));
        builder.Then(() => column.Val.Width, 150);
        builder.Then(out Variable<IRepository> repository, () => column.Val.Repository, name: "repository");
        builder.Then(() => repository.Val.ReadOnly, true);
        builder.Then(() => TestActions.Refresh(column.Val));
        builder.Then(() => input.Val.Use(column.Val));
        builder.Then(out Variable<IRepository> chainRepository, () => input.Val.GetOrAddColumn("Chain").Repository, name: "chainRepository");
        builder.Then(() => chainRepository.Val.ReadOnly, true);
        builder.Then(out Variable<string> title, () => TestActions.Format(input.Val.Title));
        builder.Then(() => input.Val.Title, title);
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        FakeGrid logicalGrid = new();
        executor.Execute(new GridAdapter(logicalGrid));
        Assert.True(logicalGrid.InterfaceCalls > 0);
        Assert.Equal(typeof(IColumn), TestActions.LastGenericType);
        FakeGrid grid = new();
        builder.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal(150, grid.GetColumn("Name").Width);
        Assert.True(grid.GetColumn("Name").Repository.ReadOnly);
        Assert.True(grid.GetColumn("Name").Used);
        Assert.True(grid.GetColumn("Chain").Repository.ReadOnly);
        Assert.Equal("[start]", grid.Title);
        Assert.Equal(typeof(GridColumnAdapter), TestActions.LastGenericType);
        Assert.Equal(0, grid.InterfaceCalls);
        FakeGrid concreteGrid = new();
        builder.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(concreteGrid));
        Assert.Equal(typeof(GridColumnAdapter), TestActions.LastGenericType);
        Assert.Equal(0, concreteGrid.InterfaceCalls);
    }

    /// <summary>Verifies independent inputs and nested results across requested target signatures.</summary>
    [Fact]
    public void Multiple_inputs_are_ordered_and_nested_values_still_specialize()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer, int>(out Variable<IContainer> container, out Variable<int> width);
        builder.Then(out Variable<IColumn> column, () => container.Val.GetOrAddColumn("Name"));
        builder.Then(() => column.Val.SetWidth(width.Val));
        IExecutor<IContainer, int> executor = builder.Build<IExecutor<IContainer, int>>(Configuration);
        FakeGrid logicalGrid = new();
        executor.Execute(new GridAdapter(logicalGrid), 101);
        Assert.Equal(101, logicalGrid.GetColumn("Name").Width);
        FakeGrid concreteGrid = new();
        builder.Build<IExecutor<GridAdapter, int>>(Configuration).Execute(new GridAdapter(concreteGrid), 202);
        Assert.Equal(202, concreteGrid.GetColumn("Name").Width);
    }

    [Fact]
    public void Adapter_factories_are_explicit_operations_in_the_plan()
    {
        var builder = ExecutorBuilder.Start<FakeGrid>(out var source)
            .Then(out var container, () => GridAdapter.Create(source.Val))
            .Then(() => container.Val.SetTitle("expression"))
            .Then(out var column, () => container.Val.GetOrAddColumn("Expression"))
            .Then(() => column.Val.SetWidth(23));
        var executor = builder.Build<IExecutor<FakeGrid>>(Configuration.RegisterProvider(typeof(GridAdapter)));
        FakeGrid grid = new();
        executor.Execute(grid);
        Assert.Equal("expression", grid.Title);
        Assert.Equal(23, grid.GetColumn("Expression").Width);
    }

    [Fact]
    public void Explicit_adapter_chains_compose_like_any_other_operations()
    {
        var plan = ExecutorBuilder.Start<ChainLevelOne>(out var source)
            .Then(out var second, () => new ChainLevelTwo(source.Val))
            .Then(out var third, () => new ChainLevelThree(second.Val))
            .Then(() => third.Val.Record());
        TestActions.Observed.Clear();
        plan.Build<IExecutor<ChainLevelOne>>().Execute(new ChainLevelOne(11));
        Assert.Equal([11], TestActions.Observed);
    }

    [Fact]
    public void Argument_binding_does_not_invent_adapter_paths()
    {
        var plan = ExecutorBuilder.Start<ChainLevelOne>(out var source)
            .Then(Method(typeof(TestActions), nameof(TestActions.RecordChainLevelThree)), [source]);
        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<ChainLevelOne>>());
    }

    /// <summary>Verifies plan values infer expression parameter types and preserve fluent chaining.</summary>
    [Fact]
    public void Expression_parameter_types_are_inferred_from_plan_values()
    {
        RegisterHelpers();
        var executor = ExecutorBuilder.Start<IContainer, int>(out var container, out var width)
            .Then(out var column, () => container.Val.GetOrAddColumn("Implicit"))
            .Then(() => column.Val.Width, 150)
            .Then(() => column.Val.SetWidth(width.Val))
            .Build<IExecutor<GridAdapter, int>>(Configuration);
        FakeGrid grid = new();
        executor.Execute(new GridAdapter(grid), 275);
        Assert.Equal(275, grid.GetColumn("Implicit").Width);
    }

    /// <summary>Verifies extension-defined expression arities use the common typed lambda bridge.</summary>
    [Fact]
    public void Expressions_support_extension_defined_arities_without_core_overloads()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.GlobalValue = 0;
        Expression<Action<int, int, int, int, int, int, int, int, int, int>> observe = (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) => TestActions.Observe(value10);
        Expression<Func<int, int, int, int, int, int, int, int, int, int, int>> select = (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) => value10;
        Expression<Func<int, int, int, int, int, int, int, int, int, int, int>> target = (value1, value2, value3, value4, value5, value6, value7, value8, value9, value10) => TestActions.GlobalValue;
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out var value1, () => 1, name: "value1")
            .Then(out var value2, () => 2, name: "value2")
            .Then(out var value3, () => 3, name: "value3")
            .Then(out var value4, () => 4, name: "value4")
            .Then(out var value5, () => 5, name: "value5")
            .Then(out var value6, () => 6, name: "value6")
            .Then(out var value7, () => 7, name: "value7")
            .Then(out var value8, () => 8, name: "value8")
            .Then(out var value9, () => 9, name: "value9")
            .Then(out var value10, () => 10, name: "value10").ThenExpression(observe, value1, value2, value3, value4, value5, value6, value7, value8, value9, value10)
            .Then<int>(select, [value1, value2, value3, value4, value5, value6, value7, value8, value9, value10], out Variable<int> selected)
            .Then<int>(target, [value1, value2, value3, value4, value5, value6, value7, value8, value9, value10], selected)
            .Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([10], TestActions.Observed);
        Assert.Equal(10, TestActions.GlobalValue);
    }

    /// <summary>Verifies registered nested types are promoted even when no earlier plan result has introduced them.</summary>
    [Fact]
    public void Concrete_entries_promote_registered_nested_types_before_results_exist()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> container);
        builder.Then(out Variable<IEnumerable<IColumn>> columns, () => container.Val.GetColumns());
        builder.ForEach(columns, (loop, item) => loop.Then(() => item.Val.SetWidth(17)));
        FakeGrid grid = new();
        grid.AddColumn("A");
        grid.AddColumn("B");
        builder.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal(17, grid.GetColumn("A").Width);
        Assert.Equal(17, grid.GetColumn("B").Width);
    }

    /// <summary>Verifies raw reflection calls use a single operand sequence for static and instance methods.</summary>
    [Fact]
    public void Raw_calls_and_member_operations_do_not_treat_the_first_input_as_special()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        MethodInfo setEnabled = Method<IContainer>(nameof(IContainer.SetEnabled));
        PropertyInfo title = Property<IContainer>(nameof(IContainer.Title));
        builder.Then(setEnabled, [input, true]);
        builder.Then(title, "raw", [input]);
        builder.Then(Method(typeof(TestActions), nameof(TestActions.AppendMarker)), [input, "!"]);
        builder.Then(() => TestActions.Observe(9));
        FakeGrid grid = new();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.True(grid.Enabled);
        Assert.Equal("raw!", grid.Title);
        Assert.Equal([9], TestActions.Observed);
    }

    /// <summary>Verifies fields on value-type plan values are addressed for writes and reads.</summary>
    [Fact]
    public void Value_type_fields_use_addresses_for_assignments_and_increments()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<MutableValue> value, () => TestActions.CreateMutableValue());
        FieldInfo number = typeof(MutableValue).GetField(nameof(MutableValue.Number)) ?? throw new InvalidOperationException();
        builder.Then(number, 5, [value]);
        builder.Then(() => value.Val.Number, () => value.Val.Number + 2);
        builder.Then(() => TestActions.Observe(value.Val.Number));
        builder.Then(out Variable<int> negative, () => -value.Val.Number);
        builder.Then(() => TestActions.Observe(negative.Val));
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([7, -7], TestActions.Observed);
    }

    /// <summary>Verifies typed local, comparison, binary, and unary operations compose with plan values.</summary>
    [Fact]
    public void Typed_structural_value_operations_compose_without_operand_wrappers()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<MutableValue> value, () => TestActions.CreateMutableValue());
        FieldInfo number = typeof(MutableValue).GetField(nameof(MutableValue.Number)) ?? throw new InvalidOperationException();
        builder.Then(out Variable<int> current, number, [value], name: "current");
        Variable<int> local;
        builder.Declare(out local, "local");
        builder.Then(local, 3);
        builder.Then(local, local);
        builder.Then(out Variable<int> increment, () => current.Val + local.Val, name: "increment");
        builder.Then(out Variable<bool> isThree, () => local.Val == 3);
        builder.If(isThree, branch => branch.Then(() => value.Val.Number, () => -increment.Val));
        builder.Then(() => TestActions.Observe(value.Val.Number));
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([-3], TestActions.Observed);
    }

    /// <summary>Verifies failed loop construction restores the builder state for later loops.</summary>
    [Fact]
    public void Failed_loop_callbacks_restore_loop_state()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        Assert.Throws<InvalidOperationException>(() => builder.While(_ => throw new InvalidOperationException("condition"), _ =>
        {
        }));
        Assert.Throws<InvalidOperationException>(() => builder.Break());
        builder.While(loop =>
        {
            loop.Then(out Variable<bool> more, () => input.Val.HasMore(), name: "more");
            return more;
        }, loop => loop.Break());
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
    }

    /// <summary>Verifies static calls can produce values that are consumed by later operations.</summary>
    [Fact]
    public void Static_results_are_values_in_the_same_linear_plan()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<string> text, () => TestActions.Format("computed"));
        builder.Then(() => input.Val.Title, text);
        builder.Then(Property(typeof(TestActions), nameof(TestActions.GlobalValue)), 9, []);
        builder.Then(() => TestActions.RecordGlobalValue());
        FakeGrid grid = new();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("[computed]", grid.Title);
        Assert.Equal(9, TestActions.GlobalValue);
        Assert.Equal(9, TestActions.RecordedGlobalValue);
    }

    /// <summary>Verifies conditional values and short-circuit calls retain their runtime evaluation order.</summary>
    [Fact]
    public void Conditional_and_short_circuit_values_are_lazy()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<bool> available, () => input.Val.Enabled && input.Val.HasMore());
        builder.If(available, branch => branch.Then(() => input.Val.SetTitle("available")));
        builder.Then(out Variable<string> state, () => input.Val.Enabled ? "enabled" : "disabled");
        builder.Then(() => input.Val.Title, state);
        FakeGrid grid = new();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("disabled", grid.Title);
        Assert.Equal(0, grid.HasMoreCalls);
    }

    /// <summary>Verifies that an if expression can contain nested short-circuit logic without an exposed condition variable.</summary>
    [Fact]
    public void If_accepts_nested_boolean_expression_directly()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.If(() => input.Val.Enabled && (input.Val.HasMore() || !input.Val.HasMore()), branch => branch.Then(() => input.Val.SetTitle("matched")), branch => branch.Then(() => input.Val.SetTitle("not matched")));
        FakeGrid grid = new();
        grid.Enabled = true;
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("matched", grid.Title);
        Assert.Equal(1, grid.HasMoreCalls);
    }

    /// <summary>Verifies while, do/while, and for loops accept direct boolean expressions.</summary>
    [Fact]
    public void Loops_accept_boolean_expressions_directly()
    {
        RegisterHelpers();
        ExecutorBuilder whileBuilder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> whileInput);
        whileBuilder.While(() => whileInput.Val.HasMore(), loop => loop.Then(() => whileInput.Val.Advance()));
        FakeGrid whileGrid = new();
        whileBuilder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(whileGrid));
        Assert.Equal(3, whileGrid.Position);
        ExecutorBuilder doWhileBuilder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> doWhileInput);
        doWhileBuilder.DoWhile(() => doWhileInput.Val.HasMore(), loop => loop.Then(() => doWhileInput.Val.Advance()));
        FakeGrid doWhileGrid = new();
        doWhileBuilder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(doWhileGrid));
        Assert.Equal(3, doWhileGrid.Position);
        ExecutorBuilder forBuilder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> forInput)
            .Then(out Variable<int> index, 0, "index")
            .For(() => index.Val < 3, loop => loop.Then(() => index.Val, () => index.Val + 1), loop => loop.Then(() => forInput.Val.Advance()));
        FakeGrid forGrid = new();
        forBuilder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(forGrid));
        Assert.Equal(3, forGrid.Position);
    }

    /// <summary>Verifies concrete constructors and calls to built executors are emitted without a runtime plan interpreter.</summary>
    [Fact]
    public void Constructors_and_built_executors_can_be_nested()
    {
        RegisterHelpers();
        ExecutorBuilder columnBuilder = ExecutorBuilder.Start<IColumn>(out Variable<IColumn> columnInput);
        columnBuilder.Then(() => columnInput.Val.Width, 42);
        IExecutor<IColumn> columnExecutor = columnBuilder.Build<IExecutor<IColumn>>(Configuration);
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<IColumn> column, () => input.Val.GetOrAddColumn("Nested"));
        builder.Then(() => columnExecutor.Execute(column.Val));
        builder.Then(out Variable<string> constructorText, () => "constructed").Then(out Variable<CreatedValue> created, Constructor<CreatedValue, string>(), [constructorText]).Then(() => created.Val.Record());
        FakeGrid grid = new();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal(42, grid.GetColumn("Nested").Width);
        Assert.Equal("constructed", CreatedValue.LastValue);
        ExecutorBuilder hostBuilder = ExecutorBuilder.Start<IContainer, int>(out Variable<IContainer> host, out Variable<int> hostWidth);
        FakeGrid nestedGrid = new();
        IExecutor<IColumn, int> nestedExecutor = ExecutorBuilder.Start<IColumn, int>(out Variable<IColumn> nestedColumn, out Variable<int> nestedWidth)
            .Then(() => nestedColumn.Val.Width, nestedWidth)
            .Build<IExecutor<IColumn, int>>(Configuration);
        hostBuilder.Then(out Variable<IColumn> specializedColumn, () => host.Val.GetOrAddColumn("Specialized"))
            .Then(() => nestedExecutor.Execute(specializedColumn.Val, hostWidth.Val))
            .Build<IExecutor<IContainer, int>>(Configuration).Execute(new GridAdapter(nestedGrid), 84);
        Assert.Equal(84, nestedGrid.GetColumn("Specialized").Width);
    }

    /// <summary>Verifies captured built executors use ordinary method binding.</summary>
    [Fact]
    public void Captured_built_executors_use_the_normal_call_binding_path()
    {
        RegisterHelpers();
        var columnExecutor = ExecutorBuilder.Start<IColumn>(out var columnInput).Then(() => TestActions.Refresh(columnInput.Val)).Then(() => columnInput.Val.Width, 42).Build<IExecutor<GridColumnAdapter>>(Configuration);
        var executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out var column, () => container.Val.GetOrAddColumn("Captured"))
            .Then(typeof(IExecutor<IColumn>).GetMethod(nameof(IExecutor<IColumn>.Execute))!, [columnExecutor, column])
            .Build<IExecutor<GridAdapter>>(Configuration);
        TestActions.LastGenericType = null;
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal(typeof(GridColumnAdapter), TestActions.LastGenericType);
    }

    [Fact]
    public void Crossed_instance_parameter_specializations_are_ambiguous()
    {
        RegisterHelpers();
        LogicalActions actions = new SpecializedActions();
        object marker = new GridMarker();
        var plan = ExecutorBuilder.Start<IContainer>(out var container).Then(out var column, () => container.Val.GetOrAddColumn("Arguments")).Then(() => actions.Apply(column.Val, marker));
        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<GridAdapter>>(Configuration));
    }

    /// <summary>Verifies discarded calls may select a specialized method with an unrelated return type.</summary>
    [Fact]
    public void Discarded_results_allow_specialized_unrelated_returns()
    {
        RegisterHelpers();
        LogicalSelector selector = new SpecializedSelector();
        var executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out var column, () => container.Val.GetOrAddColumn("Discarded"))
            .Then(() => selector.Select(column.Val))
            .Build<IExecutor<GridAdapter>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal(1, selector.Selected);
    }

    /// <summary>Verifies captured results reject an incompatible specialized return and retain the compatible method.</summary>
    [Fact]
    public void Captured_results_bind_to_the_selected_producer_return_type()
    {
        RegisterHelpers();
        LogicalSelector selector = new SpecializedSelector();
        var executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out var column, () => container.Val.GetOrAddColumn("CapturedReturn"))
            .Then(out Variable<IColumn> result, () => selector.Select(column.Val), name: "result")
            .Build<IExecutor<GridAdapter>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal(1, selector.Selected);
    }

    [Fact]
    public void Crossed_static_parameter_specializations_are_ambiguous()
    {
        RegisterHelpers();
        Configuration.RegisterProvider(typeof(SpecializedStaticActions));
        SpecializedStaticActions.Selected = 0;
        object marker = new GridMarker();
        var plan = ExecutorBuilder.Start<IContainer>(out var container).Then(out var column, () => container.Val.GetOrAddColumn("Static")).Then(() => LogicalStaticActions.Apply(column.Val, marker));
        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<GridAdapter>>(Configuration));
    }

    /// <summary>Verifies type and member metadata append operations to a fluent builder.</summary>
    [Fact]
    public void Type_metadata_populates_a_builder_and_composes_with_explicit_operations()
    {
        IExecutor<IGridView> executor = ExecutorBuilder.Start<IGridView>(out var grid).Then(() => RecordGridMetadata(typeof(MetadataUser))).Then(() => grid.Val.Refresh()).Build<IExecutor<IGridView>>(Configuration);
        TestGridView view = new();
        executor.Execute(view);
        Assert.True(view.ReadOnly);
        Assert.Equal(1, view.RefreshCount);
        Assert.Equal(("User name", 180), (view.GetOrAddColumn("Name").Label, view.GetOrAddColumn("Name").Width));
        Assert.Equal(("Enabled", 80), (view.GetOrAddColumn("Enabled").Label, view.GetOrAddColumn("Enabled").Width));
        Assert.Same(view.GetOrAddColumn("Name"), view.PrimaryColumn);
    }

    /// <summary>Verifies an independent metadata reader can be recorded as ordinary preparation.</summary>
    [Fact]
    public void Metadata_reader_can_be_recorded_as_an_ordinary_preparation_call()
    {
        IExecutor<IGridView> executor = ExecutorBuilder.Start<IGridView>(out _).Then(() => RecordGridMetadata(typeof(MetadataUser))).Build<IExecutor<IGridView>>(Configuration);
        TestGridView view = new();
        executor.Execute(view);
        Assert.True(view.ReadOnly);
        Assert.Equal(180, view.GetOrAddColumn("Name").Width);
    }

    /// <summary>Verifies an independent reader accepts a runtime metadata type.</summary>
    [Fact]
    public void Runtime_metadata_type_can_populate_a_builder()
    {
        IExecutor<IGridView> executor = ExecutorBuilder.Start<IGridView>(out var grid).Then(() => RecordGridMetadata(typeof(MetadataUser))).Build<IExecutor<IGridView>>(Configuration);
        TestGridView view = new();
        executor.Execute(view);
        Assert.True(view.ReadOnly);
        Assert.Equal(80, view.GetOrAddColumn("Enabled").Width);
    }

    /// <summary>Verifies if, while, foreach, local values, continue, and break are represented structurally.</summary>
    [Fact]
    public void Structural_control_flow_is_re_evaluated_at_runtime()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<bool> enabled, () => input.Val.Enabled, name: "enabled");
        builder.If(enabled, branch => branch.Then(() => input.Val.SetTitle("enabled")), branch => branch.Then(() => input.Val.SetTitle("disabled")));
        Variable<int> index;
        builder.Declare(out index, "index");
        builder.Then(index, 0);
        builder.While(loop =>
        {
            loop.Then(out Variable<bool> more, () => input.Val.HasMore(), name: "more");
            return more;
        }, loop =>
        {
            loop.Then(() => input.Val.Advance());
            loop.Then(index, () => index.Val + 1);
            loop.Then(() => TestActions.Observe(index.Val));
        });
        builder.Then(out Variable<IEnumerable<IColumn>> columns, () => input.Val.GetColumns());
        builder.ForEach(columns, (loop, item) =>
        {
            loop.Then(out Variable<int> width, () => item.Val.Width);
            loop.If(() => width.Val == 0, match => match.Continue());
            loop.Then(() => item.Val.SetWidth(25));
            loop.Break();
        });
        FakeGrid grid = new()
        {
            Enabled = true
        };
        grid.AddColumn("A");
        grid.AddColumn("B").Width = 10;
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("enabled", grid.Title);
        Assert.Equal([1, 2, 3], TestActions.Observed);
        Assert.Equal(0, grid.GetColumn("A").Width);
        Assert.Equal(25, grid.GetColumn("B").Width);
    }

    /// <summary>Verifies C#-style for and do/while structures use the same local slots as while blocks.</summary>
    [Fact]
    public void For_and_do_while_structures_share_minimal_local_slots()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        Variable<int> index;
        builder.Declare(out index, "index");
        builder.Then(index, 0);
        builder.For(loop =>
        {
            Variable<int> current = index;
            {
                loop.Then(out var recordedResult, () => current.Val < 3);
                return recordedResult;
            }
        }, loop => loop.Then(index, () => index.Val + 1), loop => loop.Then(() => TestActions.Observe(index.Val)));
        Variable<int> doIndex;
        builder.Declare(out doIndex, "doIndex");
        builder.Then(doIndex, 0);
        builder.DoWhile(loop =>
        {
            Variable<int> current = doIndex;
            {
                loop.Then(out var recordedResult, () => current.Val < 2);
                return recordedResult;
            }
        }, loop => loop.Then(doIndex, () => doIndex.Val + 1));
        builder.Then(() => TestActions.Observe(doIndex.Val));
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([0, 1, 2, 2], TestActions.Observed);
    }

    /// <summary>Verifies a for continue transfers to the increment region before the next condition test.</summary>
    [Fact]
    public void For_continue_runs_increment_before_next_condition()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var input);
        builder.For(0, index => index < 4, (loop, index) => loop.Then(index, () => index.Val + 1), (loop, index) => loop.If(() => index.Val == 1, branch => branch.Continue()).Then(() => TestActions.Observe(index.Val)));
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([0, 2, 3], TestActions.Observed);
    }

    /// <summary>Verifies idempotent event subscription and method-handler construction.</summary>
    [Fact]
    public void Fluent_result_variables_are_declared_before_their_calls()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var input)
            .Declare(out Variable<IColumn> column, "column")
            .Declare(out Variable<int> width, "width")
            .Then(column, () => input.Val.GetOrAddColumn("Fluent"))
            .Then(width, () => TestActions.ReturnInt(42))
            .Then(() => column.Val.Width, width)
            .Then(() => column.Val.SetWidth(column.Val.Width + 1));
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        FakeGrid grid = new();
        executor.Execute(new GridAdapter(grid));
        Assert.Equal(43, grid.GetColumn("Fluent").Width);
    }

    /// <summary>Verifies result-producing operations all keep the builder as their fluent return value.</summary>
    [Fact]
    public void Fluent_result_operations_store_into_predeclared_variables()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedLong.Clear();
        TestActions.ObservedText.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Declare<int>(out var number, "number")
            .Declare<long>(out var longNumber, "longNumber")
            .Declare<int>(out var sum, "sum")
            .Declare<bool>(out var less, "less")
            .Declare<bool>(out var notLess, "notLess")
            .Declare<int>(out var negative, "negative")
            .Declare<int[]>(out var values, "values")
            .Declare<int>(out var index, "index")
            .Declare<int>(out var current, "current")
            .Then<object>(out var boxed, "boxed")
            .Then<string>(out var text, "text")
            .Then(number, 3)
            .Then(index, 0)
            .Then(longNumber, () => (long)number.Val)
            .Then(sum, () => number.Val + 2)
            .Then(less, () => number.Val < 5)
            .Then(notLess, () => !less.Val)
            .Then(negative, () => -number.Val)
            .Then(values, () => new int[] { number.Val, 4 })
            .Then(current, () => values.Val[index.Val])
            .Then(boxed, () => TestActions.GetObject())
            .Then(text, () => (boxed.Val as string)!)
            .Then(() => TestActions.Observe(current.Val))
            .Then(() => TestActions.ObserveLong(longNumber.Val))
            .Then(() => TestActions.RecordText(text.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([3], TestActions.Observed);
        Assert.Equal([3L], TestActions.ObservedLong);
        Assert.Equal(["typed"], TestActions.ObservedText);
    }

    /// <summary>Verifies parameterless captured expressions resolve variables, chained calls, assignments, comparisons, and ref/out arguments.</summary>
    [Fact]
    public void Captured_expressions_use_variables_without_parameter_overloads()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Declare(out Variable<IColumn> column, "column")
            .Declare(out Variable<int> width, "width")
            .Declare<int>(out var outWidth, "outWidth")
            .Declare<bool>(out var wide, "wide")
            .Then(column, () => container.Val.GetOrAddColumn("CapturedSyntax"))
            .Then(() => width, 100)
            .Then(() => TestActions.UpdateByReference(ref width.Val))
            .Then(() => TestActions.TryGetWidth(out outWidth.Val))
            .Then(() => column.Val.Width, () => width)
            .Then(wide, () => width.Val < 150)
            .If(wide, branch => branch.Then(() => container.Val.SetEnabled(true)))
            .Then(() => column.Val.SetWidth(width.Val))
            .Then(() => TestActions.Observe(outWidth))
            .Build<IExecutor<IContainer>>(Configuration);
        FakeGrid grid = new();
        executor.Execute(new GridAdapter(grid));
        Assert.Equal([37], TestActions.Observed);
        Assert.Equal(101, grid.GetColumn("CapturedSyntax").Width);
        Assert.True(grid.Enabled);
    }

    /// <summary>Verifies variable-backed event handlers use the same handler operand for repeated subscription and removal.</summary>
    [Fact]
    public void Variable_event_handlers_are_reused_for_idempotent_subscription()
    {
        RegisterHelpers();
        IExecutor<IContainer, EventHandler> subscribe = ExecutorBuilder.Start<IContainer, EventHandler>(out var input, out var handler)
            .Then(() => TestActions.ApplyChanged(input.Val, handler.Val))
            .Build<IExecutor<IContainer, EventHandler>>(Configuration);
        IExecutor<IContainer, EventHandler> unsubscribe = ExecutorBuilder.Start<IContainer, EventHandler>(out var removeInput, out var removeHandler)
            .Then(() => TestActions.RemoveChanged(removeInput.Val, removeHandler.Val))
            .Build<IExecutor<IContainer, EventHandler>>(Configuration);
        TestActions.EventCalls = 0;
        EventHandler callback = TestActions.OnChanged;
        FakeGrid grid = new();
        subscribe.Execute(new GridAdapter(grid), callback);
        subscribe.Execute(new GridAdapter(grid), callback);
        grid.RaiseChanged();
        Assert.Equal(1, TestActions.EventCalls);
        unsubscribe.Execute(new GridAdapter(grid), callback);
        grid.RaiseChanged();
        Assert.Equal(1, TestActions.EventCalls);
    }

    /// <summary>Verifies a variable-backed handler remains a plan reference when serialized.</summary>
    [Fact]
    public void Json_round_trip_preserves_variable_event_handlers()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer, EventHandler>(out var input, out var handler).Then(() => TestActions.ApplyChanged(input.Val, handler.Val));
        string json = ExecutorJson.Serialize(builder);
        ExecutorBuilder restored = ExecutorJson.Deserialize(json);
        TestActions.EventCalls = 0;
        FakeGrid grid = new();
        restored.Build<IExecutor<GridAdapter, EventHandler>>(Configuration).Execute(new GridAdapter(grid), TestActions.OnChanged);
        grid.RaiseChanged();
        Assert.Equal(1, TestActions.EventCalls);
    }

    /// <summary>Verifies idempotent event subscription and method-handler construction.</summary>
    [Fact]
    public void Events_remove_before_adding_and_support_handlers()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(() => TestActions.PrepareChanged(input.Val));
        builder.Then(() => TestActions.PrepareGlobalChanged());
        TestActions.EventCalls = 0;
        FakeGrid grid = new();
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(grid));
        executor.Execute(new GridAdapter(grid));
        grid.RaiseChanged();
        TestActions.RaiseGlobalChanged();
        Assert.Equal(2, TestActions.EventCalls);
    }

    /// <summary>Verifies a source plan can be merged repeatedly with different target values.</summary>
    [Fact]
    public void Builders_can_be_merged_with_bound_values()
    {
        RegisterHelpers();
        ExecutorBuilder source = ExecutorBuilder.Start<IContainer, string>(out var sourceContainer, out var sourceMarker).Then(() => TestActions.AppendMarker(sourceContainer.Val, sourceMarker.Val));
        ExecutorBuilder target = ExecutorBuilder.Start<IContainer>(out var container).Scope(scope => scope.Then<string>(out _, "first", name: "marker")
            .Merge(source.ToScope())).Scope(scope => scope.Then<string>(out _, "second", name: "marker")
            .Merge(source.ToScope()));
        FakeGrid grid = new();
        target.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("startfirstsecond", grid.Title);
    }

    /// <summary>Verifies a custom derived executor contract remains available through the lower-level merge overload.</summary>
    [Fact]
    public void Custom_executor_contracts_can_be_merged()
    {
        RegisterHelpers();
        ExecutorBuilder source = new ExecutorBuilder(typeof(IContainer));
        Variable<IContainer> sourceContainer = source.Input<IContainer>(0);
        source.Then(() => sourceContainer.Val.SetTitle("custom"));
        ExecutorBuilder target = ExecutorBuilder.Start<IContainer>(out var container).Merge(source.ToScope());
        FakeGrid grid = new();
        target.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("custom", grid.Title);
    }

    /// <summary>Verifies Merge remaps source locals and nested loop sequences.</summary>
    [Fact]
    public void Merged_builders_preserve_nested_control_flow()
    {
        RegisterHelpers();
        ExecutorBuilder source = ExecutorBuilder.Start<IContainer, int>(out var sourceContainer, out var sourceCount);
        Variable<int> sourceIndex;
        source.Declare(out sourceIndex, "sourceIndex");
        source.Then(sourceIndex, 0).While(() => sourceIndex.Val < sourceCount.Val, loop => loop.Then(() => sourceContainer.Val.Advance()).Then(sourceIndex, () => sourceIndex.Val + 1));
        Assert.DoesNotContain(source.Nodes, node => node.Kind == "LocalLoad");
        ExecutorBuilder target = ExecutorBuilder.Start<IContainer>(out var container);
        Variable<int> count;
        target.Declare(out count, "count");
        target.Then(count, 3).Merge(source.ToScope());
        FakeGrid grid = new();
        target.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal(3, grid.Position);
    }

    /// <summary>Verifies composition resolves enclosing inputs at compilation.</summary>
    [Fact]
    public void Merged_builder_inputs_are_resolved_from_the_enclosing_scope()
    {
        ExecutorBuilder source = ExecutorBuilder.Start<IContainer, string>(out var container, out var marker);
        source.Then(() => container.Val.SetTitle(marker.Val));
        ExecutorBuilder target = ExecutorBuilder.Start<IContainer>(out _).Merge(source.ToScope());
        Assert.Throws<ExecutorBindingException>(() => target.Build<IExecutor<IContainer>>());
        var complete = ExecutorBuilder.Start<IContainer>(out _).Then<string>(out _, "resolved").Merge(source.ToScope());
        FakeGrid grid = new();
        complete.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("resolved", grid.Title);
    }

    /// <summary>Verifies a derived executor interface is implemented by the emitted type.</summary>
    [Fact]
    public void Derived_executor_interfaces_are_preserved()
    {
        RegisterHelpers();
        ExecutorBuilder builder = new ExecutorBuilder(typeof(IContainer));
        Variable<IContainer> input = builder.Input<IContainer>(0);
        builder.Then(() => input.Val.SetTitle("derived"));
        IContainerExecutor executor = builder.Build<IContainerExecutor>(Configuration);
        FakeGrid grid = new();
        executor.Execute(new GridAdapter(grid));
        Assert.Equal("derived", grid.Title);
    }

    /// <summary>Verifies values created inside a branch cannot be used after that branch.</summary>
    [Fact]
    public void Branch_values_cannot_escape_their_scope()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(out Variable<bool> condition, () => true);
        Variable<string>? branchValue = null;
        builder.If(condition, branch => branch.Then(out branchValue, () => "branch"));
        builder.Then(() => input.Val.Title, branchValue!);
        Assert.Throws<ExecutorBindingException>(() => builder.Build<IExecutor<IContainer>>(Configuration));
    }

    /// <summary>Verifies an unchanged root input is not promoted by another input's specialization.</summary>
    [Fact]
    public void Unchanged_root_inputs_are_not_promoted_by_other_inputs()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer, IColumn>(out _, out Variable<IColumn> column);
        Variable<IColumn> local;
        builder.Declare(out local, "local");
        var executor = builder.Then(local, column).Then(() => TestActions.ObserveColumn(local.Val)).Build<IExecutor<GridAdapter, IColumn>>(Configuration);
        LogicalOnlyColumn logicalColumn = new();
        TestActions.LastColumn = null;
        executor.Execute(new GridAdapter(new FakeGrid()), logicalColumn);
        Assert.Same(logicalColumn, TestActions.LastColumn);
    }

    /// <summary>Verifies mutable arrays preserve their logical element type.</summary>
    [Fact]
    public void Array_values_keep_logical_element_types_when_the_array_is_mutable()
    {
        RegisterHelpers();
        var executor = ExecutorBuilder.Start<IContainer>(out var container)
            .Then(out Variable<IColumn[]> columns, () => new[] { container.Val.GetOrAddColumn("Array") })
            .Then(() => columns.Val[0].SetWidth(33))
            .Build<IExecutor<GridAdapter>>(Configuration);
        FakeGrid grid = new();
        executor.Execute(new GridAdapter(grid));
        Assert.Equal(33, grid.GetColumn("Array").Width);
    }

    /// <summary>Verifies checked expression arithmetic and conversions preserve overflow behavior.</summary>
    [Fact]
    public void Checked_expression_operations_preserve_overflow_behavior()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(out Variable<int> safeValue, () => TestActions.ReturnInt(30), name: "safeValue");
        builder.Then(out Variable<byte> converted, () => checked((byte)safeValue.Val));
        builder.Then(() => TestActions.ObserveByte(converted.Val));
        builder.Then(out Variable<int> overflowValue, () => TestActions.ReturnInt(int.MaxValue), name: "overflowValue");
        builder.Then(out _, () => checked(overflowValue.Val + 1));
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        TestActions.ObservedByte.Clear();
        Assert.Throws<OverflowException>(() => executor.Execute(new GridAdapter(new FakeGrid())));
        Assert.Equal([(byte)30], TestActions.ObservedByte);
    }

    /// <summary>Verifies unary complement and small-integer promotion use their expression result types.</summary>
    [Fact]
    public void Unary_complement_and_small_integer_arithmetic_follow_expression_types()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(out Variable<int> complement, () => ~1, name: "complement");
        builder.Then(() => TestActions.Observe(complement.Val));
        builder.Then(out Variable<byte> first, () => (byte)200, name: "first");
        builder.Then(out Variable<byte> second, () => (byte)100, name: "second");
        builder.Then(out Variable<int> sum, () => first.Val + second.Val, name: "sum");
        builder.Then(() => TestActions.Observe(sum.Val));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([-2, 300], TestActions.Observed);
    }

    /// <summary>Verifies mixed numeric comparisons promote both operands before emitting comparison IL.</summary>
    [Fact]
    public void Mixed_numeric_comparisons_use_the_common_numeric_type()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(out Variable<int> value, () => 1);
        builder.Then(out Variable<bool> result, () => value.Val < TestActions.LongLimit);
        builder.If(result, branch => branch.Then(() => TestActions.Observe(1)), branch => branch.Then(() => TestActions.Observe(2)));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1], TestActions.Observed);
    }

    /// <summary>Verifies checked conversions preserve unsigned source interpretation.</summary>
    [Fact]
    public void Checked_unsigned_conversions_throw_when_the_unsigned_value_does_not_fit()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(out Variable<uint> value, () => TestActions.ReturnUInt(uint.MaxValue));
        builder.Then(out _, () => checked((int)value.Val));
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        Assert.Throws<OverflowException>(() => executor.Execute(new GridAdapter(new FakeGrid())));
    }

    /// <summary>Verifies enum bitwise operations and unsigned shifts preserve their expression result types.</summary>
    [Fact]
    public void Enum_bitwise_and_unsigned_shift_expressions_use_their_operator_types()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _)
            .Then(out Variable<TestFlags> flags, () => TestFlags.First | TestFlags.Second)
            .Then(() => TestActions.ObserveFlags(flags.Val))
            .Then(out Variable<uint> unsigned, () => uint.MaxValue)
            .Then(out Variable<int> count, () => 1)
            .Then(out Variable<uint> shifted, () => unsigned.Val >> count.Val, name: "shifted")
            .Then(() => TestActions.ObserveUInt(shifted.Val));
        TestActions.ObservedFlags.Clear();
        TestActions.ObservedUInt.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([TestFlags.First | TestFlags.Second], TestActions.ObservedFlags);
        Assert.Equal([uint.MaxValue >> 1], TestActions.ObservedUInt);
    }

    /// <summary>Verifies reflected enum constants retain their enum type when read as plan values.</summary>
    [Fact]
    public void Reflected_enum_constants_keep_their_declared_type()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        FieldInfo field = typeof(TestActions).GetField(nameof(TestActions.DefaultFlags), BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException();
        builder.Then(out Variable<TestFlags> flags, field, []);
        builder.Then(() => TestActions.ObserveFlags(flags.Val));
        TestActions.ObservedFlags.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([TestFlags.First], TestActions.ObservedFlags);
    }

    /// <summary>Verifies recorded result types remain authoring hints until the selected method binds.</summary>
    [Fact]
    public void Reflected_call_results_use_the_selected_method_type()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        MethodInfo method = Method(typeof(TestActions), nameof(TestActions.ReturnInt));
        TestActions.Observed.Clear();
        builder.Then(out Variable<string> value, method, [1]).Then(Method(typeof(TestActions), nameof(TestActions.Observe)), [value]);
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1], TestActions.Observed);
    }

    /// <summary>Verifies local loads and increments cannot observe an unassigned default value.</summary>
    [Fact]
    public void Local_values_require_definite_assignment()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Declare<int>(out var local, "local").Then(() => TestActions.Observe(local.Val));
        Assert.Throws<ExecutorBindingException>(() => builder.Build<IExecutor<IContainer>>(Configuration));
    }

    /// <summary>Verifies a produced value is not mistaken for a null reference constant during compatibility checks.</summary>
    [Fact]
    public void Produced_values_keep_their_actual_types_during_argument_validation()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then<int>(out Variable<int> value, 7);
        builder.Then(Method(typeof(TestActions), nameof(TestActions.RecordText)), [value]);
        Assert.Throws<ExecutorBindingException>(() => builder.Build<IExecutor<IContainer>>(Configuration));
    }

    /// <summary>Verifies a do/while body definitely assigns locals before its condition and following sequence.</summary>
    [Fact]
    public void Do_while_local_assignments_are_available_after_the_first_iteration()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedUInt.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        Variable<int> local;
        builder.Declare(out local, "local");
        builder.DoWhile(() => local.Val == 8, loop =>
        {
            loop.Then(local, 7);
        });
        builder.Then(() => TestActions.Observe(local.Val));
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([7], TestActions.Observed);
    }

    /// <summary>Verifies the direct value, conversion, array, reference-cast, and conditional builder operations.</summary>
    [Fact]
    public void Structural_value_operations_are_available_without_expression_trees()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedLong.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out _)
            .Then<int>(out var value, 7, name: "value")
            .Then(out var longValue, () => (long)value.Val)
            .Then(() => TestActions.ObserveLong(longValue.Val))
            .Then(out var values, () => new int[] { value.Val, 2 })
            .Then<int>(out var index, 0, name: "index")
            .Then(() => values.Val[index.Val], 9)
            .Then(out var first, () => values.Val[index.Val], name: "first")
            .Then(() => TestActions.Observe(first.Val))
            .Then<object>(out var boxed, "text")
            .Then(out var text, () => (boxed.Val as string)!)
            .Then(() => TestActions.RecordText(text.Val))
            .Then<bool>(out var condition, true)
            .Then(out var selected, () => condition.Val ? 11 : 12)
            .Then(() => TestActions.Observe(selected.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        TestActions.ObservedText.Clear();
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([7L], TestActions.ObservedLong);
        Assert.Equal([9, 11], TestActions.Observed);
        Assert.Equal(["text"], TestActions.ObservedText);
    }

    /// <summary>Verifies nullable constants and nullable-to-nullable conversions preserve both values and nulls.</summary>
    [Fact]
    public void Nullable_values_are_emitted_without_losing_their_declared_type()
    {
        RegisterHelpers();
        TestActions.ObservedNullable.Clear();
        TestActions.ObservedNullableLong.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out _)
            .Then<int?>(out var value, 7, name: "value")
            .Then(() => TestActions.RecordNullable(value.Val))
            .Then<int>(out var integer, 8)
            .Then(() => TestActions.RecordNullable(integer.Val))
            .Then(out var converted, () => (long?)value.Val)
            .Then(() => TestActions.RecordNullableLong(converted.Val))
            .Then<int?>(out var empty, (int?)null, name: "empty")
            .Then(() => TestActions.RecordNullable(empty.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([7, 8, null], TestActions.ObservedNullable);
        Assert.Equal([7], TestActions.ObservedNullableLong);
    }

    /// <summary>Verifies nullable values use boxing semantics when converted to object-compatible interfaces.</summary>
    [Fact]
    public void Nullable_values_can_be_boxed_to_reference_interfaces()
    {
        RegisterHelpers();
        TestActions.ObservedObjects.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out _)
            .Then<int?>(out var value, 7, name: "value")
            .Then(out var comparable, () => (IComparable?)value.Val, name: "comparable")
            .Then(() => TestActions.RecordObject(comparable.Val))
            .Then<int?>(out var empty, (int?)null, name: "empty")
            .Then(out var emptyComparable, () => (IComparable?)empty.Val, name: "emptyComparable")
            .Then(() => TestActions.RecordObject(emptyComparable.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([7, null], TestActions.ObservedObjects);
    }

    /// <summary>Verifies value-type specialization does not create invalid generic or array conversions.</summary>
    [Fact]
    public void Value_type_specialization_does_not_create_invalid_generic_or_array_conversions()
    {
        RegisterHelpers();
        TestActions.ColumnCount = 0;
        TestActions.ColumnArrayCount = 0;
        Configuration.RegisterProvider(typeof(Enumerable));
        var executor = ExecutorBuilder.Start<IContainer>(out var input)
            .Then(out var columns, () => input.Val.GetColumns())
            .Then(() => TestActions.RecordColumns(columns.Val))
            .Then(out var array, () => input.Val.GetColumns().ToArray())
            .Then(() => TestActions.RecordColumnArray(array.Val))
            .Build<IExecutor<GridAdapter>>(Configuration);
        FakeGrid grid = new();
        grid.AddColumn("A");
        executor.Execute(new GridAdapter(grid));
        Assert.Equal(1, TestActions.ColumnCount);
        Assert.Equal(1, TestActions.ColumnArrayCount);
    }

    /// <summary>Verifies interface results retain their declared contract when the producer returns an interface.</summary>
    [Fact]
    public void Logical_interface_results_remain_valid_without_concrete_bindings()
    {
        RegisterHelpers();
        var executor = ExecutorBuilder.Start<ILogicalReturnContainer>(out var input)
            .Then(out var column, () => input.Val.GetColumn())
            .Then(out var columns, () => new IColumn[] { column.Val })
            .Then(() => TestActions.RecordColumnArray(columns.Val))
            .Then<bool>(out var condition, true)
            .Then(out var conditionalColumn, () => condition.Val ? input.Val.GetColumn() : input.Val.GetColumn(), "conditionalColumn")
            .Then(() => TestActions.ObserveColumn(conditionalColumn.Val))
            .Then(() => TestActions.ObserveColumn(column.Val))
            .Build<IExecutor<LogicalReturnAdapter>>(Configuration);
        TestActions.LastColumn = null;
        TestActions.ColumnArrayCount = 0;
        executor.Execute(new LogicalReturnAdapter());
        Assert.IsType<LogicalOnlyColumn>(TestActions.LastColumn);
        Assert.Equal(1, TestActions.ColumnArrayCount);
        ExecutorBuilder localBuilder = ExecutorBuilder.Start<ILogicalReturnContainer>(out var localInput);
        Variable<IColumn> local;
        localBuilder.Declare(out local, "local");
        localBuilder.Then(out var localColumn, () => localInput.Val.GetColumn(), name: "localColumn");
        localBuilder.Then(local, localColumn);
        localBuilder.Then(out var secondLocalColumn, () => localInput.Val.GetColumn(), name: "secondLocalColumn");
        localBuilder.Then(local, secondLocalColumn);
        localBuilder.Then(() => TestActions.ObserveColumn(local.Val));
        localBuilder.Build<IExecutor<ILogicalReturnContainer>>(Configuration).Execute(new LogicalReturnAdapter());
    }

    /// <summary>Verifies structural bitwise and shift operations require and preserve their C# promoted result types.</summary>
    [Fact]
    public void Structural_bitwise_and_shift_operations_use_csharp_result_types()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then<byte>(out var first, (byte)3, name: "first");
        builder.Then<byte>(out var second, (byte)1, name: "second");
        builder.Then(out Variable<int> masked, () => first.Val & second.Val, name: "masked");
        builder.Then(() => TestActions.Observe(masked.Val));
        builder.Then(out Variable<int> shifted, () => first.Val << second.Val, name: "shifted");
        builder.Then(() => TestActions.Observe(shifted.Val));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1, 6], TestActions.Observed);
    }

    /// <summary>Verifies nullable equality and ordering use C# null semantics.</summary>
    [Fact]
    public void Nullable_comparisons_use_csharp_null_semantics()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then<int?>(out var empty, (int?)null, name: "empty");
        builder.Then<int?>(out var value, 2, name: "value");
        builder.Then<int?>(out var otherEmpty, (int?)null);
        builder.Then(out Variable<bool> equal, () => empty.Val == otherEmpty.Val, name: "equal");
        builder.Then(out Variable<bool> less, () => empty.Val < value.Val, name: "less");
        builder.Then(out Variable<bool> valueNotEqualToEmpty, () => value.Val != empty.Val, name: "valueNotEqualToEmpty");
        builder.Then(out Variable<bool> emptyNotEqualToValue, () => empty.Val != value.Val, name: "emptyNotEqualToValue");
        builder.If(equal, branch => branch.Then(() => TestActions.Observe(1)), branch => branch.Then(() => TestActions.Observe(2)));
        builder.If(less, branch => branch.Then(() => TestActions.Observe(3)), branch => branch.Then(() => TestActions.Observe(4)));
        builder.If(valueNotEqualToEmpty, branch => branch.Then(() => TestActions.Observe(5)), branch => branch.Then(() => TestActions.Observe(6)));
        builder.If(emptyNotEqualToValue, branch => branch.Then(() => TestActions.Observe(7)), branch => branch.Then(() => TestActions.Observe(8)));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1, 4, 5, 7], TestActions.Observed);
    }

    /// <summary>Verifies floating ordered comparisons preserve false results for NaN and invalid promotions are rejected.</summary>
    [Fact]
    public void Numeric_operations_follow_csharp_ordering_rules()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedUInt.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then<double>(out var nan, double.NaN, name: "nan");
        builder.Then<double>(out var one, 1D, name: "one");
        builder.Then(out Variable<bool> lessOrEqual, () => nan.Val <= one.Val, name: "lessOrEqual");
        builder.Then(out Variable<bool> greaterOrEqual, () => nan.Val >= one.Val, name: "greaterOrEqual");
        builder.If(lessOrEqual, branch => branch.Then(() => TestActions.Observe(1)), branch => branch.Then(() => TestActions.Observe(2)));
        builder.If(greaterOrEqual, branch => branch.Then(() => TestActions.Observe(3)), branch => branch.Then(() => TestActions.Observe(4)));
        builder.Then<uint>(out var unsigned, uint.MaxValue, name: "unsigned");
        builder.Then<char>(out var character, (char)1);
        builder.Then(out Variable<uint> sum, () => unsigned.Val + character.Val, name: "sum");
        builder.Then(() => TestActions.ObserveUInt(sum.Val));
        builder.Then<long>(out var signedLong, 1L);
        TestActions.ObservedLong.Clear();
        builder.Then(out var mixed, () => unsigned.Val + signedLong.Val);
        builder.Then(() => TestActions.ObserveLong(mixed.Val));
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([2, 4], TestActions.Observed);
        Assert.Equal([0], TestActions.ObservedUInt);
        Assert.Equal([4294967296L], TestActions.ObservedLong);
    }

    /// <summary>Verifies unsigned arithmetic promotes only the signed types required by C#.</summary>
    [Fact]
    public void Unsigned_arithmetic_uses_csharp_promotion_for_signed_and_small_types()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then<uint>(out var unsigned, uint.MaxValue, name: "unsigned");
        builder.Then<int>(out var signed, -1, name: "signed");
        builder.Then(out Variable<long> signedSum, () => unsigned.Val + signed.Val);
        builder.Then(() => TestActions.ObserveLong(signedSum.Val));
        builder.Then<byte>(out var small, (byte)1, name: "small");
        builder.Then(out Variable<uint> smallSum, () => unsigned.Val + small.Val, name: "smallSum");
        builder.Then(() => TestActions.ObserveUInt(smallSum.Val));
        TestActions.ObservedLong.Clear();
        TestActions.ObservedUInt.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([(long)uint.MaxValue - 1], TestActions.ObservedLong);
        Assert.Equal([0], TestActions.ObservedUInt);
    }

    /// <summary>Verifies a value-type instance can call a method declared by a reference base type.</summary>
    [Fact]
    public void Value_type_instance_calls_box_for_reference_declared_methods()
    {
        RegisterHelpers();
        MethodInfo toString = typeof(object).GetMethod(nameof(object.ToString), []) ?? throw new InvalidOperationException();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out _)
            .Then<MutableValue>(out var value, new MutableValue { Number = 12 })
            .Then<string>(out var text, toString, [value])
            .Then(() => TestActions.RecordText(text.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        TestActions.ObservedText.Clear();
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Single(TestActions.ObservedText);
        Assert.NotNull(TestActions.ObservedText[0]);
    }

    /// <summary>Verifies increments fall back to a writable logical member when a concrete shadow is read-only.</summary>
    [Fact]
    public void Concrete_read_only_members_fall_back_for_increment_operations()
    {
        Configuration = new ExecutorBuildConfiguration();
        PropertyInfo value = Property<IWritableValue>(nameof(IWritableValue.Value));
        var executor = ExecutorBuilder.Start<IWritableValue>(out var input).Then(() => input.Val.Value, () => input.Val.Value + 1).Build<IExecutor<ReadOnlyShadow>>(Configuration);
        ReadOnlyShadow shadow = new();
        executor.Execute(shadow);
        Assert.Equal(1, shadow.Value);
    }

    /// <summary>Verifies failed expression normalization does not leave partially imported plan nodes behind.</summary>
    [Fact]
    public void Failed_expression_normalization_is_transactional()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var input);
        builder.Then(() => input.Val.SetTitle("before"));
        int before = builder.Nodes.Count();
        Expression<Action<IContainer>> unsupported = value => TestActions.Observe(value.Title.Length + new int[value.Title.Length].Length);
        Assert.Throws<NotSupportedException>(() => builder.ThenExpression(unsupported, input));
        Assert.Equal(before, builder.Nodes.Count());
        FakeGrid grid = new();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal("before", grid.Title);
    }

    /// <summary>Verifies a read-only same-named concrete member cannot shadow a writable logical member.</summary>
    [Fact]
    public void Concrete_read_only_members_fall_back_to_logical_writers()
    {
        Configuration = new ExecutorBuildConfiguration();
        var executor = ExecutorBuilder.Start<IWritableValue>(out var input).Then(() => input.Val.Value, 5).Build<IExecutor<ReadOnlyShadow>>(Configuration);
        ReadOnlyShadow shadow = new();
        executor.Execute(shadow);
        Assert.Equal(5, shadow.Value);
    }

    /// <summary>Verifies reference type-as expressions return a null result instead of throwing.</summary>
    [Fact]
    public void Type_as_expressions_return_the_expected_reference_value()
    {
        RegisterHelpers();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out _)
            .Then(out Variable<object> value, () => TestActions.GetObject())
            .Then(out Variable<string?> text, () => value.Val as string)
            .Then(() => TestActions.RecordText(text.Val))
            .Build<IExecutor<IContainer>>(Configuration);
        TestActions.ObservedText.Clear();
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal(["typed"], TestActions.ObservedText);
    }

    /// <summary>Verifies equal reference constants remain distinct plan operands.</summary>
    [Fact]
    public void Equal_reference_constants_are_not_merged()
    {
        RegisterHelpers();
        EqualToken first = new(1);
        EqualToken second = new(2);
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        MethodInfo record = Method(typeof(TestActions), nameof(TestActions.RecordToken));
        builder.Then(record, [first]);
        builder.Then(record, [second]);
        TestActions.ObservedTokens.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Collection(TestActions.ObservedTokens, value => Assert.Same(first, value), value => Assert.Same(second, value));
    }

    /// <summary>Verifies distinct equal mutable value constants are copied before instance calls.</summary>
    [Fact]
    public void Equal_mutable_value_constants_are_not_shared()
    {
        RegisterHelpers();
        MutableToken first = new();
        MutableToken second = new();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        MethodInfo increment = Method<MutableToken>(nameof(MutableToken.Increment));
        builder.Then(increment, [first]);
        builder.Then(increment, [second]);
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1, 1], TestActions.Observed);
    }

    /// <summary>Verifies by-reference calls reject constants before dynamic IL is emitted.</summary>
    [Fact]
    public void By_reference_method_operands_require_variables()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        MethodInfo method = Method(typeof(TestActions), nameof(TestActions.UpdateByReference));
        Assert.Throws<ArgumentException>(() => builder.Then(method, [0]));
    }

    /// <summary>Verifies delegate event subscriptions use the same idempotent policy as method subscriptions.</summary>
    [Fact]
    public void Delegate_event_subscriptions_are_idempotent()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        EventHandler handler = TestActions.OnChanged;
        builder.Then(() => TestActions.ApplyChanged(input.Val, handler));
        TestActions.EventCalls = 0;
        FakeGrid grid = new();
        IExecutor<IContainer> executor = builder.Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(grid));
        executor.Execute(new GridAdapter(grid));
        grid.RaiseChanged();
        Assert.Equal(1, TestActions.EventCalls);
    }

    /// <summary>Verifies a same-named incompatible concrete member does not hide the logical member.</summary>
    [Fact]
    public void Incompatible_concrete_members_fall_back_to_the_logical_member()
    {
        Configuration = new ExecutorBuildConfiguration();
        ExecutorBuilder builder = ExecutorBuilder.Start<IShadow>(out Variable<IShadow> input);
        builder.Then(() => input.Val.Number, 5);
        var executor = builder.Build<IExecutor<ShadowAdapter>>(Configuration);
        ShadowAdapter shadow = new();
        executor.Execute(shadow);
        Assert.Equal(5, shadow.LogicalNumber);
        Assert.Null(shadow.Text);
    }

    /// <summary>Verifies expression arrays, array indexing, and user-defined operators are normalized into executable nodes.</summary>
    [Fact]
    public void Expressions_support_arrays_and_user_defined_operators()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> input);
        builder.Then(() => input.Val.SetTitle("array"));
        builder.Then(() => TestActions.RecordText(new[] { input.Val.Title, "tail" }[1]));
        builder.Then(out Variable<CustomNumber> first, () => new CustomNumber(2), name: "first");
        builder.Then(out Variable<CustomNumber> second, () => new CustomNumber(3), name: "second");
        builder.Then(out Variable<CustomNumber> total, () => first.Val + second.Val, name: "total");
        builder.Then(() => TestActions.RecordCustom(total.Val));
        TestActions.ObservedText.Clear();
        TestActions.ObservedCustom.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal(["tail"], TestActions.ObservedText);
        Assert.Equal([5], TestActions.ObservedCustom.Select(value => value.Value));
    }

    /// <summary>Verifies return stops the current executor sequence without affecting later plan construction.</summary>
    [Fact]
    public void Return_stops_emission_at_runtime()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(() => TestActions.Observe(1));
        builder.Return();
        builder.Then(() => TestActions.Observe(2));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1], TestActions.Observed);
    }

    /// <summary>Verifies a foreach body can return from the complete executor.</summary>
    [Fact]
    public void ForEach_body_can_return_from_executor()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var container).Then(out var columns, () => container.Val.GetColumns()).ForEach(columns, (loop, item) =>
        {
            loop.Then(out var width, () => item.Val.Width);
            loop.If(() => width.Val == 0, branch => branch.Return());
            loop.Then(() => TestActions.Observe(1));
        }).Then(() => TestActions.Observe(2)).Build<IExecutor<IContainer>>(Configuration);
        FakeGrid grid = new();
        grid.AddColumn("A");
        grid.AddColumn("B").Width = 10;
        executor.Execute(new GridAdapter(grid));
        Assert.Empty(TestActions.Observed);
    }

    /// <summary>Verifies structural logical values short circuit their right sequence.</summary>
    [Fact]
    public void Structural_logical_values_short_circuit()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(out Variable<bool> left, () => false, name: "left");
        builder.Then(out var result, () => left.Val && TestActions.ObserveAndReturn(3));
        TestActions.Observed.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Empty(TestActions.Observed);
    }

    /// <summary>Verifies cloning remains independently editable.</summary>
    [Fact]
    public void Plans_can_be_cloned_and_remain_independently_editable()
    {
        RegisterHelpers();
        ExecutorBuilder first = ExecutorBuilder.Start<IContainer>(out var container);
        first.Then(() => TestActions.Observe(1));
        ExecutorBuilder clone = first.Clone();
        ExecutorNodeInfo removable = clone.Nodes.First(node => node.Name == nameof(TestActions.Observe));
        clone.Remove(removable);
        Assert.Single(first.Nodes);
        Assert.Empty(clone.Nodes);
        TestActions.Observed.Clear();
        first.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([1], TestActions.Observed);
    }

    [Fact]
    public void Implemented_interfaces_remain_callable_without_explicit_method_mappings()
    {
        Configuration = new ExecutorBuildConfiguration();
        MethodInfo logical = Method<IExplicitContract>(nameof(IExplicitContract.Apply));
        ExecutorBuilder builder = ExecutorBuilder.Start<IExplicitContract>(out Variable<IExplicitContract> input);
        builder.Then(logical, [input, "value"]);
        ExplicitAdapter adapter = new();
        builder.Build<IExecutor<ExplicitAdapter>>(Configuration).Execute(adapter);
        Assert.Equal("logical:value", adapter.Value);
    }

    /// <summary>Verifies reflection operands retain their actual type for numeric IL conversion.</summary>
    [Fact]
    public void Reflection_operands_convert_numeric_constants_at_emission()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(Method(typeof(TestActions), nameof(TestActions.RecordShort)), [300]);
        TestActions.ObservedShort.Clear();
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([(short)300], TestActions.ObservedShort);
    }

    /// <summary>Verifies unnamed variables remain independent and explicit names are unique.</summary>
    [Fact]
    public void Variables_can_be_named_and_duplicate_names_are_rejected()
    {
        ExecutorBuilder inputs = ExecutorBuilder.Start<IContainer, IContainer>(out Variable<IContainer> first, out Variable<IContainer> second, "first", "second");
        Assert.Equal("first", first.Name);
        Assert.Equal("second", second.Name);
        ExecutorBuilder duplicateInputs = new(typeof(IContainer), typeof(IContainer));
        duplicateInputs.Input<IContainer>(0);
        Assert.NotSame(duplicateInputs.Input<IContainer>(0), duplicateInputs.Input<IContainer>(1));
        ExecutorBuilder calls = ExecutorBuilder.Start<IContainer>(out Variable<IContainer> container);
        calls.Then(out Variable<IColumn> firstColumn, () => container.Val.GetOrAddColumn("First"), "firstColumn");
        calls.Then(out Variable<IColumn> secondColumn, () => container.Val.GetOrAddColumn("Second"), "secondColumn");
        Assert.Equal("firstColumn", firstColumn.Name);
        Assert.Equal("secondColumn", secondColumn.Name);
        calls.Then(out Variable<IColumn> unnamedColumn, () => container.Val.GetOrAddColumn("Unnamed"));
        calls.Then(out Variable<IColumn> anotherColumn, () => container.Val.GetOrAddColumn("Another"));
        Assert.NotSame(unnamedColumn, anotherColumn);
        Variable<int> counter;
        calls.Declare(out counter, "counter");
        Assert.Equal("counter", counter.Name);
        calls.Declare(out Variable<int> duplicate, "counter").Then(counter, 1).Then(duplicate, 2);
        Assert.Throws<InvalidOperationException>(() => calls.Build<IExecutor<IContainer>>(Configuration));
    }

    /// <summary>Verifies mutable variables remain direct references when passed through the reflection call API.</summary>
    [Fact]
    public void Mutable_variables_remain_direct_references_for_reflection_calls()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        Variable<int> counter;
        builder.Declare(out counter, "counter");
        builder.Then(counter, 4).Then(Method(typeof(TestActions), nameof(TestActions.Observe)), [counter]);
        Assert.True(builder.TryGetVariable<int>("counter", out Variable<int>? namedCounter));
        Assert.Equal("counter", namedCounter?.Name);
        Assert.DoesNotContain(builder.Nodes, node => node.Kind == "LocalLoad");
        builder.Build<IExecutor<IContainer>>(Configuration).Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([4], TestActions.Observed);
    }

    /// <summary>Verifies a builder round-trips through JSON and remains executable.</summary>
    [Fact]
    public void Builders_round_trip_through_json()
    {
        RegisterHelpers();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var container).Then(out var column, () => container.Val.GetOrAddColumn("Json")).Then(() => column.Val.Width, 175);
        string json = ExecutorJson.Serialize(builder);
        Assert.Contains("\"variables\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"values\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"locals\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("executorType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("returnType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("static", json, StringComparison.Ordinal);
        Assert.DoesNotContain("elementType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("arrayType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("targetType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("itemType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":\"value\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":\"variable\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":{\"kind\"", json, StringComparison.Ordinal);
        ExecutorBuilder restored = ExecutorJson.Deserialize(json);
        FakeGrid grid = new();
        restored.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal(175, grid.GetColumn("Json").Width);
    }

    /// <summary>Verifies JSON reconstructs operation types that are derived from logical values and members.</summary>
    [Fact]
    public void Json_round_trip_reconstructs_derived_operation_types()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedLong.Clear();
        TestActions.ObservedText.Clear();
        TestActions.LastColumn = null;
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var container)
            .Then<int>(out var value, 7, name: "value")
            .Then(out var longValue, () => (long)value.Val)
            .Then(() => TestActions.ObserveLong(longValue.Val))
            .Then(out var values, () => new int[] { value.Val, 2 })
            .Then<int>(out var index, 0, name: "index")
            .Then(() => values.Val[index.Val], 9)
            .Then(out var first, () => values.Val[index.Val], name: "first")
            .Then(() => TestActions.Observe(first.Val))
            .Then<string>(out var textValue, "text", name: "textValue")
            .Then(out var text, () => textValue.Val as string, name: "text")
            .Then(() => TestActions.RecordText(text.Val))
            .Then(out var columns, () => container.Val.GetColumns(), name: "columns").ForEach(columns, (loop, item) => loop.Then(() => TestActions.ObserveColumn(item.Val)));
        string json = ExecutorJson.Serialize(builder);
        ExecutorBuilder restored = ExecutorJson.Deserialize(json);
        FakeGrid grid = new();
        grid.AddColumn("A");
        grid.AddColumn("B");
        restored.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(grid));
        Assert.Equal([7L], TestActions.ObservedLong);
        Assert.Equal([9], TestActions.Observed);
        Assert.Equal(["text"], TestActions.ObservedText);
        Assert.NotNull(TestActions.LastColumn);
    }

    /// <summary>Verifies JSON preserves mutable locals, nested control flow, and event metadata.</summary>
    [Fact]
    public void Json_round_trip_preserves_nested_control_flow_and_events()
    {
        RegisterHelpers();
        TestActions.EventCalls = 0;
        TestActions.Observed.Clear();
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out var container);
        Variable<int> index;
        builder.Declare(out index, "index");
        builder.Then(index, 0).Then(() => TestActions.PrepareChanged(container.Val)).While(() => index.Val < 1, loop => loop.Then(() => TestActions.Observe(index.Val)).Then(index, () => index.Val + 1));
        string json = ExecutorJson.Serialize(builder);
        Assert.DoesNotContain("executorType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("returnType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("static", json, StringComparison.Ordinal);
        Assert.DoesNotContain("elementType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("arrayType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("targetType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("itemType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":\"value\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":\"variable\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":{\"kind\"", json, StringComparison.Ordinal);
        ExecutorBuilder restored = ExecutorJson.Deserialize(json);
        FakeGrid grid = new();
        restored.Build<IExecutor<GridAdapter>>(Configuration).Execute(new GridAdapter(grid));
        grid.RaiseChanged();
        Assert.Equal([0], TestActions.Observed);
        Assert.Equal(1, TestActions.EventCalls);
    }

    /// <summary>Verifies JSON serialization rejects external instance-dependent constants.</summary>
    [Fact]
    public void Json_serialization_rejects_external_instance_constants()
    {
        ExecutorBuilder builder = ExecutorBuilder.Start<IContainer>(out _);
        builder.Then(Method(typeof(TestActions), nameof(TestActions.RecordToken)), [new EqualToken(7)]);
        Assert.Throws<NotSupportedException>(() => ExecutorJson.Serialize(builder));
    }

    /// <summary>Verifies direct reflection calls retain constants and flow variables as raw operands until build.</summary>
    [Fact]
    public void Reflection_calls_resolve_mixed_raw_operands_at_build_time()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        TestActions.ObservedText.Clear();
        TestActions.ObservedTokens.Clear();
        DateTime timestamp = new(2026, 9, 6, 12, 30, 0, DateTimeKind.Utc);
        EqualToken user = new(7);
        var executor = ExecutorBuilder.Start<IContainer, DateTime>(out _, out var date)
            .Then(Method(typeof(TestActions), nameof(TestActions.RecordMixed), typeof(int), typeof(string), typeof(DateTime), typeof(EqualToken)), [1, "String", date, user])
            .Build<IExecutor<GridAdapter, DateTime>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()), timestamp);
        Assert.Equal([1], TestActions.Observed);
        Assert.Equal([$"String:{timestamp:O}"], TestActions.ObservedText);
        Assert.Same(user, TestActions.ObservedTokens.Single());
    }

    /// <summary>Verifies reflection calls can create their result variable without a separate declaration.</summary>
    [Fact]
    public void Reflection_call_creates_result_variable_with_out_overload()
    {
        RegisterHelpers();
        TestActions.Observed.Clear();
        IExecutor<IContainer> executor = ExecutorBuilder.Start<IContainer>(out var input)
            .Then<MutableValue>(out var value, Method(typeof(TestActions), nameof(TestActions.CreateMutableValue)), [], "created")
            .Then(() => TestActions.Observe(value.Val.Number))
            .Build<IExecutor<IContainer>>(Configuration);
        executor.Execute(new GridAdapter(new FakeGrid()));
        Assert.Equal([0], TestActions.Observed);
        Assert.Equal("created", value.Name);
    }

    private static void RegisterHelpers()
    {
        Configuration.RegisterProvider(typeof(GridAdapter));
    }

    private static ConstructorInfo Constructor<TConcrete, TSource>() => typeof(TConcrete).GetConstructor([typeof(TSource)]) ?? throw new InvalidOperationException();
    private static MethodInfo Method<T>(string name) => Method(typeof(T), name);
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException();
    private static MethodInfo Method(Type type, string name, params Type[] parameterTypes) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null) ?? throw new InvalidOperationException();
    private static PropertyInfo Property<T>(string name) => typeof(T).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException();
    private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException();
    /// <summary>Logical container contract used by the executor examples.</summary>
    public interface IContainer
    {
        /// <summary>Gets or sets whether the container is enabled.</summary>
        bool Enabled
        {
            get; set;
        }

        /// <summary>Gets or sets the title.</summary>
        string Title
        {
            get; set;
        }

        /// <summary>Gets or adds one logical column.</summary>
        IColumn GetOrAddColumn(string name);
        /// <summary>Gets all logical columns.</summary>
        IEnumerable<IColumn> GetColumns();
        /// <summary>Gets whether more loop work exists.</summary>
        bool HasMore();
        /// <summary>Advances the loop state.</summary>
        void Advance();
        /// <summary>Sets enabled state.</summary>
        void SetEnabled(bool value);
        /// <summary>Sets the title.</summary>
        void SetTitle(string value);
        /// <summary>Uses a column.</summary>
        void Use(IColumn column);
        /// <summary>Signals a change.</summary>
        event EventHandler Changed;
    }

    /// <summary>Logical display target used to test type and member metadata.</summary>
    public interface IGridView
    {
        /// <summary>Gets or sets whether the generated view is read-only.</summary>
        bool ReadOnly
        {
            get; set;
        }

        /// <summary>Gets or creates a column by its logical member name.</summary>
        IGridColumn GetOrAddColumn(string name);
        /// <summary>Marks one column as the primary metadata-selected column.</summary>
        void UseColumn(IGridColumn column);
        /// <summary>Refreshes the view.</summary>
        void Refresh();
    }

    /// <summary>Logical display column used to test member metadata.</summary>
    public interface IGridColumn
    {
        /// <summary>Gets or sets the displayed label.</summary>
        string Label
        {
            get; set;
        }

        /// <summary>Gets or sets the displayed width.</summary>
        int Width
        {
            get; set;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GridDefaultsAttribute : Attribute;
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class GridColumnAttribute(string label, int width) : Attribute
    {
        public string Label { get; } = label;
        public int Width { get; } = width;
    }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UseColumnAttribute(string memberName) : Attribute
    {
        public string MemberName { get; } = memberName;
    }
    [GridDefaults]
    public sealed class MetadataUser
    {
        [GridColumn("User name", 180)]
        public string Name { get; set; } = "";
        [GridColumn("Enabled", 80), UseColumn(nameof(Name))]
        public bool Enabled
        {
            get; set;
        }
    }
    public static ExecutorScope RecordGridMetadata(Type type)
    {
        var builder = ExecutorBuilder.Start().Reference<IGridView>(out var target);
        if (type.IsDefined(typeof(GridDefaultsAttribute)))
        {
            builder.Then(() => target.Val.ReadOnly, true);
        }
        foreach (var property in type.GetProperties())
        {
            if (property.GetCustomAttribute<GridColumnAttribute>() is { } attribute)
            {
                string label = attribute.Label;
                int width = attribute.Width;
                builder.Then(out var column, () => target.Val.GetOrAddColumn(property.Name), property.Name);
                builder.Then(() => column.Val.Label, label);
                builder.Then(() => column.Val.Width, width);
            }
            if (property.GetCustomAttribute<UseColumnAttribute>() is { } use)
            {
                builder.Reference<IGridColumn>(use.MemberName, out var column);
                builder.Then(() => target.Val.UseColumn(column.Val));
            }
        }
        return builder.ToScope();
    }

    /// <summary>Concrete grid target used by the metadata tests.</summary>
    public sealed class TestGridView : IGridView
    {
        private readonly Dictionary<string, TestGridColumn> columns = [];
        /// <summary>Gets or sets whether the view is read-only.</summary>
        public bool ReadOnly
        {
            get; set;
        }
        /// <summary>Gets the number of refresh calls.</summary>
        public int RefreshCount
        {
            get; private set;
        }
        /// <summary>Gets the metadata-selected primary column.</summary>
        public IGridColumn? PrimaryColumn
        {
            get; private set;
        }

        /// <summary>Gets or creates a concrete test column.</summary>
        public IGridColumn GetOrAddColumn(string name)
        {
            if (!columns.TryGetValue(name, out TestGridColumn? column))
            {
                column = new(name);
                columns.Add(name, column);
            }

            return column;
        }

        /// <summary>Stores the metadata-selected primary column.</summary>
        public void UseColumn(IGridColumn column) => PrimaryColumn = column;
        /// <summary>Records a refresh call.</summary>
        public void Refresh() => RefreshCount++;
    }

    /// <summary>Concrete grid column used by the metadata tests.</summary>
    public sealed class TestGridColumn : IGridColumn
    {
        /// <summary>Creates a test column.</summary>
        public TestGridColumn(string name) => Name = name;
        /// <summary>Gets the logical column name.</summary>
        public string Name
        {
            get;
        }
        /// <summary>Gets or sets the displayed label.</summary>
        public string Label { get; set; } = "";
        /// <summary>Gets or sets the displayed width.</summary>
        public int Width
        {
            get; set;
        }
    }

    /// <summary>Logical instance method surface used to test parameter specialization ordering.</summary>
    public class LogicalActions
    {
        /// <summary>Gets the selected overload marker.</summary>
        public int Selected
        {
            get; protected set;
        }

        /// <summary>Handles the logical call.</summary>
        public virtual void Apply(IColumn column, object marker) => Selected = 3;
    }

    /// <summary>Concrete instance methods with conflicting parameter specialization.</summary>
    public sealed class SpecializedActions : LogicalActions
    {
        /// <summary>Handles the first-argument specialization.</summary>
        public void Apply(GridColumnAdapter column, object marker) => Selected = 1;
        /// <summary>Handles the second-argument specialization.</summary>
        public void Apply(object column, GridMarker marker) => Selected = 2;
    }

    /// <summary>Marker type used as the second specialized method argument.</summary>
    public sealed class GridMarker;
    /// <summary>Logical selector used to test result compatibility during specialization.</summary>
    public class LogicalSelector
    {
        /// <summary>Gets the selected method marker.</summary>
        public int Selected
        {
            get; protected set;
        }

        /// <summary>Selects a logical column.</summary>
        public virtual IColumn Select(IColumn value)
        {
            Selected = 3;
            return value;
        }
    }

    /// <summary>Concrete selector with an incompatible specialized return.</summary>
    public sealed class SpecializedSelector : LogicalSelector
    {
        /// <summary>Selects a concrete column with an unrelated return type.</summary>
        public UnrelatedResult Select(GridColumnAdapter value)
        {
            Selected = 1;
            return new();
        }
    }

    /// <summary>Return type unrelated to the logical selector result.</summary>
    public sealed class UnrelatedResult;
    /// <summary>Logical static method surface used to test static specialization.</summary>
    public class LogicalStaticActions
    {
        /// <summary>Gets the selected static method marker.</summary>
        public static int Selected
        {
            get; set;
        }

        /// <summary>Handles the logical static call.</summary>
        public static void Apply(IColumn column, object marker) => Selected = 3;
    }

    /// <summary>Concrete static methods with conflicting parameter specialization.</summary>
    public sealed class SpecializedStaticActions : LogicalStaticActions
    {
        /// <summary>Handles the first-argument specialization.</summary>
        public static void Apply(GridColumnAdapter column, object marker) => Selected = 1;
        /// <summary>Handles the second-argument specialization.</summary>
        public static void Apply(object column, GridMarker marker) => Selected = 2;
    }

    /// <summary>Derived executor contract used to verify custom interface preservation.</summary>
    public interface IContainerExecutor : IExecutor<IContainer>;
    /// <summary>Logical member contract used to test incompatible concrete members.</summary>
    public interface IShadow
    {
        /// <summary>Gets or sets the logical number.</summary>
        int Number
        {
            get; set;
        }
    }

    /// <summary>Writable contract used to test fallback from a read-only concrete member.</summary>
    public interface IWritableValue
    {
        /// <summary>Gets or sets the value.</summary>
        int Value
        {
            get; set;
        }
    }

    /// <summary>Logical container whose result implementation is intentionally not the registered column concrete type.</summary>
    public interface ILogicalReturnContainer
    {
        /// <summary>Gets a logical column result.</summary>
        IColumn GetColumn();
    }

    /// <summary>Logical method contract used to test explicit method bindings.</summary>
    public interface IExplicitContract
    {
        /// <summary>Applies one logical value.</summary>
        void Apply(string value);
    }

    /// <summary>Logical column contract used for nested specialization.</summary>
    public interface IColumn
    {
        /// <summary>Gets or sets the width.</summary>
        int Width
        {
            get; set;
        }

        /// <summary>Gets the repository.</summary>
        IRepository Repository
        {
            get;
        }

        /// <summary>Sets the width.</summary>
        void SetWidth(int value);
    }

    /// <summary>Logical repository contract used for nested specialization.</summary>
    public interface IRepository
    {
        /// <summary>Gets or sets read-only state.</summary>
        bool ReadOnly
        {
            get; set;
        }
    }

    /// <summary>Mutable backing object representing a technology container.</summary>
    public sealed class FakeGrid
    {
        private readonly Dictionary<string, FakeColumn> _columns = new(StringComparer.Ordinal);
        /// <summary>Gets or sets enabled state.</summary>
        public bool Enabled
        {
            get; set;
        }
        /// <summary>Gets or sets the title.</summary>
        public string Title { get; set; } = "start";
        /// <summary>Gets the number of calls through logical interface dispatch.</summary>
        public int InterfaceCalls
        {
            get; set;
        }
        /// <summary>Gets the number of calls through concrete dispatch.</summary>
        public int ConcreteCalls
        {
            get; set;
        }
        /// <summary>Gets the current loop position.</summary>
        public int Position
        {
            get; set;
        }
        /// <summary>Gets the number of loop-condition calls.</summary>
        public int HasMoreCalls
        {
            get; set;
        }

        /// <summary>Gets the changed event backing delegate.</summary>
        public event EventHandler? Changed;
        /// <summary>Gets or creates a backing column.</summary>
        public FakeColumn GetColumn(string name) => _columns.TryGetValue(name, out FakeColumn? column) ? column : _columns[name] = new(name);
        /// <summary>Adds a backing column.</summary>
        public FakeColumn AddColumn(string name) => GetColumn(name);
        /// <summary>Gets all backing columns.</summary>
        public IEnumerable<FakeColumn> GetColumns() => _columns.Values;
        /// <summary>Gets whether the loop has more positions.</summary>
        public bool HasMore() => Position < 3;
        /// <summary>Raises the changed event.</summary>
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Concrete type with an incompatible same-named public member.</summary>
    public sealed class ShadowAdapter : IShadow
    {
        /// <summary>Gets or sets a deliberately incompatible public property.</summary>
        public string? Text
        {
            get; set;
        }
        /// <summary>Gets or sets a deliberately incompatible same-named property.</summary>
        public string? Number
        {
            get; set;
        }

        int IShadow.Number
        {
            get; set;
        }
        /// <summary>Gets the logical number through the public test view.</summary>
        public int LogicalNumber => ((IShadow)this).Number;
    }

    /// <summary>Concrete value with a read-only same-named member and an explicit writable contract.</summary>
    public sealed class ReadOnlyShadow : IWritableValue
    {
        private int _value;
        /// <summary>Gets the concrete read-only value.</summary>
        public int Value => _value;

        int IWritableValue.Value
        {
            get => _value; set => _value = value;
        }
    }

    /// <summary>Concrete container that returns a valid but non-registered logical column implementation.</summary>
    public sealed class LogicalReturnAdapter : ILogicalReturnContainer
    {
        /// <summary>Gets a logical-only column.</summary>
        public IColumn GetColumn() => new LogicalOnlyColumn();
    }

    /// <summary>Logical-only column used to verify unchanged root inputs.</summary>
    public sealed class LogicalOnlyColumn : IColumn
    {
        /// <summary>Gets or sets the width.</summary>
        public int Width
        {
            get; set;
        }
        /// <summary>Gets a repository for the column.</summary>
        public IRepository Repository { get; } = new LogicalOnlyRepository();

        /// <summary>Sets the width.</summary>
        public void SetWidth(int value) => Width = value;
    }

    /// <summary>Logical-only repository used by the logical-only column.</summary>
    public sealed class LogicalOnlyRepository : IRepository
    {
        /// <summary>Gets or sets read-only state.</summary>
        public bool ReadOnly
        {
            get; set;
        }
    }

    public sealed class ExplicitAdapter : IExplicitContract
    {
        /// <summary>Gets the applied value.</summary>
        public string? Value
        {
            get; private set;
        }

        void IExplicitContract.Apply(string value) => Value = $"logical:{value}";
    }

    /// <summary>Mutable backing object representing a technology column.</summary>
    public sealed class FakeColumn
    {
        /// <summary>Creates a backing column.</summary>
        public FakeColumn(string name) => Name = name;
        /// <summary>Gets the column name.</summary>
        public string Name
        {
            get;
        }
        /// <summary>Gets or sets width.</summary>
        public int Width
        {
            get; set;
        }
        /// <summary>Gets the repository.</summary>
        public FakeRepository Repository { get; } = new();
        /// <summary>Gets whether the column was used.</summary>
        public bool Used
        {
            get; set;
        }
    }

    /// <summary>Mutable backing object representing a technology repository.</summary>
    public sealed class FakeRepository
    {
        /// <summary>Gets or sets read-only state.</summary>
        public bool ReadOnly
        {
            get; set;
        }
    }

    /// <summary>Concrete value-type container adapter.</summary>
    public readonly struct GridAdapter : IContainer
    {
        private readonly FakeGrid _grid;
        /// <summary>Creates a container adapter.</summary>
        public GridAdapter(FakeGrid grid) => _grid = grid;
        /// <summary>Creates a container adapter through a static factory.</summary>
        public static GridAdapter Create(FakeGrid grid) => new(grid);
        /// <summary>Gets or sets enabled state through concrete dispatch.</summary>
        public bool Enabled
        {
            get => _grid.Enabled;
            set
            {
                _grid.ConcreteCalls++;
                _grid.Enabled = value;
            }
        }

        bool IContainer.Enabled
        {
            get => Enabled;
            set
            {
                _grid.InterfaceCalls++;
                Enabled = value;
            }
        }

        /// <summary>Gets or sets the title through concrete dispatch.</summary>
        public string Title
        {
            get => _grid.Title;
            set
            {
                _grid.ConcreteCalls++;
                _grid.Title = value;
            }
        }

        string IContainer.Title
        {
            get => Title;
            set
            {
                _grid.InterfaceCalls++;
                Title = value;
            }
        }

        /// <summary>Gets a concrete column adapter.</summary>
        public GridColumnAdapter GetOrAddColumn(string name) => new(_grid.GetColumn(name));
        IColumn IContainer.GetOrAddColumn(string name)
        {
            _grid.InterfaceCalls++;
            return GetOrAddColumn(name);
        }

        /// <summary>Gets concrete column adapters.</summary>
        public IEnumerable<GridColumnAdapter> GetColumns() => _grid.GetColumns().Select(x => new GridColumnAdapter(x));
        IEnumerable<IColumn> IContainer.GetColumns() => GetColumns().Cast<IColumn>();
        /// <summary>Gets loop state through concrete dispatch.</summary>
        public bool HasMore()
        {
            _grid.HasMoreCalls++;
            return _grid.HasMore();
        }

        bool IContainer.HasMore()
        {
            _grid.InterfaceCalls++;
            return HasMore();
        }

        /// <summary>Advances through concrete dispatch.</summary>
        public void Advance()
        {
            _grid.ConcreteCalls++;
            _grid.Position++;
        }

        void IContainer.Advance()
        {
            _grid.InterfaceCalls++;
            Advance();
        }

        /// <summary>Sets enabled state through concrete dispatch.</summary>
        public void SetEnabled(bool value)
        {
            _grid.ConcreteCalls++;
            _grid.Enabled = value;
        }

        void IContainer.SetEnabled(bool value)
        {
            _grid.InterfaceCalls++;
            SetEnabled(value);
        }

        /// <summary>Sets title through concrete dispatch.</summary>
        public void SetTitle(string value)
        {
            _grid.ConcreteCalls++;
            _grid.Title = value;
        }

        void IContainer.SetTitle(string value)
        {
            _grid.InterfaceCalls++;
            SetTitle(value);
        }

        /// <summary>Uses a concrete column adapter.</summary>
        public void Use(GridColumnAdapter column)
        {
            _grid.ConcreteCalls++;
            _grid.GetColumn(column.Name).Used = true;
        }

        void IContainer.Use(IColumn column)
        {
            _grid.InterfaceCalls++;
            Use((GridColumnAdapter)column);
        }

        /// <summary>Bridges the changed event.</summary>
        public event EventHandler Changed { add => _grid.Changed += value; remove => _grid.Changed -= value; }
    }

    /// <summary>Concrete value-type column adapter.</summary>
    public readonly struct GridColumnAdapter : IColumn
    {
        private readonly FakeColumn _column;
        /// <summary>Creates a column adapter.</summary>
        public GridColumnAdapter(FakeColumn column) => _column = column;
        /// <summary>Gets the backing name.</summary>
        public string Name => _column.Name;
        /// <summary>Gets or sets width through concrete dispatch.</summary>
        public int Width
        {
            get => _column.Width; set => _column.Width = value;
        }

        int IColumn.Width
        {
            get => Width; set => Width = value;
        }
        /// <summary>Gets the concrete repository adapter.</summary>
        public GridRepositoryAdapter Repository => new(_column.Repository);

        IRepository IColumn.Repository => Repository;

        /// <summary>Sets width through concrete dispatch.</summary>
        public void SetWidth(int value) => Width = value;
        void IColumn.SetWidth(int value) => SetWidth(value);
    }

    /// <summary>Concrete value-type repository adapter.</summary>
    public readonly struct GridRepositoryAdapter : IRepository
    {
        private readonly FakeRepository _repository;
        /// <summary>Creates a repository adapter.</summary>
        public GridRepositoryAdapter(FakeRepository repository) => _repository = repository;
        /// <summary>Gets or sets read-only state.</summary>
        public bool ReadOnly
        {
            get => _repository.ReadOnly; set => _repository.ReadOnly = value;
        }
    }

    /// <summary>Concrete object used to test constructor calls.</summary>
    public sealed class CreatedValue
    {
        /// <summary>Gets the last recorded value.</summary>
        public static string? LastValue
        {
            get; private set;
        }

        /// <summary>Creates a value.</summary>
        public CreatedValue(string value) => Value = value;
        /// <summary>Gets the value.</summary>
        public string Value
        {
            get;
        }

        /// <summary>Records the value for the test.</summary>
        public void Record() => LastValue = Value;
    }

    /// <summary>First value in a chained source adaptation.</summary>
    public sealed class ChainLevelOne
    {
        /// <summary>Creates the first chained value.</summary>
        public ChainLevelOne(int value) => Value = value;
        /// <summary>Gets the chained value.</summary>
        public int Value
        {
            get;
        }
    }

    /// <summary>Logical intermediate type in a chained source adaptation.</summary>
    public interface IChainLevelTwo
    {
        /// <summary>Gets the chained value.</summary>
        int Value
        {
            get;
        }
    }

    /// <summary>Concrete intermediate type in a chained source adaptation.</summary>
    public sealed class ChainLevelTwo : IChainLevelTwo
    {
        /// <summary>Creates the intermediate value.</summary>
        public ChainLevelTwo(ChainLevelOne value) => Value = value.Value;
        /// <summary>Gets the chained value.</summary>
        public int Value
        {
            get;
        }
    }

    /// <summary>Final value in a chained source adaptation.</summary>
    public sealed class ChainLevelThree
    {
        /// <summary>Creates the final chained value.</summary>
        public ChainLevelThree(IChainLevelTwo value) => Value = value.Value;
        /// <summary>Gets the chained value.</summary>
        public int Value
        {
            get;
        }

        /// <summary>Records the chained value.</summary>
        public void Record() => TestActions.Observed.Add(Value);
    }

    /// <summary>Base value used to test an assignable source wrapper.</summary>
    public class SourceBase
    {
        /// <summary>Creates a source base value.</summary>
        public SourceBase(int value) => Value = value;
        /// <summary>Gets the source value.</summary>
        public int Value
        {
            get;
        }
    }

    /// <summary>Concrete value produced from an assignable source.</summary>
    public sealed class SourceConcrete : SourceBase
    {
        /// <summary>Creates a concrete source value.</summary>
        public SourceConcrete(SourceBase value) : base(value.Value)
        {
        }

        /// <summary>Records the source value.</summary>
        public void Record() => TestActions.Observed.Add(Value);
    }

    /// <summary>Source used to verify ambiguous adaptation paths are reported.</summary>
    public sealed class AmbiguousSource;
    /// <summary>First intermediate value in an ambiguous adaptation path.</summary>
    public sealed class AmbiguousIntermediateA
    {
        /// <summary>Creates the first intermediate value.</summary>
        public AmbiguousIntermediateA(AmbiguousSource value)
        {
        }
    }

    /// <summary>Second intermediate value in an ambiguous adaptation path.</summary>
    public sealed class AmbiguousIntermediateB
    {
        /// <summary>Creates the second intermediate value.</summary>
        public AmbiguousIntermediateB(AmbiguousSource value)
        {
        }
    }

    /// <summary>Target shared by both ambiguous adaptation paths.</summary>
    public sealed class AmbiguousTarget
    {
        /// <summary>Creates the ambiguity-test target from the first intermediate value.</summary>
        public AmbiguousTarget(AmbiguousIntermediateA value)
        {
        }

        /// <summary>Creates the ambiguity-test target from the second intermediate value.</summary>
        public AmbiguousTarget(AmbiguousIntermediateB value)
        {
        }
    }

    /// <summary>Reference value with deliberately equal instances.</summary>
    public sealed class EqualToken
    {
        /// <summary>Creates an equal-comparison token.</summary>
        public EqualToken(int value) => Value = value;
        /// <summary>Gets the token value.</summary>
        public int Value
        {
            get;
        }

        /// <summary>Compares all tokens as equal for the test.</summary>
        public override bool Equals(object? obj) => obj is EqualToken;
        /// <summary>Gets the shared test hash code.</summary>
        public override int GetHashCode() => 0;
    }

    /// <summary>Value type with an overloaded addition operator.</summary>
    public readonly struct CustomNumber
    {
        /// <summary>Creates a custom number.</summary>
        public CustomNumber(int value) => Value = value;
        /// <summary>Gets the number value.</summary>
        public int Value
        {
            get;
        }

        /// <summary>Adds two custom numbers.</summary>
        public static CustomNumber operator +(CustomNumber left, CustomNumber right) => new(left.Value + right.Value);
    }

    /// <summary>Mutable value type used to exercise address-based member emission.</summary>
    public struct MutableValue
    {
        /// <summary>Gets or sets the mutable number.</summary>
        public int Number;
    }

    /// <summary>Mutable value type used to verify constant instance isolation.</summary>
    public struct MutableToken
    {
        /// <summary>Gets or sets the mutable value.</summary>
        public int Value;
        /// <summary>Increments this value and records the result.</summary>
        public void Increment() => TestActions.Observed.Add(++Value);
    }

    /// <summary>Flags used to verify enum bitwise expression emission.</summary>
    [Flags]
    public enum TestFlags
    {
        /// <summary>First flag.</summary>
        First = 1,
        /// <summary>Second flag.</summary>
        Second = 2
    }

    /// <summary>Static operations used by the executor plans.</summary>
    public static class TestActions
    {
        public static bool ObserveAndReturn(int value)
        {
            Observe(value);
            return true;
        }

        public static ExecutorScope PrepareChanged(IContainer target)
        {
            EventHandler handler = OnChanged;
            return ExecutorBuilder.Start<IContainer>(out var input).Call(nameof(ApplyChanged), [input, handler]).ToScope();
        }

        public static ExecutorScope PrepareGlobalChanged()
        {
            EventHandler handler = OnChanged;
            return ExecutorBuilder.Start().Call(nameof(ApplyGlobalChanged), [handler]).ToScope();
        }

        public static void ApplyChanged(IContainer target, EventHandler handler)
        {
            target.Changed -= handler;
            target.Changed += handler;
        }

        public static void RemoveChanged(IContainer target, EventHandler handler) => target.Changed -= handler;
        public static void ApplyGlobalChanged(EventHandler handler)
        {
            GlobalChanged -= handler;
            GlobalChanged += handler;
        }

        /// <summary>Gets the test comparison limit.</summary>
        public static long LongLimit { get; set; } = long.MaxValue;
        /// <summary>Gets the type selected for the generic refresh call.</summary>
        public static Type? LastGenericType
        {
            get; set;
        }
        /// <summary>Gets observed loop values.</summary>
        public static List<int> Observed { get; } = [];
        /// <summary>Gets observed reference tokens.</summary>
        public static List<EqualToken> ObservedTokens { get; } = [];
        /// <summary>Gets observed text values.</summary>
        public static List<string?> ObservedText { get; } = [];
        /// <summary>Gets observed boxed values.</summary>
        public static List<object?> ObservedObjects { get; } = [];
        /// <summary>Gets observed custom numbers.</summary>
        public static List<CustomNumber> ObservedCustom { get; } = [];
        /// <summary>Gets observed short values.</summary>
        public static List<short> ObservedShort { get; } = [];
        /// <summary>Gets observed byte values.</summary>
        public static List<byte> ObservedByte { get; } = [];
        /// <summary>Gets observed unsigned values.</summary>
        public static List<uint> ObservedUInt { get; } = [];
        /// <summary>Gets observed nullable integer values.</summary>
        public static List<int?> ObservedNullable { get; } = [];
        /// <summary>Gets observed nullable long values.</summary>
        public static List<long?> ObservedNullableLong { get; } = [];
        /// <summary>Gets the number of columns received through a logical enumerable.</summary>
        public static int ColumnCount
        {
            get; set;
        }
        /// <summary>Gets the number of columns received through a logical array.</summary>
        public static int ColumnArrayCount
        {
            get; set;
        }
        /// <summary>Gets observed long values.</summary>
        public static List<long> ObservedLong { get; } = [];
        /// <summary>Gets observed enum values.</summary>
        public static List<TestFlags> ObservedFlags { get; } = [];

        /// <summary>Gets a reflected enum constant.</summary>
        public const TestFlags DefaultFlags = TestFlags.First;
        /// <summary>Gets the last observed column.</summary>
        public static IColumn? LastColumn
        {
            get; set;
        }
        /// <summary>Gets the number of changed callbacks.</summary>
        public static int EventCalls
        {
            get; set;
        }

        /// <summary>Raises the static changed event.</summary>
        public static event EventHandler? GlobalChanged;
        /// <summary>Gets or sets a static value.</summary>
        public static int GlobalValue
        {
            get; set;
        }
        /// <summary>Gets the last recorded static value.</summary>
        public static int RecordedGlobalValue
        {
            get; private set;
        }

        /// <summary>Creates a mutable value.</summary>
        public static MutableValue CreateMutableValue() => new();
        /// <summary>Suspends layout.</summary>
        public static void SuspendLayout()
        {
        }

        /// <summary>Formats a title.</summary>
        public static string Format(string value) => $"[{value}]";
        /// <summary>Records the static value.</summary>
        public static void RecordGlobalValue() => RecordedGlobalValue = GlobalValue;
        /// <summary>Raises the static event.</summary>
        public static void RaiseGlobalChanged() => GlobalChanged?.Invoke(null, EventArgs.Empty);
        /// <summary>Refreshes a specialized column through a constrained generic call.</summary>
        public static void Refresh<T>(T column)
            where T : IColumn
        {
            LastGenericType = typeof(T);
            column.SetWidth(column.Width);
        }

        /// <summary>Appends a marker to a container title.</summary>
        public static void AppendMarker(IContainer container, string marker) => container.Title += marker;
        /// <summary>Records one loop value.</summary>
        public static void Observe(int value) => Observed.Add(value);
        /// <summary>Records one reference token.</summary>
        public static void RecordToken(EqualToken value) => ObservedTokens.Add(value);
        /// <summary>Records a mixed raw-operand call.</summary>
        public static void RecordMixed(int number, string text, DateTime timestamp, EqualToken user)
        {
            Observed.Add(number);
            ObservedText.Add($"{text}:{timestamp:O}");
            ObservedTokens.Add(user);
        }

        /// <summary>Records one text value.</summary>
        public static void RecordText(string? value) => ObservedText.Add(value);
        /// <summary>Records one boxed value.</summary>
        public static void RecordObject(object? value) => ObservedObjects.Add(value);
        /// <summary>Records one custom number.</summary>
        public static void RecordCustom(CustomNumber value) => ObservedCustom.Add(value);
        /// <summary>Records a value adapted through the registered source chain.</summary>
        public static void RecordChainLevelThree(ChainLevelThree value) => Observed.Add(value.Value);
        /// <summary>Records one short value.</summary>
        public static void RecordShort(short value) => ObservedShort.Add(value);
        /// <summary>Records one byte value.</summary>
        public static void ObserveByte(byte value) => ObservedByte.Add(value);
        /// <summary>Records one unsigned value.</summary>
        public static void ObserveUInt(uint value) => ObservedUInt.Add(value);
        /// <summary>Records a nullable integer value.</summary>
        public static void RecordNullable(int? value) => ObservedNullable.Add(value);
        /// <summary>Records a nullable long value.</summary>
        public static void RecordNullableLong(long? value) => ObservedNullableLong.Add(value);
        /// <summary>Records an ambiguity-test target.</summary>
        public static void RecordAmbiguousTarget(AmbiguousTarget value)
        {
        }

        /// <summary>Records the number of logical columns in an enumerable.</summary>
        public static void RecordColumns<T>(IEnumerable<T> values) => ColumnCount = values.Count();
        /// <summary>Records the number of logical columns in an array.</summary>
        public static void RecordColumnArray<T>(T[] values) => ColumnArrayCount = values.Length;
        /// <summary>Records one long value.</summary>
        public static void ObserveLong(long value) => ObservedLong.Add(value);
        /// <summary>Records one enum value.</summary>
        public static void ObserveFlags(TestFlags value) => ObservedFlags.Add(value);
        /// <summary>Records one column value.</summary>
        public static void ObserveColumn(IColumn value) => LastColumn = value;
        /// <summary>Returns a boxed text value.</summary>
        public static object GetObject() => "typed";
        /// <summary>Returns the supplied integer.</summary>
        public static int ReturnInt(int value) => value;
        /// <summary>Returns the supplied unsigned integer.</summary>
        public static uint ReturnUInt(uint value) => value;
        /// <summary>Updates a value through a by-reference parameter.</summary>
        public static void UpdateByReference(ref int value) => value++;
        /// <summary>Writes a value through an out parameter.</summary>
        public static void TryGetWidth(out int value) => value = 37;
        /// <summary>Creates the first chained source value.</summary>
        public static ChainLevelOne CreateChainLevelOne() => new(7);
        /// <summary>Handles a changed event.</summary>
        public static void OnChanged(object? sender, EventArgs args) => EventCalls++;
    }
}

