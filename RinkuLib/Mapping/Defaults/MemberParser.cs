using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rinku.Mapping;
/// <summary>
/// A validated mapping between a class member (Property, Field, or Method) 
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
    /// The matcher that negotiates the data type and column name for this member.
    /// </summary>
    public readonly ParamInfo Param;
    /// <summary>
    /// The type of the class that owns or receives this member assignment.
    /// </summary>
    public readonly Type TargetType;
    /// <summary>
    /// Initializes a new instance of the <see cref="MemberParser"/> class.
    /// </summary>
    /// <param name="Member">The property, field, or method to map.</param>
    /// <param name="Param">The column matching rules for the member.</param>
    /// <exception cref="Exception">Thrown if the member is static, read-only, or type-mismatched.</exception>
    public MemberParser(MemberInfo Member, ParamInfo Param) {
        var val = Validate(Member, Param, allowNonPublicSetter: false);
        if (val is Exception ex)
            throw ex;
        this.Member = Member;
        this.Param = Param;
        this.TargetType = (Type)val;
    }
    private MemberParser(MemberInfo Member, ParamInfo Param, Type TargetType) {
        this.Member = Member;
        this.Param = Param;
        this.TargetType = TargetType;
    }
    /// <summary>
    /// Attempts to create a new <see cref="MemberParser"/>.
    /// </summary>
    /// <param name="member">The candidate member for parsing.</param>
    /// <param name="param">The matcher to associate with the member.</param>
    /// <param name="memberParser">When this method returns, contains the parser if successful, otherwise null.</param>
    /// <param name="allowNonPublicSetter">Whether a non-public property setter may be used.</param>
    /// <returns><c>true</c> if the member is a valid, writable target, otherwise <c>false</c>.</returns>
    public static bool TryNew(MemberInfo member, ParamInfo param, [MaybeNullWhen(false)] out MemberParser memberParser, bool allowNonPublicSetter = false) {
        var val = Validate(member, param, allowNonPublicSetter);
        if (val is not Type t) {
            memberParser = null;
            return false;
        }
        memberParser = new(member, param, t);
        return true;
    }
    private static object Validate(MemberInfo member, ParamInfo param, bool allowNonPublicSetter) {
        bool isWriteable = false;
        Type? detectedMemberType = null;
        Type? detectedTargetType = null;

        switch (member) {
            case PropertyInfo prop:
                if (prop.GetAccessors(true)[0].IsStatic)
                    return new RinkuConfigurationException(ErrorCodes.UnusableMember, "Properties must be instance members");

                detectedMemberType = prop.PropertyType;
                detectedTargetType = prop.DeclaringType;
                isWriteable = prop.CanWrite && prop.GetSetMethod(nonPublic: allowNonPublicSetter) != null;
                break;

            case FieldInfo field:
                if (field.IsStatic)
                    return new RinkuConfigurationException(ErrorCodes.UnusableMember, "Fields must be instance members");

                detectedMemberType = field.FieldType;
                detectedTargetType = field.DeclaringType;
                isWriteable = !field.IsInitOnly && !field.IsLiteral;
                break;

            case MethodInfo method:
                if (method.ReturnType != typeof(void))
                    return new RinkuConfigurationException(ErrorCodes.UnusableMember, "A member setter method must return void");
                var parameters = method.GetParameters();
                if (method.IsStatic) {
                    if (parameters.Length != 2)
                        return new RinkuConfigurationException(ErrorCodes.UnusableMember, "A static setter takes 2 parameters (Instance, Value)");
                    detectedTargetType = parameters[0].ParameterType;
                    detectedMemberType = parameters[1].ParameterType;
                    if (method.IsGenericMethodDefinition) {
                        if (!detectedTargetType.IsGenericType)
                            return new RinkuConfigurationException(ErrorCodes.UnusableMember, "A generic setter takes its type parameters from the instance it writes to, so that instance has to be generic too");
                        Type[] methodGenericArgs = method.GetGenericArguments();
                        Type[] targetGenericArgs = detectedTargetType.GetGenericArguments();
                        if (methodGenericArgs.Length != targetGenericArgs.Length)
                            return new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Generic mismatch: Method has {methodGenericArgs.Length} type params, but Instance type has {targetGenericArgs.Length}");
                        for (int i = 0; i < methodGenericArgs.Length; i++)
                            if (methodGenericArgs[i] != targetGenericArgs[i])
                                return new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Generic mismatch: Method has {methodGenericArgs[i]} type param, but Instance type has {targetGenericArgs[i]}");
                    }
                    isWriteable = true;
                }
                else if (parameters.Length == 1) {
                    if (method.IsGenericMethodDefinition)
                        return new RinkuConfigurationException(ErrorCodes.UnusableMember, "An instance setter takes its type from the instance, so it must not be generic itself");
                    detectedTargetType = method.DeclaringType;
                    detectedMemberType = parameters[0].ParameterType;
                    isWriteable = true;
                }
                break;
        }
        if (detectedMemberType == null || detectedTargetType == null)
            return new RinkuConfigurationException(ErrorCodes.UnusableMember, "Member is not a supported writeable field, property, or method");
        if (!isWriteable)
            return new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Member '{member.Name}' is read-only or inaccessible");
        if (detectedMemberType != param.Type)
            return new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Type mismatch: Member expects {detectedMemberType.Name}, but Param provides {param.Type.Name}");

        return detectedTargetType;
    }
}
