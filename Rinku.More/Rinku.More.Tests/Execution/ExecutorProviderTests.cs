using System.Reflection;
using Rinku;
using Rinku.More;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorProviderTests
{
    [Fact]
    public void External_providers_specialize_one_plan_for_desktop_and_web()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(DesktopOperations)).RegisterProvider(typeof(WebOperations));
        var plan = ExecutorBuilder.Start<ITextTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));
        var desktopExecutor = plan.Build<IExecutor<Desktop>>(configuration);
        var webExecutor = plan.Build<IExecutor<Web>>(configuration);
        var desktop = new Desktop();
        var web = new Web();

        desktopExecutor.Execute(desktop);
        webExecutor.Execute(web);

        Assert.Equal("desktop:Name", desktop.Caption);
        Assert.Equal("web:Name", web.Caption);
    }

    [Fact]
    public void Attributes_and_manual_authoring_record_the_same_operations()
    {
        var manual = ExecutorBuilder.Start<ITextTarget>(out var manualTarget).Then(() => LogicalOperations.SetCaption(manualTarget.Val, "Name"));
        var attributed = ExecutorBuilder.Start<ITextTarget>(out var attributeTarget);
        RecordCaption(attributed, attributeTarget);
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(DesktopOperations));
        var first = new Desktop();
        var second = new Desktop();

        manual.Build<IExecutor<Desktop>>(configuration).Execute(first);
        attributed.Build<IExecutor<Desktop>>(configuration).Execute(second);

        Assert.Equal("desktop:Name", first.Caption);
        Assert.Equal(first.Caption, second.Caption);
    }

    [Fact]
    public void Assembly_discovery_registers_decorated_types_and_individual_methods_once()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterAssembly(typeof(ExecutorProviderTests).Assembly).RegisterAssembly(typeof(ExecutorProviderTests).Assembly);
        var plan = ExecutorBuilder.Start<ITextTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));
        var desktop = new Desktop();
        plan.Build<IExecutor<Desktop>>(configuration).Execute(desktop);
        Assert.Equal("desktop:Name", desktop.Caption);

        var selective = ExecutorBuilder.Start<SelectiveTarget>(out var selected).Then(typeof(ISelectedOperations).GetMethod(nameof(ISelectedOperations.Selected))!, [selected, "Name"]);
        var selectedTarget = new SelectiveTarget();
        selective.Build<IExecutor<SelectiveTarget>>(configuration).Execute(selectedTarget);
        Assert.Equal("Name", selectedTarget.Caption);

        var unregistered = ExecutorBuilder.Start<SelectiveTarget>(out var ignored).Then(typeof(ISelectedOperations).GetMethod(nameof(ISelectedOperations.Ignored))!, [ignored]);
        Assert.Throws<ExecutorBindingException>(() => unregistered.Build<IExecutor<SelectiveTarget>>(configuration));
    }

    [Fact]
    public void Static_interfaces_enforce_provider_contracts_and_compile_to_concrete_calls()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>();
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target)
            .Then(typeof(ICaptionOperations<CheckedTarget>).GetMethod(nameof(ICaptionOperations<CheckedTarget>.SetCaption))!, [target, "Name"])
            .Then(typeof(IVisibilityOperations<CheckedTarget>).GetMethod(nameof(IVisibilityOperations<CheckedTarget>.SetVisible))!, [target, true]);
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("Name", value.Caption);
        Assert.True(value.Visible);
    }

    [Fact]
    public void Missing_static_interface_implementation_fails_during_build()
    {
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(ICaptionOperations<CheckedTarget>).GetMethod(nameof(ICaptionOperations<CheckedTarget>.SetCaption))!, [target, "Name"]);

        var error = Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<CheckedTarget>>());

        Assert.Contains(nameof(ICaptionOperations<CheckedTarget>.SetCaption), error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Priority_breaks_equal_signature_ties_independently_of_registration_order(bool reverse)
    {
        var configuration = new ExecutorBuildConfiguration();
        if (reverse)
        {
            configuration.RegisterProvider<PriorityOperations>(10).RegisterProvider<CheckedOperations>();
        }
        else
        {
            configuration.RegisterProvider<CheckedOperations>().RegisterProvider<PriorityOperations>(10);
        }
        var plan = CaptionPlan();
        var target = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("priority", target.Caption);
    }

    [Fact]
    public void Incompatible_higher_priority_methods_do_not_hide_valid_candidates()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>().RegisterProvider(typeof(WebOperations), 100);
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("Name", target.Caption);
    }

    [Fact]
    public void Equal_priority_and_specificity_fails_instead_of_using_registration_order()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>().RegisterProvider<PriorityOperations>();

        Assert.Throws<InvalidOperationException>(() => CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration));
    }

    [Fact]
    public void More_specific_parameter_wins_over_object_and_base_interfaces()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(SpecificOperations));
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("specific", target.Caption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exact_match_wins_over_a_higher_priority_interface_match(bool reverse)
    {
        var configuration = new ExecutorBuildConfiguration();
        MethodInfo broad = typeof(SpecificOperations).GetMethod(nameof(SpecificOperations.SetCaption), [typeof(ITextTarget), typeof(string)])!;
        if (reverse)
        {
            configuration.RegisterMethod(broad, 100).RegisterProvider<CheckedOperations>();
        }
        else
        {
            configuration.RegisterProvider<CheckedOperations>().RegisterMethod(broad, 100);
        }
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("Name", target.Caption);
    }

    [Fact]
    public void More_specific_interface_wins_over_a_higher_priority_object_match()
    {
        MethodInfo broad = typeof(SpecificOperations).GetMethod(nameof(SpecificOperations.SetCaption), [typeof(object), typeof(string)])!;
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(SpecificOperations)).RegisterMethod(broad, 100);
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("specific", target.Caption);
    }

    [Fact]
    public void Numeric_widening_wins_over_higher_priority_boxing()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(NumericChoiceOperations))
            .RegisterMethod(typeof(NumericChoiceOperations).GetMethod(nameof(NumericChoiceOperations.Choose), [typeof(CheckedTarget), typeof(object)])!, 100);
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(INumericChoiceOperations).GetMethod(nameof(INumericChoiceOperations.Choose))!, [target, 42]);
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("long", value.Caption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_candidate_dominating_every_argument_wins_regardless_of_priority(bool reverse)
    {
        var configuration = new ExecutorBuildConfiguration();
        if (reverse)
        {
            configuration.RegisterProvider(typeof(ExactCrossedOperations), -100).RegisterProvider(typeof(CrossedOperations), 100);
        }
        else
        {
            configuration.RegisterProvider(typeof(CrossedOperations), 100).RegisterProvider(typeof(ExactCrossedOperations), -100);
        }
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(ICrossedOperations).GetMethod(nameof(ICrossedOperations.Set))!, [target, target]);
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("exact", value.Caption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_discovery_keeps_the_highest_route_score_without_adding_scores(bool reverse)
    {
        var configuration = new ExecutorBuildConfiguration();
        MethodInfo method = typeof(InstanceTarget).GetMethod(nameof(InstanceTarget.SetCaption))!;
        if (reverse)
        {
            configuration.RegisterMethod(method, 3).RegisterProvider<InstanceTarget>(4);
        }
        else
        {
            configuration.RegisterProvider<InstanceTarget>(4).RegisterMethod(method, 3);
        }
        configuration.RegisterProvider(typeof(InstanceBridge), 5);
        var pool = new ExecutorMethodPool(configuration.Snapshot());
        pool.Include(typeof(InstanceTarget));
        var instance = Assert.Single(pool.Find(nameof(InstanceTarget.SetCaption)), candidate => candidate.Member is MethodInfo found && ExecutorMethodCandidate.SameMethod(found, method));
        Assert.Equal(4, instance.Priority);

        var plan = ExecutorBuilder.Start<InstanceTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));
        var value = new InstanceTarget();
        plan.Build<IExecutor<InstanceTarget>>(configuration).Execute(value);
        Assert.Equal("static", value.Caption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_registrations_cannot_downgrade_the_same_candidate(bool reverse)
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<PriorityOperations>(5);
        MethodInfo method = typeof(CheckedOperations).GetMethod(nameof(CheckedOperations.SetCaption))!;
        if (reverse)
        {
            configuration.RegisterProvider<CheckedOperations>(1).RegisterProvider<CheckedOperations>(10).RegisterMethod(method, 2).RegisterMethod(method, 8);
        }
        else
        {
            configuration.RegisterProvider<CheckedOperations>(10).RegisterProvider<CheckedOperations>(1).RegisterMethod(method, 8).RegisterMethod(method, 2);
        }
        var registrations = configuration.Snapshot();
        Assert.Equal(10, Assert.Single(registrations.Providers, item => item.Type == typeof(CheckedOperations)).Priority);
        Assert.Equal(8, Assert.Single(registrations.Candidates).Priority);
        var value = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("Name", value.Caption);
    }

    [Fact]
    public void A_later_automatic_discovery_can_raise_a_negative_registration_score()
    {
        var pool = new ExecutorMethodPool(new ExecutorBuildConfiguration().RegisterProvider<InstanceTarget>(-10).Snapshot());
        Assert.Equal(-10, Assert.Single(pool.Find(nameof(InstanceTarget.SetCaption))).Priority);

        pool.Include(typeof(InstanceTarget));

        Assert.Equal(0, Assert.Single(pool.Find(nameof(InstanceTarget.SetCaption))).Priority);
    }

    [Fact]
    public void Inherited_reflection_views_merge_but_closed_declaring_types_remain_distinct()
    {
        MethodInfo inherited = typeof(DerivedGenericOwner<int>).GetMethod(nameof(GenericOwner<int>.Configure))!;
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOwner<int>), 2)
            .RegisterProvider(typeof(DerivedGenericOwner<int>), 5).RegisterMethod(inherited, 4).RegisterProvider(typeof(GenericOwner<string>), 7);
        var pool = new ExecutorMethodPool(configuration.Snapshot());

        var candidates = pool.Find(nameof(GenericOwner<int>.Configure));

        Assert.Equal(2, candidates.Count);
        Assert.Equal(5, Assert.Single(candidates, item => item.Member.DeclaringType == typeof(GenericOwner<int>)).Priority);
        Assert.Equal(7, Assert.Single(candidates, item => item.Member.DeclaringType == typeof(GenericOwner<string>)).Priority);
    }

    [Fact]
    public void Closed_generic_methods_keep_their_type_arguments_during_resolution()
    {
        MethodInfo generic = typeof(GenericOperations).GetMethod(nameof(GenericOperations.SetValue))!;
        MethodInfo preferred = typeof(PreferredGenericOperations).GetMethod(nameof(PreferredGenericOperations.SetValue))!;
        var configuration = new ExecutorBuildConfiguration().RegisterMethod(generic.MakeGenericMethod(typeof(int)), 10)
            .RegisterMethod(generic.MakeGenericMethod(typeof(DateTime)), 100).RegisterMethod(preferred.MakeGenericMethod(typeof(int)), 20);
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(() => GenericLogicalOperations.SetValue(target.Val, 42));
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("preferred", value.Caption);
    }

    [Fact]
    public void Generic_definition_and_closed_registration_do_not_accumulate_priority()
    {
        MethodInfo generic = typeof(GenericOperations).GetMethod(nameof(GenericOperations.SetValue))!;
        MethodInfo preferred = typeof(PreferredGenericOperations).GetMethod(nameof(PreferredGenericOperations.SetValue))!;
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations), 2)
            .RegisterMethod(generic.MakeGenericMethod(typeof(int)), 4).RegisterMethod(preferred.MakeGenericMethod(typeof(int)), 5);
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(() => GenericLogicalOperations.SetValue(target.Val, 42));
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("preferred", value.Caption);
    }

    [Fact]
    public void Crossed_parameter_specificities_remain_ambiguous()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(CrossedOperations));
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(ICrossedOperations).GetMethod(nameof(ICrossedOperations.Set))!, [target, target]);

        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<CheckedTarget>>(configuration));
    }

    [Fact]
    public void Global_and_local_priorities_compose_without_changing_compiled_executors()
    {
        var global = ExecutorBuildConfiguration.Global;
        try
        {
            global.RegisterProvider<CheckedOperations>(5);
            var plan = CaptionPlan();
            var original = plan.Build<IExecutor<CheckedTarget>>();
            var local = plan.Build<IExecutor<CheckedTarget>>(new ExecutorBuildConfiguration().RegisterProvider<PriorityOperations>(10));
            global.RegisterProvider<PriorityOperations>(20);
            var updated = plan.Build<IExecutor<CheckedTarget>>();
            var first = new CheckedTarget();
            var second = new CheckedTarget();
            var third = new CheckedTarget();

            original.Execute(first);
            local.Execute(second);
            updated.Execute(third);

            Assert.Equal("Name", first.Caption);
            Assert.Equal("priority", second.Caption);
            Assert.Equal("priority", third.Caption);
        }
        finally
        {
            global.Clear();
        }
    }

    [Fact]
    public void Method_and_provider_priorities_compose()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<DecoratedPriorityOperations>(2).RegisterProvider<PriorityOperations>(10);
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("decorated", target.Caption);
    }

    [Fact]
    public void Duplicate_registration_does_not_create_an_ambiguity()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>().RegisterProvider<CheckedOperations>();
        var target = new CheckedTarget();

        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);

        Assert.Equal("Name", target.Caption);
    }

    [Fact]
    public void Provider_execution_allocates_nothing_after_build()
    {
        var executor = CaptionPlan().Build<IExecutor<CheckedTarget>>(new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>());
        var target = new CheckedTarget();
        for (int index = 0; index < 100; index++)
        {
            executor.Execute(target);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10000; index++)
        {
            executor.Execute(target);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Explicit_priority_resolves_crossed_specializations()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(CrossedOperations)).RegisterProvider(typeof(PreferredCrossedOperations), 1);
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(ICrossedOperations).GetMethod(nameof(ICrossedOperations.Set))!, [target, target]);
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.True(value.Visible);
    }

    [Theory]
    [InlineData(10, "generic")]
    [InlineData("text", "logical")]
    public void Generic_candidates_are_closed_and_constraints_are_checked<T>(T input, string expected)
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericLogicalOperations)).RegisterProvider(typeof(GenericOperations), 1);
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(() => GenericLogicalOperations.SetValue(target.Val, input));
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal(expected, value.Caption);
    }

    [Fact]
    public void Attribute_generated_operations_remain_editable_before_compilation()
    {
        var plan = ExecutorBuilder.Start<ITextTarget>(out var target);
        RecordCaption(plan, target);
        plan.Then(() => LogicalOperations.SetCaption(target.Val, "Changed"));
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(DesktopOperations));
        var desktop = new Desktop();

        plan.Build<IExecutor<Desktop>>(configuration).Execute(desktop);

        Assert.Equal("desktop:Changed", desktop.Caption);
    }

    [Fact]
    public void Concurrent_registration_and_compilation_use_complete_provider_registrations()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>();
        Parallel.For(0, 16, _ =>
        {
            configuration.RegisterProvider<CheckedOperations>();
            var target = new CheckedTarget();
            CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);
            Assert.Equal("Name", target.Caption);
        });
    }

    private static ExecutorBuilder CaptionPlan() => ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(ICaptionOperations<CheckedTarget>).GetMethod(nameof(ICaptionOperations<CheckedTarget>.SetCaption))!, [target, "Name"]);

    [Fact]
    public void Type_registrations_retain_types_instead_of_expanded_methods()
    {
        var registrations = new ExecutorBuildConfiguration().RegisterProvider<CheckedOperations>().Snapshot();

        Assert.Equal(typeof(CheckedOperations), Assert.Single(registrations.Providers).Type);
        Assert.Empty(registrations.Candidates);
    }

    [Fact]
    public void Individual_method_registration_does_not_include_its_siblings()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterMethod(typeof(CheckedOperations).GetMethod(nameof(CheckedOperations.SetCaption))!);
        var target = new CheckedTarget();
        CaptionPlan().Build<IExecutor<CheckedTarget>>(configuration).Execute(target);
        Assert.Equal("Name", target.Caption);

        var visibility = ExecutorBuilder.Start<CheckedTarget>(out var value).Then(typeof(IVisibilityOperations<CheckedTarget>).GetMethod(nameof(IVisibilityOperations<CheckedTarget>.SetVisible))!, [value, true]);
        Assert.Throws<ExecutorBindingException>(() => visibility.Build<IExecutor<CheckedTarget>>(configuration));
    }

    [Fact]
    public void Static_logical_call_resolves_to_unregistered_instance_method_on_entry_type()
    {
        var plan = ExecutorBuilder.Start<InstanceTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));
        var value = new InstanceTarget();

        plan.Build<IExecutor<InstanceTarget>>().Execute(value);

        Assert.Equal("instance", value.Caption);
    }

    [Fact]
    public void Instance_logical_call_resolves_to_individually_registered_static_method()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterMethod(typeof(CheckedOperations).GetMethod(nameof(CheckedOperations.SetCaption))!);
        var plan = ExecutorBuilder.Start<ILogicalTarget>(out var target).Then(() => target.Val.SetCaption("Name"));
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("Name", value.Caption);
    }

    [Fact]
    public void A_new_result_type_contributes_its_methods_without_provider_registration()
    {
        var configuration = new ExecutorBuildConfiguration();
        var plan = ExecutorBuilder.Start<IFactory>(out var factory).Then(out var child, () => factory.Val.Create()).Then(() => ChildOperations.Mark(child.Val));
        var value = new ActualFactory();

        plan.Build<IExecutor<ActualFactory>>(configuration).Execute(value);

        Assert.True(value.Child.Marked);
    }

    [Fact]
    public void Static_and_instance_candidates_with_identical_normalized_signatures_are_ambiguous()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(InstanceBridge));
        var plan = ExecutorBuilder.Start<InstanceTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));

        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<InstanceTarget>>(configuration));
    }

    [Fact]
    public void An_individually_registered_instance_method_can_win_by_priority()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(InstanceBridge))
            .RegisterMethod(typeof(InstanceTarget).GetMethod(nameof(InstanceTarget.SetCaption))!, 1);
        var plan = ExecutorBuilder.Start<InstanceTarget>(out var target).Then(() => LogicalOperations.SetCaption(target.Val, "Name"));
        var value = new InstanceTarget();

        plan.Build<IExecutor<InstanceTarget>>(configuration).Execute(value);

        Assert.Equal("instance", value.Caption);
    }

    [Fact]
    public void Resolution_can_lift_a_struct_to_nullable_without_a_registered_adapter()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(NullableOperations));
        var plan = ExecutorBuilder.Start<CheckedTarget>(out var target).Then(typeof(IStructOperations).GetMethod(nameof(IStructOperations.SetNumber))!, [target, new NumberValue(42)]);
        var value = new CheckedTarget();

        plan.Build<IExecutor<CheckedTarget>>(configuration).Execute(value);

        Assert.Equal("42", value.Caption);
    }

    [Fact]
    public void Resolution_does_not_implicitly_unwrap_a_nullable_struct()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(NonNullableOperations));
        var plan = ExecutorBuilder.Start<CheckedTarget, NumberValue?>(out var target, out var number).Then(typeof(INullableStructOperations).GetMethod(nameof(INullableStructOperations.SetNumber))!, [target, number]);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<CheckedTarget, NumberValue?>>(configuration));
    }

    [Fact]
    public void Typed_build_compiles_only_the_requested_signature_without_a_logical_fallback()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(DesktopOperations)).RegisterProvider(typeof(WebOperations));
        var plan = ExecutorBuilder.Start<ITextTarget>(out var target).Then(typeof(ICaptionOperations<ITextTarget>).GetMethod(nameof(ICaptionOperations<ITextTarget>.SetCaption))!, [target, "Name"]);
        var executor = plan.Build<IExecutor<Desktop>>(configuration);
        var desktop = new Desktop();

        executor.Execute(desktop);

        Assert.Equal("desktop:Name", desktop.Caption);
        Assert.IsNotAssignableFrom<IExecutor<ITextTarget>>(executor);
        Assert.IsNotAssignableFrom<IExecutor<Web>>(executor);
    }

    [Fact]
    public void Typed_build_preserves_custom_interfaces_and_multiple_inputs()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(DesktopOperations));
        var plan = ExecutorBuilder.Start<ITextTarget, string>(out var target, out var caption).Then(() => LogicalOperations.SetCaption(target.Val, caption.Val));
        var executor = plan.Build<IDesktopExecutor>(configuration);
        var desktop = new Desktop();

        executor.Execute(desktop, "Name");

        Assert.Equal("desktop:Name", desktop.Caption);
    }

    [Fact]
    public void Typed_build_uses_only_the_requested_target_signature()
    {
        var configuration = new ExecutorBuildConfiguration();
        var plan = ExecutorBuilder.Start<ExecutorSubsystemTests.IContainer>(out var target).Then(() => target.Val.Enabled, true);
        var executor = plan.Build<IExecutor<ExecutorSubsystemTests.FakeGrid>>(configuration);
        var grid = new ExecutorSubsystemTests.FakeGrid();

        executor.Execute(grid);

        Assert.True(grid.Enabled);
        Assert.IsNotAssignableFrom<IExecutor<ExecutorSubsystemTests.GridAdapter>>(executor);
    }

    [Fact]
    public void Struct_inputs_can_use_explicit_interface_methods_and_setters_without_boxing()
    {
        var plan = ExecutorBuilder.Start<IValueTarget>(out var target).Then(() => target.Val.SetCaption("Name")).Then(() => target.Val.Visible, true);
        var executor = plan.Build<IExecutor<ValueTarget>>();
        var state = new CheckedTarget();
        var value = new ValueTarget(state);
        executor.Execute(value);

        Assert.Equal("Name", state.Caption);
        Assert.True(state.Visible);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10000; index++)
        {
            executor.Execute(value);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    public interface IValueTarget
    {
        void SetCaption(string caption);
        bool Visible
        {
            get; set;
        }
    }

    public readonly struct ValueTarget(CheckedTarget state) : IValueTarget
    {
        void IValueTarget.SetCaption(string caption) => state.Caption = caption;
        bool IValueTarget.Visible
        {
            get => state.Visible; set => state.Visible = value;
        }
    }

    public interface IDesktopExecutor : IExecutor<Desktop, string>;

    public readonly record struct NumberValue(int Value);

    public interface IStructOperations
    {
        static abstract void SetNumber(CheckedTarget target, NumberValue value);
    }

    public interface INullableStructOperations
    {
        static abstract void SetNumber(CheckedTarget target, NumberValue? value);
    }

    public static class NullableOperations
    {
        public static void SetNumber(CheckedTarget target, NumberValue? value) => target.Caption = value?.Value.ToString() ?? string.Empty;
    }

    public static class NonNullableOperations
    {
        public static void SetNumber(CheckedTarget target, NumberValue value) => target.Caption = value.Value.ToString();
    }

    public interface ILogicalTarget : IExpressionContract
    {
        void SetCaption(string caption);
    }

    public class InstanceTarget : ITextTarget
    {
        public string Caption { get; set; } = string.Empty;
        public void SetCaption(string caption) => Caption = "instance";
    }

    public static class InstanceBridge
    {
        public static void SetCaption(InstanceTarget target, string caption) => target.Caption = "static";
    }

    public interface IFactory : IExpressionContract
    {
        IChild Create();
    }

    public interface IChild;

    public class ActualFactory
    {
        public ActualChild Child { get; } = new();
        public ActualChild Create() => Child;
    }

    public class ActualChild : IChild
    {
        public bool Marked
        {
            get; set;
        }
        public static void Mark(ActualChild child) => child.Marked = true;
    }

    public static class ChildOperations
    {
        public static void Mark(IChild child) => throw new NotSupportedException();
    }

    public interface ITextTarget
    {
        string Caption
        {
            get; set;
        }
    }

    public class Desktop : ITextTarget
    {
        public string Caption { get; set; } = string.Empty;
    }

    public class Web : ITextTarget
    {
        public string Caption { get; set; } = string.Empty;
    }

    public static class LogicalOperations
    {
        public static void SetCaption(ITextTarget target, string caption) => target.Caption = caption;
    }

    [ExecutorProvider]
    public static class DesktopOperations
    {
        public static void SetCaption(Desktop target, string caption) => target.Caption = "desktop:" + caption;
    }

    [ExecutorProvider]
    public static class WebOperations
    {
        public static void SetCaption(Web target, string caption) => target.Caption = "web:" + caption;
    }

    public interface ICaptionOperations<T>
    {
        static abstract void SetCaption(T target, string caption);
    }

    public interface IVisibilityOperations<T>
    {
        static abstract void SetVisible(T target, bool visible);
    }

    public interface IFieldOperations<T> : ICaptionOperations<T>, IVisibilityOperations<T>;

    public class CheckedTarget : ITextTarget
    {
        public string Caption { get; set; } = string.Empty;
        public bool Visible
        {
            get; set;
        }
    }

    public class CheckedOperations : IFieldOperations<CheckedTarget>
    {
        public static void SetCaption(CheckedTarget target, string caption) => target.Caption = caption;
        public static void SetVisible(CheckedTarget target, bool visible) => target.Visible = visible;
    }

    public class PriorityOperations
    {
        public static void SetCaption(CheckedTarget target, string caption) => target.Caption = "priority";
    }

    [ExecutorProvider(3)]
    public class DecoratedPriorityOperations
    {
        [ExecutorMethod(6)]
        public static void SetCaption(CheckedTarget target, string caption) => target.Caption = "decorated";
    }

    public static class SpecificOperations
    {
        public static void SetCaption(object target, string caption) => ((CheckedTarget)target).Caption = "object";
        public static void SetCaption(ITextTarget target, string caption) => target.Caption = "specific";
    }

    public interface ICrossedOperations
    {
        static abstract void Set(CheckedTarget first, CheckedTarget second);
    }

    public static class CrossedOperations
    {
        public static void Set(CheckedTarget first, object second)
        {
        }
        public static void Set(object first, CheckedTarget second)
        {
        }
    }

    public static class PreferredCrossedOperations
    {
        public static void Set(CheckedTarget first, object second) => first.Visible = true;
    }

    public static class ExactCrossedOperations
    {
        public static void Set(CheckedTarget first, CheckedTarget second) => first.Caption = "exact";
    }

    public interface INumericChoiceOperations
    {
        static abstract void Choose(CheckedTarget target, int value);
    }

    public static class NumericChoiceOperations
    {
        public static void Choose(CheckedTarget target, long value) => target.Caption = "long";
        public static void Choose(CheckedTarget target, object value) => target.Caption = "boxed";
    }

    public class GenericOwner<T>
    {
        public void Configure(T value)
        {
        }
    }

    public class DerivedGenericOwner<T> : GenericOwner<T>;

    public static class PreferredGenericOperations
    {
        public static void SetValue<T>(CheckedTarget target, T value) => target.Caption = "preferred";
    }

    public static class GenericLogicalOperations
    {
        public static void SetValue<T>(CheckedTarget target, T value) => target.Caption = "logical";
    }

    public static class GenericOperations
    {
        public static void SetValue<T>(CheckedTarget target, T value) where T : struct => target.Caption = "generic";
    }

    public class SelectiveTarget
    {
        public string Caption { get; set; } = string.Empty;
    }

    public interface ISelectedOperations
    {
        static abstract void Selected(SelectiveTarget target, string caption);
        static abstract void Ignored(SelectiveTarget target);
    }

    public static class SelectiveOperations
    {
        [ExecutorMethod]
        public static void Selected(SelectiveTarget target, string caption) => target.Caption = caption;
        public static void Ignored(SelectiveTarget target)
        {
        }
    }

    [NameCaption]
    public class DescribedModel;

    public class NameCaptionAttribute(string text = "Name") : Attribute
    {
        public string Text { get; } = text;
    }

    private static void RecordCaption(ExecutorBuilder builder, Variable<ITextTarget> target)
    {
        foreach (var attribute in typeof(DescribedModel).GetMetadataAttributes<NameCaptionAttribute>())
        {
            string caption = attribute.Text;
            builder.Then(() => LogicalOperations.SetCaption(target.Val, caption));
        }
    }
}
