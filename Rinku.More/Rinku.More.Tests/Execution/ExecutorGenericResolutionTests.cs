using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorGenericResolutionTests
{
    [Fact]
    public void Generic_input_candidates_do_not_require_matching_logical_arity()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations));
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(out var item, () => new Widget()).Then(() => LogicalOperations.Use(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal(typeof(Host), value.HostType);
        Assert.Equal([typeof(Widget)], value.Types);
    }

    [Fact]
    public void Result_only_generic_parameter_binds_to_the_declared_interface()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(GenericProducers));
        var plan = ExecutorBuilder.Start<Host>(out var host)
            .Then(out var previous, () => new Widget())
            .Then<IWidget>(out var item, typeof(IFactories).GetMethod(nameof(IFactories.Create))!, [])
            .Then(() => GenericOperations.Record(host.Val, previous.Val))
            .Then(() => GenericOperations.Record(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal([typeof(Widget), typeof(IWidget)], value.Types);
    }

    [Fact]
    public void Open_generic_static_contracts_can_be_recorded_directly_as_method_info()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(GenericProducers));
        var plan = ExecutorBuilder.Start<Host>(out var host)
            .Then<IWidget>(out var item, typeof(IGenericFactory).GetMethod(nameof(IGenericFactory.Create))!, [])
            .Then(() => GenericOperations.Record(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal([typeof(IWidget)], value.Types);
    }

    [Fact]
    public void Discarding_an_open_generic_result_does_not_supply_an_unbound_type_argument()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(GenericProducers));
        var plan = ExecutorBuilder.Start<Host>(out _).Then(typeof(IGenericFactory).GetMethod(nameof(IGenericFactory.Create))!, []);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Host>>(configuration));
    }

    [Fact]
    public void Result_inference_supports_generic_collections_through_their_interfaces()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(GenericProducers));
        var plan = ExecutorBuilder.Start<Host>(out var host)
            .Then<IReadOnlyList<IWidget>>(out var items, typeof(IFactories).GetMethod(nameof(IFactories.CreateMany))!, [])
            .Then(() => GenericOperations.Record(host.Val, items.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal([typeof(List<IWidget>)], value.Types);
    }

    [Fact]
    public void Input_and_result_parameters_are_inferred_from_their_roles()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(GenericProducers));
        var plan = ExecutorBuilder.Start<Host>(out var host)
            .Then<IWidget>(out var item, typeof(IFactories).GetMethod(nameof(IFactories.CreateFor))!, [host])
            .Then(() => GenericOperations.Record(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal(typeof(Host), value.HostType);
        Assert.Equal([typeof(IWidget)], value.Types);
    }

    [Fact]
    public void Nongeneric_candidate_can_implement_a_generic_logical_call()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations)).RegisterProvider(typeof(ConcreteProducers));
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(out var item, () => GenericProducers.Create<IWidget>()).Then(() => GenericOperations.Record(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal([typeof(Widget)], value.Types);
    }

    [Fact]
    public void Nongeneric_equivalent_signature_wins_before_registration_priority()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(GenericOperations), 100).RegisterProvider(typeof(ConcreteOperations));
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(out var item, () => new Widget()).Then(() => GenericOperations.Use(host.Val, item.Val));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.True(value.Concrete);
    }

    [Fact]
    public void Result_constraints_do_not_invent_a_concrete_implementation_for_an_interface()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(ConstrainedProducers));
        var plan = ExecutorBuilder.Start<Host>(out _).Then<IWidget>(out _, typeof(IFactories).GetMethod(nameof(IFactories.Create))!, []);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Host>>(configuration));
    }

    [Fact]
    public void Generic_parameters_without_input_or_result_information_remain_unresolved()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(UnresolvedProducers));
        var plan = ExecutorBuilder.Start<Host>(out _).Then<IWidget>(out _, typeof(IFactories).GetMethod(nameof(IFactories.Create))!, []);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Host>>(configuration));
    }

    [Fact]
    public void Generic_nullable_input_infers_its_underlying_struct_before_lifting()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(NullableOperations));
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(typeof(INullableOperations).GetMethod(nameof(INullableOperations.Set))!, [host, 42]);
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.Equal([typeof(int)], value.Types);
    }

    [Fact]
    public void Generic_inference_does_not_choose_between_different_implementations_of_one_interface()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(MultipleShapeOperations));
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(typeof(IMultipleShapeOperations).GetMethod(nameof(IMultipleShapeOperations.Set))!, [host, new MultipleShapes()]);

        Assert.Throws<ExecutorBindingException>(() => plan.Build<IExecutor<Host>>(configuration));
    }

    [Fact]
    public void More_specific_generic_parameter_pattern_wins_before_priority()
    {
        var configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(SpecificGenericOperations)).RegisterProvider(typeof(BroadGenericOperations), 100);
        var plan = ExecutorBuilder.Start<Host>(out var host).Then(() => BroadGenericOperations.Set(host.Val, 42));
        var value = new Host();

        plan.Build<IExecutor<Host>>(configuration).Execute(value);

        Assert.True(value.Concrete);
    }

    public interface IWidget;
    public class Widget : IWidget;
    public interface IValues<T>;
    public class MultipleShapes : IValues<int>, IValues<string>;

    public class Host
    {
        public Type? HostType
        {
            get; set;
        }
        public List<Type> Types { get; } = [];
        public bool Concrete
        {
            get; set;
        }
    }

    public interface IFactories
    {
        static abstract IWidget Create();
        static abstract IReadOnlyList<IWidget> CreateMany();
        static abstract IWidget CreateFor(Host host);
    }

    public interface IGenericFactory
    {
        static abstract TResult Create<TResult>();
    }

    public static class GenericProducers
    {
        public static TResult Create<TResult>() => (TResult)(object)new Widget();
        public static List<TResult> CreateMany<TResult>() => [(TResult)(object)new Widget()];
        public static TResult CreateFor<THost, TResult>(THost host) where THost : Host
        {
            host.HostType = typeof(THost);
            return (TResult)(object)new Widget();
        }
    }

    public static class ConcreteProducers
    {
        public static Widget Create() => new();
    }

    public static class ConstrainedProducers
    {
        public static TResult Create<TResult>() where TResult : new() => new();
    }

    public static class UnresolvedProducers
    {
        public static TResult Create<TUnused, TResult>() => (TResult)(object)new Widget();
    }

    public static class LogicalOperations
    {
        public static void Use(Host host, IWidget item) => throw new NotSupportedException();
    }

    public static class GenericOperations
    {
        public static void Use<THost, TItem>(THost host, TItem item) where THost : Host
        {
            host.HostType = typeof(THost);
            host.Types.Add(typeof(TItem));
        }
        public static void Record<T>(Host host, T item) => host.Types.Add(typeof(T));
    }

    public static class ConcreteOperations
    {
        public static void Use(Host host, Widget item) => host.Concrete = true;
    }

    public interface INullableOperations
    {
        static abstract void Set(Host host, int value);
    }

    public static class NullableOperations
    {
        public static void Set<T>(Host host, T? value) where T : struct => host.Types.Add(typeof(T));
    }

    public interface IMultipleShapeOperations
    {
        static abstract void Set(Host host, MultipleShapes value);
    }

    public static class MultipleShapeOperations
    {
        public static void Set<T>(Host host, IValues<T> values) => host.Types.Add(typeof(T));
    }

    public static class SpecificGenericOperations
    {
        public static void Set<T>(Host host, T value) => host.Concrete = true;
    }

    public static class BroadGenericOperations
    {
        public static void Set<THost, TValue>(THost host, TValue value) where THost : Host => host.Concrete = false;
    }
}
