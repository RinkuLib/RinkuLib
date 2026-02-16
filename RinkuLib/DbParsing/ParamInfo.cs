using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// The default implementation of <see cref="IDbTypeParserMatcher"/>. 
/// Handles the standard negotiation flow for constructor parameters, properties, and fields.
/// </summary>
/// <remarks>
/// <para><b>I. Negotiation Strategy:</b></para>
/// This class implements a two-tiered resolution process based on the global registry. 
/// If the target type is <b>Registered</b> in <see cref="TypeParsingInfo"/>, it triggers 
/// a recursive negotiation to build a complex object. If the type is unregistered, 
/// it attempts a direct column-to-value mapping.
/// 
/// <para><b>II. Thread-Safe Configuration:</b></para>
/// It maintains the state for <see cref="INameComparer"/> and <see cref="INullColHandler"/> 
/// using <see cref="Interlocked"/> operations, allowing for dynamic updates to naming 
/// and null-handling rules during the registration phase.
/// </remarks>
public class ParamInfo(Type Type, INullColHandler NullColHandler, INameComparer NameComparer) : IDbTypeParserMatcher {
    /// <summary>
    /// The current strategy for handling database NULL values.
    /// </summary>
    public INullColHandler NullColHandler { get => field; set => Interlocked.Exchange(ref field, value); } = NullColHandler;
    /// <summary>
    /// The logic used to match column names against this member's identifiers.
    /// </summary>
    public INameComparer NameComparer { get => field; set => Interlocked.Exchange(ref field, value); } = NameComparer;
    /// <summary>
    /// The C# type of the parameter or member. (Can be generic)
    /// </summary>
    public Type Type = Type;
    /// <summary>
    /// Updates the <see cref="NullColHandler"/> to handle a recovery jump if a null is encountered.
    /// </summary>
    public void SetInvalidOnNull(bool invalidOnNull) => NullColHandler = NullColHandler.SetInvalidOnNull(Type, invalidOnNull);
    Type IDbTypeParserMatcher.TargetType => Type;
    /// <summary>
    /// Adds an alternative name to the existing <see cref="NameComparer"/>.
    /// </summary>
    public void AddAltName(string altName)
        => NameComparer = NameComparer.AddAltName(altName);
    /// <summary>
    /// Returns the primary name defined for this member.
    /// </summary>
    public string GetName() => NameComparer.GetDefaultName();
    /// <summary>
    /// The default logic for resolving a member into an emission node.
    /// </summary>
    /// <remarks>
    /// <b>The Default Flow:</b>
    /// <list type="number">
    /// <item><b>Normalization:</b> Unwraps <see cref="Nullable{T}"/> and resolves generic 
    /// placeholders (e.g., <c>T</c> to <c>int</c>) using <paramref name="declaringTypeArguments"/>.</item>
    /// <item><b>Recursive Registry Lookup:</b> Checks if the resolved type exists in the 
    /// <see cref="TypeParsingInfo"/> registry. If found, it delegates to that type's 
    /// <c>TryGetParser</c> to handle nested object construction.</item>
    /// <item><b>Basic Column Mapping:</b> If the type is not registered, the engine searches 
    /// <paramref name="columns"/> for a direct match. A match is confirmed if:
    /// <list type="bullet">
    /// <item>The column type is compatible (<see cref="TypeExtensions.CanConvert"/>).</item>
    /// <item>The column name satisfies the <see cref="NameComparer"/> (accounting for active <see cref="ColModifier"/>).</item>
    /// </list>
    /// </item>
    /// </list>
    /// </remarks>
    public DbItemParser? TryGetParser(Type[] declaringTypeArguments, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        var t = Nullable.GetUnderlyingType(Type);
        var closedType = (t ?? Type).CloseType(declaringTypeArguments);
        var defaultProvider = NullColHandler as MayProvideDefaultValue;
        var actualNull = defaultProvider?.NullColHandler ?? NullColHandler;
        if (TypeParsingInfo.TryGetInfo(closedType, out var typeInfo) && typeInfo.Matcher != BaseTypeMatcher.Instance) {
            var node = typeInfo.TryGetParser(closedType.IsGenericType ? closedType.GetGenericArguments() : [], NameComparer.GetDefaultName(), actualNull, columns, colModifier.Add(NameComparer), t is not null, ref colUsage);
            if (node is not null)
                return node;
            if (t is not null)
                closedType = typeof(Nullable<>).MakeGenericType(closedType);
            return defaultProvider?.TryGetItemParser(closedType);
        }
        int i = 0;
        for (; i < columns.Length; i++) {
            if (colUsage.IsUsed(i))
                continue;
            var column = columns[i];
            if (column.Type.CanConvert(closedType) && colModifier.Match(column.Name, NameComparer))
                break;
        }
        if (t is not null)
            closedType = typeof(Nullable<>).MakeGenericType(closedType);
        if (i >= columns.Length)
            return defaultProvider?.TryGetItemParser(closedType);
        colUsage.Use(i);
        return new BasicParser(closedType, NameComparer.GetDefaultName(), actualNull, i);
    }
    /// <summary>
    /// Creates a matcher for a constructor or method parameter if the type is usable.
    /// </summary>
    public static IDbTypeParserMatcher? TryNew(ParameterInfo p)
        => !TypeParsingInfo.IsUsableType(p.ParameterType) ? null :
            Create(p.ParameterType, p.Name, p.GetCustomAttributes(true), p);
    /// <summary>
    /// Creates a matcher for a class property if the type is usable.
    /// </summary>
    public static IDbTypeParserMatcher? TryNew(PropertyInfo p) {
        if (!TypeParsingInfo.IsUsableType(p.PropertyType))
            return null;

        object[] attributes = p.GetCustomAttributes(true);
        bool hasNotNull = false;
        for (int i = 0; i < attributes.Length; i++) {
            if (attributes[i] is NotNullAttribute) {
                hasNotNull = true;
                break;
            }
        }
        if (!hasNotNull) {
            var returnParam = p.GetMethod?.ReturnParameter;
            if (returnParam is not null && returnParam.IsDefined(typeof(NotNullAttribute), true))
                attributes = [..attributes, returnParam.GetCustomAttributes(typeof(NotNullAttribute), true)[0]];
        }

        return Create(p.PropertyType, p.Name, attributes, p);
    }

