using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace RinkuLib.DbParsing;
/// <summary>
/// Specifies that an instantiation method allows for additional property or field 
/// assignments after the initial object creation.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
public class CanCompleteWithMembersAttribute : Attribute;
/// <summary>
/// Represents a validated candidate for object instantiation, wrapping a 
/// <see cref="ConstructorInfo"/> or static <see cref="MethodInfo"/>.
/// </summary>
public class MethodCtorInfo {
    /// <summary>The constructor or static method used for instantiation.</summary>
    public readonly MethodBase MethodBase;
    /// <summary>The matchers for each parameter in the method signature.</summary>
    public readonly IDbTypeParserMatcher[] Parameters;
    /// <summary>
    /// If true, the engine can continue to map additional properties or fields 
    /// after the primary method/constructor has been called.
    /// </summary>
    public readonly bool CanCompleteWithMembers;
    /// <summary>
    /// Resolves the type that this method/constructor produces.
    /// </summary>
    public Type TargetType => MethodBase is MethodInfo method ? method.ReturnType : MethodBase.DeclaringType
                ?? throw new InvalidOperationException($"{nameof(MethodBase)} must be a {nameof(MethodInfo)} or a {nameof(ConstructorInfo)} and have a DeclaringType.");
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
    public MethodCtorInfo(MethodBase MethodBase, IDbTypeParserMatcher[] Parameters) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null)
            throw ex;
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        this.CanCompleteWithMembers = MethodBase.IsDefined(typeof(CanCompleteWithMembersAttribute));
    }
    /// <summary>
    /// Initializes a new instance of <see cref="MethodCtorInfo"/> with explicit member-completion control.
    /// </summary>
    /// <param name="MethodBase">The constructor or method to wrap.</param>
    /// <param name="Parameters">The matchers for the method parameters.</param>
    /// <param name="CanCompleteWithPropOrField">Whether to allow post-creation member mapping.</param>
    public MethodCtorInfo(MethodBase MethodBase, IDbTypeParserMatcher[] Parameters, bool CanCompleteWithPropOrField) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null)
            throw ex;
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        this.CanCompleteWithMembers = CanCompleteWithPropOrField;
    }
    private MethodCtorInfo(MethodBase MethodBase, IDbTypeParserMatcher[] Parameters, bool CanCompleteWithPropOrField, bool _) {
        this.MethodBase = MethodBase;
        this.Parameters = Parameters;
        this.CanCompleteWithMembers = CanCompleteWithPropOrField;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/>, returning false if validation fails.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, [MaybeNullWhen(false)] out MethodCtorInfo mci) => TryNew(MethodBase, TryMakeParameters(MethodBase), out mci);
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/>, returning false if validation fails.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, IDbTypeParserMatcher[]? Parameters, [MaybeNullWhen(false)] out MethodCtorInfo mci) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null) {
            mci = null;
            return false;
        }
        mci = new(MethodBase, Parameters!, MethodBase.IsDefined(typeof(CanCompleteWithMembersAttribute)), true);
        return true;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MethodCtorInfo"/> with explicit member-completion control.
    /// </summary>
    public static bool TryNew(MethodBase MethodBase, IDbTypeParserMatcher[]? Parameters, bool CanCompleteWithPropOrField, [MaybeNullWhen(false)] out MethodCtorInfo mci) {
        var ex = Validate(MethodBase, Parameters);
        if (ex is not null) {
            mci = null;
            return false;
        }
        mci = new(MethodBase, Parameters!, CanCompleteWithPropOrField, true);
        return true;
    }
    /// <summary>
    /// Validates that the provided matchers are compatible with the method's parameters.
    /// </summary>
    /// <returns>An <see cref="Exception"/> if invalid; otherwise, null.</returns>
    public static Exception? Validate(MethodBase methodBase, IDbTypeParserMatcher[]? parameters) {
        if (parameters is null)
            return new Exception("parameters cant be null");
        if (parameters.Length == 0)
            return new Exception("cannot use parameterless ctor or method");
        var methodParameters = methodBase.GetParameters();
        if (methodParameters.Length != parameters.Length)
            return new Exception("all the parameters must match with the ctor or method parameters");
        for (int i = 0; i < parameters.Length; i++)
            if (methodParameters[i].ParameterType != parameters[i].TargetType)
                return new Exception("all the parameters must match with the ctor or method parameters");
        if (methodBase is ConstructorInfo)
            return null;
        if (methodBase is not MethodInfo method)
            return new Exception("methodBase base must be constructorInfo or methodInfo");
        return ValidateMethodReturn(method);
    }
    /// <summary>
    /// Validates the return type and generic constraints of a static factory method.
    /// </summary>
    public static Exception? ValidateMethodReturn(MethodInfo method) {
        if (!method.IsStatic)
            return new Exception("method must be static");
        if (method.DeclaringType == method.ReturnType) {
            if (method.IsGenericMethod)
                return new Exception("static method from the same type must be nonGeneric");
            return null;
        }
        if (!method.ReturnType.IsGenericType) {
            if (method.IsGenericMethod)
                return new Exception("method should not be generic");
            return null;
        }
        if (!method.IsGenericMethod)
            return new Exception("method should have the same generic parameters as returning type");
        var typeArgs = method.ReturnType.GetGenericArguments();
        var methodArgs = method.GetGenericArguments();
        if (typeArgs.Length != methodArgs.Length)
            return new Exception("method should have the same generic parameters as returning type");
        for (int j = 0; j < typeArgs.Length; j++)
            if (typeArgs[j] != methodArgs[j])
                return new Exception("method should have the same generic parameters as returning type");
        return null;
    }
    /// <summary>
    /// Provides a string representation of the method signature for debugging.
    /// </summary>
    public override string ToString() => $"{MethodBase.Name}({string.Join(", ", Parameters.Select(p => p.TargetType.ShortName()))})";
    /// <summary>
    /// Automatically generates default matchers for all parameters of a method.
    /// </summary>
    /// <returns>An array of matchers, or null if any parameter is invalid.</returns>
    public static IDbTypeParserMatcher[]? TryMakeParameters(MethodBase methodBase) {
        var type = methodBase is MethodInfo method ? method.ReturnType : methodBase.DeclaringType;
        if (type is null)
            return null;
        var parameters = methodBase.GetParameters();
        if (parameters.Length == 0)
            return [];
        var ps = new IDbTypeParserMatcher[parameters.Length];
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
        for (int i = 0; i < len; i++) {
            var typeThis = this.Parameters[i].TargetType;
            var typeOther = info.Parameters[i].TargetType;
            if (typeThis.IsAssignableTo(typeOther))
                continue;
            return false;
        }
        return true;
    }
    /// <summary>
    /// Compares signatures to determine if two candidates are functionally identical.
    /// </summary>
    public bool IsSameSignature(MethodCtorInfo b) {
        if (Parameters.Length != b.Parameters.Length)
            return false;
        for (int i = 0; i < Parameters.Length; i++) {
            if (Parameters[i].TargetType != b.Parameters[i].TargetType ||
                string.Equals(Parameters[i].GetName(), b.Parameters[i].GetName(), StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }
        return true;
    }
    /// <summary>
    /// Orders a set of candidates so the most specific ones are checked first during negotiation.
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
    /// Thread-safely inserts this candidate into an existing sorted array of candidates.
    /// </summary>
    /// <param name="infos">A reference to the sorted candidate array.</param>
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
