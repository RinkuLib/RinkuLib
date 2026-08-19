using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading;

namespace Rinku.Tracking.Runtime;

// Canonical runtime-generation shape. The first registration freezes the option tree permanently.
/// <summary>Configures a generated tracking shape.</summary>
public sealed class RuntimeTrackingOptions<TOriginal> {
    private readonly List<RuntimeTrackingMemberOptions> _members = [];
    private readonly List<IRuntimeTrackingCapability<TOriginal>> _capabilities = [];
    private readonly List<IRuntimeTrackingTypeContributor<TOriginal>> _contributors = [];
    private readonly ConcurrentDictionary<Type, Lazy<object>> _registrationCache = new();
    private RuntimeTrackingContractMemberConvention<TOriginal> _contractMemberConvention = DefaultContractMemberConvention;
    private bool? _dynamicAccess;
    private bool? _notifications;
    private bool? _parameterDefault;
    private Type? _resolvedContract;
    private bool _frozen;
    internal RuntimeNewOriginalCall<TOriginal>? NewOriginalCall { get; private set; }

    /// <summary>Creates runtime tracking options.</summary>
    public RuntimeTrackingOptions(bool includeDefaultMembers = true) {
        if (includeDefaultMembers) IncludeOriginalMembers();
    }

    /// <summary>Gets configured members.</summary>
    public IReadOnlyList<RuntimeTrackingMemberOptions> Members => _members;
    /// <summary>Gets configured capabilities.</summary>
    public IReadOnlyList<IRuntimeTrackingCapability<TOriginal>> Capabilities => _capabilities;
    /// <summary>Gets whether the options are frozen.</summary>
    public bool IsFrozen => _frozen;
    /// <summary>Gets the dynamic-access override.</summary>
    public bool? DynamicAccessOverride => _dynamicAccess;
    /// <summary>Gets the notification override.</summary>
    public bool? NotificationsOverride => _notifications;
    internal IReadOnlyList<IRuntimeTrackingTypeContributor<TOriginal>> Contributors => _contributors;

    /// <summary>Gets or creates a typed member option.</summary>
    public RuntimeTrackingMemberOptions Member<T>(string name) => Member(name, typeof(T));

