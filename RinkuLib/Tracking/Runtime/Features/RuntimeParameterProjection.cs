using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Querying;
using Rinku.Querying.Parameters;

namespace Rinku.Tracking.Runtime;

/// <summary>Provides query parameter access for a generated tracking type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeTrackingParameterAccessorAttribute : AccessorEmitterHandler
{
    /// <inheritdoc/>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper)
    {
        if (!RuntimeTrackingParameterRegistry.TryGet(type, out RuntimeTrackingParameterShape? shape)) return null;
        if (!shape.HasProperty(member.Name)) return null;
        ReadOnlySpan<char> key = GetName(varChar, mapper.Keys[index]);
        if (!shape.TryGetParameter(key, out PropertyInfo? mapped))
            return RuntimeTrackingDisabledParameterEmitter.Instance;
        return CreateMemberAdapter(mapped, varChar, index, type, mapper);
    }

    /// <inheritdoc/>
    public override ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper)
    {
        if (!RuntimeTrackingParameterRegistry.TryGet(type, out RuntimeTrackingParameterShape? shape)) return null;
        ReadOnlySpan<char> key = GetName(varChar, mapper.Keys[index]);
        if (!shape.TryGetParameter(key, out PropertyInfo? member)) return null;
        return new RuntimeTrackingTypeEmitterAdapter(member, ResolveEmitter(member, varChar, index, type, mapper));
    }

    /// <inheritdoc/>
    public override ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper, Type valueType)
    {
        if (RuntimeTrackingParameterRegistry.TryGet(type, out _))
        {
            return GetTypeEmitter(varChar, index, type, mapper);
        }
        return typeof(IRuntimeMemberAccess).IsAssignableFrom(type) ? new RuntimeMemberParameterEmitter(GetName(varChar, mapper.Keys[index]).ToString(), valueType) : null;
    }

    private static RuntimeTrackingMemberEmitterAdapter CreateMemberAdapter(PropertyInfo member, char varChar, int index, Type type, Mapper mapper)
        => new(member, ResolveEmitter(member, varChar, index, type, mapper));

    private static IAccessorEmitter ResolveEmitter(PropertyInfo member, char varChar, int index, Type type, Mapper mapper)
    {
        AccessorEmitterHandler? custom = member.GetCustomAttribute<AccessorEmitterHandler>(inherit: true);
        if (custom is null && RuntimeTrackingParameterRegistry.TryGet(type, out var shape))
            custom = shape.DefaultAccessor;
        return custom?.GetMemberEmitter(varChar, index, type, member, mapper)
            ?? RuntimeTrackingDefaultParameterEmitter.Instance;
    }

    private static ReadOnlySpan<char> GetName(char varChar, string key)
    {
        ReadOnlySpan<char> span = key;
        return span.Length != 0 && varChar != default && span[0] == varChar ? span[1..] : span;
    }
}

internal sealed class RuntimeMemberParameterEmitter(string name, Type valueType) : TypeAccessorEmitterBase
{
    private static readonly MethodInfo Read = typeof(RuntimeMemberParameterEmitter).GetMethod(nameof(TryRead), BindingFlags.Static | BindingFlags.NonPublic)!;
    private LocalBuilder? _value;

    protected override void EmitCondition(ILGenerator il, Type type)
    {
        _value = il.DeclareLocal(valueType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Ldloca, _value);
        il.Emit(OpCodes.Call, Read.MakeGenericMethod(valueType));
    }

    protected override void EmitValue(ILGenerator il, Type type) => il.Emit(OpCodes.Ldloc, _value!);
    protected override Type GetValueType(Type type) => valueType;

    private static bool TryRead<T>(IRuntimeMemberAccess source, string key, [MaybeNullWhen(false)] out T value)
    {
        if (RuntimeTrackingParameterRegistry.TryGet(source.GetType(), out var shape))
        {
            if (!shape.TryGetParameter(key, out var member))
            {
                value = default;
                return false;
            }
            key = member.Name;
        }
        int index = source.Mapper.GetIndex(key);
        if (index >= 0)
        {
            return source.TryGet(index, out value);
        }
        value = default;
        return false;
    }
}

internal static class RuntimeTrackingParameterRegistry
{
    private static readonly ConcurrentDictionary<Type, RuntimeTrackingParameterShape> Shapes = new();
    internal static void Register(Type type, RuntimeTrackingParameterShape shape) => Shapes[type] = shape;
    internal static bool TryGet(Type type, [NotNullWhen(true)] out RuntimeTrackingParameterShape? shape) => Shapes.TryGetValue(type, out shape);
}

