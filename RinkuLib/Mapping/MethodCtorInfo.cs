using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rinku.Mapping;
/// <summary>
/// Specifies that an instantiation method allows for additional property or field 
/// assignments after the initial object creation.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public class CanCompleteWithMembersAttribute : Attribute;
/// <summary>
/// Defines that all the parameters type should be registered if they aren't
/// </summary>

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class AreReadableAttribute : Attribute;
/// <summary>
/// A validated candidate for object instantiation, wrapping a
/// A validated candidate for object instantiation, wrapping a
/// <see cref="ConstructorInfo"/> or static <see cref="MethodInfo"/>.
/// </summary>
public class MethodCtorInfo {
    private static Func<MethodCtorInfo, MethodCtorInfo>? _registrationInitializer;
    /// <summary>
    /// Adjusts each construction path produced by <see cref="TryNew(MethodBase, out MethodCtorInfo)"/> before
    /// automatic registration can publish it. Return the received path after changing it or return a replacement.
    /// Configure this once during application startup. Direct construction does not invoke it.
    /// </summary>
    public static Func<MethodCtorInfo, MethodCtorInfo>? RegistrationInitializer {
        get => Volatile.Read(ref _registrationInitializer);
        set => Volatile.Write(ref _registrationInitializer, value);
    }
    /// <summary>Flags for a mci instance</summary>
    [Flags]
    public enum AdditionalFlags : byte {
        /// <summary>
        /// Allows properties and fields to be filled after construction.
        /// </summary>
        CanCompleteWithMembers = 0b1,
        /// <summary>
        /// Automatically registers the parameter types if they are not already registered.
        /// </summary>
        ParametersAreReadable = 0b10,
    }
    /// <summary>The constructor or static method used for instantiation.</summary>
    public readonly MethodBase MethodBase;
    /// <summary>The matchers for each parameter in the method signature.</summary>
    public readonly ParamInfo[] Parameters;
    private AdditionalFlags _flags;
    /// <summary>Flags indicating additional info used in various situations</summary>
    public AdditionalFlags Flags {
        get => _flags;
        set => _flags = value;
    }
    /// <summary>
    /// Gets whether properties and fields may be filled after construction.
    /// </summary>
    public bool CanCompleteWithMembers => Flags.HasFlag(AdditionalFlags.CanCompleteWithMembers);
    /// <summary>
    /// Automatically registers the parameter types if they are not already registered.
    /// </summary>
    public bool ParametersAreReadable => Flags.HasFlag(AdditionalFlags.ParametersAreReadable);
    /// <summary>
    /// Resolves the type that this method/constructor produces.
    /// </summary>
    public Type TargetType => MethodBase is MethodInfo method ? method.ReturnType : MethodBase.DeclaringType
                ?? throw new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, $"{nameof(MethodBase)} must be a {nameof(MethodInfo)} or a {nameof(ConstructorInfo)} and have a DeclaringType");
    private IGroupingRule? _groupKey;
    private bool _groupKeyResolved;
    /// <summary>
    /// The grouping rule this construction declares, its <see cref="GroupKeyAttribute"/> parameters or a
    /// <see cref="GroupKeyMethodAttribute"/>, which overrides the type-level rule when this construction is chosen.
    /// <see langword="null"/> when it declares none, so the type's rule (or the default) applies. Read once from the
    /// attributes, or set directly to override a construction's boundary at registration time.
    /// </summary>
    public IGroupingRule? GroupKey {
        get {
            if (!_groupKeyResolved) {
                _groupKey = MakeGroupKey(MethodBase);
                _groupKeyResolved = true;
            }
            return _groupKey;
        }
        set {
            _groupKey = value;
            _groupKeyResolved = true;
        }
    }
    private static IGroupingRule? MakeGroupKey(MethodBase construction)
        => GroupKeyScan.Resolve(construction.DeclaringType!, [construction, .. construction.GetParameters()]);
    /// <summary>
    /// Initializes a new instance of <see cref="MethodCtorInfo"/>.
    /// </summary>
    /// <param name="MethodBase">The constructor or method to wrap.</param>
    /// <exception cref="Exception">Thrown if validation fails.</exception>
    public MethodCtorInfo(MethodBase MethodBase) : this(MethodBase, TryMakeParameters(MethodBase)!) { }
    /// <summary>
    /// Initializes a new instance of <see cref="MethodCtorInfo"/>.
    /// </summary>
    /// <param name="MethodBase">The constructor or method to wrap.</param>
    /// <param name="Parameters">The matchers for the method parameters.</param>
    /// <exception cref="Exception">Thrown if validation fails.</exception>
    public MethodCtorInfo(MethodBase MethodBase, ParamInfo[] Parameters) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null)
            throw ex;
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        if (MethodBase.IsDefined(typeof(CanCompleteWithMembersAttribute)))
            _flags |= AdditionalFlags.CanCompleteWithMembers;
        if (MethodBase.IsDefined(typeof(AreReadableAttribute)))
            _flags |= AdditionalFlags.ParametersAreReadable;
    }
    /// <summary>
    /// Initializes a new instance of <see cref="MethodCtorInfo"/> with explicit member-completion control.
    /// </summary>
    /// <param name="MethodBase">The constructor or method to wrap.</param>
    /// <param name="Parameters">The matchers for the method parameters.</param>
    /// <param name="Flags">The additional flags.</param>
    public MethodCtorInfo(MethodBase MethodBase, ParamInfo[] Parameters, AdditionalFlags Flags) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null)
            throw ex;
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        _flags = Flags;
    }
    private MethodCtorInfo(MethodBase MethodBase, ParamInfo[] Parameters, AdditionalFlags Flags, bool _) {
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        _flags = Flags;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/>, returning false if validation fails.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, [MaybeNullWhen(false)] out MethodCtorInfo mci) => TryNew(MethodBase, TryMakeParameters(MethodBase), out mci);
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/>, returning false if validation fails.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, ParamInfo[]? Parameters, [MaybeNullWhen(false)] out MethodCtorInfo mci) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null) {
            mci = null;
            return false;
        }
        AdditionalFlags flags = default;
        if (MethodBase.IsDefined(typeof(CanCompleteWithMembersAttribute)))
            flags |= AdditionalFlags.CanCompleteWithMembers;
        if (MethodBase.IsDefined(typeof(AreReadableAttribute)))
            flags |= AdditionalFlags.ParametersAreReadable;
        var created = new MethodCtorInfo(MethodBase, Parameters!, flags, true);
        mci = TypeParsingInfo.ApplyRegistrationInitializer(Volatile.Read(ref _registrationInitializer), created);
        if (mci.TargetType != created.TargetType)
            throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch,
                $"The replacement construction creates {mci.TargetType} instead of {created.TargetType}");
        return true;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/> with explicit member-completion control.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, ParamInfo[]? Parameters, AdditionalFlags Flags, [MaybeNullWhen(false)] out MethodCtorInfo mci) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null) {
            mci = null;
            return false;
        }
        var created = new MethodCtorInfo(MethodBase, Parameters!, Flags, true);
        mci = TypeParsingInfo.ApplyRegistrationInitializer(Volatile.Read(ref _registrationInitializer), created);
        if (mci.TargetType != created.TargetType)
            throw new RinkuConfigurationException(ErrorCodes.TargetTypeMismatch,
                $"The replacement construction creates {mci.TargetType} instead of {created.TargetType}");
        return true;
    }
    /// <summary>
    /// Validates that the provided matchers are compatible with the method's parameters.
    /// </summary>
    /// <returns>An <see cref="Exception"/> if invalid, otherwise null.</returns>
    public static Exception? Validate(MethodBase methodBase, ParamInfo[]? parameters) {
        if (parameters is null)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "Parameters cannot be null.");
        if (parameters.Length == 0)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "cannot use parameterless ctor or method");
        var methodParameters = methodBase.GetParameters();
        if (methodParameters.Length != parameters.Length)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "all the parameters must match with the ctor or method parameters");
        for (int i = 0; i < parameters.Length; i++)
            if (methodParameters[i].ParameterType != parameters[i].Type)
                return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "all the parameters must match with the ctor or method parameters");
        if (methodBase is ConstructorInfo)
            return null;
        if (methodBase is not MethodInfo method)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "methodBase base must be constructorInfo or methodInfo");
        return ValidateMethodReturn(method);
    }
    /// <summary>
    /// Validates the return type and generic constraints of a static factory method.
    /// </summary>
    public static Exception? ValidateMethodReturn(MethodInfo method) {
        if (!method.IsStatic)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory must be static");
        if (method.DeclaringType == method.ReturnType) {
            if (method.IsGenericMethod)
                return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory on the type it builds takes its type parameters from that type, so it must not be generic itself");
            return null;
        }
        if (!method.ReturnType.IsGenericType) {
            if (method.IsGenericMethod)
                return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory returning a non-generic type must not be generic itself");
            return null;
        }
        if (!method.IsGenericMethod)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory must carry the same type parameters as the type it returns, in the same order");
        var typeArgs = method.ReturnType.GetGenericArguments();
        var methodArgs = method.GetGenericArguments();
        if (typeArgs.Length != methodArgs.Length)
            return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory must carry the same type parameters as the type it returns, in the same order");
        for (int j = 0; j < typeArgs.Length; j++)
            if (typeArgs[j] != methodArgs[j])
                return new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable, "a factory must carry the same type parameters as the type it returns, in the same order");
        return null;
    }
    /// <summary>
    /// Provides a string representation of the method signature for debugging.
    /// </summary>
    public override string ToString() => $"{MethodBase.Name}({string.Join(", ", Parameters.Select(p => p.Type.ShortName()))})";
    /// <summary>
    /// Automatically generates default matchers for all parameters of a method.
    /// </summary>
    /// <returns>An array of matchers, or null if any parameter is invalid.</returns>
    public static ParamInfo[]? TryMakeParameters(MethodBase methodBase) {
        var type = methodBase is MethodInfo method ? method.ReturnType : methodBase.DeclaringType;
        if (type is null)
            return null;
        var parameters = methodBase.GetParameters();
        if (parameters.Length == 0)
            return [];
        var ps = new ParamInfo[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            var param = parameters[i];
            var p = ParamInfo.TryNew(param);
            if (p is null)
                return null;
            ps[i] = p;
        }
        return ps;
    }
    /// <summary>
    /// Determines if this candidate is more specific (takes precedence) than another.
    /// Precedence is based on parameter count and type assignment compatibility.
    /// </summary>
    public bool IsMoreSpecific(MethodCtorInfo info) {
        if (info == null) return true;
        int len = info.Parameters.Length;
        if (len > this.Parameters.Length)
            return false;
        if (this.MethodBase == info.MethodBase)
            return false;
        bool hasMoreSpecific = false;
        for (int i = 0; i < len; i++) {
            var typeThis = this.Parameters[i].Type;
            var typeOther = info.Parameters[i].Type;
            if (typeThis.IsAssignableTo(typeOther)) {
                if (!hasMoreSpecific)
                    hasMoreSpecific = typeThis != typeOther;
                continue;
            }
            return false;
        }
        return hasMoreSpecific || len < this.Parameters.Length;
    }
    /// <summary>
    /// Compares signatures to determine if two candidates are functionally identical.
    /// </summary>
    public bool IsSameSignature(MethodCtorInfo b) {
        if (Parameters.Length != b.Parameters.Length)
            return false;
        for (int i = 0; i < Parameters.Length; i++) {
            if (Parameters[i].Type != b.Parameters[i].Type ||
                !string.Equals(Parameters[i].NameComparer.GetDefaultName(), b.Parameters[i].NameComparer.GetDefaultName(), StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }
        return true;
    }
    /// <summary>
    /// Orders construction candidates from the most specific to the least specific.
    /// </summary>
    public static MethodCtorInfo[] GetOrderedInfos(Span<MethodCtorInfo> infos) {
        var arr = new MethodCtorInfo[infos.Length];
        for (int i = 0; i < infos.Length; i++) {
            var current = infos[i];
            int targetIndex = i;
            for (int j = 0; j < i; j++)
                if (current.IsMoreSpecific(arr[j])) {
                    targetIndex = j;
                    break;
                }
            if (targetIndex < i)
                Array.Copy(arr, targetIndex, arr, targetIndex + 1, i - targetIndex);
            arr[targetIndex] = current;
        }
        return arr;
    }
    /// <summary>
    /// Adds or replaces this candidate in a list ordered from the most specific choice to the least specific.
    /// </summary>
    /// <param name="infos">The candidate list to update.</param>
    public void InsertInto(ref MethodCtorInfo[] infos) {
        int existingI = 0;
        var len = infos.Length;
        for (; existingI < len; existingI++)
            if (IsSameSignature(infos[existingI]))
                break;
        var i = existingI - 1;
        for (; i >= 0; i--)
            if (infos[i].IsMoreSpecific(this))
                break;
        i++;
        if (existingI < len) {
            if (i < existingI)
                Array.Copy(infos, i, infos, i + 1, existingI - i);
            infos[i] = this;
            return;
        }
        var newArr = new MethodCtorInfo[len + 1];
        if (i < len)
            Array.Copy(infos, i, newArr, i + 1, len - i);
        if (i > 0)
            Array.Copy(infos, 0, newArr, 0, i);
        infos = newArr;
        infos[i] = this;
    }
}
