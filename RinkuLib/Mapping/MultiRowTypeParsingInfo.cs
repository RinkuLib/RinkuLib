using System.Reflection;
using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>
/// Registers a type that adds values from several rows into one result.
/// Supply how to create the working value, how to add one element, and how to create the final result.
/// Use <see cref="ForList"/> for lists and <see cref="ForArray"/> for arrays.
/// </summary>
public class MultiRowTypeParsingInfo : TypeParsingInfo, IMultiRowTypeParsingInfo {
    /// <summary>
    /// The construction that seeds an empty accumulator, a parameterless constructor or a static factory. The
    /// accumulator each row folds into is its declaring type (a constructor) or its return (a factory).
    /// </summary>
    public readonly MethodBase InitialState;
    /// <summary>
    /// The method that adds one element to the working value.
    /// Its single parameter defines the element type and any return value is ignored.
    /// </summary>
    public readonly MethodBase Add;
    /// <summary>
    /// The construction from the accumulator to the declared value, or <see langword="null"/> when the accumulator
    /// already is the value (a <c>List</c>, or a set filled in place).
    /// </summary>
    public readonly MethodBase? Finish;
    /// <summary>Builds a multi-row info seeding the accumulator with <paramref name="initialState"/>, folding with <paramref name="add"/> (required to determine element type), and finishing with <paramref name="finish"/> (<see langword="null"/> for identity).</summary>
    public MultiRowTypeParsingInfo(MethodBase initialState, MethodBase? add, MethodBase? finish) {
        if (add is null)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, $"{nameof(MultiRowTypeParsingInfo)} requires Add to be provided to determine the element type");
        InitialState = initialState;
        Add = add;
        Finish = finish;
    }

    private static readonly ConstructorInfo ListCtor = typeof(List<>).GetConstructor(Type.EmptyTypes)!;
    private static readonly MethodInfo ListAdd = typeof(List<>).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .First(m => m.Name == nameof(List<int>.Add) && m.GetParameters().Length == 1);
    /// <summary>The built-in <c>List&lt;&gt;</c> mapping, where the accumulator is already the result.</summary>
    public static readonly MultiRowTypeParsingInfo ForList = new(ListCtor, ListAdd, null);
    /// <summary>
    /// Maps an array, folding into a <c>List&lt;element&gt;</c> and finishing with its <c>ToArray</c>. Register it
    /// against a specific array type, for example <c>AddOrSet(typeof(Child[]), MultiRowTypeParsingInfo.ForArray)</c>.
    /// </summary>
    public static readonly MultiRowTypeParsingInfo ForArray = new(ListCtor, ListAdd, typeof(List<>).GetMethod(nameof(List<int>.ToArray), Type.EmptyTypes)!);

    /// <inheritdoc/>
    public override void ValidateCanUseType(Type targetType) {
        if (targetType.IsArray && targetType.GetArrayRank() == 1)
            return;
        if (targetType.IsGenericType && targetType.GetGenericArguments().Length == 1)
            return;
        var result = Finish is null ? AccumulatorOf(InitialState) : ResultOf(Finish);
        if (targetType == result)
            return;
        throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
            $"{nameof(MultiRowTypeParsingInfo)} maps a single-dimension array, a single-element collection, or {result}, and cannot be bound with {targetType}");
    }

    /// <inheritdoc/>
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
        RegisterGenericArguments(currentClosedType, callerFlags);
        var elementType = ElementOf(currentClosedType);
        var initialState = Close(InitialState, elementType);
        var finish = Finish is null ? null : Close(Finish, elementType);
        var accumulatorType = AccumulatorOf(initialState);
        var add = ResolveAdd(elementType, Add);

        bool isRoot = paramInfo.Type == ParamInfo.NoType;
        TypeParsingInfo elementInfo;
        if (isRoot)
            elementInfo = ForceGet(elementType);
        else {
            if (!TryGetInfo(elementType, out var nestedElementInfo))
                return null;
            elementInfo = nestedElementInfo;
        }

        INullColHandler elementNullability;
        if (!isRoot)
            elementNullability = AbortOnNullAndNotNullHandle.Instance;
        else if (paramInfo.NullColHandler == TypeParser.GetDefaultNullColHandler(currentClosedType))
            elementNullability = TypeParser.GetDefaultNullColHandler(elementType);
        else
            elementNullability = paramInfo.NullColHandler;
        var elementParamInfo = new ParamInfo(elementType, elementNullability, paramInfo.NameComparer);
        var elementNode = elementInfo.TryGetParser(elementType, previousUsages, elementParamInfo, columns, colModifier, ref colUsage);
        return elementNode is null ? null
            : new AccumulatorPlan(elementNode, elementType, accumulatorType, add, initialState, finish, paramInfo.NullColHandler);
    }

    private Type ElementOf(Type currentClosedType) {
        var addParams = Add.GetParameters();
        if (addParams.Length == 0)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
                $"{nameof(MultiRowTypeParsingInfo)} requires Add to have a single parameter specifying the element type");
        var paramType = addParams[0].ParameterType;

        if (!paramType.IsGenericParameter)
            return paramType;

        if (currentClosedType.IsArray)
            return paramType.CloseType([currentClosedType.GetElementType()!]);
        return paramType.CloseType(currentClosedType.GetGenericArguments());
    }

    internal static Type AccumulatorOf(MethodBase initialState)
        => initialState is ConstructorInfo ctor ? ctor.DeclaringType! : ((MethodInfo)initialState).ReturnType;

    private static Type ResultOf(MethodBase finish)
        => finish is ConstructorInfo ctor ? ctor.DeclaringType! : ((MethodInfo)finish).ReturnType;

    internal static MethodBase Close(MethodBase construction, Type elementType) {
        var declaring = construction.DeclaringType!;
        if (!declaring.IsGenericTypeDefinition)
            return construction;
        var closed = declaring.MakeGenericType(elementType);
        return MethodBase.GetMethodFromHandle(construction.MethodHandle, closed.TypeHandle)!;
    }

    internal static MethodInfo ResolveAdd(Type elementType, MethodBase add) => (MethodInfo)Close(add, elementType);

}

/// <summary>
/// Keeps a null element in a collection instead of dropping it.
/// A reference or nullable element becomes <see langword="null"/> and another value type becomes its default value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class KeepNullElementsAttribute : Attribute, INullColHandlerMaker {
    /// <inheritdoc/>
    public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param) => KeepNullElementsHandle.Instance;
}

internal sealed class AccumulatorPlan(DbItemPlan element, Type elementType, Type accumulatorType, MethodInfo add, MethodBase initialState, MethodBase? construct, INullColHandler nullRule) : DbItemPlan, IMultiRowPlan {
    public DbItemPlan Element => element;
    public INullColHandler NullRule => nullRule;
    public Type ElementType => elementType;
    public Type BufferType => accumulatorType;
    public MethodInfo AddMethod => add;
    public MethodBase InitialState => initialState;
    public MethodBase? Construct => construct;
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
    public override bool IsSequencial(ref int previousIndex) => false;
    public override IEnumerable<DbItemPlan> Children => [element];
}