internal sealed class RuntimeTrackingParameterShape
{
    private readonly Dictionary<string, PropertyInfo> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PropertyInfo> _properties = new(StringComparer.OrdinalIgnoreCase);

    internal AccessorEmitterHandler? DefaultAccessor { get; }

    internal RuntimeTrackingParameterShape(Type generatedType, IReadOnlyList<RuntimeTrackingParameterPlanMember> members, ParameterConflictBehavior conflictBehavior, AccessorEmitterHandler? defaultAccessor)
    {
        DefaultAccessor = defaultAccessor;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (int i = 0; i < members.Count; i++)
        {
            RuntimeTrackingParameterPlanMember member = members[i];
            PropertyInfo? property = generatedType.GetProperty(member.Name, flags);
            if (property is null) continue;
            _properties.Add(member.Name, property);
            if (!member.IncludeInParameters || property.IsDefined(typeof(ParameterIgnoreAttribute), inherit: true)) continue;

            ParameterNameAttribute? rename = property.GetCustomAttribute<ParameterNameAttribute>(inherit: true);
            Add(rename?.Name ?? member.Name, property, conflictBehavior);
            foreach (ParameterAliasAttribute alias in property.GetCustomAttributes<ParameterAliasAttribute>(inherit: true))
                Add(alias.Name, property, conflictBehavior);
        }
    }

    internal bool TryGetParameter(ReadOnlySpan<char> name, [NotNullWhen(true)] out PropertyInfo? member)
        => _parameters.TryGetValue(name.ToString(), out member);

    internal bool TryGetParameter(string name, [NotNullWhen(true)] out PropertyInfo? member) => _parameters.TryGetValue(name, out member);

    internal bool MatchesParameter(ReadOnlySpan<char> parameter, string member) {
        foreach (var pair in _parameters)
            if (parameter.Equals(pair.Key.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return pair.Value.Name.Equals(member, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    internal bool HasProperty(string name) => _properties.ContainsKey(name);

    private void Add(string name, PropertyInfo member, ParameterConflictBehavior conflictBehavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = NormalizeName(name);
        if (normalized.Length == 0) throw new ArgumentException("A runtime parameter name cannot contain only a variable prefix.", nameof(name));
        if (_parameters.TryGetValue(normalized, out PropertyInfo? existing) && !ReferenceEquals(existing, member))
        {
            if (conflictBehavior == ParameterConflictBehavior.TakeOne) return;
            throw new InvalidOperationException($"Runtime parameter name '{name}' is provided by both '{existing.Name}' and '{member.Name}'.");
        }
        _parameters[normalized] = member;
    }

    private static string NormalizeName(string name)
    {
        char first = name[0];
        return !char.IsLetterOrDigit(first) && first != '_' ? name[1..] : name;
    }
}

internal readonly record struct RuntimeTrackingParameterPlanMember(string Name, bool IncludeInParameters);

internal sealed class RuntimeTrackingMemberEmitterAdapter(PropertyInfo source, IAccessorEmitter emitter) : IAccessorEmitter
{
    private readonly Type _accessType = source.DeclaringType ?? throw new InvalidOperationException($"Generated tracking property {source} has no declaring type.");
    public void Validate(Type type, MemberInfo member) => emitter.Validate(_accessType, source);
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => emitter.Emit(il, index, key, _accessType, source, handlerValues, handlerIndex, handlerValue, bindValue);
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
        => emitter.EmitUseWith(il, index, _accessType, source, bindValue, context);
    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member) => emitter.EmitStackUsage(il, _accessType, source);
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member) => emitter.EmitStackValue(il, _accessType, source);
    public Type GetStackType(Type type, MemberInfo member) => emitter.GetStackType(_accessType, source);
}

internal sealed class RuntimeTrackingTypeEmitterAdapter(PropertyInfo member, IAccessorEmitter emitter) : ITypeAccessorEmitter
{
    private readonly Type _accessType = member.DeclaringType ?? throw new InvalidOperationException($"Generated tracking property {member} has no declaring type.");
    public void Validate(Type type) => emitter.Validate(_accessType, member);
    public void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => emitter.Emit(il, index, key, _accessType, member, handlerValues, handlerIndex, handlerValue, bindValue);
    public void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue, UseWithEmissionContext context)
        => emitter.EmitUseWith(il, index, _accessType, member, bindValue, context);
    public void EmitStackUsage(ILGenerator il, Type type) => emitter.EmitStackUsage(il, _accessType, member);
    public void EmitStackValue(ILGenerator il, Type type) => emitter.EmitStackValue(il, _accessType, member);
    public Type GetStackType(Type type) => emitter.GetStackType(_accessType, member);
}

internal sealed class RuntimeTrackingDefaultParameterEmitter : IAccessorEmitter
{
    internal static readonly RuntimeTrackingDefaultParameterEmitter Instance = new();
    public void Validate(Type type, MemberInfo member) { }

    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue,
            e => EmitUsage(e, type, member), e => AccessorEmitter.EmitMemberValue(e, type, member));

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
        => AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
            e => EmitUsage(e, type, member, context.SourceArgument),
            e => AccessorEmitter.EmitMemberValue(e, type, member, context.SourceArgument), context);

    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member) => EmitUsage(il, type, member);
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member) => AccessorEmitter.EmitMemberLoad(il, type, member);
    public Type GetStackType(Type type, MemberInfo member) => ParameterMemberAccess.GetMemberType(member);

    private static void EmitUsage(ILGenerator il, Type type, MemberInfo member, int sourceArgument = 0)
    {
        Type memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (!memberType.IsValueType)
        {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }
        if (Nullable.GetUnderlyingType(memberType) is not null)
        {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            LocalBuilder value = il.DeclareLocal(memberType);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, value);
            MethodInfo hasValue = memberType.GetProperty(nameof(Nullable<>.HasValue))?.GetMethod
                ?? throw new MissingMethodException(memberType.FullName, $"get_{nameof(Nullable<>.HasValue)}");
            il.Emit(OpCodes.Call, hasValue);
            return;
        }
        il.Emit(OpCodes.Ldc_I4_1);
    }
}

