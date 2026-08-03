using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// A key of one or more marked sources, each a member (property or field) or a construction parameter, negotiated
/// to its own column with reuse and compared by its own equality. Several sources compose a composite key. It is
/// the key of a marked member or, when a construction's marked parameters override the type, of those parameters.
/// </summary>
public sealed class EqualityGroupingRule : IGroupingRule {
    private readonly object[] Sources;
    /// <summary>A key over one or more marked members (properties or fields).</summary>
    public EqualityGroupingRule(params MemberInfo[] members) => Sources = RequireSources(members);
    /// <summary>A key over one or more marked construction parameters.</summary>
    public EqualityGroupingRule(params ParameterInfo[] parameters) => Sources = RequireSources(parameters);
    /// <summary>A key over one or more columns named directly, each read as whatever type its column carries.</summary>
    public EqualityGroupingRule(params string[] columns) => Sources = RequireSources(columns);
    /// <inheritdoc/>
    public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
        colModifier.Flags |= UsageFlags.CanReuse | UsageFlags.RemoveSequentialRead;
        var components = new (IBoundaryReader, IBoundaryField)[Sources.Length];
        for (int i = 0; i < Sources.Length; i++) {
            var (name, type) = Resolve(Sources[i], spanningType, columns);
            var reader = GroupKeyNegotiation.NegotiateReader(name, type, columns, colModifier, Sources[i].ToString()!);
            components[i] = (build.Reader(reader, type), build.Field(type));
        }
        return new EqualityBoundary(components);
    }
    private static (INameComparer Name, Type Type) Resolve(object source, Type spanningType, ColumnInfo[] columns) => source switch {
        PropertyInfo or FieldInfo => FromMember(Closed(spanningType, (MemberInfo)source)),
        ParameterInfo p => FromParam(Closed(spanningType, p)),
        string name => ResolveColumn(name, columns),
        _ => throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"a group key source must be a property, field, parameter, or column name, not {source?.GetType()}"),
    };
    private static (INameComparer, Type) FromMember(MemberInfo member) => member switch {
        PropertyInfo p => (Usable(member, ParamInfo.TryNew(p)).NameComparer, p.PropertyType),
        FieldInfo f => (Usable(member, ParamInfo.TryNew(f)).NameComparer, f.FieldType),
        _ => throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"a group key member must be a property or field, not {member?.GetType()}"),
    };
    private static (INameComparer, Type) FromParam(ParameterInfo p) => (Usable(p, ParamInfo.TryNew(p)).NameComparer, p.ParameterType);
    private static object[] RequireSources<T>(T[] sources) {
        if (sources is null || sources.Length == 0)
            throw new RinkuConfigurationException(ErrorCodes.GroupKeyUnmapped, "an equality group key requires at least one source");
        return sources.Cast<object>().ToArray();
    }
    /// <summary>Closes a member declared on a generic definition to the spanning type, so a key on a generic member reads its resolved type.</summary>
    private static MemberInfo Closed(Type spanningType, MemberInfo member) {
        if (member.DeclaringType is { IsGenericTypeDefinition: true } && spanningType.IsGenericType)
            return (MemberInfo?)spanningType.GetProperty(member.Name) ?? spanningType.GetField(member.Name) ?? member;
        return member;
    }
    /// <summary>Closes a construction parameter declared on a generic definition to the spanning type, so a parameter key reads its resolved type.</summary>
    private static ParameterInfo Closed(Type spanningType, ParameterInfo p) {
        if (p.Member.DeclaringType is { IsGenericTypeDefinition: true } && spanningType.IsGenericType)
            return ((MethodBase)p.Member.GetClosedMember(spanningType)).GetParameters()[p.Position];
        return p;
    }
    /// <summary>Resolves a column-name key to the column's own type, so the key reads it whatever that type is. Only the name is matched.</summary>
    private static (INameComparer, Type) ResolveColumn(string name, ColumnInfo[] columns) {
        for (int i = 0; i < columns.Length; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return (ParamInfo.Create(columns[i].Type, name, []).NameComparer, columns[i].Type);
        throw new RinkuConfigurationException(ErrorCodes.GroupKeyUnmapped, $"the group key column {name} matched no column");
    }
    private static ParamInfo Usable(object source, ParamInfo? param)
        => param ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"the group key {source} is not a usable type");
}

