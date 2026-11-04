using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Rinku.Mapping;
using Rinku.Mapping.Emission;

namespace Rinku.More.Globalization;

/// <summary>
/// Rinku mapping for <see cref="Word"/>. A member named <c>Name</c> consumes the
/// neutral <c>Name</c> column and any culture-suffixed siblings such as
/// <c>NameFr</c>, <c>NameEn</c>, or <c>Namefr-CA</c>.
/// </summary>
public sealed class WordRinkuTypeParsingInfo : TypeParsingInfo
{
    /// <summary>The reusable mapping instance registered for <see cref="Word"/>.</summary>
    public static WordRinkuTypeParsingInfo Instance { get; } = new();

    private WordRinkuTypeParsingInfo() { }

    /// <summary>Recognizes a neutral or culture-suffixed column using the same rules as Word mapping.</summary>
    public static bool TryGetColumnCulture(string columnName, string memberName, [NotNullWhen(true)] out string? culture) {
        INameComparer[] comparers = [new NameComparer(memberName)];
        return CultureSuffixNameComparer.TryMatch(columnName, comparers, out culture);
    }

    /// <inheritdoc />
    public override void ValidateCanUseType(Type targetType)
    {
        if (targetType != typeof(Word))
            throw new ArgumentException($"{nameof(WordRinkuTypeParsingInfo)} can only map {typeof(Word)}.", nameof(targetType));
    }

    /// <inheritdoc />
    public override DbItemPlan? TryGetParser(
        Type currentClosedType,
        RecursiveInfo previousUsages,
        ParamInfo paramInfo,
        ColumnInfo[] columns,
        ColModifier colModifier,
        ref ColumnUsage colUsage,
        MethodCtorInfo.AdditionalFlags callerFlags = default)
    {
        if (!previousUsages.CanContinue(currentClosedType, colUsage.NbUsed, out _))
            return null;

        // Use Rinku's complete name-comparer chain, not a hand-rolled lookup of
        // the property name. This keeps [Alt], naming rules, and parent paths
        // working exactly as they do for ordinary Rinku members.
        paramInfo.UpdateColModifier(ref colModifier);
        colModifier = colModifier.Add(paramInfo.NameComparer);
        paramInfo.EnterSubtree(ref colModifier, colUsage.NbClaims);
        if (colModifier.Comparers.Length == 0)
        {
            int ordinal = colModifier.Flags.HasFlag(UsageFlags.SequentialRead) ? colUsage.LastIndexUsed + 1 : 0;
            while (ordinal < columns.Length && colUsage.IsUsed(ordinal))
                ordinal++;
            if (ordinal >= columns.Length || columns[ordinal].Type != typeof(string))
                return paramInfo.FallbackTryGetParser(currentClosedType);
            colModifier = colModifier.Add(new NameComparer(CultureSuffixNameComparer.GetBaseName(columns[ordinal].Name)));
        }

        List<Column> matches = [];
        for (var ordinal = 0; ordinal < columns.Length; ordinal++)
        {
            if (colUsage.IsUsed(ordinal) || columns[ordinal].Type != typeof(string))
                continue;

            if (CultureSuffixNameComparer.TryMatch(columns[ordinal].Name, colModifier.Comparers, out var culture))
                matches.Add(new Column(ordinal, culture));
        }

        if (matches.Count == 0)
            return paramInfo.FallbackTryGetParser(currentClosedType);

        for (var i = 0; i < matches.Count; i++)
            colUsage.Use(matches[i].Ordinal);

        return new WordPlan([.. matches]);
    }

    /// <summary>
    /// Matches either a neutral member column or a member column followed by a
    /// culture name. The prefix is delegated to Rinku's ordinary comparer chain,
    /// which keeps <see cref="AltAttribute"/> and custom naming rules intact.
    /// </summary>
    private sealed class CultureSuffixNameComparer : INameComparer
    {
        public static readonly CultureSuffixNameComparer Instance = new();

        public string GetDefaultName() => string.Empty;

        public bool Contains(string name) => false;

        public bool Match(ReadOnlySpan<char> columnName, Span<INameComparer> nameComparers)
            => TryMatch(columnName, nameComparers, out _);

