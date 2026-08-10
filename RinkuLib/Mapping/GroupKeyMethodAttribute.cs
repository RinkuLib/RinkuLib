using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Points a constructor or factory at a static <c>(bool same, TKey next) Method(TKey stored, ...)</c> that decides
/// the group boundary, naming it by <see cref="Method"/>. When that construction is chosen its method is the key,
/// overriding the type-level key the same way marked parameters do. A construction carries this or marked
/// parameters, never both.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, AllowMultiple = false)]
public sealed class GroupKeyMethodAttribute(string method) : Attribute, IGroupingRuleMaker {
    /// <summary>The name of the static boundary method on the construction's declaring type.</summary>
    public string Method { get; } = method;
    /// <summary>A named boundary method is the only group key on its construction.</summary>
    public bool Composes(ICustomAttributeProvider carrier) => false;
    /// <inheritdoc/>
    public IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers) {
        var construction = (MethodBase)carriers[0];
        var boundary = construction.DeclaringType!.GetMethod(Method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType, $"[GroupKeyMethod] names no static method {Method} on {construction.DeclaringType}");
        return new MethodGroupingRule(boundary);
    }
}
