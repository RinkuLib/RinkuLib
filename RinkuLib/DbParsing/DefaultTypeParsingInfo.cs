using System.Reflection;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing; 
/// <summary>The default implementation of TypeParsingInfo</summary>
public class DefaultTypeParsingInfo(Type Type) : TypeParsingInfo {
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
            throw new ArgumentException($"The associated type with this instance is {Type} so it can't be bound with {TargetType}");
    }
    /// <summary>
    /// The internal state tracker indicating if the automatic discovery of members and 
    /// constructors (Registration Phase) has been performed.
    /// </summary>
    private bool IsInit;
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
                if (!c.TargetType.IsStackEquivalent(Type))
                    throw new InvalidOperationException($"the method or constructor must be of type {Type} (returning type)");
                var declare = c.MethodBase.DeclaringType!;
                if (declare != Type && declare.IsGenericType)
                    throw new Exception($"Cannot add a possible construction from a generic type other then the target type Target:{Type} Used:{declare}");
            }
            Interlocked.Exchange(ref MCIs, value.ToArray());
        }
    }
    private MemberParser[] Members = [];
    /// <summary>
    /// A collection of public properties and fields that can be set after instantiation.
    /// </summary>
    public ReadOnlySpan<MemberParser> AvailableMembers {
        get => Members; set {
            for (var i = 0; i < value.Length; i++) {
                var c = value[i];
                if (!c.TargetType.IsStackEquivalent(Type))
                    throw new InvalidOperationException($"the method or constructor must be of type {Type}");
                var declare = c.Member.DeclaringType!;
                if (declare != Type && declare.IsGenericType)
                    throw new Exception($"Cannot add a possible construction from a generic type other then the target type Target:{Type} Used:{declare}");
            }
            Interlocked.Exchange(ref Members, value.ToArray());
        }
    }
    private MethodBase? ParameterlessConstructor {
        get => field; set {
            if (value is ConstructorInfo) {
                if (value.DeclaringType != Type)
                    throw new InvalidOperationException($"the constructor must be of type {Type}");
            }
            else {
                if (value is not MethodInfo method)
                    throw new InvalidOperationException("the value must be a ctor or a method");
                if (method.ReturnType != Type)
                    throw new InvalidOperationException($"the method must return {Type}");
                var ex = MethodCtorInfo.ValidateMethodReturn(method);
                if (ex is not null)
                    throw ex;
            }
            Interlocked.Exchange(ref field, value);
        }
    }/// <summary>
     /// Scans the type via reflection to find all public constructors, static methods, 
     /// properties, and fields for automatic mapping.
     /// </summary>
    public void Init() {
        lock (WriteLock) {
            if (IsInit)
                return;
            var type = Nullable.GetUnderlyingType(Type) ?? Type;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            List<MemberParser> memberParsers = [];
            for (int i = 0; i < fields.Length; i++) {
                var field = fields[i];
                if (!field.IsInitOnly)
                    continue;
                var p = ParamInfo.TryNew(field);
                if (p is not null)
                    memberParsers.Add(new(field, p));
            }
            for (int i = 0; i < props.Length; i++) {
                var prop = props[i];
                if (!prop.CanWrite || prop.GetSetMethod() is null)
                    continue;
                var p = ParamInfo.TryNew(prop);
                if (p is not null)
                    memberParsers.Add(new(prop, p));
            }
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var staticMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            var infoList = new List<MethodCtorInfo>(constructors.Length);
            foreach (var constructor in constructors) {
                var ps = MethodCtorInfo.TryMakeParameters(constructor);
                if (MethodCtorInfo.TryNew(constructor, ps, out var mci))
                    infoList.Add(mci);
                else if (ParameterlessConstructor is null && ps is not null && ps.Length == 0)
                    ParameterlessConstructor = constructor;
            }
            foreach (var method in staticMethods) {
                if (method.ReturnType != type || method.IsGenericMethod || !method.IsStatic)
                    continue;
                var ps = MethodCtorInfo.TryMakeParameters(method);
                if (MethodCtorInfo.TryNew(method, ps, out var mci))
                    infoList.Add(mci);
            }
            if (memberParsers.Count > 0) {
                if (Members.Length == 0)
                    Members = [.. memberParsers];
                else {
                    var mp = CollectionsMarshal.AsSpan(memberParsers);
                    var result = new MemberParser[Members.Length + mp.Length];
                    for (int i = 0; i < mp.Length; i++)
                        result[i] = mp[i];
                    Array.Copy(Members, 0, result, Members.Length, Members.Length);
                    Members = result;
                }
            }
            if (infoList.Count > 0) {
                var infos = CollectionsMarshal.AsSpan(infoList);
                if (MCIs.Length == 0)
                    MCIs = MethodCtorInfo.GetOrderedInfos(infos);
                else {
                    var result = new MethodCtorInfo[MCIs.Length + infos.Length];
                    Array.Copy(MCIs, 0, result, 0, MCIs.Length);
                    infos.CopyTo(result.AsSpan(MCIs.Length));
                    MCIs = MethodCtorInfo.GetOrderedInfos(result);
                }
            }
            IsInit = true;
        }
    }
    /// <inheritdoc/>
    public override void AddAltName(string defaultName, string nameToAdd) {
        if (!IsInit)
            Init();
        for (int i = 0; i < MCIs.Length; i++) {
            var parameters = MCIs[i].Parameters;
            for (int j = 0; j < parameters.Length; j++) {
                var p = parameters[j];
                if (string.Equals(p.GetName(), defaultName, StringComparison.OrdinalIgnoreCase))
                    p.AddAltName(nameToAdd);
            }
        }
        for (int i = 0; i < Members.Length; i++) {
            var p = Members[i].Param;
            if (string.Equals(p.GetName(), defaultName, StringComparison.OrdinalIgnoreCase))
                p.AddAltName(nameToAdd);
        }
    }
    /// <inheritdoc/>
    public override void SetInvalidOnNull(string defaultName, bool invalidOnNull) {
        if (!IsInit)
            Init();
        for (int i = 0; i < MCIs.Length; i++) {
            var parameters = MCIs[i].Parameters;
            for (int j = 0; j < parameters.Length; j++) {
                var p = parameters[j];
                if (string.Equals(p.GetName(), defaultName, StringComparison.OrdinalIgnoreCase))
                    p.SetInvalidOnNull(invalidOnNull);
            }
        }
    }
    /// <inheritdoc/>
    public override void AddPossibleConstruction(MethodBase methodBase)
        => AddPossibleConstruction(new MethodCtorInfo(methodBase));
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public void AddPossibleConstruction(MethodCtorInfo mci) {
        lock (WriteLock) {
            var target = mci.TargetType;
            if (!target.IsStackEquivalent(Type))
                throw new Exception($"the expected type is {Type} but the provided type via the method is {mci.TargetType}");
            var declare = mci.MethodBase.DeclaringType!;
            if (declare != Type && declare.IsGenericType)
                throw new Exception($"Cannot add a possible construction from a generic type other then the target type Target:{Type} Used:{declare}");
            mci.InsertInto(ref MCIs);
        }
    }
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        if (!IsInit)
            Init();
        colModifier = colModifier.Add(paramInfo.NameComparer);
        Span<bool> checkpoint = stackalloc bool[colUsage.Length];
        colUsage.InitCheckpoint(checkpoint, out var lastIndUsed);
        var mcis = MCIs;
        List<DbItemParser> readers = [];
        MemberInfo? method = null;
        var actualType = Nullable.GetUnderlyingType(currentClosedType) ?? currentClosedType;
        var genericArguments = actualType.IsGenericType ? actualType.GetGenericArguments() : [];
        bool canCompleteWithMembers = false;
        for (int i = 0; i < mcis.Length; i++) {
            var mci = mcis[i];
            bool forcedRegister = mci.ParametersAreReadable;
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
                var node = typeInfo.TryGetParser(actualType, paramClosedType, param, columns, colModifier, ref colUsage);
                if (node is null)
                    break;
                readers.Add(node);
            }
            if (readers.Count == parameters.Length) {
                method = mci.MethodBase.GetClosedMember(currentClosedType);
                canCompleteWithMembers = mci.CanCompleteWithMembers;
                break;
            }
            colUsage.Rollback(checkpoint, lastIndUsed);
            readers.Clear();
        }
        if (method is null) {
            method = ParameterlessConstructor?.GetClosedMember(currentClosedType);
            if (method is null)
                return paramInfo.FallbackTryGetParser(currentClosedType);
            canCompleteWithMembers = true;
        }
        if (!canCompleteWithMembers)
            return new CustomClassParser(parentType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, method, readers);
        List<(MemberInfo, DbItemParser)> memberReaders = [];
        var members = Members;
        colModifier.Flags &= ~UsageFlags.SequentialRead;
        for (int i = 0; i < members.Length; i++) {
            var param = members[i].Param;
            var t = Nullable.GetUnderlyingType(param.Type);
            var isNullableStruct = t is not null;
            var paramClosedType = (t ?? param.Type).CloseType(genericArguments);
            if (isNullableStruct)
                paramClosedType = typeof(Nullable<>).MakeGenericType(paramClosedType);
            if (!TryGetInfo(paramClosedType, out var typeInfo))
                throw new Exception("should not happend");
            var node = typeInfo.TryGetParser(actualType, paramClosedType, param, columns, colModifier, ref colUsage);
            if (node is not null)
                memberReaders.Add((members[i].Member.GetClosedMember(currentClosedType), node));
        }
        if (memberReaders.Count == 0 && readers.Count == 0)
            return null;
        return new CustomClassParser(parentType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, method, readers, memberReaders);
    }
}
