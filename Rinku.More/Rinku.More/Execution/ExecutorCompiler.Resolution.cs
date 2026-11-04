using System.Reflection;

namespace Rinku;

internal static partial class ExecutorCompiler
{
    internal static bool IsUnsigned(Type type)
    {
        Type actualType = Nullable.GetUnderlyingType(type) ?? type;
        return actualType == typeof(byte) || actualType == typeof(ushort) || actualType == typeof(uint) || actualType == typeof(ulong) || actualType == typeof(char);
    }

    internal static bool CanBoxNullableTo(Type source, Type target)
    {
        Type? underlying = Nullable.GetUnderlyingType(source);
        return underlying is not null && !target.IsValueType && (target == typeof(object) || target == typeof(ValueType) || target.IsAssignableFrom(underlying));
    }

    internal static bool IsNumeric(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(char) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    internal static bool IsNullableCompatible(Type expected, Type actual)
    {
        Type? expectedUnderlying = Nullable.GetUnderlyingType(expected);
        Type? actualUnderlying = Nullable.GetUnderlyingType(actual);
        if (expectedUnderlying is null)
            return false;
        if (actualUnderlying is null)
            return expectedUnderlying == actual || IsNumeric(expectedUnderlying) && IsNumeric(actual);
        return expectedUnderlying == actualUnderlying || IsNumeric(expectedUnderlying) && IsNumeric(actualUnderlying);
    }

    internal static Type GetNumericPromotionType(Type left, Type right)
    {
        if (left == typeof(decimal) || right == typeof(decimal))
        {
            if (left == typeof(float) || left == typeof(double) || right == typeof(float) || right == typeof(double))
                throw new InvalidOperationException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(decimal);
        }
        if (left == typeof(double) || right == typeof(double))
            return typeof(double);
        if (left == typeof(float) || right == typeof(float))
            return typeof(float);
        if (left == typeof(ulong) || right == typeof(ulong))
        {
            Type other = left == typeof(ulong) ? right : left;
            if (other == typeof(sbyte) || other == typeof(byte) || other == typeof(short) || other == typeof(ushort) || other == typeof(int) || other == typeof(long))
                throw new InvalidOperationException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(ulong);
        }
        if (left == typeof(long) || right == typeof(long))
        {
            if (left == typeof(uint) || right == typeof(uint))
                throw new InvalidOperationException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(long);
        }
        if (left == typeof(uint) || right == typeof(uint))
        {
            Type other = left == typeof(uint) ? right : left;
            return other == typeof(sbyte) || other == typeof(short) || other == typeof(int) ? typeof(long) : typeof(uint);
        }
        return typeof(int);
    }

    internal static Type GetBitwiseResultType(Type left, Type right)
    {
        if (left == typeof(bool) && right == typeof(bool) || left.IsEnum && left == right)
            return left;
        return GetNumericPromotionType(left, right);
    }

    internal static Type GetShiftResultType(Type left)
        => left == typeof(sbyte) || left == typeof(byte) || left == typeof(short) || left == typeof(ushort) || left == typeof(char) ? typeof(int) : left;

    internal static Type GetComparisonType(Type left, Type right)
    {
        if (left == right)
            return left.IsValueType && !left.IsPrimitive && !left.IsEnum ? typeof(object) : left;
        if (left.IsAssignableFrom(right))
            return left;
        if (right.IsAssignableFrom(left))
            return right;
        return typeof(object);
    }

    internal static void InferGenericArguments(Type parameterType, Type actualType, Dictionary<Type, Type?> inferred)
    {
        if (parameterType.IsByRef)
        {
            InferGenericArguments(parameterType.GetElementType() ?? parameterType, actualType, inferred);
            return;
        }
        if (parameterType.IsGenericParameter)
        {
            if (!inferred.TryGetValue(parameterType, out Type? previous))
                inferred[parameterType] = actualType;
            else if (previous is not null && previous != actualType)
            {
                if (HasImplicitConversion(previous, actualType))
                    inferred[parameterType] = actualType;
                else if (!HasImplicitConversion(actualType, previous))
                    inferred[parameterType] = null;
            }
            return;
        }
        if (parameterType.IsArray)
        {
            if (actualType.IsArray && parameterType.GetArrayRank() == actualType.GetArrayRank())
                InferGenericArguments(parameterType.GetElementType() ?? parameterType, actualType.GetElementType() ?? actualType, inferred);
            return;
        }
        if (Nullable.GetUnderlyingType(parameterType) is Type underlying && actualType.IsValueType)
        {
            InferGenericArguments(underlying, Nullable.GetUnderlyingType(actualType) ?? actualType, inferred);
            return;
        }
        if (!parameterType.IsGenericType)
            return;
        Type parameterDefinition = parameterType.GetGenericTypeDefinition();
        if (!TryGetGenericShape(actualType, parameterDefinition, out Type? actualShape) || actualShape is null)
            return;
        Type[] parameterArguments = parameterType.GetGenericArguments();
        Type[] actualArguments = actualShape.GetGenericArguments();
        for (int i = 0; i < parameterArguments.Length; i++)
            InferGenericArguments(parameterArguments[i], actualArguments[i], inferred);
    }

    internal static bool TryGetGenericShape(Type actualType, Type genericDefinition, out Type? shape)
    {
        if (actualType.IsGenericType && actualType.GetGenericTypeDefinition() == genericDefinition)
        {
            shape = actualType;
            return true;
        }
        shape = null;
        foreach (Type implemented in actualType.GetInterfaces())
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == genericDefinition)
            {
                if (shape is not null && shape != implemented)
                {
                    shape = null;
                    return false;
                }
                shape = implemented;
            }
        Type? baseType = actualType.BaseType;
        while (baseType is not null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == genericDefinition)
            {
                shape = baseType;
                return true;
            }
            baseType = baseType.BaseType;
        }
        return shape is not null;
    }

    internal static bool IsMoreSpecific(ExecutorCallCandidate leftCall, IReadOnlyList<int> left, ExecutorCallCandidate rightCall, IReadOnlyList<int> right, bool preferGeneric = false)
    {
        bool better = false;
        MethodBase? leftMethod = leftCall.Member as MethodBase;
        MethodBase? rightMethod = rightCall.Member as MethodBase;
        if (leftMethod is MethodInfo { IsStatic: false, DeclaringType.IsInterface: false } && rightMethod is MethodInfo { IsStatic: false, DeclaringType.IsInterface: true } && leftMethod.Name == rightMethod.Name && left.SequenceEqual(right) && leftCall.Inputs.AsSpan(1).SequenceEqual(rightCall.Inputs.AsSpan(1)))
            return true;
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] > right[i])
                return false;
            if (left[i] < right[i])
            {
                better = true;
                continue;
            }
            Type leftType = leftCall.Inputs[i];
            Type rightType = rightCall.Inputs[i];
            if (leftType == rightType)
                continue;
            if (!HasImplicitConversion(leftType, rightType) || HasImplicitConversion(rightType, leftType))
                return false;
            better = true;
        }
        if (better)
            return true;
        if ((leftMethod?.IsGenericMethod == true) != (rightMethod?.IsGenericMethod == true))
            return preferGeneric ? leftMethod?.IsGenericMethod == true : leftMethod?.IsGenericMethod != true;
        if (leftMethod?.IsGenericMethod != true || rightMethod?.IsGenericMethod != true)
            return false;
        MethodInfo leftDefinition = ((MethodInfo)leftMethod).GetGenericMethodDefinition();
        MethodInfo rightDefinition = ((MethodInfo)rightMethod).GetGenericMethodDefinition();
        ParameterInfo[] leftPattern = leftDefinition.GetParameters();
        ParameterInfo[] rightPattern = rightDefinition.GetParameters();
        for (int i = 0; i < left.Count; i++)
        {
            Type leftType = leftMethod is MethodInfo { IsStatic: false } && i == 0 ? leftDefinition.DeclaringType! : leftPattern[i - (leftMethod is MethodInfo { IsStatic: false } ? 1 : 0)].ParameterType;
            Type rightType = rightMethod is MethodInfo { IsStatic: false } && i == 0 ? rightDefinition.DeclaringType! : rightPattern[i - (rightMethod is MethodInfo { IsStatic: false } ? 1 : 0)].ParameterType;
            if (!IsAtLeastAsSpecific(leftType, rightType))
                return false;
            better |= !IsAtLeastAsSpecific(rightType, leftType);
        }
        return better;
    }

    internal static bool IsAtLeastAsSpecific(Type left, Type right)
    {
        if (left == right || right.IsGenericParameter)
            return true;
        if (left.IsGenericParameter)
            return false;
        if (left.IsByRef && right.IsByRef || left.IsArray && right.IsArray && left.GetArrayRank() == right.GetArrayRank())
            return IsAtLeastAsSpecific(left.GetElementType()!, right.GetElementType()!);
        if (!left.IsGenericType || !right.IsGenericType || left.GetGenericTypeDefinition() != right.GetGenericTypeDefinition())
            return false;
        Type[] leftArguments = left.GetGenericArguments();
        Type[] rightArguments = right.GetGenericArguments();
        for (int i = 0; i < leftArguments.Length; i++)
        {
            if (!IsAtLeastAsSpecific(leftArguments[i], rightArguments[i]))
                return false;
        }
        return true;
    }

    internal static bool HasImplicitConversion(Type source, Type target) => HasStandardImplicitConversion(source, target) || FindImplicitOperator(source, target) is not null;

    internal static bool HasStandardImplicitConversion(Type source, Type target)
    {
        if (source == target || target.IsAssignableFrom(source))
            return true;
        Type? targetUnderlying = Nullable.GetUnderlyingType(target);
        if (targetUnderlying is not null)
            return HasStandardImplicitConversion(Nullable.GetUnderlyingType(source) ?? source, targetUnderlying);
        if (Nullable.GetUnderlyingType(source) is not null || !IsNumeric(source) || !IsNumeric(target))
            return false;
        TypeCode destination = Type.GetTypeCode(target);
        return Type.GetTypeCode(source) switch
        {
            TypeCode.SByte => destination is TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.Byte => destination is TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.Int16 => destination is TypeCode.Int32 or TypeCode.Int64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.UInt16 or TypeCode.Char => destination is TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal || source == typeof(char) && target == typeof(ushort),
            TypeCode.Int32 => destination is TypeCode.Int64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.UInt32 => destination is TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.Int64 or TypeCode.UInt64 => destination is TypeCode.Single or TypeCode.Double or TypeCode.Decimal,
            TypeCode.Single => destination == TypeCode.Double,
            _ => false
        };
    }

    internal static bool IsExpressionContract(Type type) => typeof(IExpressionContract).IsAssignableFrom(type);

    internal static bool ContainsExpressionContract(Type type) => IsExpressionContract(type) || type.IsArray && ContainsExpressionContract(type.GetElementType() ?? type) || type.IsGenericType && type.GetGenericArguments().Any(ContainsExpressionContract);

    internal static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];
        return type.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

}
