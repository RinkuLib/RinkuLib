using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Rinku.Tracking.Runtime.Configuration;
using Rinku.Tracking.Runtime.Emit;
using Rinku.Tracking.Runtime.Model;

namespace Rinku.Tracking.Runtime;

/// <summary>Single expandable contract for first-phase configuration.</summary>
public interface IRuntimeTrackingOption<TOriginal>
{
    /// <summary>Applies this option to a generation definition.</summary>
    void Apply(RuntimeTrackingTypeDefinition<TOriginal> type);
}

internal interface IRuntimeTrackingOptionCloneable<TOriginal>
{
    IRuntimeTrackingOption<TOriginal> CloneOption(Action changing);
}

/// <summary>
/// Provides ordered options for a generated tracking type.
/// </summary>
public sealed class RuntimeTrackingOptions<TOriginal>
{
    private readonly List<IRuntimeTrackingOption<TOriginal>> _options = [];
    private readonly ConcurrentDictionary<Type, Lazy<object>> _registrations = new();
    private RuntimeTrackingOptions<TOriginal>? _bindingOptions;
    private bool _frozen;

    /// <summary>Creates a runtime tracking option set.</summary>
    public RuntimeTrackingOptions(bool includeOriginalMembers = true)
    {
        if (includeOriginalMembers) _options.Add(RuntimeOriginalDiscoveryOption<TOriginal>.Instance);
        _options.Add(RuntimeParameterProjectionOption<TOriginal>.Instance);
    }

    /// <summary>Gets the ordered options.</summary>
    public IReadOnlyList<IRuntimeTrackingOption<TOriginal>> Options => _options;
    /// <summary>Gets whether registration has frozen the options.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>Adds an option.</summary>
    public RuntimeTrackingOptions<TOriginal> Add(IRuntimeTrackingOption<TOriginal> option)
    {
        ArgumentNullException.ThrowIfNull(option);
        Changing();
        _options.Add(option);
        return this;
    }

    /// <summary>Applies an interface contract.</summary>
    public RuntimeTrackingOptions<TOriginal> Apply<TContract>()
    {
        if (!typeof(TContract).IsInterface)
            throw new InvalidOperationException($"Runtime tracking contract {typeof(TContract)} must be an interface.");
        return Add(new RuntimeInterfaceOption<TOriginal>(typeof(TContract)));
    }

