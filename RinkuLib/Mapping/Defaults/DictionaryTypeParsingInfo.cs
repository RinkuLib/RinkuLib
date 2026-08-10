using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;
using Rinku.Mapping.Emission;

namespace Rinku.Mapping.Defaults;

/// <summary>
/// Maps <see cref="Dictionary{TKey,TValue}"/> with string keys and object values by reading the current
/// reader's names and values at row time. Unlike a generated object or <see cref="DynaObject"/>, the
/// dictionary does not bake the columns it owns into its parser.
/// </summary>
internal sealed class DictionaryTypeParsingInfo : TypeParsingInfo {
    internal static readonly DictionaryTypeParsingInfo Instance = new();
    private static readonly Type DictionaryType = typeof(Dictionary<string, object>);

    private DictionaryTypeParsingInfo() { }

    /// <inheritdoc/>
    public override void ValidateCanUseType(Type targetType) {
        if (targetType != DictionaryType)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
                $"The type may only be {DictionaryType}");
    }

    /// <inheritdoc/>
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages,
        ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage,
        MethodCtorInfo.AdditionalFlags callerFlags = default) {
        if (!previousUsages.CanContinue(currentClosedType, colUsage.NbUsed, out _))
            return null;

        int excludedCount = colUsage.NbUsed;
        int[] excluded = excludedCount == 0 ? [] : new int[excludedCount];
        int[] owned = new int[columns.Length - excludedCount];
        int excludedIndex = 0;
        int ownedIndex = 0;
        for (int i = 0; i < columns.Length; i++) {
            if (colUsage.IsUsed(i))
                excluded[excludedIndex++] = i;
            else {
                owned[ownedIndex++] = i;
                colUsage.Use(i);
            }
        }
        return new DictionaryRowPlan(owned, DictionaryRowReader.ForExcluded(excluded));
    }
}

/// <summary>A negotiated leaf that reserves every unclaimed column and emits one runtime dictionary read.</summary>
internal sealed class DictionaryRowPlan(int[] ownedOrdinals, DictionaryRowReader reader) : SimpleDbItemParser {
    private static readonly MethodInfo ReadMethod = typeof(DictionaryRowReader).GetMethod(nameof(DictionaryRowReader.Read))!;
    private readonly int[] OwnedOrdinals = ownedOrdinals;
    private readonly DictionaryRowReader Reader = reader;

    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;

    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < OwnedOrdinals.Length; i++) {
            int ordinal = OwnedOrdinals[i];
            if (previousIndex >= ordinal)
                return false;
            previousIndex = ordinal;
        }
        return true;
    }

    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint,
        out object? targetObject) {
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Call, ReadMethod);
        targetObject = Reader;
    }
}

/// <summary>
/// The bound runtime portion of dictionary mapping. It asks the reader for the schema on every row and skips
/// only ordinals owned by the surrounding typed plan, allowing all other columns to change between calls.
/// </summary>
internal sealed class DictionaryRowReader : IGeneratedParserTarget {
    private static readonly DictionaryRowReader AllColumns = new([]);
    private readonly int[] ExcludedOrdinals;

    private DictionaryRowReader(int[] excludedOrdinals) => ExcludedOrdinals = excludedOrdinals;

    internal static DictionaryRowReader ForExcluded(int[] excludedOrdinals)
        => excludedOrdinals.Length == 0 ? AllColumns : new(excludedOrdinals);

    public Dictionary<string, object> Read(DbDataReader reader) {
        int fieldCount = reader.FieldCount;
        var result = new Dictionary<string, object>(fieldCount - CountExcludedBefore(fieldCount),
            StringComparer.OrdinalIgnoreCase);
        int excludedIndex = 0;
        for (int ordinal = 0; ordinal < fieldCount; ordinal++) {
            if (excludedIndex < ExcludedOrdinals.Length && ExcludedOrdinals[excludedIndex] == ordinal) {
                excludedIndex++;
                continue;
            }
            object value = reader.GetValue(ordinal);
            Add(result, reader.GetName(ordinal), value is DBNull ? null! : value);
        }
        return result;
    }

    private int CountExcludedBefore(int fieldCount) {
        int count = 0;
        while (count < ExcludedOrdinals.Length && ExcludedOrdinals[count] < fieldCount)
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
        => other is DictionaryRowReader reader && ExcludedOrdinals.AsSpan().SequenceEqual(reader.ExcludedOrdinals);

    void IDisposable.Dispose() { }
}
