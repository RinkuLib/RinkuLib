using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rinku.Tracking.Runtime;

/// <summary>Builds one generated tracking member.</summary>
public sealed class RuntimeTrackingMemberBuilder {
    private readonly Type _originalType;
    private IRuntimeOriginalReader _reader;
    private IRuntimeOriginalWriter? _writer;
    private readonly List<MemberInfo> _metadataSources = [];

    internal RuntimeTrackingMemberBuilder(PropertyInfo property) : this(property.DeclaringType ?? throw new ArgumentException("Property has no declaring type.", nameof(property)), property) { }

    internal RuntimeTrackingMemberBuilder(Type originalType, PropertyInfo property) {
        _originalType = originalType;
        if (property.GetMethod?.IsPublic != true || property.GetMethod.IsStatic || property.GetIndexParameters().Length != 0)
            throw new ArgumentException($"Source property {property} must have a public instance getter and cannot be an indexer.", nameof(property));
        if (property.DeclaringType is null || !property.DeclaringType.IsAssignableFrom(originalType))
            throw new ArgumentException($"Source property {property} is not callable on {originalType}.", nameof(property));
        SourceMember = property;
        Name = property.Name;
        ValueType = property.PropertyType;
        _reader = new PropertyOriginalReader(property);
        if (property.SetMethod?.IsPublic == true) _writer = new PropertyOriginalWriter(property);
        IsEditable = _writer is not null;
        _metadataSources.Add(property);
    }

    internal RuntimeTrackingMemberBuilder(FieldInfo field) : this(field.DeclaringType ?? throw new ArgumentException("Field has no declaring type.", nameof(field)), field) { }

    internal RuntimeTrackingMemberBuilder(Type originalType, FieldInfo field) {
        _originalType = originalType;
        if (!field.IsPublic || field.IsStatic)
            throw new ArgumentException($"Source field {field} must be public and instance-accessible to the generated assembly.", nameof(field));
        if (field.DeclaringType is null || !field.DeclaringType.IsAssignableFrom(originalType))
            throw new ArgumentException($"Source field {field} is not callable on {originalType}.", nameof(field));
        SourceMember = field;
        Name = field.Name;
        ValueType = field.FieldType;
        _reader = new FieldOriginalReader(field);
        if (!field.IsInitOnly && !field.IsLiteral) _writer = new FieldOriginalWriter(field);
        IsEditable = _writer is not null;
        _metadataSources.Add(field);
    }

    internal RuntimeTrackingMemberBuilder(Type originalType, string name, Type valueType) {
        _originalType = originalType;
        Name = name;
        ValueType = valueType;
        _reader = null!;
    }

    private RuntimeTrackingMemberBuilder(RuntimeTrackingMemberBuilder source) {
        _originalType = source._originalType;
        _reader = source._reader;
        _writer = source._writer;
        SourceMember = source.SourceMember;
        Name = source.Name;
        ValueType = source.ValueType;
        IsEditable = source.IsEditable;
        Ignore = source.Ignore;
        IncludeInRuntimeAccess = source.IncludeInRuntimeAccess;
        IncludeInParameters = source.IncludeInParameters;
        ParameterNames = source.ParameterNames is null ? null : new List<string>(source.ParameterNames);
        ExposeProperty = source.ExposeProperty;
        _metadataSources.AddRange(source._metadataSources);
    }

    internal RuntimeTrackingMemberBuilder Clone() => new(this);

    /// <summary>Gets the source member.</summary>
    public MemberInfo? SourceMember { get; }
    /// <summary>Gets or sets the generated name.</summary>
    public string Name { get; set; }
    /// <summary>Gets the value type.</summary>
    public Type ValueType { get; }
    /// <summary>Gets or sets whether the member is editable.</summary>
    public bool IsEditable { get; set; }
    internal bool HasReader => _reader is not null;
    internal bool HasWriter => _writer is not null;
    /// <summary>Gets or sets whether the member is ignored.</summary>
    public bool Ignore { get; set; }
    /// <summary>Gets or sets whether runtime access includes the member.</summary>
    public bool IncludeInRuntimeAccess { get; set; } = true;
    /// <summary>Gets or sets whether parameters include the member.</summary>
    public bool IncludeInParameters { get; set; } = true;
    /// <summary>Gets or sets alternate parameter names.</summary>
    public IReadOnlyList<string>? ParameterNames { get; set; }
    /// <summary>Gets or sets whether a property is exposed.</summary>
    public bool ExposeProperty { get; set; } = true;

    /// <summary>Uses a property as the read source.</summary>
    public void ReadFrom(PropertyInfo property) {
        ValidateSourceMember(property, property.GetMethod?.IsPublic == true, nameof(property));
        ValidateType(property.PropertyType, nameof(property));
        _reader = new PropertyOriginalReader(property);
        AddMetadataSource(property);
    }

    /// <summary>Uses a field as the read source.</summary>
    public void ReadFrom(FieldInfo field) {
        ValidateSourceMember(field, field.IsPublic && !field.IsStatic, nameof(field));
        ValidateType(field.FieldType, nameof(field));
        _reader = new FieldOriginalReader(field);
        AddMetadataSource(field);
    }

    /// <summary>Uses a method as the read source.</summary>
    public void ReadFrom(MethodInfo method) {
        ArgumentNullException.ThrowIfNull(method);
        if (method.ReturnType != ValueType) throw new ArgumentException($"Getter {method} returns {method.ReturnType}, expected {ValueType}.", nameof(method));
        ValidateGetter(method);
        _reader = new MethodOriginalReader(_originalType, method, ValueType);
    }

