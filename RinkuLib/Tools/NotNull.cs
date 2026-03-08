namespace RinkuLib.Tools;
/// <summary>Used to throw if the parsed value result in a null</summary>
public readonly struct NotNull<T>(T value) where T : notnull {
    /// <summary>The underlying value</summary>
    public readonly T Value = value;
    /// <inheritdoc/>
    public static implicit operator T(NotNull<T> val) => val.Value;
    /// <inheritdoc/>
    public static implicit operator NotNull<T>(T val) => new(val);
}