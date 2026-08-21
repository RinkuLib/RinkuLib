using System.Reflection;
using System.Runtime.InteropServices;
using Rinku.Internal;

namespace Rinku.Mapping.Defaults; 
/// <summary>Controls optional discovery and registration behavior for default object mappings.</summary>
[Flags]
public enum DefaultTypeParsingFlags : byte {
    /// <summary>Uses the default public discovery and registration behavior.</summary>
    None = 0,
    /// <summary>Allows immediate constructor parameter and writable member types to register on demand.</summary>
    DirectTypesAreReadable = 1,
    /// <summary>Allows discovery of non-public constructors, factories, fields, properties, and setters.</summary>
    UsePrivateMembers = 2,
}
/// <summary>Provides the standard mapping settings for a class, record, or struct.</summary>
public class DefaultTypeParsingInfo(Type Type) : TypeParsingInfo, ICanAddPossibleConstructor, ICanProvideParamInfos, ICanAddMember, ICanProvideConstructions, ICanProvideMembers, ICanUpdateGroupKey {
    /// <inheritdoc/>
    public IGroupingRule? GroupKey { get; set; }
    internal static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        WriteLock = new();
    /// <summary>The type used</summary>
    public readonly Type Type = Nullable.GetUnderlyingType(Type) ?? Type;
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (TargetType != Type)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
                $"The associated type with this instance is {Type} so it can't be bound with {TargetType}");
    }
    private bool IsValidTarget(Type target)
        => target.IsStackEquivalent(Type)
        || (Type.IsGenericTypeDefinition && target.IsGenericType && target.GetGenericTypeDefinition() == Type);
    private bool IsInit;
    /// <summary>
    /// Optional discovery and registration behavior for this mapping.
    /// Type attributes supply the initial value. Change the flags before the type is first parsed.
    /// </summary>
    public DefaultTypeParsingFlags Flags { get; set; } =
        (Type.IsDefined(typeof(AreReadableAttribute), inherit: true)
            ? DefaultTypeParsingFlags.DirectTypesAreReadable
            : DefaultTypeParsingFlags.None)
        | (Type.IsDefined(typeof(UsePrivateMembersAttribute), inherit: true)
            ? DefaultTypeParsingFlags.UsePrivateMembers
            : DefaultTypeParsingFlags.None);
    private MethodCtorInfo[] MCIs = [];
    /// <summary>
    /// The collection of prioritized construction paths (constructors or static factory methods) 
    /// discovered or manually registered for this type.
    /// </summary>
    public ReadOnlySpan<MethodCtorInfo> PossibleConstructors {
        get {
            if (!IsInit)
                Init();
            return MCIs;
        }
        set {
            for (var i = 0; i < value.Length; i++) {
                var c = value[i];
                if (!IsValidTarget(c.TargetType))
                    throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch, $"the method or constructor must be of type {Type} (returning type)");
                var declare = c.MethodBase.DeclaringType!;
                if (declare != Type && declare.IsGenericType)
                    throw new RinkuConfigurationException(ErrorCodes.ForeignGenericSource, $"Cannot add a possible construction from a generic type other then the target type Target:{Type} Used:{declare}");
            }
            Interlocked.Exchange(ref MCIs, value.ToArray());
        }
    }
    private MemberParser[] Members = [];
    /// <summary>
    /// A collection of public properties and fields that can be set after instantiation.
    /// </summary>
    public ReadOnlySpan<MemberParser> AvailableMembers {
        get {
            if (!IsInit)
                Init();
            return Members;
        }
        set {
            for (var i = 0; i < value.Length; i++) {
                var c = value[i];
                if (!IsValidTarget(c.TargetType))
                    throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch, $"the member must belong to {Type}, and {c.Member} belongs to {c.TargetType}");
                var declare = c.Member.DeclaringType!;
                if (declare != Type && declare.IsGenericType)
                    throw new RinkuConfigurationException(ErrorCodes.ForeignGenericSource, $"Cannot add a member from a generic type other then the target type Target:{Type} Used:{declare}");
            }
            Interlocked.Exchange(ref Members, value.ToArray());
        }
    }
    private MethodBase? ParameterlessConstructor { get; set; }
    /// <summary>
    /// Scans the type via reflection to find constructors, static methods,
    /// properties, and fields for automatic mapping.
    /// </summary>
    public void Init() {
        lock (WriteLock) {
            if (IsInit)
                return;
            var type = Nullable.GetUnderlyingType(Type) ?? Type;
            bool usePrivateMembers = Flags.HasFlag(DefaultTypeParsingFlags.UsePrivateMembers);
            bool directTypesAreReadable = Flags.HasFlag(DefaultTypeParsingFlags.DirectTypesAreReadable);
            var memberFlags = BindingFlags.Public | BindingFlags.Instance;
            if (usePrivateMembers)
                memberFlags |= BindingFlags.NonPublic;
            var props = type.GetProperties(memberFlags);
            var fields = type.GetFields(memberFlags);
            List<MemberParser> memberParsers = [];
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];
                if (field.IsInitOnly || field.IsLiteral)
                    continue;
                var p = directTypesAreReadable ? ParamInfo.Create(field) : ParamInfo.TryNew(field);
                if (p is not null && MemberParser.TryNew(field, p, out var member, usePrivateMembers))
                    memberParsers.Add(member);
            }
            for (int i = 0; i < props.Length; i++) {
                var prop = props[i];
                if (!prop.CanWrite || prop.GetSetMethod(nonPublic: usePrivateMembers) is null)
                    continue;
                var p = directTypesAreReadable ? ParamInfo.Create(prop) : ParamInfo.TryNew(prop);
                if (p is not null && MemberParser.TryNew(prop, p, out var member, usePrivateMembers))
                    memberParsers.Add(member);
            }
            var constructorFlags = BindingFlags.Public | BindingFlags.Instance;
            var staticMethodFlags = BindingFlags.Public | BindingFlags.Static;
            if (usePrivateMembers) {
                constructorFlags |= BindingFlags.NonPublic;
                staticMethodFlags |= BindingFlags.NonPublic;
            }
            var constructors = type.GetConstructors(constructorFlags);
            var staticMethods = type.GetMethods(staticMethodFlags);
            var infoList = new List<MethodCtorInfo>(constructors.Length);
            foreach (var constructor in constructors) {
                var ps = directTypesAreReadable
                    ? MethodCtorInfo.MakeParameters(constructor)
                    : MethodCtorInfo.TryMakeParameters(constructor);
                if (MethodCtorInfo.TryNew(constructor, ps, out var mci))
                    infoList.Add(mci);
                else if (ParameterlessConstructor is null && ps is not null && ps.Length == 0)
                    ParameterlessConstructor = constructor;
            }
            if (!usePrivateMembers)
                ParameterlessConstructor ??= type.GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
            foreach (var method in staticMethods) {
                if (method.ReturnType != type || method.IsGenericMethod || !method.IsStatic)
                    continue;
                var ps = directTypesAreReadable
                    ? MethodCtorInfo.MakeParameters(method)
                    : MethodCtorInfo.TryMakeParameters(method);
                if (MethodCtorInfo.TryNew(method, ps, out var mci))
                    infoList.Add(mci);
            }
            if (memberParsers.Count > 0) {
                if (Members.Length == 0) {
                    Members = [.. memberParsers];
                } else {
                    var mp = CollectionsMarshal.AsSpan(memberParsers);
                    var result = new MemberParser[Members.Length + mp.Length];
                    Array.Copy(Members, 0, result, 0, Members.Length);
                    mp.CopyTo(result.AsSpan(Members.Length));
                    Members = result;
                }
            }
            if (infoList.Count > 0) {
                var infos = CollectionsMarshal.AsSpan(infoList);
                if (MCIs.Length == 0) {
                    MCIs = MethodCtorInfo.GetOrderedInfos(infos);
                } else {
                    var result = new MethodCtorInfo[MCIs.Length + infos.Length];
                    Array.Copy(MCIs, 0, result, 0, MCIs.Length);
                    infos.CopyTo(result.AsSpan(MCIs.Length));
                    MCIs = MethodCtorInfo.GetOrderedInfos(result);
                }
            }
            GroupKey ??= GroupKeyScan.Resolve(type,
                [type, .. props, .. fields,
                    .. type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)]);
            IsInit = true;
        }
    }
    /// <inheritdoc/>
    public IEnumerable<ParamInfo> GetParamInfos() {
        if (!IsInit)
            Init();
        for (int i = 0; i < MCIs.Length; i++) {
            var parameters = MCIs[i].Parameters;
            for (int j = 0; j < parameters.Length; j++)
                yield return parameters[j];
        }
        for (int i = 0; i < Members.Length; i++)
            yield return Members[i].Param;
    }
    /// <inheritdoc/>
    public void AddPossibleConstruction(MethodCtorInfo mci) {
        lock (WriteLock) {
            var target = mci.TargetType;
            if (!IsValidTarget(target))
                throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch, $"the expected type is {Type} but the provided type via the method is {mci.TargetType}");
            var declare = mci.MethodBase.DeclaringType!;
            if (declare != Type && declare.IsGenericType)
                throw new RinkuConfigurationException(ErrorCodes.ForeignGenericSource, $"Cannot add a possible construction from a generic type other then the target type Target:{Type} Used:{declare}");
            mci.InsertInto(ref MCIs);
        }
    }
    /// <inheritdoc/>
    public void AddMember(MemberParser member) {
        lock (WriteLock) {
            if (!IsValidTarget(member.TargetType))
                throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch, $"the member must be of type {Type}");
            var declare = member.Member.DeclaringType!;
            if (declare != Type && declare.IsGenericType)
                throw new RinkuConfigurationException(ErrorCodes.ForeignGenericSource, $"Cannot add a member from a generic type other then the target type Target:{Type} Used:{declare}");
            var result = new MemberParser[Members.Length + 1];
            Array.Copy(Members, result, Members.Length);
            result[^1] = member;
            Interlocked.Exchange(ref Members, result);
        }
    }
    /// <inheritdoc/>
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
        if (!IsInit)
            Init();
        var actualType = Nullable.GetUnderlyingType(currentClosedType) ?? currentClosedType;
        RegisterGenericArguments(actualType, callerFlags);
        if (!previousUsages.CanContinue(actualType, colUsage.NbUsed, out previousUsages))
            return null;
        colModifier = colModifier.Add(paramInfo.NameComparer);
        paramInfo.EnterSubtree(ref colModifier, colUsage.NbClaims);
        Span<bool> checkpoint = stackalloc bool[colUsage.Length];
        colUsage.InitCheckpoint(checkpoint, out var lastIndUsed, out var nbClaims);
        var mcis = MCIs;
        List<DbItemPlan> readers = [];
        MethodBase? method = null;
        MethodCtorInfo? constructionInfo = null;
        var genericArguments = actualType.IsGenericType ? actualType.GetGenericArguments() : [];
        bool directTypesAreReadable = Flags.HasFlag(DefaultTypeParsingFlags.DirectTypesAreReadable);
        var typeReadableFlag = directTypesAreReadable
            ? MethodCtorInfo.AdditionalFlags.ParametersAreReadable
            : default;
        bool canCompleteWithMembers = false;
        for (int i = 0; i < mcis.Length; i++) {
            var mci = mcis[i];
            bool forcedRegister = directTypesAreReadable || mci.ParametersAreReadable || callerFlags.HasFlag(MethodCtorInfo.AdditionalFlags.ParametersAreReadable);
            var parameters = mci.Parameters;
            for (int j = 0; j < parameters.Length; j++) {
                var param = parameters[j];
                var t = Nullable.GetUnderlyingType(param.Type);
                var isNullableStruct = t is not null;
                var paramClosedType = (t ?? param.Type).CloseType(genericArguments);
                if (isNullableStruct)
                    paramClosedType = typeof(Nullable<>).MakeGenericType(paramClosedType);
                if (!TryGetInfo(paramClosedType, out var typeInfo)) {
                    if (!forcedRegister)
                        break;
                    typeInfo = ForceGet(paramClosedType);
                }
                var node = typeInfo.TryGetParser(paramClosedType, previousUsages, param, columns, colModifier, ref colUsage, mci.Flags | typeReadableFlag);
                if (node is null)
                    break;
                readers.Add(node);
            }
            if (readers.Count == parameters.Length) {
                method = (MethodBase)mci.MethodBase.GetClosedMember(currentClosedType);
                canCompleteWithMembers = mci.CanCompleteWithMembers;
                constructionInfo = mci;
                break;
            }
            colUsage.Rollback(checkpoint, lastIndUsed, nbClaims);
            readers.Clear();
        }
        if (method is null) {
            method = (MethodBase?)ParameterlessConstructor?.GetClosedMember(currentClosedType);
            if (method is null)
                return paramInfo.FallbackTryGetParser(currentClosedType);
            canCompleteWithMembers = true;
        }
        var constructionRule = constructionInfo?.GroupKey;
        var typeRule = GroupKey;
        IGroupingRule[] groupingRules = constructionRule is null
            ? typeRule is null ? [] : [typeRule]
            : typeRule is null ? [constructionRule] : [constructionRule, typeRule];
        List<(MemberInfo, DbItemPlan)>? memberReaders = null;
        if (canCompleteWithMembers) {
            memberReaders = [];
            var members = Members;
            for (int i = 0; i < members.Length; i++) {
                var param = members[i].Param;
                var t = Nullable.GetUnderlyingType(param.Type);
                var isNullableStruct = t is not null;
                var paramClosedType = (t ?? param.Type).CloseType(genericArguments);
                if (isNullableStruct)
                    paramClosedType = typeof(Nullable<>).MakeGenericType(paramClosedType);
                if (!TryGetInfo(paramClosedType, out var typeInfo)) {
                    if (!directTypesAreReadable)
                        throw new RinkuInternalException(ErrorCodes.InternalInvariant, "reached a branch believed unreachable while discovering construction paths");
                    typeInfo = ForceGet(paramClosedType);
                }
                var node = typeInfo.TryGetParser(paramClosedType, previousUsages, param, columns, colModifier, ref colUsage, typeReadableFlag);
                if (node is not null)
                    memberReaders.Add((members[i].Member.GetClosedMember(currentClosedType), node));
            }
            if (memberReaders.Count == 0 && readers.Count == 0)
                return null;
        }
        return new CustomClassParser(previousUsages.LatestUsedType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, method, readers, memberReaders) {
            GroupingRules = groupingRules,
            Context = colModifier,
        };
    }
}