    /// <summary>
    /// Creates a matcher for a class field if the type is usable.
    /// </summary>
    public static IDbTypeParserMatcher? TryNew(FieldInfo f)
        => !TypeParsingInfo.IsUsableType(f.FieldType) ? null :
            Create(f.FieldType, f.Name, f.GetCustomAttributes(true), f);
    /// <summary>
    /// The central factory for matcher creation. Processes all custom attributes to define behavior.
    /// </summary>
    /// <remarks>
    /// <b>Attribute Hierarchy:</b>
    /// <list type="bullet">
    /// <item>If any attribute implements <see cref="IDbReadingMatcherMaker"/>, it takes control and creates the matcher.</item>
    /// <item><see cref="AltAttribute"/> instances are collected to build optimized <see cref="INameComparer"/> versions.</item>
    /// <item>Null-handling is determined by <see cref="NotNullAttribute"/>, <see cref="InvalidOnNullAttribute"/>, or custom <see cref="INullColHandlerMaker"/>.</item>
    /// </list>
    /// </remarks>
    public static IDbTypeParserMatcher Create(Type type, string? name, object[] attributes, object? param = null) {
        int altCount = 0;
        INullColHandler? nullColHandler = null;
        bool isInvalidOnNull = false;
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (attr is AltAttribute)
                altCount++;
            if (attr is IDbReadingMatcherMaker mm)
                return mm.MakeMatcher(type, name, attributes, param);
            if (attr is INullColHandler nch)
                nullColHandler = nch;
            if (attr is InvalidOnNullAttribute)
                isInvalidOnNull = true;
            if (attr is INullColHandlerMaker nchm)
                nullColHandler = nchm.MakeColHandler(type, name, attributes, param);
            if (attr is NotNullAttribute)
                nullColHandler = NotNullHandle.Instance;
        }
        nullColHandler ??= type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        nullColHandler = nullColHandler.SetInvalidOnNull(type, isInvalidOnNull);
        if (param is ParameterInfo p && IsTypeDefault(p))
            nullColHandler = new DefaultValueProvider(nullColHandler);
        string[] altNames = [];
        if (altCount > 0) {
            altNames = new string[altCount];
            int altIdx = 0;
            for (int i = 0; i < attributes.Length; i++)
                if (attributes[i] is AltAttribute alt)
                    altNames[altIdx++] = alt.AlternativeName;
        }
        INameComparer comparer;
        if (name is null) {
            if (altNames.Length == 0)
                comparer = new NoNameComparer();
            else
                comparer = new NameComparerArray(altNames);
        }
        else if (altNames.Length == 0)
            comparer = new NameComparer(name);
        else if (altNames.Length == 1)
            comparer = new NameComparerTwo(name, altNames[0]);
        else
            comparer = new NameComparerMany(name, altNames);
        return new ParamInfo(type, nullColHandler, comparer);
    }
    private static bool IsTypeDefault(ParameterInfo p) {
        if (!p.HasDefaultValue)
            return false;

        object? value = p.DefaultValue;
        Type type = p.ParameterType;

        if (value == null || value == DBNull.Value) {
            if (type.IsGenericParameter)
                return true;
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }

        Type actualType = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

        try {
            switch (Type.GetTypeCode(actualType)) {
                case TypeCode.Boolean:
                    return value is bool b && !b;
                case TypeCode.Char:
                    return value is char c && c == '\0';
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return Convert.ToInt64(value) == 0;
                case TypeCode.Single:
                case TypeCode.Double:
                    return Convert.ToDouble(value) == 0.0;
                case TypeCode.Decimal:
                    return Convert.ToDecimal(value) == 0m;
                case TypeCode.DateTime:
                    return value is DateTime dt && dt == default;

                case TypeCode.Object:
                    if (actualType == typeof(Guid))
                        return value is Guid g && g == Guid.Empty;
                    if (actualType == typeof(TimeSpan))
                        return value is TimeSpan ts && ts == TimeSpan.Zero;
                    if (actualType == typeof(DateTimeOffset))
                        return value is DateTimeOffset dto && dto == default;
                    return value.Equals(Activator.CreateInstance(actualType));

                default:
                    return false;
            }
        }
        catch {
            return false;
        }
    }
}