    /// <summary>Adds typed options for a member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> Member<TValue>(string name) {
        var option = new RuntimeTrackingMemberOptions<TOriginal, TValue>(name, Changing);
        Add(option);
        return option;
    }

    /// <summary>Configures a member and continues the option chain.</summary>
    public RuntimeTrackingOptions<TOriginal> Member<TValue>(string name, Action<RuntimeTrackingMemberOptions<TOriginal, TValue>> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Member<TValue>(name));
        return this;
    }

    /// <summary>Configures a selected member and implements its interface when an interface is selected.</summary>
    public RuntimeTrackingOptions<TOriginal> Member<TContract, TValue>(Expression<Func<TContract, TValue>> selector, Action<RuntimeTrackingMemberOptions<TOriginal, TValue>> configure) {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);
        if (selector.Body is not MemberExpression { Member: PropertyInfo or FieldInfo } member || member.Expression != selector.Parameters[0])
            throw new ArgumentException("Select a direct instance property or field.", nameof(selector));
        if (!typeof(TContract).IsInterface && typeof(TContract) != typeof(TOriginal))
            throw new ArgumentException("Select a member of the original type or an interface contract.", nameof(selector));
        Member(member.Member.Name, configure);
        return typeof(TContract).IsInterface ? Apply<TContract>() : this;
    }

    /// <summary>Initializes editable values without constructing an accepted original.</summary>
    public RuntimeTrackingOptions<TOriginal> InitializeNew<TEdit>(Action<TEdit> initialize) {
        ArgumentNullException.ThrowIfNull(initialize);
        return Add(new RuntimeNewItemOption<TOriginal, TEdit>(initialize));
    }

    /// <summary>Runs a policy after accepting edited values.</summary>
    public RuntimeTrackingOptions<TOriginal> OnConfirm(Action<ITrackingItem<TOriginal>> policy) {
        ArgumentNullException.ThrowIfNull(policy);
        return Add(new RuntimeLifecycleOption<TOriginal>(policy, true));
    }

    /// <summary>Runs a policy after reverting edited values, including directly stored members.</summary>
    public RuntimeTrackingOptions<TOriginal> OnRevert(Action<ITrackingItem<TOriginal>> policy) {
        ArgumentNullException.ThrowIfNull(policy);
        return Add(new RuntimeLifecycleOption<TOriginal>(policy, false));
    }

    /// <summary>Configures how confirmation creates or updates the accepted original.</summary>
    public RuntimeTrackingOptions<TOriginal> ConfirmUsing(Func<ITrackingItem<TOriginal>, TOriginal> confirm, TrackingConfirmation? operation = null) {
        ArgumentNullException.ThrowIfNull(confirm);
        if (operation is not null and not TrackingConfirmation.Insert and not TrackingConfirmation.Update)
            throw new ArgumentOutOfRangeException(nameof(operation));
        return Add(new RuntimeConfirmationOption<TOriginal>(confirm, operation));
    }
    /// <summary>Configures confirmation to construct a fresh original value.</summary>
    public RuntimeTrackingOptions<TOriginal> ConstructOnConfirm(ConstructorInfo? constructor = null) => ConfirmUsing(TrackingConstruction.Constructor<TOriginal>(constructor));
    /// <summary>Constructs an original only on insertion. Updates keep normal in-place snapshot confirmation.</summary>
    public RuntimeTrackingOptions<TOriginal> ConstructOnInsert(ConstructorInfo? constructor = null) => ConfirmUsing(TrackingConstruction.Constructor<TOriginal>(constructor), TrackingConfirmation.Insert);

    /// <summary>Treats a null original reference as unavailable.</summary>
    public RuntimeTrackingOptions<TOriginal> UseNullAsMissingOriginal()
        => Add(new RuntimeOriginalStorageOption<TOriginal>(new RuntimeNullOriginalStorage<TOriginal>()));

    internal RuntimeTrackingOptions<TOriginal> CloneUnfrozen()
    {
        var clone = new RuntimeTrackingOptions<TOriginal>(includeOriginalMembers: false);
        for (int i = 0; i < _options.Count; i++)
        {
            IRuntimeTrackingOption<TOriginal> option = _options[i];
            clone._options.Add(option is IRuntimeTrackingOptionCloneable<TOriginal> cloneable
                ? cloneable.CloneOption(clone.Changing)
                : option);
        }
        return clone;
    }

    internal RuntimeTrackingRegistration<TOriginal, TEdit> GetBindingRegistration<TEdit>() {
        if (_options.Contains(Binding.RuntimeBindingOption<TOriginal>.Instance)) return GetRegistration<TEdit>();
        _frozen = true;
        var binding = Volatile.Read(ref _bindingOptions);
        if (binding is null)
            lock (_registrations) {
                binding = _bindingOptions;
                if (binding is null) {
                    binding = CloneUnfrozen();
                    binding.Add(Binding.RuntimeBindingOption<TOriginal>.Instance);
                    Volatile.Write(ref _bindingOptions, binding);
                }
            }
        return binding.GetRegistration<TEdit>();
    }

    /// <summary>Gets or creates a registration for an interface contract.</summary>
    public RuntimeTrackingRegistration<TOriginal, TEdit> GetRegistration<TEdit>()
    {
        Type contract = typeof(TEdit);
        if (!contract.IsInterface)
            throw new InvalidOperationException($"Generated tracking contract {contract} must be an interface. Use a concrete T directly with TrackingList<T> when you own its construction.");

        _frozen = true;
        Lazy<object> registration = _registrations.GetOrAdd(contract, _ => new Lazy<object>(
            () => RuntimeTrackingRegistration<TOriginal, TEdit>.Build(BuildDefinition(contract)),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return (RuntimeTrackingRegistration<TOriginal, TEdit>)registration.Value;
    }

    internal RuntimeTrackingTypeDefinition<TOriginal> BuildDefinition(Type contract)
    {
        var type = new RuntimeTrackingTypeDefinition<TOriginal>(contract);
        for (int i = 0; i < _options.Count; i++) _options[i].Apply(type);
        new RuntimeInterfaceOption<TOriginal>(contract).Apply(type);
        new RuntimeInterfaceOption<TOriginal>(typeof(ITrackingItem<TOriginal>)).Apply(type);
        type.Validate();
        return type;
    }

    private void Changing()
    {
        if (_frozen) throw new InvalidOperationException("RuntimeTrackingOptions are frozen after first materialization.");
    }
}

/// <summary>Provides ordered options for one generated member.</summary>
public sealed class RuntimeTrackingMemberOptions<TOriginal, TValue> : IRuntimeTrackingOption<TOriginal>, IRuntimeTrackingOptionCloneable<TOriginal>
{
    private readonly string _name;
    private readonly Action _changing;
    private Action<ITrackingItem<TOriginal>>? _confirmed;
    private Action<ITrackingItem<TOriginal>>? _reverted;
    /// <summary>Gets the source member name.</summary>
    public string Name => _name;
    private readonly List<Action<RuntimeTrackingMemberConfigurator>> _changes = [];

    internal RuntimeTrackingMemberOptions(string name, Action changing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _changing = changing;
    }

    /// <summary>Makes the member read-only.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> ReadOnly() => Change(static member => member.ReadOnly());
    /// <summary>Enables nested editing.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> NestedEdit(NestedEditMode mode = NestedEditMode.InPlace) => Change(member => member.NestedEdit(mode));
    /// <summary>Stores the member directly on the generated item.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> Direct() => Change(static member => member.Direct());
    /// <summary>Stores a direct value in the edit snapshot.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> SnapshotValue() => Change(static member => member.SnapshotValue());
    /// <summary>Creates a member value when editing begins.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> CreateOnEdit(Func<TValue> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        return Change(member => member.CreateOnEdit(factory));
    }
    /// <summary>Removes the member from generation.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> Ignore() => Change(static member => member.Ignore());
    /// <summary>Sets whether a CLR property is exposed.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> Expose(bool exposed = true) => Change(member => member.Expose(exposed));
    /// <summary>Sets whether runtime access includes the member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> RuntimeAccess(bool enabled = true) => Change(member => member.RuntimeAccess(enabled));
    /// <summary>Sets whether parameter projection includes the member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> Parameters(bool enabled = true) => Change(member => member.Parameters(enabled));

    /// <summary>Registers a policy that runs when the member is confirmed.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> OnConfirm(Action<ITrackingItem<TOriginal>> policy) {
        ArgumentNullException.ThrowIfNull(policy);
        _changing();
        _confirmed += policy;
        return this;
    }

    /// <summary>Registers a policy that runs when the member is reverted.</summary>
    public RuntimeTrackingMemberOptions<TOriginal, TValue> OnRevert(Action<ITrackingItem<TOriginal>> policy) {
        ArgumentNullException.ThrowIfNull(policy);
        _changing();
        _reverted += policy;
        return this;
    }

    /// <inheritdoc/>
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        RuntimeTrackingMemberDefinition<TOriginal> member = type.GetOrAddMember(_name, typeof(TValue));
        RuntimeTrackingMemberConfigurator configurator = RuntimeTrackingMemberConfigurator.Create(type, member);
        for (int i = 0; i < _changes.Count; i++) _changes[i](configurator);
        type.ConfirmedPolicy += _confirmed;
        type.RevertedPolicy += _reverted;
    }

    private RuntimeTrackingMemberOptions<TOriginal, TValue> Change(Action<RuntimeTrackingMemberConfigurator> change)
    {
        _changing();
        _changes.Add(change);
        return this;
    }

    IRuntimeTrackingOption<TOriginal> IRuntimeTrackingOptionCloneable<TOriginal>.CloneOption(Action changing)
    {
        var clone = new RuntimeTrackingMemberOptions<TOriginal, TValue>(_name, changing) { _confirmed = _confirmed, _reverted = _reverted };
        clone._changes.AddRange(_changes);
        return clone;
    }
}