internal sealed class RuntimeTrackingDisabledParameterEmitter : IAccessorEmitter
{
    internal static readonly RuntimeTrackingDisabledParameterEmitter Instance = new();
    public void Validate(Type type, MemberInfo member) { }
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) { }
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
        => AccessorEmitter.EmitUseWithSlot(il, index, bindValue: false, static e => e.Emit(OpCodes.Ldc_I4_0), static _ => { }, context);
    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member) => il.Emit(OpCodes.Ldc_I4_0);
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member) => AccessorEmitter.EmitMemberLoad(il, type, member);
    public Type GetStackType(Type type, MemberInfo member) => ParameterMemberAccess.GetMemberType(member);
}

internal sealed class RuntimeParameterProjectionOption<TOriginal> : IRuntimeTrackingOption<TOriginal>
{
    internal static readonly RuntimeParameterProjectionOption<TOriginal> Instance = new();
    private RuntimeParameterProjectionOption() { }

    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        for (int i = 0; i < type.TypeEmitters.Count; i++)
            if (type.TypeEmitters[i] is RuntimeParameterProjectionEmitter<TOriginal>) return;
        type.AddTypeEmitter(new RuntimeParameterProjectionEmitter<TOriginal>());
    }
}

internal sealed class RuntimeParameterProjectionEmitter<TOriginal> : IRuntimeTrackingTypeEmitter<TOriginal>
{
    private static readonly ConstructorInfo AttributeConstructor = typeof(RuntimeTrackingParameterAccessorAttribute).GetConstructor(Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(RuntimeTrackingParameterAccessorAttribute).FullName, ".ctor()");

    public void Emit(RuntimeTrackingEmitContext<TOriginal> context)
        => context.TypeBuilder.SetCustomAttribute(new CustomAttributeBuilder(AttributeConstructor, []));

    public void Complete(RuntimeTrackingGeneratedType<TOriginal> type)
    {
        ParameterConflictBehavior conflict = typeof(TOriginal).GetCustomAttribute<ParameterConflictAttribute>(inherit: true)?.Behavior
            ?? ParameterConflictBehavior.Throw;
        var members = new RuntimeTrackingParameterPlanMember[type.Definition.Members.Count];
        for (int i = 0; i < members.Length; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = type.Definition.Members[i];
            members[i] = new(member.Name, member.IncludeInParameters);
        }
        RuntimeTrackingParameterRegistry.Register(type.Type, new RuntimeTrackingParameterShape(type.Type, members, conflict, typeof(TOriginal).GetCustomAttribute<AccessorEmitterHandler>(inherit: true)));
    }
}
