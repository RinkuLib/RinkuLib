using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// A result shape that accepts a <c>NULL</c> value where a plain <typeparamref name="T"/> would throw. Ask
/// for <c>MaybeNull&lt;T&gt;</c> when the row is there but its value may be null. A missing row still throws,
/// for that use <see cref="Optional{T}"/>.
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
/// The seam that lets a wrapper be a result shape, it says how to build one from a parsed value. A shape of
/// your own implements this and the engine can then produce it the way it produces the built-in ones.
/// </summary>
public interface IWrapping<TSelf, T> where TSelf : IWrapping<TSelf, T> {
    /// <summary>Wraps a parsed value into this shape.</summary>
    public abstract static TSelf Make(T val);
}
/// <summary>
/// A result shape for "one row or none", for reference types. A missing row gives an empty value instead of
/// throwing. A present-but-<c>NULL</c> value still throws, for that use <see cref="MaybeNull{T}"/>, or stack
/// both with <see cref="OptionalNullable{T}"/>.
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
/// A result shape for "one row or none", for value types, the counterpart to <see cref="Optional{T}"/>. A
/// missing row gives an empty value instead of throwing.
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
/// A result shape that accepts both a missing row and a <c>NULL</c> value, <see cref="Optional{T}"/> and
/// <see cref="MaybeNull{T}"/> stacked. Either case flattens to a single <see cref="HasValue"/> of
/// <see langword="false"/>.
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
/// A result shape for "exactly one row". No row gives a default <c>Single&lt;T&gt;</c>, and a second row
/// throws, so it enforces that the query returned a single result.
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