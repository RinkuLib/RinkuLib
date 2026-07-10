using RinkuLib.Tools;

namespace RinkuLib.Queries;

#if !NET8_0_OR_GREATER
/// <summary>Holds <see cref="NotSet"/> on frameworks without static interface members.</summary>
public static class QuerySegmentHandler {
    /// <summary>
    /// A placeholder handler for a spot a special handler will bind later. Trying to render it before that
    /// throws, so an unbound handler surfaces at once rather than producing wrong SQL.
    /// </summary>
    public static readonly IQuerySegmentHandler NotSet = new NotSetHandler();
}
#endif
/// <summary>
/// Turns a variable's value into the SQL text that stands in its place, quoting a string, expanding a list,
/// writing a number, or injecting raw text. Register your own under a suffix letter in
/// <see cref="QueryFactory.BaseHandlerMapper"/> to add a marker of your own.
/// </summary>
public interface IQuerySegmentHandler {
#if NET8_0_OR_GREATER
    /// <summary>
    /// A placeholder handler for a spot a special handler will bind later. Trying to render it before that
    /// throws, so an unbound handler surfaces at once rather than producing wrong SQL.
    /// </summary>
    public static readonly IQuerySegmentHandler NotSet = new NotSetHandler();
#endif
    /// <summary>
    /// Writes the SQL for <paramref name="value"/> into the query being built.
    /// </summary>
    /// <param name="sb">The builder the query is being assembled in.</param>
    /// <param name="value">The value bound to this spot for the current run.</param>
    public void Handle(ref ValueStringBuilder sb, object value);
}
/// <summary>
/// Makes a handler for a specific marker, given the marker's full name as written in the query.
/// </summary>
/// <param name="Name">The marker name including its underscore and suffix letter, such as <c>Order_N</c>.</param>
public delegate T HandlerGetter<out T>(string Name) where T : IQuerySegmentHandler;
/// <summary>
/// The placeholder that throws if a handler spot is rendered before a real handler is bound.
/// </summary>
internal class NotSetHandler() : IQuerySegmentHandler {
    public void Handle(ref ValueStringBuilder sb, object? value)
        => throw new NotImplementedException();
}