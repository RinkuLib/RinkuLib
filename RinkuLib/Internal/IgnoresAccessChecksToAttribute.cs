namespace System.Runtime.CompilerServices;

/// <summary>
/// Applied to a dynamic assembly to let its emitted code reach the non-public members of the named assembly.
/// The runtime JIT honours it the same way <see cref="System.Reflection.Emit.DynamicMethod"/> honours
/// <c>skipVisibility</c>. Defined here because the type is not exposed by the base class libraries.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute {
    /// <summary>The simple name of the assembly whose access checks are ignored.</summary>
    public string AssemblyName { get; } = assemblyName;
}
