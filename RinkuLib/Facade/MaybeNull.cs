using System.Diagnostics.CodeAnalysis;
using Rinku.Mapping;

namespace Rinku;
/// <summary>
/// Reads one row whose value may be <c>NULL</c>.
/// A missing row still throws. Use <see cref="OptionalNullable{T}"/> when the row may also be missing.
/// </summary>
public readonly struct MaybeNull<T>([MaybeNull][NoName] T value) where T : class {
    /// <summary>Whether a non-null value was read.</summary>
    public bool HasValue => Value is not null;
    /// <summary>The value, or <see langword="null"/>.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(MaybeNull<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator MaybeNull<T>(T? val) => val is null ? new() : new(val);
}
/// <summary>
/// Lets a wrapper be used as a query result type.
/// Implement this interface when a custom wrapper can be created from one value.
/// </summary>
public interface IWrapping<TSelf, T> where TSelf : IWrapping<TSelf, T> {
    /// <summary>Creates the wrapper from a value returned by a query.</summary>
    public abstract static TSelf Make(T val);
}
/// <summary>
/// Reads one reference value or reports that no row was returned.
/// A present <c>NULL</c> still throws. Use <see cref="OptionalNullable{T}"/> to accept it.
/// </summary>
public readonly struct Optional<T>([NoName] T value) : IWrapping<Optional<T>, T> where T : class {
    /// <summary>Whether a row was read.</summary>
    public bool HasValue => Value is not null;
    /// <summary>The value, or <see langword="null"/> when there was no row.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(Optional<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator Optional<T>(T? val) => val is null ? new() : new(val);
    /// <inheritdoc/>
    public static Optional<T> Make(T val) => new(val);
}
/// <summary>
/// Reads one value type or reports that no row was returned.
/// </summary>
public readonly struct OptionalStruct<T>([NoName] T value) : IWrapping<OptionalStruct<T>, T> where T : struct {
    /// <summary>Whether a row was read.</summary>
    public bool HasValue => Value.HasValue;
    /// <summary>The value, or empty when there was no row.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(OptionalStruct<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator OptionalStruct<T>(T? val) => !val.HasValue ? new() : new(val.Value);
    /// <inheritdoc/>
    public static OptionalStruct<T> Make(T val) => new(val);
}
/// <summary>
/// Reads one value type while accepting a missing row or a <c>NULL</c> value.
/// Both cases set <see cref="HasValue"/> to <see langword="false"/>.
/// </summary>
public readonly struct OptionalNullableStruct<T>([MaybeNull][NoName] T? value) : IWrapping<OptionalNullableStruct<T>, T?> where T : struct {
    /// <summary>Whether a non-null value was read.</summary>
    public bool HasValue => Value.HasValue;
    /// <summary>The value, or empty when the row was missing or its value was null.</summary>
    public readonly T? Value = value;
    /// <inheritdoc/>
    public static implicit operator T?(OptionalNullableStruct<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator OptionalNullableStruct<T>(T? val) => !val.HasValue ? new() : new(val.Value);
    /// <inheritdoc/>
    public static OptionalNullableStruct<T> Make(T? val) => new(val);
}
/// <summary>
/// Reads one reference value while accepting a missing row or a <c>NULL</c> value.
/// Both cases set <see cref="HasValue"/> to <see langword="false"/>.
/// </summary>
public readonly struct OptionalNullable<T>([MaybeNull][NoName] T value) : IWrapping<OptionalNullable<T>, T> where T : class {
    /// <summary>Whether a non-null value was read.</summary>
    public bool HasValue => Value is not null;
    /// <summary>The value, or <see langword="null"/> when the row was missing or its value was null.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(OptionalNullable<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator OptionalNullable<T>(T? val) => val is null ? new() : new(val);
    /// <inheritdoc/>
    public static OptionalNullable<T> Make(T val) => new(val);
}
/// <summary>
/// Requires exactly one result.
/// No result and a second result both throw.
/// </summary>
public readonly struct Single<T>([NoName] T value) : IWrapping<Single<T>, T> {
    /// <summary>The single value.</summary>
    public readonly T Value = value;
    /// <inheritdoc/>
    public static implicit operator T(Single<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator Single<T>(T val) => new(val);
    /// <inheritdoc/>
    public static Single<T> Make(T val) => new(val);
}

/// <summary>
/// Reads zero or one reference result.
/// No result gives an empty value and a second result throws. A present <c>NULL</c> still throws.
/// </summary>
public readonly struct SingleOrDefault<T>([NoName] T value) : IWrapping<SingleOrDefault<T>, T> where T : class {
    /// <summary>Whether a row was read.</summary>
    public bool HasValue => Value is not null;
    /// <summary>The value, or <see langword="null"/> when there was no row.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(SingleOrDefault<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator SingleOrDefault<T>(T? val) => val is null ? new() : new(val);
    /// <inheritdoc/>
    public static SingleOrDefault<T> Make(T val) => new(val);
}
/// <summary>
/// Reads zero or one value type result.
/// No result gives an empty value and a second result throws. A present <c>NULL</c> still throws.
/// </summary>
public readonly struct SingleOrDefaultStruct<T>([NoName] T value) : IWrapping<SingleOrDefaultStruct<T>, T> where T : struct {
    /// <summary>Whether a row was read.</summary>
    public bool HasValue => Value.HasValue;
    /// <summary>The value, or empty when there was no row.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(SingleOrDefaultStruct<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator SingleOrDefaultStruct<T>(T? val) => !val.HasValue ? new() : new(val.Value);
    /// <inheritdoc/>
    public static SingleOrDefaultStruct<T> Make(T val) => new(val);
}
/// <summary>
/// Reads zero or one reference result while accepting a <c>NULL</c> value.
/// A missing row and a <c>NULL</c> value both give an empty value. A second result throws.
/// </summary>
public readonly struct SingleOrDefaultNullable<T>([MaybeNull][NoName] T value) : IWrapping<SingleOrDefaultNullable<T>, T> where T : class {
    /// <summary>Whether a non-null value was read.</summary>
    public bool HasValue => Value is not null;
    /// <summary>The value, or <see langword="null"/> when there was no row or its value was null.</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(SingleOrDefaultNullable<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator SingleOrDefaultNullable<T>(T? val) => val is null ? new() : new(val);
    /// <inheritdoc/>
    public static SingleOrDefaultNullable<T> Make(T val) => new(val);
}
/// <summary>
/// Reads zero or one value type result while accepting a <c>NULL</c> value.
/// A missing row and a <c>NULL</c> value both give an empty value. A second result throws.
/// </summary>
public readonly struct SingleOrDefaultNullableStruct<T>([MaybeNull][NoName] T? value) : IWrapping<SingleOrDefaultNullableStruct<T>, T?> where T : struct {
    /// <summary>Whether a non-null value was read.</summary>
    public bool HasValue => Value.HasValue;
    /// <summary>The value, or empty when there was no row or its value was null.</summary>
    public readonly T? Value = value;
    /// <inheritdoc/>
    public static implicit operator T?(SingleOrDefaultNullableStruct<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator SingleOrDefaultNullableStruct<T>(T? val) => !val.HasValue ? new() : new(val.Value);
    /// <inheritdoc/>
    public static SingleOrDefaultNullableStruct<T> Make(T? val) => new(val);
}
