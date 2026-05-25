using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;

namespace RinkuLib.TypeAccessing;
/// <summary>Indicate that the value returned may be null</summary>
public readonly struct MaybeNull<T>([NoName] T? value) where T : class {
    /// <summary>Indicate if the item has a value</summary>
    public bool HasValue => Value is not null;
    /// <summary>The underlying value</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(MaybeNull<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator MaybeNull<T>(T? val) => new(val);
}
/// <summary></summary>
public interface IWrapping<TSelf, T> where TSelf : IWrapping<TSelf, T> {
    /// <summary>Provide a way to wrap the value</summary>
    public abstract static TSelf Make(T val);
}
/// <summary>Indicate that there may not have any value returned</summary>
public readonly struct Optional<T>([NoName] T value) : IWrapping<Optional<T>, T> where T : class {
    /// <summary>Indicate if the item has a value</summary>
    public bool HasValue => Value is not null;
    /// <summary>The underlying value</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(Optional<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator Optional<T>(T val) => new(val);
    /// <inheritdoc/>
    public static Optional<T> Make(T val) => new(val);
}
/// <summary>Indicate that there may not have any value returned</summary>
public readonly struct OptionalStruct<T>([NoName] T value) : IWrapping<OptionalStruct<T>, T> where T : struct {
    /// <summary>Indicate if the item has a value</summary>
    public bool HasValue => Value.HasValue;
    /// <summary>The underlying value</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(OptionalStruct<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator OptionalStruct<T>(T val) => new(val);
    /// <inheritdoc/>
    public static OptionalStruct<T> Make(T val) => new(val);
}
/// <summary>Indicate that there may not have any value returned or that the value returned may be null</summary>
public readonly struct OptionalNullable<T>([MaybeNull][NoName] T value) : IWrapping<OptionalNullable<T>, T> where T : class {
    /// <summary>Indicate if the item has a value</summary>
    public bool HasValue => Value is not null;
    /// <summary>The underlying value</summary>
    [MaybeNull]
    public readonly T? Value = value;
    /// <inheritdoc/>
    [return: MaybeNull]
    public static implicit operator T?(OptionalNullable<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator OptionalNullable<T>(T val) => new(val);
    /// <inheritdoc/>
    public static OptionalNullable<T> Make(T val) => new(val);
}
/// <summary>Used to throw if the parsed value result in a null</summary>
public readonly struct Single<T>([NoName] T value) : IWrapping<Single<T>, T> {
    /// <summary>The underlying value</summary>
    public readonly T Value = value;
    /// <inheritdoc/>
    public static implicit operator T(Single<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator Single<T>(T val) => new(val);
    /// <inheritdoc/>
    public static Single<T> Make(T val) => new(val);
}