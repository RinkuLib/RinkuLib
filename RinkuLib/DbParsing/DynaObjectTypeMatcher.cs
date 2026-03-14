using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing; 
internal class DynaObjectTypeInfo : TypeParsingInfo {
    public static readonly DynaObjectTypeInfo Instance = new();
    private DynaObjectTypeInfo() { }
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (TargetType != typeof(DynaObject))
            throw new ArgumentException($"The type may only be {typeof(DynaObject)}");
    }
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo? paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        Mapper mapper = MakeMapper(columns, colUsage);
        var len = mapper.Count;
        var readers = new DbItemParser[len];
        var arguments = new Type[len > DynaObjParser.MaxArguments ? DynaObjParser.MaxArguments : len];
        var ind = 0;
        for (int i = 0; i < colUsage.Length; i++) {
            if (colUsage.IsUsed(i))
                continue;
            var col = columns[i];
            var type = i >= DynaObjParser.MaxArguments ? typeof(object) : col.Type;
            if (type.IsValueType && col.IsNullable && Nullable.GetUnderlyingType(type) is null)
                type = typeof(Nullable<>).MakeGenericType(type);
            var r = ForceGet(type).TryGetParser(currentClosedType, type, NullableTransientParamInfo, columns, colModifier, ref colUsage);
            if (r is null)
                return null;
            if (i < DynaObjParser.MaxArguments)
                arguments[ind] = type;
            readers[ind] = r;
            ind++;
        }
        if (readers.Length <= DynaObjParser.MaxArguments)
            return new DynaObjParser(arguments, readers, mapper);
        return new DynaObjParserInfinite(arguments, readers, mapper);
    }
    private static Mapper MakeMapper(ColumnInfo[] columns, ColumnUsage colUsage) {
        var count = columns.Length;
        for (int i = 0; i < colUsage.Length; i++)
            if (colUsage.IsUsed(i))
                count--;
        var deduplicatedNames = new string[count];
        int ind = 0;
        for (int i = 0; i < columns.Length; i++) {
            if (colUsage.IsUsed(i))
                continue;
            deduplicatedNames[ind++] = columns[i].Name;
        }
        var mapper = Mapper.GetMapper(deduplicatedNames);
        if (mapper.Count == count)
            return mapper;
        var seen = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++) {
            string originalName = deduplicatedNames[i];
            if (seen.TryGetValue(originalName, out int suffix)) {
                string newName;
                do {
                    newName = $"{originalName}#{suffix++}";
                } while (seen.ContainsKey(newName));

                deduplicatedNames[i] = newName;
                seen[originalName] = suffix;
                seen[newName] = 2;
            }
            else {
                deduplicatedNames[i] = originalName;
                seen[originalName] = 2;
            }
        }
        return Mapper.GetMapper(deduplicatedNames);
    }
}

/// <summary>
/// A parser that handle a dynaobject generation
/// </summary>
public class DynaObjParser(Type[] Arguments, DbItemParser[] Parameters, Mapper Mapper) : DbItemParser {
    /// <summary></summary>
    public static int MaxArguments => DynaTypes.Length - 1;
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
    private readonly Mapper Mapper = Mapper;
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
        var ctor = DynaTypes[argCount].MakeGenericType(Arguments).GetConstructor([.. Arguments, typeof(Mapper)])
            ?? throw new Exception($"the ctor for {nameof(DynaObject)} with {argCount} arguments cannot be found");

        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Newobj, ctor);
        targetObject = Mapper;
    }
}
/// <summary>
/// A parser that handle a dynaobject generation
/// </summary>
public class DynaObjParserInfinite(Type[] Arguments, DbItemParser[] Parameters, Mapper Mapper) : DbItemParser {
    internal const int ArgumentCount = 12;
    private readonly Type[] Arguments = Arguments;
    private readonly DbItemParser[] Parameters = Parameters;
    private readonly Mapper Mapper = Mapper;
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
        if (Parameters.Length <= ArgumentCount || Arguments.Length != ArgumentCount)
            throw new Exception();
        var arrLen = Parameters.Length - ArgumentCount;
        for (int i = 0; i < ArgumentCount; i++)
            Parameters[i].Emit(cols, generator, nullSetPoint, out _);
        generator.Emit(OpCodes.Ldc_I4, arrLen);
        generator.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arrLen; i++) {
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldc_I4, i);
            Parameters[ArgumentCount + i].Emit(cols, generator, nullSetPoint, out _);
            generator.Emit(OpCodes.Stelem_Ref);
        }
        var ctor = typeof(DynaObjectInfinite<,,,,,,,,,,,>).MakeGenericType(Arguments).GetConstructor([.. Arguments, typeof(object[]), typeof(Mapper)])
            ?? throw new Exception($"the ctor for {nameof(DynaObjectInfinite<,,,,,,,,,,,>)} cannot be found");

        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Newobj, ctor);
        targetObject = Mapper;
    }
}