using System.Diagnostics.CodeAnalysis;

namespace Rinku;

public delegate bool TryGet<in TSource, TValue>(TSource source, [MaybeNullWhen(false)] out TValue value);

public delegate bool TryGet<in TSource, in TArgument, TValue>(TSource source, TArgument argument, [MaybeNullWhen(false)] out TValue value);
