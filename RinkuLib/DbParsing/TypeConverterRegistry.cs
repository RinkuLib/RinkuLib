using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.DbParsing;

/// <summary>
/// Optional application-wide converters for database scalar types.
/// </summary>
/// <remarks>
/// The default converter search remains in place. A registered converter is an earlier, explicit choice for
/// one source and target pair. It only supplies the scalar conversion; null handling and the surrounding
/// parsing plan still belong to the normal mapping pipeline.
/// </remarks>
public static class TypeConverterRegistry {
    private static readonly ConcurrentDictionary<(Type Source, Type Target), ITypeConverter> Converters = [];

    /// <summary>
    /// Registers the conversion used when a reader exposes <typeparamref name="TSource"/> and the mapped
    /// member expects <typeparamref name="TTarget"/>.
    /// </summary>
    /// <param name="converter">The conversion to call for a non-null reader value.</param>
    public static void Register<TSource, TTarget>(Func<TSource, TTarget> converter) {
        ArgumentNullException.ThrowIfNull(converter);
        DelegateTypeConverter<TSource, TTarget>.Conversion = converter;
        Converters[(typeof(TSource), typeof(TTarget))] = DelegateTypeConverter<TSource, TTarget>.Instance;
        TypeParsingInfo.GetOrAdd<TTarget>(BaseTypeInfo.Instance);
        TypeParsingInfo.TouchConfiguration();
    }

    internal static bool TryGet(Type source, Type target, [MaybeNullWhen(false)] out ITypeConverter converter)
        => Converters.TryGetValue((source, target), out converter);

    internal static bool HasTarget(Type target)
        => Converters.Keys.Any(pair => pair.Target == target);
}

internal sealed class DelegateTypeConverter<TSource, TTarget> : ITypeConverter {
    internal static readonly DelegateTypeConverter<TSource, TTarget> Instance = new();
    internal static Func<TSource, TTarget> Conversion = null!;
    private static readonly MethodInfo InvokeMethod = typeof(DelegateTypeConverter<TSource, TTarget>)
        .GetMethod(nameof(Invoke), BindingFlags.Static | BindingFlags.NonPublic)!;

    public Type OutputType => typeof(TTarget);

    private static TTarget Invoke(TSource value) => Conversion(value);

    public void EmitConversion(Generator generator, Type sourceType)
        => generator.Emit(OpCodes.Call, InvokeMethod);
}
