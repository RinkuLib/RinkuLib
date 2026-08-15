using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Mapping.Defaults; 
internal class DynaObjectTypeInfo : TypeParsingInfo {
    public static readonly DynaObjectTypeInfo Instance;
    static DynaObjectTypeInfo() {
        Instance = new();
    }
    private DynaObjectTypeInfo() { }
    public override void ValidateCanUseType(Type TargetType) {
        if (TargetType != typeof(DynaObject))
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, $"The type may only be {typeof(DynaObject)}");
    }
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo? paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
        if (!previousUsages.CanContinue(currentClosedType, colUsage.NbUsed, out previousUsages))
            return null;
        paramInfo ??= NullableTransientParamInfo;
        colModifier = colModifier.Add(paramInfo.NameComparer);
        paramInfo.EnterSubtree(ref colModifier, colUsage.NbClaims);

        var ownedOrdinals = GetOwnedOrdinals(columns, colModifier.Comparers, colUsage);
        if (ownedOrdinals.Length == 0)
            return null;
        Mapper mapper = MakeMapper(columns, ownedOrdinals);
        var len = mapper.Count;
        var readers = new DbItemPlan[len];
        var arguments = new Type[len > DynaObjParser.MaxArguments ? DynaObjParser.MaxArguments : len];
        for (int ind = 0; ind < ownedOrdinals.Length; ind++) {
            var col = columns[ownedOrdinals[ind]];
            var type = ind >= DynaObjParser.MaxArguments ? typeof(object) : col.Type;
            if (type.IsValueType && col.IsNullable && Nullable.GetUnderlyingType(type) is null)
                type = typeof(Nullable<>).MakeGenericType(type);
            var r = ForceGet(type).TryGetParser(type, previousUsages, NullableTransientParamInfo, columns, colModifier, ref colUsage);
            if (r is null)
                return null;
            if (ind < DynaObjParser.MaxArguments)
                arguments[ind] = type;
            readers[ind] = r;
        }
        if (readers.Length <= DynaObjParser.MaxArguments)
            return new DynaObjParser(arguments, readers, mapper);
        return new DynaObjParserInfinite(arguments, readers, mapper);
    }
    private static int[] GetOwnedOrdinals(ColumnInfo[] columns, INameComparer[] comparers, ColumnUsage colUsage) {
        List<int> owned = [];
        for (int i = 0; i < columns.Length; i++)
            if (!colUsage.IsUsed(i) && NoNameComparer.Instance.Match(columns[i].Name, comparers))
                owned.Add(i);
        return [.. owned];
    }

    private static Mapper MakeMapper(ColumnInfo[] columns, int[] ownedOrdinals) {
        var count = ownedOrdinals.Length;
        var deduplicatedNames = new string[count];
        for (int i = 0; i < count; i++)
            deduplicatedNames[i] = columns[ownedOrdinals[i]].Name;
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

internal class DynaObjParser(Type[] Arguments, DbItemPlan[] Parameters, Mapper Mapper) : SimpleDbItemParser {
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
    private readonly DbItemPlan[] Parameters = Parameters;
    private readonly Mapper Mapper = Mapper;
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < Parameters.Length; i++)
            if (!Parameters[i].IsSequencial(ref previousIndex))
                return false;
        return true;
    }
    public override IEnumerable<DbItemPlan> Children => Parameters;
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        for (int i = 0; i < Parameters.Length; i++)
            ((ISimpleDbItemPlan)Parameters[i]).Emit(cols, generator, nullSetPoint);
        int argCount = Arguments.Length;
        var ctor = DynaTypes[argCount].MakeGenericType(Arguments).GetConstructor([.. Arguments, typeof(Mapper)])
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"the ctor for {nameof(DynaObject)} with {argCount} arguments cannot be found");

        generator.EmitTarget(Mapper);
        generator.Emit(OpCodes.Newobj, ctor);
    }
}
internal class DynaObjParserInfinite(Type[] Arguments, DbItemPlan[] Parameters, Mapper Mapper) : SimpleDbItemParser {
    internal const int ArgumentCount = 12;
    private readonly Type[] Arguments = Arguments;
    private readonly DbItemPlan[] Parameters = Parameters;
    private readonly Mapper Mapper = Mapper;
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < Parameters.Length; i++)
            if (!Parameters[i].IsSequencial(ref previousIndex))
                return false;
        return true;
    }
    public override IEnumerable<DbItemPlan> Children => Parameters;
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        if (Parameters.Length <= ArgumentCount || Arguments.Length != ArgumentCount)
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"the dyna emitter got {Parameters.Length} parameters and {Arguments.Length} arguments for an arity of {ArgumentCount}");
        var arrLen = Parameters.Length - ArgumentCount;
        for (int i = 0; i < ArgumentCount; i++)
            ((ISimpleDbItemPlan)Parameters[i]).Emit(cols, generator, nullSetPoint);
        generator.Emit(OpCodes.Ldc_I4, arrLen);
        generator.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arrLen; i++) {
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldc_I4, i);
            ((ISimpleDbItemPlan)Parameters[ArgumentCount + i]).Emit(cols, generator, nullSetPoint);
            generator.Emit(OpCodes.Stelem_Ref);
        }
        var ctor = typeof(DynaObjectInfinite<,,,,,,,,,,,>).MakeGenericType(Arguments).GetConstructor([.. Arguments, typeof(object[]), typeof(Mapper)])
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"the ctor for {nameof(DynaObjectInfinite<,,,,,,,,,,,>)} cannot be found");

        generator.EmitTarget(Mapper);
        generator.Emit(OpCodes.Newobj, ctor);
    }
}