/// <summary>
/// A boundary decided by a marked <c>static (bool, TKey) Method(TKey stored, ...params)</c>. The stored key is the
/// first parameter; every parameter after it is negotiated like an ordinary reader, so the method's inputs carry
/// alternates and nesting.
/// </summary>
public sealed class MethodGroupingRule : IGroupingRule {
    private readonly MethodInfo Method;
    private readonly Type KeyType;
    private readonly ParameterInfo[] Params;
    /// <summary>Validates the <c>static (bool, TKey) Method(TKey, ...)</c> shape.</summary>
    public MethodGroupingRule(MethodInfo method) {
        var parameters = method.GetParameters();
        if (!method.IsStatic || parameters.Length < 1
            || !method.ReturnType.IsGenericType || method.ReturnType.GetGenericTypeDefinition() != typeof(ValueTuple<,>)
            || method.ReturnType.GetGenericArguments()[0] != typeof(bool)
            || method.ReturnType.GetGenericArguments()[1] != parameters[0].ParameterType)
            throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType,
                $"a [GroupKey] method must be static (bool, TKey) {method.Name}(TKey, ...negotiated parameters)");
        Method = method;
        KeyType = parameters[0].ParameterType;
        Params = parameters[1..];
    }
    /// <inheritdoc/>
    public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
        colModifier.Flags |= UsageFlags.CanReuse | UsageFlags.RemoveSequentialRead;
        var readers = new IBoundaryReader[Params.Length];
        for (int i = 0; i < Params.Length; i++) {
            var p = Params[i];
            var param = ParamInfo.TryNew(p)
                ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"the group key parameter {p.Name} is not a usable type");
            var reader = GroupKeyNegotiation.NegotiateReader(param.NameComparer, p.ParameterType, columns, colModifier, $"parameter {p.Name}");
            readers[i] = build.Reader(reader, p.ParameterType);
        }
        return new MethodBoundary(Method, KeyType, build.Field(KeyType), readers);
    }
}

/// <summary>
/// The default boundary of a construction with no marked key, read from its argument shape. The arguments before the
/// first accumulator, each already negotiated for construction, become an equality key, reused so their columns still
/// feed the arguments. With none before the first accumulator the group never changes (a whole tuple of collections),
/// unless a non-accumulator argument follows an accumulator, which is ambiguous and throws <c>MissingGroupBoundary</c>.
/// It is a rule like any other so the emit only ever calls <see cref="IGroupingRule.MakeBoundary"/>.
/// </summary>
internal sealed class InferredGroupingRule(IReadOnlyList<DbItemPlan> arguments, MethodBase construction, Type resultType) : IGroupingRule {
    /// <inheritdoc/>
    public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
        int firstAccumulator = arguments.Count;
        for (int i = 0; i < arguments.Count; i++)
            if (arguments[i] is IMultiRowPlan) {
                firstAccumulator = i;
                break;
            }
        if (firstAccumulator == 0) {
            for (int i = 1; i < arguments.Count; i++)
                if (arguments[i] is not IMultiRowPlan)
                    throw new RinkuConfigurationException(ErrorCodes.MissingGroupBoundary,
                        $"{resultType} has a value after a collection and no group key to tell its groups apart; mark its key with [GroupKey]");
            return AlwaysGroupedBoundary.Instance;
        }
        var parameters = construction.GetParameters();
        var components = new (IBoundaryReader, IBoundaryField)[firstAccumulator];
        for (int i = 0; i < firstAccumulator; i++) {
            if (!DbItemPlan.AllSimple(arguments[i]))
                throw new RinkuConfigurationException(ErrorCodes.MissingGroupBoundary,
                    $"{resultType} spans a value before its collection and cannot be a default key; mark its key with [GroupKey]");
            var type = parameters[i].ParameterType;
            components[i] = (build.Reader(arguments[i], type), build.Field(type));
        }
        return new EqualityBoundary(components);
    }
}
