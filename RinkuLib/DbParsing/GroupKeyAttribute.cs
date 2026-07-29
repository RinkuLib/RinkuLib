namespace RinkuLib.DbParsing;

/// <summary>
/// Declares the group boundary key of a spanning type. On a member (property or field) the key reads that
/// member's column, and several marked members compose a composite key. On a static
/// <c>(bool same, TKey next) Method(TKey stored, ...)</c> the same-group decision is the method's own, its
/// parameters after the stored key negotiated like any reader. On a construction parameter the key reads that
/// parameter's column, composing over every marked parameter of the chosen construction.
/// <para>
/// A construction's own key takes priority: when a constructor or factory is chosen, its marked parameters form
/// the key, and only when it declares none does the type-level key (a marked member or method, or one set at
/// runtime) apply. A spanning grouping type reached with no key at all fails at parser-build time
/// (<see cref="RinkuLib.Exceptions.ErrorCodes.MissingGroupBoundary"/>).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class GroupKeyAttribute : Attribute { }
