using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rinku.Tracking.Runtime;

/// <summary>Applies configuration to one generated member.</summary>
public interface IRuntimeTrackingMemberAttribute {
    /// <summary>Applies the member configuration.</summary>
    void Apply(RuntimeTrackingMemberOptions member);
}

/// <summary>Base class for generated member attributes.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public abstract class RuntimeTrackingMemberAttribute : Attribute, IRuntimeTrackingMemberAttribute {
    /// <summary>Applies the member attribute.</summary>
    public abstract void Apply(RuntimeTrackingMemberOptions member);
}

/// <summary>Configures one generated tracking member.</summary>
public sealed class RuntimeTrackingMemberOptions {
    private readonly Type _originalType;
    private readonly Action _changing;
    private RuntimeTrackingMemberBuilder? _builder;
    private bool _runtimeValue;
    private bool _writable = true;
    private bool _ignore;
    private bool _includeInRuntimeAccess = true;
    private bool? _includeInParameters;
    private List<string>? _parameterNames;
    private bool _exposeProperty = true;
    private readonly List<MemberInfo> _metadataSources = [];

    internal RuntimeTrackingMemberOptions(Type originalType, string name, Type valueType, Action changing) {
        _originalType = originalType;
        _changing = changing;
        Name = name;
        ValueType = valueType;
    }

    internal RuntimeTrackingMemberOptions Clone(Action changing) {
        var clone = new RuntimeTrackingMemberOptions(_originalType, Name, ValueType, changing) {
            _builder = _builder?.Clone(),
            _runtimeValue = _runtimeValue,
            _writable = _writable,
            _ignore = _ignore,
            _includeInRuntimeAccess = _includeInRuntimeAccess,
            _includeInParameters = _includeInParameters,
            _exposeProperty = _exposeProperty
        };
        if (_parameterNames is not null) clone._parameterNames = new List<string>(_parameterNames);
        clone._metadataSources.AddRange(_metadataSources);
        return clone;
    }

    /// <summary>Gets the original source type.</summary>
    public Type OriginalType => _originalType;
    /// <summary>Gets the generated member name.</summary>
    public string Name { get; }
    /// <summary>Gets the generated member value type.</summary>
    public Type ValueType { get; }
    /// <summary>Gets or sets whether the member is ignored.</summary>
    public bool Ignore {
        get => _ignore;
        set { if (_ignore == value) return; _changing(); _ignore = value; }
    }
    /// <summary>Gets or sets whether runtime access includes the member.</summary>
    public bool IncludeInRuntimeAccess {
        get => _includeInRuntimeAccess;
        set { if (_includeInRuntimeAccess == value) return; _changing(); _includeInRuntimeAccess = value; }
    }
    /// <summary>Gets whether parameter projection includes the member.</summary>
    public bool IncludeInParameters => _includeInParameters ?? !_runtimeValue;
    /// <summary>Gets configured parameter names.</summary>
    public IReadOnlyList<string>? ParameterNames => _parameterNames;
    /// <summary>Gets or sets whether a CLR property is exposed.</summary>
    public bool ExposeProperty {
        get => _exposeProperty;
        set { if (_exposeProperty == value) return; _changing(); _exposeProperty = value; }
    }
    /// <summary>Gets whether the member is runtime-only.</summary>
    public bool IsRuntimeValue => _runtimeValue;
    /// <summary>Gets whether the member has explicit configuration.</summary>
    public bool IsConfigured => _runtimeValue || _builder is not null || _ignore;


    // Controls whether this member participates in direct and UseWith parameter projection.
    // Original-bound members participate by default; RuntimeValue members do not unless explicitly enabled.
    /// <summary>Controls parameter projection.</summary>
    public RuntimeTrackingMemberOptions Parameter(bool enabled = true) {
        _changing();
        _includeInParameters = enabled;
        return this;
    }

    // Replaces the default parameter/condition name for this member.
    /// <summary>Sets the parameter name.</summary>
    public RuntimeTrackingMemberOptions ParameterName(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _changing();
        _includeInParameters = true;
        _parameterNames ??= [Name];
        _parameterNames[0] = name;
        return this;
    }

