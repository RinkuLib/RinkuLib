using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rinku.Mapping;
/// <summary>Creates the name comparer used by one member or parameter.</summary>
public delegate INameComparer NameComparerFactory(Type type, string? name, string[] altNames, object[] attributes, object? param, List<INameComparerMaker> nameComparerMakers);

/// <summary>Requires the database column type to equal the parameter or member type.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ExactTypeAttribute : Attribute;
/// <summary>
/// Describes how one constructor parameter, property, or field matches and reads a column.
/// Change its name and null rules when the defaults do not match the database.
/// </summary>
public class ParamInfo(Type Type, INullColHandler NullColHandler, INameComparer NameComparer) {
    private static Func<ParamInfo, ParamInfo>? _registrationInitializer;
    /// <summary>
    /// Gets or sets the callback applied to every slot created by <see cref="Create"/>.
    /// Set it during application startup to apply a naming or null rule across the application.
    /// Return the received slot after changing it or return a replacement.
    /// Slots created directly do not call it.
    /// </summary>
    public static Func<ParamInfo, ParamInfo>? RegistrationInitializer {
        get => Volatile.Read(ref _registrationInitializer);
        set => Volatile.Write(ref _registrationInitializer, value);
    }
    /// <summary>Marks a temporary slot whose type will be supplied later.</summary>
    public static readonly Type NoType = typeof(NoTypeType);
    private static class NoTypeType;
    /// <summary>
    /// The current strategy for handling database NULL values.
    /// </summary>
    public INullColHandler NullColHandler {
        get => field;
        set => Interlocked.Exchange(ref field, value);
    } = NullColHandler;
    /// <summary>
    /// The logic used to match column names against this member's identifiers.
    /// </summary>
    public INameComparer NameComparer {
        get => field;
        set => Interlocked.Exchange(ref field, value);
    } = NameComparer;
    /// <summary>
    /// The type of the parameter or member.
    /// </summary>
    public Type Type = Type;
    /// <summary>Gets or sets whether the column type must be an exact match.</summary>
    public virtual bool RequireExactType {
        get => false;
        set {
            if (value)
                throw new InvalidOperationException($"{nameof(RequireExactType)} requires {nameof(ParamInfoPlus)}.");
        }
    }
    /// <summary>
    /// Sets whether a null value stops construction of the containing object.
    /// </summary>
    public void SetAbortOnNull(bool abortOnNull) => NullColHandler = NullColHandler.SetAbortOnNull(Type, abortOnNull);
    /// <summary>Applies this slot's reading order rules.</summary>
    public virtual void UpdateColModifier(ref ColModifier mod) { }
    /// <summary>
    /// Applies this slot's reading order rules to a nested value.
    /// </summary>
    public virtual void EnterSubtree(ref ColModifier mod, int nbClaims) { }
    /// <summary>
    /// Returns a copy that applies <paramref name="modifier"/> while keeping this slot's name, null, type,
    /// exact type, and missing column rules.
    /// </summary>
    public ParamInfo WithColModifier(IColModifier modifier) {
        ArgumentNullException.ThrowIfNull(modifier);
        if (GetType() != typeof(ParamInfo) && GetType() != typeof(ParamInfoPlus))
            return new ColModifiedParamInfo(this, modifier);
        var result = new ParamInfoPlus(Type, NullColHandler, NameComparer, modifier,
            this is ParamInfoPlus plus ? plus.FallbackParserGetter : IFallbackParserGetter.Nothing) {
            RequireExactType = RequireExactType
        };
        return result;
    }
    private sealed class ColModifiedParamInfo(ParamInfo source, IColModifier modifier)
        : ParamInfo(source.Type, source.NullColHandler, source.NameComparer) {
        public override bool RequireExactType { get; set; } = source.RequireExactType;
        public override void UpdateColModifier(ref ColModifier mod) => modifier.UpdateColModifier(ref mod);
        public override void EnterSubtree(ref ColModifier mod, int nbClaims) => modifier.EnterSubtree(ref mod, nbClaims);
        public override DbItemPlan? FallbackTryGetParser(Type type) => source.FallbackTryGetParser(type);
    }
    /// <summary>
    /// Provides a value when no result column matches this slot.
    /// </summary>
    /// <returns>The fallback plan or <see langword="null"/> when the slot is required.</returns>
    public virtual DbItemPlan? FallbackTryGetParser(Type type) => null;
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
    /// Creates the slot rules for a member or parameter and applies its attributes.
    /// </summary>
    /// <remarks>A custom <see cref="IParamInfoMaker"/> takes control before the standard attributes are applied.</remarks>
    public static ParamInfo Create(Type type, string? name, object[] attributes, object? param = null) {
        int altCount = 0;
        IParamInfoMaker maker = DefaultParamInfoMaker.Instance;
        UsageFlags usageFlags = default;
        bool hasNoName = false;
        bool requireExactType = false;
        List<INameComparerMaker> nameComparersMakers = [];
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (INameComparer.TryGetTrueName(attr, out var n))
                name = n;
            if (attr is AltAttribute)
                altCount++;
            if (attr is NoNameAttribute)
                hasNoName = true;
            if (attr is ExactTypeAttribute)
                requireExactType = true;
            if (attr is INameComparerMaker mkr)
                nameComparersMakers.Add(mkr);
            if (attr is IParamInfoMaker mm)
                maker = mm;
            if (attr is IUsageFlagModifier ufm)
                ufm.UpdateFlags(param, ref usageFlags);
        }
        var nullColHandler = GetDeclaredNullColHandler(type, name, attributes, param)
            ?? DefaultNullColHandler(type);
        string[] altNames = [];
        if (altCount > 0) {
            altNames = new string[altCount];
            int altIdx = 0;
            for (int i = 0; i < attributes.Length; i++)
                if (attributes[i] is AltAttribute alt)
                    altNames[altIdx++] = alt.AlternativeName;
        }
        INameComparer comparer = ComparerFactory(type, hasNoName ? null : name, altNames, attributes, param, nameComparersMakers);
        var matcher = maker.MakeMatcher(type, nullColHandler, comparer, name, attributes, usageFlags, param);
        if (requireExactType) {
            if (matcher is not ParamInfoPlus)
                matcher = new ParamInfoPlus(matcher.Type, matcher.NullColHandler, matcher.NameComparer,
                    IColModifier.Nothing, IFallbackParserGetter.Nothing);
            ((ParamInfoPlus)matcher).RequireExactType = true;
        }
        return TypeParsingInfo.ApplyRegistrationInitializer(Volatile.Read(ref _registrationInitializer), matcher);
    }
    private static INullColHandler DefaultNullColHandler(Type type)
        => TypeParsingInfo.TryGetInfo(type, out var info) && info is IMultiRowTypeParsingInfo
            ? AbortOnNullAndNotNullHandle.Instance
            : type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance;
    /// <summary>
    /// Resolves the nullability that a set of attributes declares, a custom
    /// <see cref="INullColHandlerMaker"/>, <see cref="NotNullAttribute"/>, <see cref="MaybeNullAttribute"/>,
    /// composed with <see cref="AbortOnNullAttribute"/>. This is the resolution <see cref="Create"/> uses
    /// before falling back to the type's own nullability.
    /// </summary>
    /// <returns>The declared handler, or <see langword="null"/> when nothing is declared.</returns>
    public static INullColHandler? GetDeclaredNullColHandler(Type type, string? name, object[] attributes, object? param = null) {
        INullColHandler? handler = null;
        bool isAbortOnNull = false;
        for (int i = 0; i < attributes.Length; i++) {
            var attr = attributes[i];
            if (attr is AbortOnNullAttribute)
                isAbortOnNull = true;
            if (attr is INullColHandlerMaker nchm)
                handler = nchm.MakeColHandler(type, name, attributes, param);
            if (attr is NotNullAttribute)
                handler = NotNullHandle.Instance;
            if (attr is MaybeNullAttribute)
                handler = NullableTypeHandle.Instance;
        }
        if (!isAbortOnNull)
            return handler;
        handler ??= type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        return handler.SetAbortOnNull(type, true);
    }
    /// <summary>Gets or sets the callback that chooses a name comparer.</summary>
    public static NameComparerFactory ComparerFactory { get; set; } = DispatchComparer;
    /// <summary>
    /// Creates a name comparer from a name, alternative names, and naming attributes.
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
            var modeFlags = usageFlags & ~UsageFlags.Subtree;  
            var colModifier = modeFlags == default
                ? IColModifier.Nothing
                : new FlagUpdater(modeFlags, usageFlags.HasFlag(UsageFlags.Subtree));
            return new ParamInfoPlus(Type, NullColHandler, NameComparer, colModifier, fallback);
        }
        return new(Type, NullColHandler, NameComparer);
    }
}