        public static string GetBaseName(string columnName)
        {
            for (int suffixStart = 1; suffixStart < columnName.Length; suffixStart++)
            {
                ReadOnlySpan<char> suffix = columnName.AsSpan(suffixStart);
                bool separator = suffix[0] is '_' or '-';
                if (separator)
                    suffix = suffix[1..];
                if (suffix.Length > 0 && TryGetCultureName(suffix, out _))
                    return columnName[..suffixStart];
            }
            return columnName;
        }

        public static bool TryMatch(ReadOnlySpan<char> columnName, Span<INameComparer> nameComparers, [NotNullWhen(true)] out string? culture)
        {
            if (nameComparers.MatchNext(columnName))
            {
                culture = string.Empty;
                return true;
            }

            // Find a valid culture suffix and let the existing comparer chain
            // verify the remaining prefix. [Alt("Title")] therefore maps both
            // Title and TitleFr for a member called Description.
            for (var suffixStart = 1; suffixStart < columnName.Length; suffixStart++)
            {
                var suffix = columnName[suffixStart..];
                if (suffix[0] is '_' or '-')
                    suffix = suffix[1..];
                if (suffix.Length == 0 || !TryGetCultureName(suffix, out culture))
                    continue;

                if (nameComparers.MatchNext(columnName[..suffixStart]))
                    return true;
            }

            culture = null;
            return false;
        }

        private static bool TryGetCultureName(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? culture)
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(name.ToString(), predefinedOnly: true).Name;
                return true;
            }
            catch (CultureNotFoundException)
            {
                culture = null;
                return false;
            }
        }
    }

    private readonly record struct Column(int Ordinal, string Culture);

    private sealed class WordPlan(Column[] columns) : SimpleDbItemParser
    {
        private static readonly ConstructorInfo WordConstructor = typeof(Word).GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException(typeof(Word).FullName, ".ctor");
        private static readonly MethodInfo AddTranslation = typeof(Word).GetMethod(nameof(Word.Add), [typeof(string), typeof(string)]) ?? throw new MissingMethodException(typeof(Word).FullName, nameof(Word.Add));
        private static readonly MethodInfo IsDbNull = typeof(DbDataReader).GetMethod(nameof(DbDataReader.IsDBNull), [typeof(int)]) ?? throw new MissingMethodException(typeof(DbDataReader).FullName, nameof(DbDataReader.IsDBNull));
        private static readonly MethodInfo GetString = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetString), [typeof(int)]) ?? throw new MissingMethodException(typeof(DbDataReader).FullName, nameof(DbDataReader.GetString));

        private readonly Column[] _columns = columns;

        public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;

        public override bool IsSequencial(ref int previousIndex)
        {
            for (var i = 0; i < _columns.Length; i++)
            {
                if (previousIndex >= _columns[i].Ordinal)
                    return false;
                previousIndex = _columns[i].Ordinal;
            }

            return true;
        }

        public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint)
        {
            var word = generator.GetLocal(typeof(Word));
            generator.Emit(OpCodes.Newobj, WordConstructor);
            generator.Emit(OpCodes.Stloc, word);

            for (var i = 0; i < _columns.Length; i++)
            {
                var column = _columns[i];
                var skip = generator.DefineLabel();

                generator.Emit(OpCodes.Ldarg_1);
                generator.Emit(OpCodes.Ldc_I4, column.Ordinal);
                generator.Emit(OpCodes.Callvirt, IsDbNull);
                generator.Emit(OpCodes.Brtrue, skip);

                generator.Emit(OpCodes.Ldloc, word);
                generator.Emit(OpCodes.Ldstr, column.Culture);
                generator.Emit(OpCodes.Ldarg_1);
                generator.Emit(OpCodes.Ldc_I4, column.Ordinal);
                generator.Emit(OpCodes.Callvirt, GetString);
                generator.Emit(OpCodes.Callvirt, AddTranslation);

                generator.MarkLabel(skip);
            }

            generator.Emit(OpCodes.Ldloc, word);
        }
    }
}

/// <summary>Registers the <see cref="Word"/> parser as soon as this assembly is loaded.</summary>
#pragma warning disable CA2255 // This library deliberately registers its Rinku type mapping on load.
public static class WordRinkuMapping
{
    /// <summary>Registers or restores Rinku's mapping for <see cref="Word"/>.</summary>
    public static void Register() => TypeParsingInfo.AddOrSet<Word>(WordRinkuTypeParsingInfo.Instance);

    [ModuleInitializer]
    internal static void RegisterOnAssemblyLoad() => Register();
}
#pragma warning restore CA2255

