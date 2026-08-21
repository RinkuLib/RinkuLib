using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Describes the generated type selected by ordered options.</summary>
public sealed class RuntimeTrackingTypeDefinition<TOriginal>
{
    private readonly List<RuntimeTrackingMemberDefinition<TOriginal>> _members = [];
    private readonly List<Type> _interfaces = [];
    private readonly List<IRuntimeTrackingTypeEmitter<TOriginal>> _typeEmitters = [];
    private readonly List<RuntimeTrackingMethodDefinition<TOriginal>> _methods = [];
    private readonly HashSet<Type> _interfaceSources = [];

    internal RuntimeTrackingTypeDefinition(Type requestedContract)
    {
        RequestedContract = requestedContract;
        OriginalStorage = RuntimeRequiredOriginalStorage<TOriginal>.Instance;
    }

    /// <summary>Gets the requested consumer contract.</summary>
    public Type RequestedContract { get; }
    /// <summary>Gets the generated member definitions.</summary>
    public IReadOnlyList<RuntimeTrackingMemberDefinition<TOriginal>> Members => _members;
    /// <summary>Gets the required interfaces.</summary>
    public IReadOnlyList<Type> Interfaces => _interfaces;
    /// <summary>Gets the type emitters.</summary>
    public IReadOnlyList<IRuntimeTrackingTypeEmitter<TOriginal>> TypeEmitters => _typeEmitters;
    /// <summary>Gets the generated method definitions.</summary>
    public IReadOnlyList<RuntimeTrackingMethodDefinition<TOriginal>> Methods => _methods;
    /// <summary>Gets or sets the factory for provisional original values.</summary>
    public Func<TOriginal>? NewOriginalFactory { get; set; }
    /// <summary>Gets or sets the original storage emitter.</summary>
    public IRuntimeOriginalStorageEmitter<TOriginal> OriginalStorage { get; set; }

    /// <summary>Finds a member by name.</summary>
    public RuntimeTrackingMemberDefinition<TOriginal>? FindMember(string name)
    {
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _members[i];
        return null;
    }

    /// <summary>Gets or adds a member definition.</summary>
    public RuntimeTrackingMemberDefinition<TOriginal> GetOrAddMember(string name, Type valueType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);
        RuntimeTrackingMemberDefinition<TOriginal>? current = FindMember(name);
        if (current is not null)
        {
            if (current.ValueType != valueType)
                throw new InvalidOperationException($"Runtime member '{name}' already exists as {current.ValueType}, not {valueType}.");
            return current;
        }

        var member = new RuntimeTrackingMemberDefinition<TOriginal>(name, valueType);
        _members.Add(member);
        return member;
    }

    /// <summary>Removes a member by name.</summary>
    public bool RemoveMember(string name)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (!string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            _members.RemoveAt(i);
            return true;
        }
        return false;
    }


    internal bool TryMarkInterfaceSource(Type interfaceType) => _interfaceSources.Add(interfaceType);

    /// <summary>Adds a required interface.</summary>
    public void RequireInterface(Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        if (!interfaceType.IsInterface) throw new ArgumentException($"{interfaceType} is not an interface.", nameof(interfaceType));
        if (!_interfaces.Contains(interfaceType)) _interfaces.Add(interfaceType);
    }


    internal RuntimeTrackingMethodDefinition<TOriginal> GetOrAddMethod(MethodInfo requirement)
    {
        for (int i = 0; i < _methods.Count; i++)
            if (_methods[i].Requirement == requirement) return _methods[i];
        var method = new RuntimeTrackingMethodDefinition<TOriginal>(requirement);
        _methods.Add(method);
        return method;
    }

    /// <summary>Adds a type emitter.</summary>
    public void AddTypeEmitter(IRuntimeTrackingTypeEmitter<TOriginal> emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        _typeEmitters.Add(emitter);
    }

    /// <summary>Validates the completed definition.</summary>
    public void Validate()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = _members[i];
            if (!names.Add(member.Name)) throw new InvalidOperationException($"Duplicate runtime member '{member.Name}'.");
            member.Validate();
        }
        for (int i = 0; i < _methods.Count; i++) _methods[i].Validate();
    }
}