    /// <summary>Gets or creates a member option.</summary>
    public RuntimeTrackingMemberOptions Member(string name, Type valueType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
                if (_members[i].ValueType != valueType)
                    throw new InvalidOperationException($"Runtime member '{name}' already exists as {_members[i].ValueType}, not {valueType}.");
                return _members[i];
            }
        Changing();
        var member = new RuntimeTrackingMemberOptions(typeof(TOriginal), name, valueType, Changing);
        if (_parameterDefault.HasValue) member.Parameter(_parameterDefault.Value);
        _members.Add(member);
        return member;
    }

    /// <summary>Gets or creates a runtime-only member.</summary>
    public RuntimeTrackingMemberOptions RuntimeValue<T>(string name) => Member<T>(name).RuntimeValue();

    /// <summary>Finds a configured member.</summary>
    public RuntimeTrackingMemberOptions? FindMember(string name) {
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _members[i];
        return null;
    }

    /// <summary>Removes a configured member.</summary>
    public bool RemoveMember(string name) {
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
                Changing();
                _members.RemoveAt(i);
                return true;
            }
        return false;
    }

    /// <summary>Removes all configured members.</summary>
    public RuntimeTrackingOptions<TOriginal> ClearMembers() {
        if (_members.Count == 0) return this;
        Changing();
        _members.Clear();
        return this;
    }

    /// <summary>Includes members from the original type.</summary>
    public RuntimeTrackingOptions<TOriginal> IncludeOriginalMembers(bool exposeProperties = true, bool includeInRuntimeAccess = true) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        Type type = typeof(TOriginal);

        foreach (PropertyInfo property in type.GetProperties(flags)) {
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0) continue;
            RuntimeTrackingMemberOptions? existing = FindMember(property.Name);
            if (existing is not null && existing.ValueType != property.PropertyType)
                throw new InvalidOperationException($"Runtime member '{property.Name}' already exists as {existing.ValueType}, not {property.PropertyType}.");
            RuntimeTrackingMemberOptions option = existing ?? Member(property.Name, property.PropertyType);
            if (existing is null) {
                option.ExposeProperty = exposeProperties;
                option.IncludeInRuntimeAccess = includeInRuntimeAccess;
                option.BindDefault();
                option.ApplyAttributes(property.GetCustomAttributes(true));
            }
        }

        foreach (FieldInfo field in type.GetFields(flags)) {
            if (field.IsStatic) continue;
            RuntimeTrackingMemberOptions? existing = FindMember(field.Name);
            if (existing is not null) {
                if (existing.ValueType != field.FieldType)
                    throw new InvalidOperationException($"Runtime member '{field.Name}' already exists as {existing.ValueType}, not {field.FieldType}.");
                continue;
            }
            RuntimeTrackingMemberOptions option = Member(field.Name, field.FieldType);
            option.ExposeProperty = exposeProperties;
            option.IncludeInRuntimeAccess = includeInRuntimeAccess;
            option.BindDefault();
            option.ApplyAttributes(field.GetCustomAttributes(true));
        }
        return this;
    }


    // Sets the default direct/UseWith parameter visibility for the generated shape.
    // It also updates members that already exist; individual members can override it afterwards.
    /// <summary>Sets the parameter projection default.</summary>
    public RuntimeTrackingOptions<TOriginal> Parameters(bool enabled = true) {
        Changing();
        _parameterDefault = enabled;
        for (int i = 0; i < _members.Count; i++) _members[i].Parameter(enabled);
        return this;
    }

    /// <summary>Sets the runtime-access default.</summary>
    public RuntimeTrackingOptions<TOriginal> DynamicAccess(bool enabled = true) {
        Changing();
        _dynamicAccess = enabled;
        return this;
    }

    /// <summary>Sets the notification default.</summary>
    public RuntimeTrackingOptions<TOriginal> Notifications(bool enabled = true) {
        Changing();
        _notifications = enabled;
        return this;
    }

    /// <summary>Sets the contract-member convention.</summary>
    public RuntimeTrackingOptions<TOriginal> ContractMembers(RuntimeTrackingContractMemberConvention<TOriginal> convention) {
        ArgumentNullException.ThrowIfNull(convention);
        Changing();
        _contractMemberConvention = convention;
        return this;
    }

    /// <summary>Adds a generated capability.</summary>
    public RuntimeTrackingOptions<TOriginal> AddCapability(IRuntimeTrackingCapability<TOriginal> capability) {
        ArgumentNullException.ThrowIfNull(capability);
        Changing();
        _capabilities.Add(capability);
        return this;
    }

    /// <summary>Adds metadata reading.</summary>
    public RuntimeTrackingOptions<TOriginal> MetadataReader<TMetadata>()
        => AddCapability(new RuntimeMetadataReaderCapability<TOriginal, TMetadata>());

    /// <summary>Adds metadata writing.</summary>
    public RuntimeTrackingOptions<TOriginal> MetadataWriter<TMetadata>()
        => AddCapability(new RuntimeMetadataWriterCapability<TOriginal, TMetadata>());

    /// <summary>Adds metadata reading and writing.</summary>
    public RuntimeTrackingOptions<TOriginal> Metadata<TMetadata>()
        => AddCapability(new RuntimeMetadataCapability<TOriginal, TMetadata>());

    /// <summary>Adds synchronous validation.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit>(RuntimeValidationHandler<TEdit> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit>(validate));

    /// <summary>Adds synchronous validation from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit>(validate, target));

    /// <summary>Adds contextual validation.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit, TContext>(RuntimeContextValidationHandler<TEdit, TContext> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeContextValidationCapability<TOriginal, TEdit, TContext>(validate));

    /// <summary>Adds contextual validation from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> ContextValidation<TEdit, TContext>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeContextValidationCapability<TOriginal, TEdit, TContext>(validate, target));

    /// <summary>Adds asynchronous validation.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit>(RuntimeAsyncValidationHandler<TEdit> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit>(validate));

    /// <summary>Adds asynchronous validation from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit>(validate, target));

    /// <summary>Adds contextual asynchronous validation.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit, TContext>(RuntimeAsyncContextValidationHandler<TEdit, TContext> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncContextValidationCapability<TOriginal, TEdit, TContext>(validate));

    /// <summary>Adds contextual asynchronous validation from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncContextValidation<TEdit, TContext>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncContextValidationCapability<TOriginal, TEdit, TContext>(validate, target));

    /// <summary>Adds validation with metadata.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit, TMetadata>(RuntimeValidationHandler<TEdit, TMetadata> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit, TMetadata>(validate));

    /// <summary>Adds validation with metadata from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit, TMetadata>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit, TMetadata>(validate, target));

    /// <summary>Adds contextual validation with metadata.</summary>
    public RuntimeTrackingOptions<TOriginal> Validation<TEdit, TContext, TMetadata>(RuntimeValidationHandler<TEdit, TContext, TMetadata> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit, TContext, TMetadata>(validate));

    /// <summary>Adds contextual validation with metadata from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> ContextValidation<TEdit, TContext, TMetadata>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeValidationCapability<TOriginal, TEdit, TContext, TMetadata>(validate, target));

    /// <summary>Adds asynchronous validation with metadata.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit, TMetadata>(RuntimeAsyncValidationHandler<TEdit, TMetadata> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit, TMetadata>(validate));

    /// <summary>Adds asynchronous validation with metadata from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit, TMetadata>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit, TMetadata>(validate, target));

    /// <summary>Adds contextual asynchronous validation with metadata.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit, TContext, TMetadata>(RuntimeAsyncValidationHandler<TEdit, TContext, TMetadata> validate)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit, TContext, TMetadata>(validate));

    /// <summary>Adds contextual asynchronous validation with metadata from a method.</summary>
    public RuntimeTrackingOptions<TOriginal> AsyncValidation<TEdit, TContext, TMetadata>(MethodInfo validate, object? target = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => AddCapability(new RuntimeAsyncValidationCapability<TOriginal, TEdit, TContext, TMetadata>(validate, target));

    /// <summary>Adds a type contributor.</summary>
    public RuntimeTrackingOptions<TOriginal> WithContributor(IRuntimeTrackingTypeContributor<TOriginal> contributor) {
        ArgumentNullException.ThrowIfNull(contributor);
        Changing();
        _contributors.Add(contributor);
        return this;
    }

    /// <summary>Sets the new-item factory.</summary>
    public RuntimeTrackingOptions<TOriginal> WithNewOriginal(Func<TOriginal> factory) {
        ArgumentNullException.ThrowIfNull(factory);
        Changing();
        NewOriginalCall = new(factory);
        return this;
    }

    /// <summary>Sets the new-item factory method.</summary>
    public RuntimeTrackingOptions<TOriginal> WithNewOriginal(MethodInfo method, object? target = null) {
        ArgumentNullException.ThrowIfNull(method);
        Changing();
        NewOriginalCall = new(method, target);
        return this;
    }

    /// <summary>Sets a constructor-based new-item factory.</summary>
    public RuntimeTrackingOptions<TOriginal> WithNewOriginal(ConstructorInfo constructor) {
        ArgumentNullException.ThrowIfNull(constructor);
        Changing();
        NewOriginalCall = new(constructor);
        return this;
    }

    /// <summary>Sets a method-based new-item factory.</summary>
    public RuntimeTrackingOptions<TOriginal> WithNewOriginal(MethodBase method, object? target = null) {
        ArgumentNullException.ThrowIfNull(method);
        return method switch {
            MethodInfo info => WithNewOriginal(info, target),
            ConstructorInfo constructor when target is null => WithNewOriginal(constructor),
            ConstructorInfo => throw new ArgumentException("A constructor cannot have a target instance.", nameof(target)),
            _ => throw new ArgumentException($"Unsupported method base {method}.", nameof(method))
        };
    }

    /// <summary>Uses the default new-item factory.</summary>
    public RuntimeTrackingOptions<TOriginal> WithDefaultNewOriginal() {
        Changing();
        NewOriginalCall = RuntimeNewOriginalCall<TOriginal>.Default();
        return this;
    }

    internal RuntimeTrackingOptions<TOriginal> CloneUnfrozen() {
        var clone = new RuntimeTrackingOptions<TOriginal>(false) {
            _contractMemberConvention = _contractMemberConvention,
            _dynamicAccess = _dynamicAccess,
            _notifications = _notifications,
            _parameterDefault = _parameterDefault,
            _resolvedContract = _resolvedContract,
            NewOriginalCall = NewOriginalCall
        };
        for (int i = 0; i < _members.Count; i++) clone._members.Add(_members[i].Clone(clone.Changing));
        clone._capabilities.AddRange(_capabilities);
        clone._contributors.AddRange(_contributors);
        return clone;
    }

    internal bool IsResolvedFor(Type contract) => _resolvedContract == contract;
    internal bool IsResolved => _resolvedContract is not null;
    internal Type? ResolvedContract => _resolvedContract;
    internal void MarkResolved(Type contract) => _resolvedContract = contract;

    internal void ApplyContractMemberConvention(RuntimeTrackingContractMemberContext<TOriginal> member)
        => _contractMemberConvention(member);

    internal bool ResolveDynamicAccess(Type exposedContract) {
        bool required = typeof(IRuntimeMemberAccess).IsAssignableFrom(exposedContract);
        if (required && _dynamicAccess == false)
            throw new InvalidOperationException($"Runtime contract {exposedContract} requires {nameof(IRuntimeMemberAccess)}, so dynamic access cannot be disabled.");
        return required || _dynamicAccess == true;
    }

    internal bool ResolveNotifications(Type exposedContract) {
        bool required = typeof(System.ComponentModel.INotifyPropertyChanged).IsAssignableFrom(exposedContract);
        if (required && _notifications == false)
            throw new InvalidOperationException($"Runtime contract {exposedContract} requires {nameof(System.ComponentModel.INotifyPropertyChanged)}, so notifications cannot be disabled.");
        return required || _notifications == true;
    }

    internal void Freeze() => _frozen = true;

    internal IRuntimeTrackingMember[] BuildMembers() {
        var result = new List<IRuntimeTrackingMember>(_members.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeTrackingMemberOptions option in _members) {
            IRuntimeTrackingMember? member = option.Build();
            if (member is null) continue;
            if (!names.Add(member.Name)) throw new InvalidOperationException($"Duplicate runtime member '{member.Name}'.");
            result.Add(member);
        }
        return result.ToArray();
    }

    internal RuntimeNewOriginalCall<TOriginal>? ResolveNewOriginalCall() {
        if (NewOriginalCall is not null) return NewOriginalCall;
        if (typeof(TOriginal).IsValueType) return RuntimeNewOriginalCall<TOriginal>.Default();
        ConstructorInfo? ctor = typeof(TOriginal).GetConstructor(Type.EmptyTypes);
        return ctor is null ? null : new RuntimeNewOriginalCall<TOriginal>(ctor);
    }

    internal RuntimeTrackingRegistration<TOriginal, TEdit> GetRegistration<TEdit>()
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        Freeze();
        Lazy<object> lazy = _registrationCache.GetOrAdd(typeof(TEdit), _ => new Lazy<object>(() => {
            RuntimeTrackingOptions<TOriginal> resolved = IsResolvedFor(typeof(TEdit))
                ? this
                : RuntimeTrackingContract<TOriginal, TEdit>.Resolve(this);
            return RuntimeTrackingRegistration<TOriginal, TEdit>.Build(resolved);
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        return (RuntimeTrackingRegistration<TOriginal, TEdit>)lazy.Value;
    }

    private void Changing() {
        if (_frozen) throw new InvalidOperationException("RuntimeTrackingOptions are frozen after their first materialization. Create a new option tree for a different generated shape.");
    }

    private static void DefaultContractMemberConvention(RuntimeTrackingContractMemberContext<TOriginal> context) {
        if (!context.Member.Ignore && !context.Member.IsConfigured) context.Member.TryBindDefault();
    }
}
