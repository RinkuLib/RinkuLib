using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
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
public class ParamInfo(Type Type, INullColHandler NullColHandler, INameComparer NameComparer) {
    /// <summary>A default type to use for singleton, should only be used in transient ParamInfos</summary>
    public static readonly Type NoType = typeof(NoTypeType);
    private static class NoTypeType;
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
    /// <summary>Provide a way to modify the col modifier based on the param info state</summary>
    public virtual void UpdateColModifier(ref ColModifier mod) { }
    /// <summary>
    /// Provide a way to retrieve a <see cref="DbItemParser"/> when the normal way fails
    /// </summary>
    /// <returns></returns>
    public virtual DbItemParser? FallbackTryGetParser(Type type) => null;
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
    /// Creates a matcher for a constructor or method parameter if the type is usable.
    /// </summary>
    public static ParamInfo? TryNew(ParameterInfo p)
        => !TypeParsingInfo.IsUsableType(p.ParameterType) ? null :
            Create(p.ParameterType, p.Name, p.GetCustomAttributes(true), p);
    /// <summary>
    /// Creates a matcher for a class property if the type is usable.
    /// </summary>
    public static ParamInfo? TryNew(PropertyInfo p) {
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
    public static ParamInfo? TryNew(FieldInfo f)
        => !TypeParsingInfo.IsUsableType(f.FieldType) ? null :
            Create(f.FieldType, f.Name, f.GetCustomAttributes(true), f);
    /// <summary>
    /// The central factory for matcher creation. Processes all custom attributes to define behavior.
    /// </summary>
    /// <remarks>
    /// <b>Attribute Hierarchy:</b>
    /// <list type="bullet">
    /// <item>If any attribute implements <see cref="IParamInfoMaker"/>, it takes control and creates the matcher.</item>
    /// <item><see cref="AltAttribute"/> instances are collected to build optimized <see cref="INameComparer"/> versions.</item>
    /// <item>Null-handling is determined by <see cref="NotNullAttribute"/>, <see cref="InvalidOnNullAttribute"/>, or custom <see cref="INullColHandlerMaker"/>.</item>
    /// </list>
    /// </remarks>
    public static ParamInfo Create(Type type, string? name, object[] attributes, object? param = null) {
        int altCount = 0;
        INullColHandler? nullColHandler = null;
        bool isInvalidOnNull = false;
        IParamInfoMaker maker = DefaultParamInfoMaker.Instance;
        UsageFlags usageFlags = default;
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (attr is AltAttribute)
                altCount++;
            if (attr is IParamInfoMaker mm)
                maker = mm;
            if (attr is INullColHandler nch)
                nullColHandler = nch;
            if (attr is InvalidOnNullAttribute)
                isInvalidOnNull = true;
            if (attr is INullColHandlerMaker nchm)
                nullColHandler = nchm.MakeColHandler(type, name, attributes, param);
            if (attr is NotNullAttribute)
                nullColHandler = NotNullHandle.Instance;
            if (attr is IUsageFlagModifier ufm)
                ufm.UpdateFlags(param, ref usageFlags);
        }
        nullColHandler ??= type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        nullColHandler = nullColHandler.SetInvalidOnNull(type, isInvalidOnNull);
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
        return maker.MakeMatcher(type, nullColHandler, comparer, name, attributes, usageFlags, param);
    }
}
internal class DefaultParamInfoMaker : IParamInfoMaker {
    public static readonly DefaultParamInfoMaker Instance = new();
    private DefaultParamInfoMaker() { }
    public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, string? name, object[] attributes, UsageFlags usageFlags, object? param) {
        var fallback = param is ParameterInfo pp && pp.IsTypeDefault() ? DefaultValueFallback.Instance : IFallbackParserGetter.Nothing;
        if (usageFlags != default || fallback != IFallbackParserGetter.Nothing) {
            var colModifier = IColModifier.Nothing;
            if (usageFlags.HasFlag(UsageFlags.CanReuse)) {
                if (usageFlags.HasFlag(UsageFlags.RemoveSequentialRead))
                    colModifier = FlagUpdater.CanReuseAndRemoveSequential;
                else if (usageFlags.HasFlag(UsageFlags.SequentialRead))
                    colModifier = FlagUpdater.CanReuseAndSequential;
                else
                    colModifier = FlagUpdater.CanReuse;
            } 
            else if (usageFlags.HasFlag(UsageFlags.RemoveSequentialRead))
                colModifier = FlagUpdater.RemoveSequentialRead;
            else if (usageFlags.HasFlag(UsageFlags.SequentialRead))
                colModifier = FlagUpdater.SequentialRead;
            return new ParamInfoPlus(Type, NullColHandler, NameComparer, colModifier, fallback);
        }
        return new(Type, NullColHandler, NameComparer);
    }
}