using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RinkuLib.DbParsing;

public class BaseTypeMatcher : IDbTypeParserInfoMatcher {
    public static readonly BaseTypeMatcher Instance = new();
    private BaseTypeMatcher() {}
    public bool CanUseType(Type TargetType)
        => TargetType.IsBaseType() || TargetType.IsEnum;
    public DbItemParser? TryGetParser(Type closedTargetType, INullColHandler nullColHandler, ColumnInfo[] columns, ColModifier colModifier, bool isNullable, ref ColumnUsage colUsage) {
        int i = 0;
        for (; i < columns.Length; i++) {
            if (colUsage.IsUsed(i))
                continue;
            var column = columns[i];
            if (column.Type.CanConvert(closedTargetType) && colModifier.Match(column.Name, NoNameComparer.Instance))
                break;
        }
        if (i >= columns.Length)
            return null;
        colUsage.Use(i);
        if (isNullable && closedTargetType.IsValueType)
            closedTargetType = typeof(Nullable<>).MakeGenericType(closedTargetType);
        return new BasicParser(closedTargetType, nullColHandler, i);
    }
}
/// <summary>
/// A metadata registry representing a specific <see cref="Type"/>. 
/// It stores the construction paths and members required to transform a schema into an object.
/// </summary>
/// <remarks>
/// <para><b>I. Instance-Level Configuration:</b></para>
/// This class provides an API to refine mapping behavior, such as adding alternative names 
/// for matching or configuring null-handling recovery. It is also responsible for executing 
/// the resolution logic that produces a parser.
/// 
/// <para><b>II. Static Registry &amp; Generic Fallback:</b></para>
/// Instances are managed through a global cache to ensure metadata consistency. 
/// The registry logic supports <b>Specialization</b>: it can store metadata for a 
/// specific closed generic type, but will fall back to the <b>Open Generic Type Definition</b> 
/// if a specific version isn't registered. All lookups automatically unwrap <see cref="Nullable{T}"/>.
/// 
/// <para><b>III. Matching Logic &amp; Injected Implementation:</b></para>
/// The class contains a default matching process that reconciles stored metadata 
/// with a received schema. A specific injection point (<see cref="Matcher"/>) allows 
/// this process to be replaced with custom logic.
/// </remarks>
public class TypeParsingInfo {
    static TypeParsingInfo() {
        AddValueTuple(typeof(ValueTuple<>));
        AddValueTuple(typeof(ValueTuple<,>));
        AddValueTuple(typeof(ValueTuple<,,>));
        AddValueTuple(typeof(ValueTuple<,,,>));
        AddValueTuple(typeof(ValueTuple<,,,,>));
        AddValueTuple(typeof(ValueTuple<,,,,,>));
        AddValueTuple(typeof(ValueTuple<,,,,,,>));
        AddValueTuple(typeof(ValueTuple<,,,,,,,>));
    }
    private static void AddValueTuple(Type tupleType) {
        ConstructorInfo[] ctors = tupleType.GetConstructors();
        Type[] genericArgs = tupleType.GetGenericArguments();
        int argCount = genericArgs.Length;
        ConstructorInfo ctor = null!;
        ParameterInfo[] parameters = null!;
        for (int i = 0; i < ctors.Length; i++) {
            ctor = ctors[i];
            parameters = ctor.GetParameters();

            if (parameters.Length != argCount)
                continue;
            bool isMatch = true;
            for (int j = 0; j < argCount; j++) {
                if (parameters[j].ParameterType != genericArgs[j]) {
                    isMatch = false;
                    break;
                }
            }
            if (isMatch)
                break;
        }
        var methodCtorInfo = new MethodCtorInfo(ctor);
        for (int i = 0; i < parameters.Length; i++)
            methodCtorInfo.Parameters[i].NameComparer = NoNameComparer.Instance;
        GetOrAdd(tupleType).AddPossibleConstruction(methodCtorInfo);
    }
    private TypeParsingInfo(Type Type, IDbTypeParserInfoMatcher? Matcher) {
        this.Type = Nullable.GetUnderlyingType(Type) ?? Type;
        IsInit = false;
        this._matcher = Matcher;
    }
    /// <summary>
    /// The internal state tracker indicating if the automatic discovery of members and 
    /// constructors (Registration Phase) has been performed.
    /// </summary>
    private bool IsInit;
    /// <summary>
    /// Global cache of type metadata. Access is managed through static methods 
    /// to ensure thread-safety and proper initialization.
    /// </summary>
    private static Dictionary<Type, TypeParsingInfo> TypeInfos = [];
    private static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        WriteLock = new();
    /// <summary>
    /// The target type being handled.
    /// </summary>
    public readonly Type Type;
    private MethodCtorInfo[] MCIs = [];
    private IDbTypeParserInfoMatcher? _matcher;
    /// <summary>
    /// A custom injection point that allows developers to replace the default matching logic.
    /// If provided, this implementation takes full control over the Negotiation Phase for this type.
    /// </summary>
    public IDbTypeParserInfoMatcher? Matcher { get {
            if (!IsInit)
                Init();
            return _matcher;
        } set {
            if (value is not null && value.CanUseType(Type))
                throw new InvalidOperationException($"the Matcher must be of type {Type}");
            Interlocked.Exchange(ref _matcher, value);
        }
    }
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
    private MethodBase? ParameterlessConstructor { get => field; set {
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
    }
    /// <summary>
    /// Checks if a type is supported for mapping. 
    /// Automatically unwraps <see cref="Nullable{T}"/> to evaluate the underlying type.
    /// </summary>
    public static bool IsUsableType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericParameter || type.IsBaseType() || type.IsEnum)
            return true;
        if (TypeInfos.ContainsKey(type))
            return true;
        if (type.IsGenericType && TypeInfos.ContainsKey(type.GetGenericTypeDefinition()))
            return true;
        if (type.IsAssignableTo(typeof(IDbReadable)))
            return true;
        return false;
    }
    /// <summary>
    /// Attempts to retrieve a registry for the specified type.
    /// </summary>
    /// <remarks>
    /// <b>Lookup Logic:</b>
    /// <list type="number">
    /// <item>Unwraps <see cref="Nullable{T}"/>.</item>
    /// <item>Returns an exact match if one exists.</item>
    /// <item>If the type is a closed generic and no exact match exists, it attempts to
    ///    return the registry for the <b>Open Generic Type Definition</b>.</item>
    /// <item>If not found and the type implements <see cref="IDbReadable"/>, it registers and 
    /// returns it, defaulting to the <b>Open Generic Type Definition</b> for generics.</item>
    /// </list>
    /// </remarks>
    public static bool TryGetInfo(Type type, [MaybeNullWhen(false)] out TypeParsingInfo typeInfo) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out typeInfo))
            return true;
        if (type.IsBaseType() || type.IsEnum) {
            typeInfo = Add(type);
            return true;
        }
        if (type.IsGenericType) {
            type = type.GetGenericTypeDefinition();
            if (TypeInfos.TryGetValue(type, out typeInfo))
                return true;
        }
        if (!type.IsAssignableTo(typeof(IDbReadable)))
            return false;
        typeInfo = Add(type);
        return true;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo ForceGet(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (!type.IsGenericType)
            return Add(type);
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        return Add(type);
    }
    /// <summary>
    /// Performs a prioritized lookup in the global cache.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Unwraps <see cref="Nullable{T}"/>.</item>
    /// <item>Returns an exact match if one exists.</item>
    /// <item>If the type is a closed generic and no exact match exists, it attempts to
    ///    return the registry for the <b>Open Generic Type Definition</b>.</item>
    /// </list>
    /// </remarks>
    public static TypeParsingInfo? Get(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (!type.IsGenericType)
            return null;
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        return null;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo GetOrAdd(Type type, bool saveAsGenericDefinitionWhenGeneric = true) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (!type.IsGenericType)
            return Add(type);
        if (!saveAsGenericDefinitionWhenGeneric)
            return Add(type);
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        return Add(type);
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo GetOrAdd<T>(bool saveAsGenericDefinitionWhenGeneric = true) => GetOrAdd(typeof(T), saveAsGenericDefinitionWhenGeneric);
    private static TypeParsingInfo Add(Type type) {
        lock (WriteLock) {
            var newDict = new Dictionary<Type, TypeParsingInfo>(TypeInfos);
            var res = new TypeParsingInfo(type, null);
            newDict[type] = res;
            TypeInfos = newDict;
            return res;
        }
    }
    /// <summary>
    /// Scans the type via reflection to find all public constructors, static methods, 
    /// properties, and fields for automatic mapping.
    /// </summary>
    public void Init() {
        lock (WriteLock) {
            if (IsInit)
                return;
            var type = Nullable.GetUnderlyingType(Type) ?? Type;
            if (BaseTypeMatcher.Instance.CanUseType(type)) {
                _matcher = BaseTypeMatcher.Instance;
                IsInit = true;
                return;
            }
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
    /// <summary>
    /// Maps a property name to a specific database column alias.
    /// </summary>
    /// <param name="defaultName">The member name in C#.</param>
    /// <param name="nameToAdd">The alternative name to add to the member.</param>
    public void AddAltName(string defaultName, string nameToAdd) {
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
    /// <summary>
    /// Configures the null-value response behavior for parameters matching <paramref name="defaultName"/>.
    /// </summary>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="jumpWhenNull">Wether or not the parameter should jump when null</param>
    public void SetJumpWhenNull(string defaultName, bool jumpWhenNull) {
        if (!IsInit)
            Init();
        for (int i = 0; i < MCIs.Length; i++) {
            var parameters = MCIs[i].Parameters;
            for (int j = 0; j < parameters.Length; j++) {
                var p = parameters[j];
                if (string.Equals(p.GetName(), defaultName, StringComparison.OrdinalIgnoreCase))
                    p.SetJumpWhenNull(jumpWhenNull);
            }
        }
    }
    public void AddPossibleConstruction(MethodBase methodBase)
        => AddPossibleConstruction(new MethodCtorInfo(methodBase));
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
    /// <summary>
    /// Evaluates a received schema against the registered metadata to emit a specialized parser.
    /// </summary>
    /// <remarks>
    /// If a custom <see cref="Matcher"/> is assigned, it is used to perform the resolution. 
    /// Otherwise, the default logic evaluates <see cref="PossibleConstructors"/> and 
    /// <see cref="AvailableMembers"/> against the provided <paramref name="columns"/> schema.
    /// </remarks>
    /// <returns>
    /// A configured <see cref="DbItemParser"/> if the schema satisfies a construction path; otherwise, null.
    /// </returns>
    public DbItemParser? TryGetParser(Type[] declaringTypeArguments, INullColHandler nullColHandler, ColumnInfo[] columns, ColModifier colModifier, bool isNullable, ref ColumnUsage colUsage) {
        if (!IsInit)
            Init();
        var closedType = Type.CloseType(declaringTypeArguments);
        if (_matcher is not null)
            return _matcher.TryGetParser(closedType, nullColHandler, columns, colModifier, isNullable, ref colUsage);
        var actualType = isNullable && closedType.IsValueType ? typeof(Nullable<>).MakeGenericType(closedType) : closedType;
        Span<bool> checkpoint = stackalloc bool[colUsage.Length];
        colUsage.InitCheckpoint(checkpoint);
        var mcis = MCIs;
        List<DbItemParser> readers = [];
        MemberInfo? method = null;
        bool canCompleteWithMembers = false;
        for (int i = 0; i < mcis.Length; i++) {
            var mci = mcis[i];
            var parameters = mci.Parameters;
            for (int j = 0; j < parameters.Length; j++) {
                var r = parameters[j].TryGetParser(declaringTypeArguments, columns, colModifier, ref colUsage);
                if (r is null)
                    break;
                readers.Add(r);
            }
            if (readers.Count == parameters.Length) {
                method = mci.MethodBase.GetClosedMember(closedType);
                canCompleteWithMembers = mci.CanCompleteWithMembers;
                break;
            }
            colUsage.Rollback(checkpoint);
            readers.Clear();
        }
        if (method is null) {
            method = ParameterlessConstructor?.GetClosedMember(closedType);
            if (method is null)
                return null;
            canCompleteWithMembers = true;
        }
        if (!canCompleteWithMembers)
            return new CustomClassParser(actualType, nullColHandler, method, readers);
        List<(MemberInfo, DbItemParser)> memberReaders = [];
        var members = Members;
        for (int i = 0; i < members.Length; i++) {
            var r = members[i].Param.TryGetParser(declaringTypeArguments, columns, colModifier, ref colUsage);
            if (r is not null)
                memberReaders.Add((members[i].Member.GetClosedMember(closedType), r));
        }
        if (memberReaders.Count == 0 && readers.Count == 0)
            return null;
        return new CustomClassParser(actualType, nullColHandler, method, readers, memberReaders);
    }
}