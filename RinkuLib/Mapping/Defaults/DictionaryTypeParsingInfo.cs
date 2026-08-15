using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;
using Rinku.Mapping.Emission;

namespace Rinku.Mapping.Defaults;

internal sealed class DictionaryTypeParsingInfo : TypeParsingInfo {
    internal static readonly DictionaryTypeParsingInfo Instance = new();
    private static readonly Type DictionaryType = typeof(Dictionary<string, object>);

    private DictionaryTypeParsingInfo() { }

    public override void ValidateCanUseType(Type targetType) {
        if (targetType != DictionaryType)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
                $"The type may only be {DictionaryType}");
    }

    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages,
        ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage,
        MethodCtorInfo.AdditionalFlags callerFlags = default) {
        if (!previousUsages.CanContinue(currentClosedType, colUsage.NbUsed, out _))
            return null;

        colModifier = colModifier.Add(paramInfo.NameComparer);
        paramInfo.EnterSubtree(ref colModifier, colUsage.NbClaims);
        bool restrictByName = colModifier.Comparers.Length != 0;

        int excludedCount = colUsage.NbUsed;
        int[] excluded = excludedCount == 0 ? [] : new int[excludedCount];
        List<int> owned = [];
        int excludedIndex = 0;
        for (int i = 0; i < columns.Length; i++) {
            if (colUsage.IsUsed(i))
                excluded[excludedIndex++] = i;
            else if (NoNameComparer.Instance.Match(columns[i].Name, colModifier.Comparers)) {
                owned.Add(i);
                colUsage.Use(i);
            }
        }
        if (owned.Count == 0)
            return null;
        return new DictionaryRowPlan([.. owned], restrictByName
            ? DictionaryRowReader.ForOwned([.. owned])
            : DictionaryRowReader.ForExcluded(excluded));
    }
}

internal sealed class DictionaryRowPlan(int[] ownedOrdinals, DictionaryRowReader reader) : SimpleDbItemParser {
    private static readonly MethodInfo ReadMethod = typeof(DictionaryRowReader).GetMethod(nameof(DictionaryRowReader.Read))!;
    private readonly int[] OwnedOrdinals = ownedOrdinals;
    private readonly DictionaryRowReader Reader = reader;

    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;

    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < OwnedOrdinals.Length; i++) {
            int ordinal = OwnedOrdinals[i];
            if (previousIndex >= ordinal)
                return false;
            previousIndex = ordinal;
        }
        return true;
    }

    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        generator.EmitTarget(Reader);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Call, ReadMethod);
    }
}

internal sealed class DictionaryRowReader : IGeneratedParserTarget {
    private static readonly DictionaryRowReader AllColumns = new(false, []);
    private readonly bool ReadOwned;
    private readonly int[] Ordinals;

    private DictionaryRowReader(bool readOwned, int[] ordinals) {
        ReadOwned = readOwned;
        Ordinals = ordinals;
    }

    internal static DictionaryRowReader ForExcluded(int[] excludedOrdinals)
        => excludedOrdinals.Length == 0 ? AllColumns : new(false, excludedOrdinals);

    internal static DictionaryRowReader ForOwned(int[] ownedOrdinals) => new(true, ownedOrdinals);

    public Dictionary<string, object> Read(DbDataReader reader) {
        if (ReadOwned)
            return ReadOwnedColumns(reader);

        int fieldCount = reader.FieldCount;
        var result = new Dictionary<string, object>(fieldCount - CountExcludedBefore(fieldCount),
            StringComparer.OrdinalIgnoreCase);
        int excludedIndex = 0;
        for (int ordinal = 0; ordinal < fieldCount; ordinal++) {
            if (excludedIndex < Ordinals.Length && Ordinals[excludedIndex] == ordinal) {
                excludedIndex++;
                continue;
            }
            object value = reader.GetValue(ordinal);
            Add(result, reader.GetName(ordinal), value is DBNull ? null! : value);
        }
        return result;
    }

    private Dictionary<string, object> ReadOwnedColumns(DbDataReader reader) {
        var result = new Dictionary<string, object>(Ordinals.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Ordinals.Length; i++) {
            int ordinal = Ordinals[i];
            object value = reader.GetValue(ordinal);
            Add(result, reader.GetName(ordinal), value is DBNull ? null! : value);
        }
        return result;
    }

    private int CountExcludedBefore(int fieldCount) {
        int count = 0;
        while (count < Ordinals.Length && Ordinals[count] < fieldCount)
            count++;
        return count;
    }

    private static void Add(Dictionary<string, object> values, string name, object value) {
        if (values.TryAdd(name, value))
            return;
        int suffix = 2;
        string candidate;
        do candidate = $"{name}#{suffix++}";
        while (values.ContainsKey(candidate));
        values.Add(candidate, value);
    }

    bool IGeneratedParserTarget.Matches(object? other)
        => other is DictionaryRowReader reader && ReadOwned == reader.ReadOwned
            && Ordinals.AsSpan().SequenceEqual(reader.Ordinals);

    void IDisposable.Dispose() { }
}
