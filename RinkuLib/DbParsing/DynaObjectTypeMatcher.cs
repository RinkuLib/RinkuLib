using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing; 
internal class DynaObjectTypeMatcher : IDbTypeParserInfoMatcher {
    public static readonly DynaObjectTypeMatcher Instance = new();
    private DynaObjectTypeMatcher() { }
    /// <inheritdoc/>
    public bool CanUseType(Type TargetType) => TargetType == typeof(DynaObject);
    /// <inheritdoc/>
    public DbItemParser? TryGetParser(Type parentType, Type[] declaringTypeArguments, string paramName, INullColHandler nullColHandler, ColumnInfo[] columns, ColModifier colModifier, bool isNullable, ref ColumnUsage colUsage, Type closedTargetType) {
        var readers = new DbItemParser[columns.Length];
        var arguments = new Type[columns.Length];
        for (int i = 0; i < readers.Length; i++) {
            var col = columns[i];
            var type = col.Type;
            var maybeNull = col.IsNullable;
            Type[] args = [];
            if (type.IsGenericType) {
                if (type.IsValueType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                    maybeNull = true;
                args = type.GetGenericArguments();
            }
            var typeInfo = TypeParsingInfo.ForceGet(type);
            var r = typeInfo.TryGetParser(parentType, args, $"val{i}", NullableTypeHandle.Instance, columns, new(), maybeNull, ref colUsage);
            if (r is null)
                return null;
            arguments[i] = type.IsValueType && maybeNull ? typeof(Nullable<>).MakeGenericType(type) : type;
            readers[i] = r;
        }
        return new DynaObjParser(arguments, readers);
    }
}

/// <summary>
/// A parser that handle a dynaobject generation
/// </summary>
public class DynaObjParser(Type[] Arguments, DbItemParser[] Parameters) : DbItemParser {
    private readonly static Type[] DynaTypes = [
        typeof(DynaObject),
        typeof(DynaObject<>),
        typeof(DynaObject<,>),
        typeof(DynaObject<,,>),
        typeof(DynaObject<,,,>),
        typeof(DynaObject<,,,,>),
        typeof(DynaObject<,,,,,>),
        typeof(DynaObject<,,,,,,>),
        typeof(DynaObject<,,,,,,,>),
        typeof(DynaObject<,,,,,,,,>),
        typeof(DynaObject<,,,,,,,,,>),
        typeof(DynaObject<,,,,,,,,,,>),
        typeof(DynaObject<,,,,,,,,,,,>),
    ];
    private readonly Type[] Arguments = Arguments;
    private readonly DbItemParser[] Parameters = Parameters;
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < Parameters.Length; i++)
            if (!Parameters[i].IsSequencial(ref previousIndex))
                return false;
        return true;
    }
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject) {
        for (int i = 0; i < Parameters.Length; i++)
            Parameters[i].Emit(cols, generator, nullSetPoint, out _);
        int argCount = Arguments.Length;
        if (argCount >= DynaTypes.Length)
            throw new NotSupportedException($"DynaObject supports up to {DynaTypes.Length - 1} arguments.");

        Type concreteType = DynaTypes[argCount].MakeGenericType(Arguments);

        var ctor = concreteType.GetConstructor([.. Arguments, typeof(Mapper)])
            ?? throw new Exception($"the ctor for {nameof(DynaObject)} with {argCount} arguments cannot be found");

        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Newobj, ctor);
        targetObject = cols.MakeMapper();
    }
}