    // Adds another parameter/condition name while retaining the member name.
    /// <summary>Adds a parameter alias.</summary>
    public RuntimeTrackingMemberOptions ParameterAlias(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _changing();
        _includeInParameters = true;
        _parameterNames ??= [Name];
        for (int i = 0; i < _parameterNames.Count; i++)
            if (string.Equals(_parameterNames[i], name, StringComparison.OrdinalIgnoreCase)) return this;
        _parameterNames.Add(name);
        return this;
    }

    /// <summary>Marks the member as runtime-only.</summary>
    public RuntimeTrackingMemberOptions RuntimeValue() {
        _changing();
        _runtimeValue = true;
        _builder = null;
        return this;
    }

    // Bind any still-unconfigured read/write side to the same-named original member.
    // Unlike the previous implementation, a missing member is an error rather than an implicit runtime value.
    /// <summary>Binds the default original member.</summary>
    public RuntimeTrackingMemberOptions BindDefault(string? sourceName = null) {
        _changing();
        if (_builder is not null && _builder.HasReader && (!_writable || _builder.HasWriter)) return this;
        string name = sourceName ?? Name;
        MemberInfo source = FindDefaultMember(name) ?? throw new MissingMemberException(
            $"Runtime member '{Name}' ({ValueType}) has no matching readable member '{name}' on {_originalType}. " +
            "Use [RuntimeValue], [BindTo]/[ReadFrom]/[WriteTo], or configure the member explicitly.");
        BindDefault(source);
        return this;
    }

    /// <summary>Attempts to bind the default original member.</summary>
    public bool TryBindDefault(string? sourceName = null) {
        _changing();
        if (_builder is not null && _builder.HasReader && (!_writable || _builder.HasWriter)) return true;
        MemberInfo? source = FindDefaultMember(sourceName ?? Name);
        if (source is null) return false;
        BindDefault(source);
        return true;
    }

    private void BindDefault(MemberInfo source) {
        _runtimeValue = false;
        if (_builder is null) {
            _builder = source switch {
                PropertyInfo property => new RuntimeTrackingMemberBuilder(_originalType, property) { Name = Name },
                FieldInfo field => new RuntimeTrackingMemberBuilder(_originalType, field) { Name = Name },
                _ => throw new InvalidOperationException()
            };
        } else {
            if (!_builder.HasReader) {
                if (source is PropertyInfo p) _builder.ReadFrom(p); else _builder.ReadFrom((FieldInfo)source);
            }
            if (_writable && !_builder.HasWriter) {
                if (source is PropertyInfo p && p.SetMethod?.IsPublic == true) _builder.WriteWith(p);
                else if (source is FieldInfo f && !f.IsInitOnly && !f.IsLiteral) _builder.WriteWith(f);
            }
        }
        if (!_writable) _builder.IsEditable = false;
    }

    /// <summary>Binds reads to a named original member.</summary>
    public RuntimeTrackingMemberOptions ReadFrom(string memberName) {
        _changing();
        EnsureOriginalBuilder();
        MemberInfo member = ResolveMember(memberName, readable: true);
        if (member is PropertyInfo property) _builder!.ReadFrom(property);
        else
            _builder!.ReadFrom((FieldInfo)member);
        _runtimeValue = false;
        return this;
    }

    /// <summary>Binds writes to a named original member.</summary>
    public RuntimeTrackingMemberOptions WriteTo(string memberName) {
        _changing();
        _writable = true;
        EnsureOriginalBuilder();
        MemberInfo member = ResolveMember(memberName, readable: false);
        if (member is PropertyInfo property) _builder!.WriteWith(property);
        else
            _builder!.WriteWith((FieldInfo)member);
        _runtimeValue = false;
        return this;
    }

    /// <summary>Binds reads to a method.</summary>
    public RuntimeTrackingMemberOptions ReadWith(MethodInfo method) {
        _changing();
        EnsureOriginalBuilder();
        _builder!.ReadFrom(method);
        _runtimeValue = false;
        return this;
    }

    /// <summary>Binds writes to a method.</summary>
    public RuntimeTrackingMemberOptions WriteWith(MethodInfo method) {
        _changing();
        _writable = true;
        EnsureOriginalBuilder();
        _builder!.WriteWith(method);
        _runtimeValue = false;
        return this;
    }

