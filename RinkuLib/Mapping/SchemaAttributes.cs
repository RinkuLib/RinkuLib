using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Rinku.Mapping;

/// <summary>Identifies a type, constructor, or method as a schema source.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class SchemaAttribute(string? lastUpdated = null) : Attribute {
    /// <summary>Gets the UTC time at which the schema was last changed.</summary>
    public DateTime? LastUpdated { get; } = lastUpdated is null ? null : DateTime.Parse(lastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

/// <summary>Identifies the schema member used by a result type and optionally records its acknowledged version.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class BasedOnAttribute : Attribute, IEnumerable<MemberInfo> {
    /// <summary>Selects constructors, or the named methods, and optionally parses an acknowledged schema timestamp.</summary>
    public BasedOnAttribute(Type declaringType, string? lastUpdated = null, string? methodName = null) {
        if (lastUpdated is not null && methodName is not null && !DateTime.TryParse(lastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _) && DateTime.TryParse(methodName, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
            (lastUpdated, methodName) = (methodName, lastUpdated);
        DeclaringType = declaringType;
        MethodName = methodName;
        LastUpdated = lastUpdated is null ? null : DateTime.Parse(lastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>Gets the type that declares the referenced member.</summary>
    public Type DeclaringType { get; }

    /// <summary>Gets the method name when this reference selects a method.</summary>
    public string? MethodName { get; }

    /// <summary>Gets the UTC time at which the link was last synchronized.</summary>
    public DateTime? LastUpdated { get; }

    /// <summary>Enumerates the schema members selected by this attribute.</summary>
    public IEnumerator<MemberInfo> GetEnumerator() {
        MemberInfo[] members = MethodName is null
            ? DeclaringType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            : DeclaringType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => method.Name == MethodName).ToArray();
        if (members.Length == 0)
            throw new InvalidOperationException($"No schema members were found for '{DeclaringType}' and selector '{MethodName}'.");
        return ((IEnumerable<MemberInfo>)members).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}