    /// <summary>Uses a property as the write source.</summary>
    public void WriteWith(PropertyInfo property) {
        ValidateSourceMember(property, property.SetMethod?.IsPublic == true, nameof(property));
        ValidateType(property.PropertyType, nameof(property));
        if (property.SetMethod is null) throw new ArgumentException($"{property} has no setter.", nameof(property));
        _writer = new PropertyOriginalWriter(property);
        IsEditable = true;
        AddMetadataSource(property);
    }

    /// <summary>Uses a field as the write source.</summary>
    public void WriteWith(FieldInfo field) {
        ValidateSourceMember(field, field.IsPublic && !field.IsStatic, nameof(field));
        ValidateType(field.FieldType, nameof(field));
        if (field.IsInitOnly || field.IsLiteral) throw new ArgumentException($"{field} is not writable.", nameof(field));
        _writer = new FieldOriginalWriter(field);
        IsEditable = true;
        AddMetadataSource(field);
    }

    /// <summary>Uses a method as the write source.</summary>
    public void WriteWith(MethodInfo method) {
        ArgumentNullException.ThrowIfNull(method);
        ValidateSetter(method);
        _writer = new MethodOriginalWriter(_originalType, method, ValueType);
        IsEditable = true;
    }

    internal IRuntimeTrackingMember Build(IReadOnlyList<MemberInfo>? additionalMetadataSources = null) {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Runtime tracking member name cannot be empty.");
        ValidateExposedType(ValueType);
        if (_reader is null) throw new InvalidOperationException($"Runtime member '{Name}' has no read behavior.");
        IReadOnlyList<MemberInfo> metadata = MergeMetadata(additionalMetadataSources);
        if (!IsEditable)
            return new OriginalReadableRuntimeTrackingMember(Name, ValueType, _reader, IncludeInRuntimeAccess, IncludeInParameters, ParameterNames, ExposeProperty, metadata);
        if (_writer is null)
            throw new InvalidOperationException($"Runtime member '{Name}' is editable but has no write behavior. Configure WriteWith(...) or make it read-only.");
        return new OriginalEditableRuntimeTrackingMember(Name, ValueType, _reader, _writer, IncludeInRuntimeAccess, IncludeInParameters, ParameterNames, ExposeProperty, metadata);
    }


    private IReadOnlyList<MemberInfo> MergeMetadata(IReadOnlyList<MemberInfo>? additional) {
        if (additional is null || additional.Count == 0) return _metadataSources;
        var result = new List<MemberInfo>(_metadataSources.Count + additional.Count);
        result.AddRange(_metadataSources);
        for (int i = 0; i < additional.Count; i++) if (!result.Contains(additional[i])) result.Add(additional[i]);
        return result;
    }

    private void ValidateGetter(MethodInfo method) {
        if (!method.IsPublic) throw new ArgumentException($"Getter {method} must be public for emitted runtime access.", nameof(method));
        if (method.IsStatic) {
            if (method.GetParameters() is not [{ ParameterType: var original }] || original != _originalType)
                throw new ArgumentException($"Static getter {method} must take exactly one {_originalType} parameter.", nameof(method));
            return;
        }
        if (method.GetParameters().Length != 0)
            throw new ArgumentException($"Instance getter {method} must take no parameters.", nameof(method));
        if (method.DeclaringType is null || !method.DeclaringType.IsAssignableFrom(_originalType))
            throw new ArgumentException($"Getter {method} is not callable on {_originalType}.", nameof(method));
    }

    private void ValidateSetter(MethodInfo method) {
        if (!method.IsPublic) throw new ArgumentException($"Setter {method} must be public for emitted runtime access.", nameof(method));
        if (method.ReturnType != typeof(void)) throw new ArgumentException($"Setter {method} must return void.", nameof(method));
        ParameterInfo[] parameters = method.GetParameters();
        if (method.IsStatic) {
            if (_originalType.IsValueType)
                throw new ArgumentException($"Static by-value setter {method} cannot mutate value-type original {_originalType}; use an instance method or custom member emitter.", nameof(method));
            if (parameters.Length != 2 || parameters[0].ParameterType != _originalType || parameters[1].ParameterType != ValueType)
                throw new ArgumentException($"Static setter {method} must have signature void M({_originalType.Name}, {ValueType.Name}).", nameof(method));
            return;
        }
        if (parameters.Length != 1 || parameters[0].ParameterType != ValueType)
            throw new ArgumentException($"Instance setter {method} must take exactly one {ValueType} parameter.", nameof(method));
        if (method.DeclaringType is null || !method.DeclaringType.IsAssignableFrom(_originalType))
            throw new ArgumentException($"Setter {method} is not callable on {_originalType}.", nameof(method));
    }

    private void ValidateSourceMember(MemberInfo member, bool accessible, string paramName) {
        if (!accessible) throw new ArgumentException($"Source member {member} must be public and instance-accessible to the generated assembly.", paramName);
        if (member.DeclaringType is null || !member.DeclaringType.IsAssignableFrom(_originalType))
            throw new ArgumentException($"Source member {member} is not callable on {_originalType}.", paramName);
    }

    private void ValidateType(Type type, string paramName) {
        if (type != ValueType) throw new ArgumentException($"Member type {type} does not match runtime value type {ValueType}.", paramName);
    }

    internal void AddMetadataSource(MemberInfo source) {
        if (!_metadataSources.Contains(source)) _metadataSources.Add(source);
    }

    private static void ValidateExposedType(Type type) {
        if (type.IsByRef || type.IsPointer || type.IsFunctionPointer || type.IsByRefLike || type.ContainsGenericParameters)
            throw new NotSupportedException($"Runtime tracking cannot expose member type {type}.");
    }
}
