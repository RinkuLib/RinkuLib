using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

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

    /// <summary>Adds options for a member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Member(string name, Type valueType)
    {
        var option = new RuntimeTrackingMemberOptions<TOriginal>(name, valueType, Changing);
        Add(option);
        return option;
    }

    /// <summary>Adds typed options for a member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Member<TValue>(string name) => Member(name, typeof(TValue));

    /// <summary>Sets the factory for provisional original values.</summary>
    public RuntimeTrackingOptions<TOriginal> WithNewOriginal(Func<TOriginal> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Add(new RuntimeNewOriginalOption<TOriginal>(factory));
    }

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
        type.NewOriginalFactory ??= RuntimeNewOriginalFactory<TOriginal>.Default;
        type.Validate();
        return type;
    }

    private void Changing()
    {
        if (_frozen) throw new InvalidOperationException("RuntimeTrackingOptions are frozen after first materialization.");
    }
}

/// <summary>Provides ordered options for one generated member.</summary>
public sealed class RuntimeTrackingMemberOptions<TOriginal> : IRuntimeTrackingOption<TOriginal>, IRuntimeTrackingOptionCloneable<TOriginal>
{
    private readonly string _name;
    private readonly Type _valueType;
    private readonly Action _changing;
    private readonly List<Action<RuntimeTrackingMemberConfigurator>> _changes = [];

    internal RuntimeTrackingMemberOptions(string name, Type valueType, Action changing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);
        _name = name;
        _valueType = valueType;
        _changing = changing;
    }

    /// <summary>Makes the member read-only.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> ReadOnly() => Change(static member => member.ReadOnly());
    /// <summary>Enables nested editing.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> NestedEdit(NestedEditMode mode = NestedEditMode.InPlace) => Change(member => member.NestedEdit(mode));
    /// <summary>Stores the member directly on the generated item.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Direct() => Change(static member => member.Direct());
    /// <summary>Stores a direct value in the edit snapshot.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> SnapshotValue() => Change(static member => member.SnapshotValue());
    /// <summary>Removes the member from generation.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Ignore() => Change(static member => member.Ignore());
    /// <summary>Sets whether a CLR property is exposed.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Expose(bool exposed = true) => Change(member => member.Expose(exposed));
    /// <summary>Sets whether runtime access includes the member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> RuntimeAccess(bool enabled = true) => Change(member => member.RuntimeAccess(enabled));
    /// <summary>Sets whether parameter projection includes the member.</summary>
    public RuntimeTrackingMemberOptions<TOriginal> Parameters(bool enabled = true) => Change(member => member.Parameters(enabled));

    /// <inheritdoc/>
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        RuntimeTrackingMemberDefinition<TOriginal> member = type.GetOrAddMember(_name, _valueType);
        RuntimeTrackingMemberConfigurator configurator = RuntimeTrackingMemberConfigurator.Create(type, member);
        for (int i = 0; i < _changes.Count; i++) _changes[i](configurator);
    }

    private RuntimeTrackingMemberOptions<TOriginal> Change(Action<RuntimeTrackingMemberConfigurator> change)
    {
        _changing();
        _changes.Add(change);
        return this;
    }

    IRuntimeTrackingOption<TOriginal> IRuntimeTrackingOptionCloneable<TOriginal>.CloneOption(Action changing)
    {
        var clone = new RuntimeTrackingMemberOptions<TOriginal>(_name, _valueType, changing);
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

    private RuntimeTrackingMemberConfigurator(
        Action ignore,
        Action readOnly,
        Action<NestedEditMode> nested,
        Action direct,
        Action snapshotValue,
        Action<bool> expose,
        Action<bool> runtimeAccess,
        Action<bool> parameters)
    {
        _ignore = ignore;
        _readOnly = readOnly;
        _nested = nested;
        _direct = direct;
        _snapshotValue = snapshotValue;
        _expose = expose;
        _runtimeAccess = runtimeAccess;
        _parameters = parameters;
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

    internal static RuntimeTrackingMemberConfigurator Create<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, RuntimeTrackingMemberDefinition<TOriginal> member)
    {
        return new RuntimeTrackingMemberConfigurator(
            () => type.RemoveMember(member.Name),
            () => MakeReadOnly(member),
            mode => MakeNested(member, mode),
            () => { member.Emitter = new RuntimeDirectFieldEmitter<TOriginal>(); member.IncludeInParameters = false; },
            () => { member.Emitter = new RuntimeDirectSnapshotEmitter<TOriginal>(); member.IncludeInParameters = false; },
            exposed => member.ExposeProperty = exposed,
            enabled => member.IncludeInRuntimeAccess = enabled,
            enabled => member.IncludeInParameters = enabled);
    }

    private static void MakeReadOnly<TOriginal>(RuntimeTrackingMemberDefinition<TOriginal> member)
    {
        if (member.Emitter is RuntimeOriginalSnapshotEmitter<TOriginal> original)
        {
            member.Emitter = original.AsReadOnly();
            return;
        }
        if (member.Emitter is RuntimeNestedSnapshotEmitter<TOriginal> nested)
        {
            member.Emitter = new RuntimeOriginalReadOnlyEmitter<TOriginal>(nested.Access);
            return;
        }
        if (member.Emitter is null) return;
        throw new InvalidOperationException($"Runtime member '{member.Name}' cannot be made original-read-only from emitter {member.Emitter.GetType()}.");
    }

    private static void MakeNested<TOriginal>(RuntimeTrackingMemberDefinition<TOriginal> member, NestedEditMode mode)
    {
        RuntimeOriginalMemberAccess access = member.Emitter switch
        {
            RuntimeOriginalSnapshotEmitter<TOriginal> original => original.Access,
            RuntimeOriginalReadOnlyEmitter<TOriginal> readOnly => readOnly.Access,
            RuntimeNestedSnapshotEmitter<TOriginal> nested => nested.Access,
            _ => throw new InvalidOperationException($"Runtime member '{member.Name}' must be original-backed before nested editing can be configured.")
        };
        member.Emitter = new RuntimeNestedSnapshotEmitter<TOriginal>(access, mode);
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
        if (member.Emitter is null)
            member.Emitter = access.CanWrite
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

internal sealed class RuntimeNewOriginalOption<TOriginal>(Func<TOriginal> factory) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) => type.NewOriginalFactory = factory;
}

internal sealed class RuntimeOriginalStorageOption<TOriginal>(IRuntimeOriginalStorageEmitter<TOriginal> storage) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type) => type.OriginalStorage = storage;
}

internal static class RuntimeNewOriginalFactory<TOriginal>
{
    internal static readonly Func<TOriginal>? Default = Build();

    private static Func<TOriginal>? Build()
    {
        Type type = typeof(TOriginal);
        if (type.IsAbstract || type.IsInterface) return null;
        var method = new DynamicMethod($"RuntimeTracking_New_{type.Name}", type, Type.EmptyTypes, typeof(RuntimeNewOriginalFactory<TOriginal>).Module, true);
        ILGenerator il = method.GetILGenerator();
        if (type.IsValueType)
        {
            LocalBuilder value = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<TOriginal>>();
        }
        ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null) return null;
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<TOriginal>>();
    }
}