/// <summary>Describes one generated member.</summary>
public sealed class RuntimeTrackingMemberDefinition<TOriginal>
{
    private readonly List<RuntimeTrackingMetadataSource> _metadataSources = [];
    private readonly List<RuntimeInterfacePropertyRequirement> _requirements = [];

    internal RuntimeTrackingMemberDefinition(string name, Type valueType)
    {
        Name = name;
        ValueType = valueType;
    }

    /// <summary>Gets the member name.</summary>
    public string Name { get; }
    /// <summary>Gets the member value type.</summary>
    public Type ValueType { get; }
    /// <summary>Gets or sets whether a CLR property is exposed.</summary>
    public bool ExposeProperty { get; set; } = true;
    /// <summary>Gets or sets whether runtime access includes the member.</summary>
    public bool IncludeInRuntimeAccess { get; set; } = true;
    /// <summary>Gets or sets whether parameter projection includes the member.</summary>
    public bool IncludeInParameters { get; set; } = true;
    /// <summary>Gets or sets the selected member emitter.</summary>
    public RuntimeTrackingMemberEmitter<TOriginal>? Emitter { get; set; }
    /// <summary>Gets the metadata sources for the generated property.</summary>
    public IReadOnlyList<RuntimeTrackingMetadataSource> MetadataSources => _metadataSources;
    internal IReadOnlyList<RuntimeInterfacePropertyRequirement> Requirements => _requirements;

    /// <summary>Adds a property metadata source.</summary>
    public void AddMetadataSource(MemberInfo source, bool inheritedOnly = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var entry = new RuntimeTrackingMetadataSource(source, inheritedOnly);
        if (!_metadataSources.Contains(entry)) _metadataSources.Add(entry);
    }

    internal void AddRequirement(PropertyInfo property)
    {
        if (property.PropertyType != ValueType)
            throw new InvalidOperationException($"Interface property {property} requires {property.PropertyType}, while runtime member '{Name}' is {ValueType}.");
        _requirements.Add(new RuntimeInterfacePropertyRequirement(property));
    }

    internal void Validate()
    {
        RuntimeTrackingMemberEmitter<TOriginal> emitter = Emitter
            ?? throw new InvalidOperationException($"Runtime member '{Name}' ({ValueType}) has no emitter. Configure how it is read/stored before generation.");

        if (!emitter.CanRead)
            throw new InvalidOperationException($"Runtime member '{Name}' has no readable implementation.");

        for (int i = 0; i < _requirements.Count; i++)
        {
            RuntimeInterfacePropertyRequirement requirement = _requirements[i];
            if (requirement.NeedsSetter && !emitter.CanWrite)
                throw new InvalidOperationException($"Runtime member '{Name}' cannot satisfy setter {requirement.Property.DeclaringType}.{requirement.Property.Name}.");
        }
    }
}

/// <summary>Describes a property metadata source.</summary>
public readonly record struct RuntimeTrackingMetadataSource
{
    /// <summary>Creates a property metadata source.</summary>
    public RuntimeTrackingMetadataSource(MemberInfo source, bool inheritedOnly)
    {
        Source = source;
        InheritedOnly = inheritedOnly;
    }

    /// <summary>Gets the source member.</summary>
    public MemberInfo Source { get; }
    /// <summary>Gets whether only inherited attributes apply.</summary>
    public bool InheritedOnly { get; }
}

internal readonly record struct RuntimeInterfacePropertyRequirement(PropertyInfo Property)
{
    internal bool NeedsGetter => Property.GetMethod is not null;
    internal bool NeedsSetter => Property.SetMethod is not null;
}


/// <summary>Describes one generated method.</summary>
public sealed class RuntimeTrackingMethodDefinition<TOriginal>
{
    internal RuntimeTrackingMethodDefinition(MethodInfo requirement) => Requirement = requirement;
    /// <summary>Gets the required interface method.</summary>
    public MethodInfo Requirement { get; }
    /// <summary>Gets or sets the method emitter.</summary>
    public RuntimeTrackingMethodEmitter<TOriginal>? Emitter { get; set; }

    internal void Validate()
    {
        if (Emitter is null)
            throw new InvalidOperationException($"Interface method {Requirement} has no generated implementation.");
    }
}

/// <summary>Emits one generated method.</summary>
public abstract class RuntimeTrackingMethodEmitter<TOriginal>
{
    /// <summary>Emits a generated method.</summary>
    protected internal abstract MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index);
}
