using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// How a multi-row type reads, the registry entry behind folding many rows into one value, whether that value is a
/// <c>List&lt;T&gt;</c>, a set, an array, or an aggregate that is no collection at all. The fold is always the same
/// three steps the emit bakes in: <see cref="InitialState"/> seeds an accumulator, <see cref="Add"/> folds one
/// element per row, and <see cref="Finish"/> turns the accumulator into the declared value. Each is a
/// <see cref="MethodBase"/>, a constructor, a static factory, or a method, so the emit calls it directly, and each
/// may be defined on an open generic accumulator (such as the <c>List&lt;&gt;</c> members) and closes to the
/// element type at negotiation. The library ships only <see cref="ForList"/>; register an instance for any other
/// multi-row type. The element read per row is the collection's generic argument or array element, or, for a
/// value that is no collection, the single parameter of <see cref="Add"/>.
/// </summary>
public class MultiRowTypeParsingInfo : TypeParsingInfo {
    /// <summary>
    /// The construction that seeds an empty accumulator, a parameterless constructor or a static factory. The
    /// accumulator each row folds into is its declaring type (a constructor) or its return (a factory).
    /// </summary>
    public readonly MethodBase InitialState;
    /// <summary>
    /// The accumulator's <c>Add</c> for one element, its single parameter the element type. Its return value,
    /// if non-void, is discarded. Can be open generic when the accumulator is generic; it is closed with
    /// the resolved element type at negotiation.
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
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, bool registerRecursively = false) {
        var elementType = ElementOf(currentClosedType);
        var initialState = Close(InitialState, elementType);
        var finish = Finish is null ? null : Close(Finish, elementType);
        var accumulatorType = AccumulatorOf(initialState);
        var add = ResolveAdd(accumulatorType, elementType, Add);

        if (!TryGetInfo(elementType, out var elementInfo))
            return null;
        var elementParamInfo = new ParamInfo(elementType, InvalidOnNullAndNotNullHandle.Instance, paramInfo.NameComparer);
        var elementNode = elementInfo.TryGetParser(elementType, previousUsages, elementParamInfo, columns, colModifier, ref colUsage, registerRecursively);
        return elementNode is null ? null
            : new AccumulatorPlan(elementNode, elementType, accumulatorType, add, initialState, finish, paramInfo.NullColHandler);
    }

    /// <summary>The element type read per row: the single parameter of the Add method, resolved from the closed collection type when Add is generic.</summary>
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

    /// <summary>The type a seeding construction produces, its declaring type for a constructor or its return for a factory.</summary>
    internal static Type AccumulatorOf(MethodBase initialState)
        => initialState is ConstructorInfo ctor ? ctor.DeclaringType! : ((MethodInfo)initialState).ReturnType;

    /// <summary>The type a finishing construction produces, its declaring type for a constructor or its return for a method.</summary>
    private static Type ResultOf(MethodBase finish)
        => finish is ConstructorInfo ctor ? ctor.DeclaringType! : ((MethodInfo)finish).ReturnType;

    /// <summary>Closes a construction defined on an open generic accumulator to <paramref name="elementType"/>; a construction on a concrete type is returned unchanged.</summary>
    internal static MethodBase Close(MethodBase construction, Type elementType) {
        var declaring = construction.DeclaringType!;
        if (!declaring.IsGenericTypeDefinition)
            return construction;
        var closed = declaring.MakeGenericType(elementType);
        return MethodBase.GetMethodFromHandle(construction.MethodHandle, closed.TypeHandle)!;
    }

    /// <summary>
    /// The <c>Add(element)</c> that folds one element in, either the registered one closed to the element type or,
    /// when none was given, the accumulator's own found by name. Throws when neither names an <c>Add</c>.
    /// </summary>
    internal static MethodInfo ResolveAdd(Type accumulatorType, Type elementType, MethodBase? add) {
        if (add is not null)
            return (MethodInfo)Close(add, elementType);
        return accumulatorType.GetMethod("Add", [elementType])
            ?? throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType,
                $"the accumulator {accumulatorType} has no Add({elementType}) to fold an element into");
    }

}

/// <summary>
/// Keeps a null element in a collection instead of dropping it, adding it as the type's default (a
/// <see langword="null"/> for a class or nullable, the zero value for a value type). It sets the collection's null
/// rule to the take-default one instead of the collapse rule, so the null no longer flows up out of the fold. It is
/// a convenience over the same <see cref="INullColHandlerMaker"/> seam any rule of your own uses.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class KeepNullElementsAttribute : Attribute, INullColHandlerMaker {
    /// <inheritdoc/>
    public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param) => KeepNullElementsHandle.Instance;
}

/// <summary>
/// The plan node for a folded value, an accumulator that reads one element per row and folds it into a single
/// instance. It is never a <see cref="SimpleDbItemParser"/>, so any plan holding one takes the multi-row road.
/// The accumulator instance is seeded once per group with <see cref="InitialState"/>, each row calls its
/// <c>Add</c>, and <see cref="Construct"/> turns it into the declared result, so a list, a set filled in place, or
/// an aggregate that keeps a running sum all take the same road, differing only in these pieces.
/// </summary>
internal sealed class AccumulatorPlan(DbItemPlan element, Type elementType, Type accumulatorType, MethodInfo add, MethodBase initialState, MethodBase? construct, INullColHandler nullRule) : DbItemPlan {
    /// <summary>The plan that reads one element from a row.</summary>
    internal DbItemPlan Element => element;
    /// <summary>The collection's own null rule, what to do when an element flows up null: skip, keep (add the default), or throw. The element itself is read with a fixed collapse rule and never null-handled.</summary>
    internal INullColHandler NullRule => nullRule;
    /// <summary>The element type each row folds in, what <see cref="AddMethod"/> takes.</summary>
    internal Type ElementType => elementType;
    /// <summary>The accumulator instance type, seeded once per group and folded into each row; this is the buffer.</summary>
    internal Type BufferType => accumulatorType;
    /// <summary>The <c>Add</c> that folds one element into the accumulator; a non-void return is discarded.</summary>
    internal MethodInfo AddMethod => add;
    /// <summary>The construction that seeds the accumulator, a constructor or a static factory the emit calls.</summary>
    internal MethodBase InitialState => initialState;
    /// <summary>The constructor or factory from the accumulator to the result, or <see langword="null"/> when the accumulator already is the result.</summary>
    internal MethodBase? Construct => construct;
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) => false;
    /// <inheritdoc/>
    internal override IEnumerable<DbItemPlan> Children => [element];
}
