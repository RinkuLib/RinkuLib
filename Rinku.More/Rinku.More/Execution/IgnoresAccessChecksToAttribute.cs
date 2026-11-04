namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute {
    public string AssemblyName { get; } = assemblyName;
}