/// <summary>Applies configuration to a generated member.</summary>
public sealed class RuntimeTrackingMemberConfigurator
{
    private readonly Action _ignore;
    private readonly Action _readOnly;
    private readonly Action<NestedEditMode> _nested;
    private readonly Action _direct;
    private readonly Action _snapshotValue;
    private readonly Action<bool> _expose;
    private readonly Action<bool> _runtimeAccess;
    private readonly Action<bool> _parameters;
    private readonly Action<Delegate> _createOnEdit;

    private RuntimeTrackingMemberConfigurator(Action ignore, Action readOnly, Action<NestedEditMode> nested, Action direct, Action snapshotValue, Action<bool> expose, Action<bool> runtimeAccess, Action<bool> parameters, Action<Delegate> createOnEdit)
    {
        _ignore = ignore;
        _readOnly = readOnly;
        _nested = nested;
        _direct = direct;
        _snapshotValue = snapshotValue;
        _expose = expose;
        _runtimeAccess = runtimeAccess;
        _parameters = parameters;
        _createOnEdit = createOnEdit;
    }

    /// <summary>Removes the member from generation.</summary>
    public void Ignore() => _ignore();
    /// <summary>Makes the member read-only.</summary>
    public void ReadOnly() => _readOnly();
    /// <summary>Enables nested editing.</summary>
    public void NestedEdit(NestedEditMode mode = NestedEditMode.InPlace) => _nested(mode);
    /// <summary>Stores the member directly on the generated item.</summary>
    public void Direct() => _direct();
    /// <summary>Stores a direct value in the edit snapshot.</summary>
    public void SnapshotValue() => _snapshotValue();
    /// <summary>Sets whether a CLR property is exposed.</summary>
    public void Expose(bool exposed = true) => _expose(exposed);
    /// <summary>Sets whether runtime access includes the member.</summary>
    public void RuntimeAccess(bool enabled = true) => _runtimeAccess(enabled);
    /// <summary>Sets whether parameter projection includes the member.</summary>
    public void Parameters(bool enabled = true) => _parameters(enabled);
    /// <summary>Creates a member value when editing begins.</summary>
    public void CreateOnEdit<TValue>(Func<TValue> factory) => _createOnEdit(factory);

