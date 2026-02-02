using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace RinkuLib.DbParsing;
/// <summary>
/// Represents a validated mapping between a class member (Property, Field, or Method) 
/// and a database column matcher.
/// </summary>
/// <remarks>
/// This class is used during the "Completion" phase of object parsing, where an existing 
/// instance is populated with additional data not provided to the constructor.
/// </remarks>
public record class MemberParser {
    /// <summary>
    /// The reflection metadata for the member being populated.
    /// </summary>
    public readonly MemberInfo Member;
    /// <summary>
    /// The matcher responsible for negotiating the data type and column name for this member.
    /// </summary>
    public readonly IDbTypeParserMatcher Param;
    /// <summary>
    /// The type of the class that owns or receives this member assignment.
    /// </summary>
    public readonly Type TargetType;
    /// <summary>
    /// Initializes a new instance of the <see cref="MemberParser"/> class.
    /// </summary>
    /// <param name="Member">The property, field, or method to map.</param>
    /// <param name="Param">The matcher for database negotiation.</param>
    /// <exception cref="Exception">Thrown if the member is static, read-only, or type-mismatched.</exception>
    public MemberParser(MemberInfo Member, IDbTypeParserMatcher Param) {
        var val = Validate(Member, Param);
        if (val is Exception ex)
            throw ex;
        this.Member = Member;
        this.Param = Param;
        this.TargetType = (Type)val;
    }
    /// <summary>
    /// Private constructor used by <see cref="TryNew"/> to bypass redundant validation.
    /// </summary>
    private MemberParser(MemberInfo Member, IDbTypeParserMatcher Param, Type TargetType) {
        this.Member = Member;
        this.Param = Param;
        this.TargetType = TargetType;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MemberParser"/>.
    /// </summary>
    /// <param name="member">The candidate member for parsing.</param>
    /// <param name="param">The matcher to associate with the member.</param>
    /// <param name="memberParser">When this method returns, contains the parser if successful; otherwise, null.</param>
    /// <returns><c>true</c> if the member is a valid, writable target; otherwise, <c>false</c>.</returns>
    public static bool TryNew(MemberInfo member, IDbTypeParserMatcher param,  [MaybeNullWhen(false)] out MemberParser memberParser) {
        var val = Validate(member, param);
        if (val is not Type t) {
            memberParser = null;
            return false;
        }
        memberParser = new(member, param, t);
        return true;
    }
    /// <summary>
    /// Validates that a member is accessible, writable, and compatible with the provided matcher.
    /// </summary>
    /// <param name="member">The member to check.</param>
    /// <param name="param">The matcher to compare against.</param>
    /// <returns>The <see cref="Type"/> of the declaring object if valid; otherwise, an <see cref="Exception"/>.</returns>
    private static object Validate(MemberInfo member, IDbTypeParserMatcher param) {
        bool isWriteable = false;
        Type? detectedMemberType = null;
        Type? detectedTargetType = null;

        switch (member) {
            case PropertyInfo prop:
                if (prop.GetAccessors(true)[0].IsStatic)
                    return new ArgumentException("Properties must be instance members.");

                detectedMemberType = prop.PropertyType;
                detectedTargetType = prop.DeclaringType;
                isWriteable = prop.CanWrite && prop.GetSetMethod(nonPublic: false) != null;
                break;

            case FieldInfo field:
                if (field.IsStatic)
                    return new ArgumentException("Fields must be instance members.");

                detectedMemberType = field.FieldType;
                detectedTargetType = field.DeclaringType;
                isWriteable = !field.IsInitOnly && !field.IsLiteral;
                break;

            case MethodInfo method:
                if (method.ReturnType != typeof(void))
                    return new Exception("A member setter method must return void");
                var parameters = method.GetParameters();
                if (method.IsStatic) {
                    if (parameters.Length != 2)
                        return new ArgumentException("Static methods must have 2 parameters (Instance, Value).");
                    detectedTargetType = parameters[0].ParameterType;
                    detectedMemberType = parameters[1].ParameterType;
                    if (method.IsGenericMethodDefinition) {
                        if (!detectedTargetType.IsGenericType)
                            return new ArgumentException("The method's generic arguments should match with the instance's (1st param) generic arguments");
                        Type[] methodGenericArgs = method.GetGenericArguments();
                        Type[] targetGenericArgs = detectedTargetType.GetGenericArguments();
                        if (methodGenericArgs.Length != targetGenericArgs.Length)
                            return new ArgumentException($"Generic mismatch: Method has {methodGenericArgs.Length} type params, but Instance type has {targetGenericArgs.Length}.");
                        for (int i = 0; i < methodGenericArgs.Length; i++)
                            if (methodGenericArgs[i] != targetGenericArgs[i])
                                return new ArgumentException($"Generic mismatch: Method has {methodGenericArgs[i]} type param, but Instance type has {targetGenericArgs[i]}.");
                    }
                    isWriteable = true;
                }
                else if (parameters.Length == 1) {
                    if (method.IsGenericMethodDefinition)
                        return new Exception("instance methods should not be generic");
                    detectedTargetType = method.DeclaringType;
                    detectedMemberType = parameters[0].ParameterType;
                    isWriteable = true;
                }
                break;
        }
        if (detectedMemberType == null || detectedTargetType == null)
            return new ArgumentException("Member is not a supported writeable field, property, or method.");
        if (!isWriteable)
            return new ArgumentException($"Member '{member.Name}' is read-only or inacessible");
        if (detectedMemberType != param.TargetType)
            return new ArgumentException($"Type mismatch: Member expects {detectedMemberType.Name}, but Param provides {param.TargetType.Name}.");

        return detectedTargetType;
    }
}
