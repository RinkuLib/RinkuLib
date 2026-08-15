using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Sets the static method that decides grouping for one constructor or factory.
/// The method receives the stored key followed by values from the current row.
/// It returns whether the group stays open and the next key.
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