    internal static RuntimeTrackingMemberConfigurator Create<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, RuntimeTrackingMemberDefinition<TOriginal> member)
    {
        return new RuntimeTrackingMemberConfigurator(
            () => type.RemoveMember(member.Name),
            () => MakeReadOnly(member),
            mode => MakeNested(member, mode),
            () => member.Emitter = new RuntimeDirectFieldEmitter<TOriginal>(),
            () => member.Emitter = new RuntimeDirectSnapshotEmitter<TOriginal>(),
            exposed => member.ExposeProperty = exposed,
            enabled => member.IncludeInRuntimeAccess = enabled,
            enabled => member.IncludeInParameters = enabled,
            factory => {
                if (factory.GetType() != typeof(Func<>).MakeGenericType(member.ValueType))
                    throw new ArgumentException($"Factory must return {member.ValueType} for member '{member.Name}'.");
                member.CreateOnEdit = factory;
            });
    }

    private static void MakeReadOnly<TOriginal>(RuntimeTrackingMemberDefinition<TOriginal> member)
    {
        if (member.Emitter is null) return;
        if (member.Emitter is IRuntimeReadOnlyEmitter<TOriginal> configurable) member.Emitter = configurable.AsReadOnly();
        else throw new InvalidOperationException($"Runtime member '{member.Name}' cannot be made read-only from emitter {member.Emitter.GetType()}.");
    }

    private static void MakeNested<TOriginal>(RuntimeTrackingMemberDefinition<TOriginal> member, NestedEditMode mode)
    {
        if (member.Emitter is IRuntimeNestedEmitter<TOriginal> configurable) member.Emitter = configurable.WithNestedEdit(mode);
        else throw new InvalidOperationException($"Runtime member '{member.Name}' does not support nested editing.");
    }
}