    /// <summary>Binds reads to a named method.</summary>
    public RuntimeTrackingMemberOptions ReadWith(string methodName) => ReadWith(_originalType, methodName);
    /// <summary>Binds reads to a method on a specified type.</summary>
    public RuntimeTrackingMemberOptions ReadWith(Type type, string methodName) => ReadWith(ResolveMethod(type, methodName, getter: true));
    /// <summary>Binds writes to a named method.</summary>
    public RuntimeTrackingMemberOptions WriteWith(string methodName) => WriteWith(_originalType, methodName);
    /// <summary>Binds writes to a method on a specified type.</summary>
    public RuntimeTrackingMemberOptions WriteWith(Type type, string methodName) => WriteWith(ResolveMethod(type, methodName, getter: false));

    /// <summary>Makes the member read-only.</summary>
    public RuntimeTrackingMemberOptions ReadOnly() {
        _changing();
        _writable = false;
        if (_builder is not null) _builder.IsEditable = false;
        return this;
    }

    /// <summary>Makes the member writable.</summary>
    public RuntimeTrackingMemberOptions Writable() {
        _changing();
        _writable = true;
        if (_builder is not null) _builder.IsEditable = _builder.HasWriter;
        return this;
    }

    internal IRuntimeTrackingMember? Build() {
        if (Ignore) return null;
        if (_runtimeValue)
            return new RuntimeStoredTrackingMember(Name, ValueType, _writable, IncludeInRuntimeAccess, IncludeInParameters, ParameterNames, ExposeProperty, _metadataSources);
        if (_builder is null)
            throw new InvalidOperationException($"Runtime member '{Name}' has no source. Bind it explicitly or mark it as RuntimeValue().");
        _builder.IncludeInRuntimeAccess = IncludeInRuntimeAccess;
        _builder.IncludeInParameters = IncludeInParameters;
        _builder.ParameterNames = ParameterNames;
        _builder.ExposeProperty = ExposeProperty;
        if (!_writable) _builder.IsEditable = false;
        return _builder.Build(_metadataSources);
    }

    internal void AddMetadataSource(MemberInfo source) {
        _changing();
        if (!_metadataSources.Contains(source)) _metadataSources.Add(source);
    }

    internal void ApplyAttributes(IEnumerable<object> attributes) {
        foreach (object attribute in attributes)
            if (attribute is IRuntimeTrackingMemberAttribute runtime) runtime.Apply(this);
    }

    private void EnsureOriginalBuilder() {
        if (_builder is not null) return;
        _runtimeValue = false;
        _builder = new RuntimeTrackingMemberBuilder(_originalType, Name, ValueType) { Name = Name };
    }

    private MemberInfo? FindDefaultMember(string name) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        PropertyInfo? property = _originalType.GetProperty(name, flags);
        if (property?.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0 && property.PropertyType == ValueType) return property;
        FieldInfo? field = _originalType.GetField(name, flags);
        return field is not null && !field.IsStatic && field.FieldType == ValueType ? field : null;
    }

    private MemberInfo ResolveMember(string name, bool readable) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        PropertyInfo? property = _originalType.GetProperty(name, flags);
        if (property is not null && property.PropertyType == ValueType &&
            (readable ? property.GetMethod?.IsPublic == true : property.SetMethod?.IsPublic == true)) return property;
        FieldInfo? field = _originalType.GetField(name, flags);
        if (field is not null && !field.IsStatic && field.FieldType == ValueType &&
            (readable || (!field.IsInitOnly && !field.IsLiteral))) return field;
        throw new MissingMemberException(_originalType.FullName, name);
    }

    private MethodInfo ResolveMethod(Type type, string methodName, bool getter) {
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)) {
            if (method.Name != methodName) continue;
            ParameterInfo[] p = method.GetParameters();
            if (getter) {
                if (method.ReturnType != ValueType) continue;
                if ((!method.IsStatic && p.Length == 0) || (method.IsStatic && p.Length == 1 && p[0].ParameterType == _originalType)) return method;
            } else {
                if (method.ReturnType != typeof(void)) continue;
                if ((!method.IsStatic && p.Length == 1 && p[0].ParameterType == ValueType) ||
                    (method.IsStatic && p.Length == 2 && p[0].ParameterType == _originalType && p[1].ParameterType == ValueType)) return method;
            }
        }
        throw new MissingMethodException(type.FullName, methodName);
    }
}
