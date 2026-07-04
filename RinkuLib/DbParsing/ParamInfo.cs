using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace RinkuLib.DbParsing;
/// <summary>Use to actualy create the comparer</summary>
public delegate INameComparer NameComparerFactory(Type type, string? name, string[] altNames, object[] attributes, object? param, List<INameComparerMaker> nameComparerMakers);
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
    /// Called by a complex parser as it enters this slot's subtree, so a reading-order flag on the slot
    /// can govern the subtree (or arm the swap for its first consumed column). Base slots use
    /// <see cref="UpdateColModifier"/> instead.
    /// </summary>
    public virtual void EnterSubtree(ref ColModifier mod, int nbUsed) { }
    /// <summary>
    /// Provide a way to retrieve a <see cref="DbItemParser"/> when the normal way fails
    /// </summary>
    /// <returns></returns>
    public virtual DbItemParser? FallbackTryGetParser(Type type) => null;
    /// <summary>
    /// Adds an alternative name to the existing <see cref="NameComparer"/>.
    /// </summary>
    public void UpdateAltName(Func<INameComparer, INameComparer?> modifier)
        => NameComparer = modifier(NameComparer) ?? NameComparer;
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
    /// <item>Null-handling is resolved by <see cref="GetDeclaredNullColHandler"/> (<see cref="NotNullAttribute"/>, <see cref="MaybeNullAttribute"/>, <see cref="InvalidOnNullAttribute"/>, or a custom <see cref="INullColHandlerMaker"/>), falling back to the type's own nullability.</item>
    /// </list>
    /// </remarks>
    public static ParamInfo Create(Type type, string? name, object[] attributes, object? param = null) {
        int altCount = 0;
        IParamInfoMaker maker = DefaultParamInfoMaker.Instance;
        UsageFlags usageFlags = default;
        bool hasNoName = false;
        List<INameComparerMaker> nameComparersMakers = [];
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (INameComparer.TryGetTrueName(attr, out var n))
                name = n;
            if (attr is AltAttribute)
                altCount++;
            if (attr is NoNameAttribute)
                hasNoName = true;
            if (attr is INameComparerMaker mkr)
                nameComparersMakers.Add(mkr);
            if (attr is IParamInfoMaker mm)
                maker = mm;
            if (attr is IUsageFlagModifier ufm)
                ufm.UpdateFlags(param, ref usageFlags);
        }
        var nullColHandler = GetDeclaredNullColHandler(type, name, attributes, param)
            ?? (type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance);
        string[] altNames = [];
        if (altCount > 0) {
            altNames = new string[altCount];
            int altIdx = 0;
            for (int i = 0; i < attributes.Length; i++)
                if (attributes[i] is AltAttribute alt)
                    altNames[altIdx++] = alt.AlternativeName;
        }
        INameComparer comparer = ComparerFactory(type, hasNoName ? null : name, altNames, attributes, param, nameComparersMakers);
        return maker.MakeMatcher(type, nullColHandler, comparer, name, attributes, usageFlags, param);
    }
    /// <summary>
    /// Resolves the nullability that a set of attributes declares: a custom
    /// <see cref="INullColHandlerMaker"/>, <see cref="NotNullAttribute"/>, <see cref="MaybeNullAttribute"/>,
    /// composed with <see cref="InvalidOnNullAttribute"/>. This is the resolution <see cref="Create"/> uses
    /// before falling back to the type's own nullability.
    /// </summary>
    /// <returns>The declared handler, or <see langword="null"/> when nothing is declared.</returns>
    public static INullColHandler? GetDeclaredNullColHandler(Type type, string? name, object[] attributes, object? param = null) {
        INullColHandler? handler = null;
        bool isInvalidOnNull = false;
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (attr is InvalidOnNullAttribute)
                isInvalidOnNull = true;
            if (attr is INullColHandlerMaker nchm)
                handler = nchm.MakeColHandler(type, name, attributes, param);
            if (attr is NotNullAttribute)
                handler = NotNullHandle.Instance;
            if (attr is MaybeNullAttribute)
                handler = NullableTypeHandle.Instance;
        }
        if (!isInvalidOnNull)
            return handler;
        handler ??= type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        return handler.SetInvalidOnNull(type, true);
    }
    /// <summary>A delegate to implement your own name comparer dispatching strategy</summary>
    public static NameComparerFactory ComparerFactory { get; set; } = DispatchComparer;
    /// <summary>
    /// The default dispatch logic you provided.
    /// </summary>
    public static INameComparer DispatchComparer(Type type, string? name, string[] altNames, object[] attributes, object? param, List<INameComparerMaker> nameComparerMakers) {
        INameComparer current;
        if (name is null) {
            current = altNames.Length switch {
                0 => NoNameComparer.Instance,
                1 => new NameComparer(altNames[0]),
                _ => new NameArray(altNames)
            };
        }
        else {
            current = altNames.Length switch {
                0 => new NameComparer(name),
                1 => new NameTwo(name, altNames[0]),
                _ => new NameArray([name, .. altNames])
            };
        }
        if (nameComparerMakers.Count == 0)
            return current;

        int maxPotential = 1 + nameComparerMakers.Count;
        var buffer = new INameComparer[maxPotential];
        int count = 0;

        if (current is not NoNameComparer)
            buffer[count++] = current;

        for (int i = 0; i < nameComparerMakers.Count; i++) {
            var created = nameComparerMakers[i].MakeComparer(type, ref current, attributes, param);

            if (created is not NoNameComparer)
                buffer[count++] = created;
        }

        return count switch {
            0 => NoNameComparer.Instance,
            1 => buffer[0],
            2 => new JoinedNameComparer(buffer[0], buffer[1]),
            _ => new NameComparerGroup(buffer[..count])
        };
    }
}
internal class DefaultParamInfoMaker : IParamInfoMaker {
    public static readonly DefaultParamInfoMaker Instance = new();
    private DefaultParamInfoMaker() { }
    public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, string? name, object[] attributes, UsageFlags usageFlags, object? param) {
        var fallback = param is ParameterInfo pp && pp.IsTypeDefault() ? DefaultValueFallback.Instance : IFallbackParserGetter.Nothing;
        if (usageFlags != default || fallback != IFallbackParserGetter.Nothing) {
            var modeFlags = usageFlags & ~UsageFlags.Subtree;   // the reading-order mode, without the scope marker
            var colModifier = modeFlags == default
                ? IColModifier.Nothing
                : new FlagUpdater(modeFlags, usageFlags.HasFlag(UsageFlags.Subtree));
            return new ParamInfoPlus(Type, NullColHandler, NameComparer, colModifier, fallback);
        }
        return new(Type, NullColHandler, NameComparer);
    }
}