internal sealed class RuntimeOriginalDiscoveryOption<TOriginal> : IRuntimeTrackingOption<TOriginal>
{
    internal static readonly RuntimeOriginalDiscoveryOption<TOriginal> Instance = new();
    private RuntimeOriginalDiscoveryOption() { }

    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (PropertyInfo property in typeof(TOriginal).GetProperties(flags))
        {
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0) continue;
            Add(type, property);
        }
        foreach (FieldInfo field in typeof(TOriginal).GetFields(flags))
        {
            if (field.IsStatic) continue;
            Add(type, field);
        }
    }

    private static void Add(RuntimeTrackingTypeDefinition<TOriginal> type, MemberInfo source)
    {
        RuntimeOriginalMemberAccess access = RuntimeOriginalMemberAccess.Create(source);
        RuntimeTrackingMemberDefinition<TOriginal> member = type.GetOrAddMember(source.Name, access.ValueType);
        member.Emitter ??= access.CanWrite
                ? new RuntimeOriginalSnapshotEmitter<TOriginal>(access)
                : new RuntimeOriginalReadOnlyEmitter<TOriginal>(access);
        RuntimeTrackingMetadataSources.AddOriginal(member, source);
        RuntimeTrackingAttributeApplication.Apply(type, member, source);
    }
}

internal sealed class RuntimeInterfaceOption<TOriginal>(Type contract) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        foreach (Type current in InterfaceGraph(contract))
        {
            if (!type.TryMarkInterfaceSource(current)) continue;
            type.RequireInterface(current);
            if (RuntimeBuiltInInterfaceOptions.TryApply(type, current)) continue;

            EventInfo[] events = current.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (events.Length != 0)
                throw new InvalidOperationException($"Tracking contract {current} declares event {events[0].Name}, but no option supplied an event emitter.");

            foreach (PropertyInfo property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true) continue;
                if (property.GetIndexParameters().Length != 0)
                    throw new InvalidOperationException($"Tracking contract {current} declares indexer {property}; configure a custom type emitter for it.");
                RuntimeTrackingMemberDefinition<TOriginal> member = type.GetOrAddMember(property.Name, property.PropertyType);
                member.AddRequirement(property);
                bool inherited = current != contract;
                member.AddMetadataSource(property, inherited);
                RuntimeTrackingAttributeApplication.Apply(type, member, property, inherited);
            }

            foreach (MethodInfo method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsStatic || method.IsSpecialName || !method.IsAbstract) continue;
                RuntimeTrackingMethodDefinition<TOriginal> requirement = type.GetOrAddMethod(method);
                if (requirement.Emitter is null && current.IsAssignableFrom(typeof(TOriginal)))
                    requirement.Emitter = new RuntimeOriginalForwardMethodEmitter<TOriginal>();
            }
        }
    }

    private static IEnumerable<Type> InterfaceGraph(Type root)
    {
        var seen = new HashSet<Type>();
        foreach (Type current in Visit(root, seen)) yield return current;
    }

    private static IEnumerable<Type> Visit(Type current, HashSet<Type> seen)
    {
        Type[] parents = current.GetInterfaces();
        for (int i = 0; i < parents.Length; i++)
            foreach (Type parent in Visit(parents[i], seen)) yield return parent;
        if (seen.Add(current)) yield return current;
    }
}

internal sealed class RuntimeNewItemOption<TOriginal, TEdit>(Action<TEdit> initialize) : IRuntimeTrackingOption<TOriginal> {
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) => type.NewItemInitializer = initialize;
}

internal sealed class RuntimeOriginalStorageOption<TOriginal>(IRuntimeOriginalStorageEmitter<TOriginal> storage) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) => type.OriginalStorage = storage;
}

internal sealed class RuntimeConfirmationOption<TOriginal>(Func<ITrackingItem<TOriginal>, TOriginal> confirm, TrackingConfirmation? operation) : IRuntimeTrackingOption<TOriginal> {
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) {
        new RuntimeInterfaceOption<TOriginal>(typeof(ITrackingItem<TOriginal>)).Apply(type);
        if (operation is null or TrackingConfirmation.Insert) type.InsertConfirmation = confirm;
        if (operation is null or TrackingConfirmation.Update) type.UpdateConfirmation = confirm;
    }
}

internal sealed class RuntimeLifecycleOption<TOriginal>(Action<ITrackingItem<TOriginal>> policy, bool confirm) : IRuntimeTrackingOption<TOriginal> {
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) {
        new RuntimeInterfaceOption<TOriginal>(typeof(ITrackingItem<TOriginal>)).Apply(type);
        if (confirm) type.ConfirmedPolicy += policy;
        else type.RevertedPolicy += policy;
    }
}
