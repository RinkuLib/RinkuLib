using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Escapes and injects a string literal directly into the SQL text.
/// </summary>
/// <remarks>
/// Wraps the provided value in single quotes. If the value is not a string, 
/// it performs a <c>ToString()</c> conversion. 
/// Use this for values that should be treated as SQL string literals.
/// </remarks>
public class StringVariableHandler() : IQuerySegmentHandler {
    /// <summary>Singleton for <see cref="StringVariableHandler"/></summary>
    public static readonly StringVariableHandler Instance = new();
    /// <summary>
    /// Used to create a <see cref="HandlerGetter{IQuerySegmentHandler}"/> delegate.
    /// </summary>
    public static StringVariableHandler Build(string _) => Instance;
    /// <inheritdoc/>
    public void Handle(ref ValueStringBuilder sb, object value) {
        if (value is not string str)
            str = value.ToString() ?? "";
        sb.Append('\'');
        sb.Append(str);
        sb.Append('\'');
    }
}
/// <summary>
/// Injects a raw value directly into the SQL text without any escaping or modification.
/// </summary>
/// <remarks>
/// <b>Caution:</b> Use this only for trusted values, such as dynamically generated 
/// table names or identifiers that cannot be parameterized.
/// </remarks>
public class RawVariableHandler() : IQuerySegmentHandler {
    /// <summary>Singleton for <see cref="RawVariableHandler"/></summary>
    public static readonly RawVariableHandler Instance = new();
    /// <summary>
    /// Used to create a <see cref="HandlerGetter{IQuerySegmentHandler}"/> delegate.
    /// </summary>
    public static RawVariableHandler Build(string _) => Instance;
    /// <inheritdoc/>
    public void Handle(ref ValueStringBuilder sb, object value)
        => sb.Append(value.ToString());
}
/// <summary>
/// Injects an integer directly into the SQL text.
/// </summary>
/// <remarks>
/// Optimized for numeric values that do not require quotes or escaping.
/// </remarks>
public class NumberVariableHandler() : IQuerySegmentHandler {
    /// <summary>Singleton for <see cref="NumberVariableHandler"/></summary>
    public static readonly NumberVariableHandler Instance = new();
    /// <summary>
    /// Used to create a <see cref="HandlerGetter{IQuerySegmentHandler}"/> delegate.
    /// </summary>
    public static NumberVariableHandler Build(string _) => Instance;
    /// <inheritdoc/>
    public void Handle(ref ValueStringBuilder sb, object value)
        => sb.Append((int)value);
}
