namespace RinkuLib.Exceptions;

/// <summary>
/// Every condition the library raises, one constant per code. The bands follow the stage the run had
/// reached, which is what decides where to look: <c>1###</c> reading the template, <c>2###</c> preparing a
/// command from it, <c>3###</c> building a parser for the target type, <c>4###</c> reading a result
/// through that parser, <c>5###</c> configuring a type, and <c>9###</c> invariants inside the library.
/// </summary>
/// <remarks>
/// The summary on each constant is the source of its entry in the error reference. Keep them describing
/// the condition rather than the fix, the fix belongs in the documentation page.
/// </remarks>
public static class ErrorCodes {
    /// <summary>The template is under two characters.</summary>
    public const string QueryTooShort = "RINKU1001";
    /// <summary>A marker or a literal comment was opened and the template ended before it closed.</summary>
    public const string UnclosedComment = "RINKU1002";
    /// <summary>A marker holds a key with nothing in it.</summary>
    public const string EmptyConditionKey = "RINKU1003";
    /// <summary>A variable carried a suffix letter no handler is registered for.</summary>
    public const string UnknownHandlerSuffix = "RINKU1004";
    /// <summary>A marker named a variable the template does not contain.</summary>
    public const string ConditionVariableNotInQuery = "RINKU1005";
    /// <summary>The template closes a scope it never opened.</summary>
    public const string UnbalancedScope = "RINKU1006";
    /// <summary>Open scopes went past the depth the parser tracks.</summary>
    public const string ScopeTooDeep = "RINKU1007";
    /// <summary>A dynamic projection construct was used outside a projection.</summary>
    public const string ProjectionOnlyConstruct = "RINKU1008";

    /// <summary>The command carries no connection to run against.</summary>
    public const string NoConnection = "RINKU2001";
    /// <summary>A handler needs a value for a variable the run did not supply.</summary>
    public const string RequiredHandlerValue = "RINKU2002";
    /// <summary>A handler was given a value it cannot render.</summary>
    public const string HandlerValueType = "RINKU2003";
    /// <summary>The parameter at the given index is not usable.</summary>
    public const string InvalidParameterAtIndex = "RINKU2004";
    /// <summary>A handler's saved state was read before a bind wrote it.</summary>
    public const string ValueNotSet = "RINKU2005";
    /// <summary>A sized parameter was asked for a database type that does not carry a size.</summary>
    public const string TypeHasNoSize = "RINKU2006";

    /// <summary>No construction path for the target type could be filled from the columns returned.</summary>
    public const string NoParserForSchema = "RINKU3001";

    /// <summary>The query returned no rows and the requested shape requires one.</summary>
    public const string NoRows = "RINKU4001";
    /// <summary>A result shape refused the rows it was handed.</summary>
    public const string ShapeRefusedResult = "RINKU4002";
    /// <summary>A column held NULL and the slot receiving it refuses null.</summary>
    public const string NullNotAllowed = "RINKU4003";
    /// <summary>A value could not be converted to the requested type.</summary>
    public const string CannotConvert = "RINKU4004";
    /// <summary>A column could not be read as the requested type. The message names which column.</summary>
    public const string CannotReadColumn = "RINKU4005";

    /// <summary>The parsing info cannot handle the type it was asked to validate.</summary>
    public const string TypeNotUsableByInfo = "RINKU5001";
    /// <summary>
    /// A construction path was offered whose own shape the engine cannot call, a factory that is not
    /// static or whose type parameters do not line up with what it returns.
    /// </summary>
    public const string ConstructionShapeNotUsable = "RINKU5002";
    /// <summary>
    /// A member was offered whose own shape the engine cannot write to, a static field, a read-only
    /// property, or a setter whose parameters do not line up with the instance it writes to.
    /// </summary>
    public const string UnusableMember = "RINKU5003";
    /// <summary>
    /// A construction path or member was offered whose type does not line up with the target, a
    /// construction that builds something else or a member that belongs to another type.
    /// </summary>
    public const string TargetTypeMismatch = "RINKU5004";
    /// <summary>
    /// A construction path or member was offered from a generic type other than the target, which the
    /// engine cannot close at parse time.
    /// </summary>
    public const string ForeignGenericSource = "RINKU5005";
    /// <summary>An accessor attribute was applied to a member whose type it cannot drive.</summary>
    public const string AttributeOnWrongMemberType = "RINKU5006";
    /// <summary>A type was asked for an operation it does not carry, such as being built from JSON.</summary>
    public const string OperationNotSupportedForType = "RINKU5007";

    /// <summary>
    /// A type's shape gives the copier nothing to work with, so tracking cannot keep the original to
    /// compare against.
    /// </summary>
    public const string NoCopyStrategy = "RINKU6001";
    /// <summary>
    /// A copy method a type declares, through <c>ICopyable</c> or <c>[CopyUsingMethod]</c>, cannot be
    /// called as one.
    /// </summary>
    public const string CopyMethodNotUsable = "RINKU6002";
    /// <summary>A tracked slot was read for display and holds no current value.</summary>
    public const string NoCurrentValue = "RINKU6003";
    /// <summary>A list was asked for a new item without a way to make one.</summary>
    public const string NoAddNewFactory = "RINKU6004";

    /// <summary>
    /// An invariant inside the library did not hold. One code covers them all, since reaching any of them
    /// leaves a caller the same thing to do, and the message carries which invariant it was.
    /// </summary>
    public const string InternalInvariant = "RINKU9001";
}
