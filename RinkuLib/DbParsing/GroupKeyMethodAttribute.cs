namespace RinkuLib.DbParsing;

/// <summary>
/// Points a constructor or factory at a static <c>(bool same, TKey next) Method(TKey stored, ...)</c> that decides
/// the group boundary, naming it by <see cref="Method"/>. When that construction is chosen its method is the key,
/// overriding the type-level key the same way marked parameters do. A construction carries this or marked
/// parameters, never both.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, AllowMultiple = false)]
public sealed class GroupKeyMethodAttribute(string method) : Attribute {
    /// <summary>The name of the static boundary method on the construction's declaring type.</summary>
    public string Method { get; } = method;
}
