using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    internal bool AcceptsNull(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        if (parameter.IsDefined(typeof(DisallowNullAttribute)))
            return false;
        if (parameter.IsDefined(typeof(AllowNullAttribute)) || Nullable.GetUnderlyingType(type) is not null)
            return true;
        return !type.IsValueType && nullability.Create(parameter).WriteState == NullabilityState.Nullable;
    }

    internal bool AcceptsNull(FieldInfo field)
    {
        if (field.IsDefined(typeof(DisallowNullAttribute)))
            return false;
        if (field.IsDefined(typeof(AllowNullAttribute)) || Nullable.GetUnderlyingType(field.FieldType) is not null)
            return true;
        return !field.FieldType.IsValueType && nullability.Create(field).WriteState == NullabilityState.Nullable;
    }

    internal void ValidateParameters(MethodBase method, OperationArgument[] operands)
    {
        ParameterInfo[] parameters = method.GetParameters();
        int offset = method is MethodInfo { IsStatic: false } ? 1 : 0;
        if (offset != 0 && State(operands[0]) == Availability.KnownNull && method.DeclaringType?.IsValueType != true)
            throw new ExecutorBindingException(ExecutorBindingFailure.NullNotAccepted, $"Instance method {method} cannot receive a known null receiver.");
        for (int index = 0; index < parameters.Length; index++)
        {
            if (!parameters[index].IsOut && State(operands[index + offset]) == Availability.KnownNull && !AcceptsNull(parameters[index]))
                throw new ExecutorBindingException(ExecutorBindingFailure.NullNotAccepted, $"Parameter {parameters[index].Name} of {method} does not accept a known null value.");
        }
    }

    internal void ApplyParameterContracts(MethodBase method, OperationArgument[] operands, PlanValue? result = null)
    {
        ParameterInfo[] parameters = method.GetParameters();
        int offset = method is MethodInfo { IsStatic: false } ? 1 : 0;
        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            if (parameter.ParameterType.IsByRef && !parameter.IsIn && operands[index + offset].Variable is Variable variable)
            {
                constants.Remove(variable.Slot);
                Define(variable.Slot, parameter.ParameterType.GetElementType()!, Availability.Available);
            }
        }
        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            if (operands[index + offset].Variable is not Variable variable)
                continue;
            if (parameter.IsDefined(typeof(NotNullAttribute)))
                variables[variable.Slot] = variables[variable.Slot] with
                {
                    State = Availability.Available
                };
            if (result is not null && method is MethodInfo { ReturnType: var returnType } && returnType == typeof(bool) && parameter.GetCustomAttribute<NotNullWhenAttribute>() is { } condition)
            {
                conditionalNonNull.TryGetValue(result.Slot, out var contracts);
                conditionalNonNull[result.Slot] = [.. contracts ?? [], (variable.Slot, condition.ReturnValue, variables[variable.Slot].Write)];
            }
        }
    }

    internal void StoreSlot(PlanSlot slot, OperationArgument source)
    {
        bool fixedValue = TryGetConstant(source, out OperationArgument constant);
        constants.Remove(slot);
        Availability state = State(source);
        Type type = variables.TryGetValue(slot, out VariableBinding previous) && previous.Bound ? previous.Type
            : state == Availability.KnownNull ? slot.LogicalType
            : fixedValue ? constant.Data?.GetType() is { } actual && !slot.LogicalType.IsValueType ? actual : slot.LogicalType
            : GetArgumentType(source);
        if (state == Availability.KnownNull && type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            throw new ExecutorBindingException(ExecutorBindingFailure.NullNotAccepted, $"Variable {slot.Name ?? slot.LogicalType.Name} cannot store null as {type}.");
        var contracts = source.Variable is Variable variable && conditionalNonNull.TryGetValue(variable.Slot, out var facts) ? facts : null;
        Define(slot, type, state);
        if (fixedValue && (constant.Data is null || type.IsInstanceOfType(constant.Data)))
            constants[slot] = OperationArgument.FromTypedConstant(type, constant.Data);
        if (contracts is not null)
            conditionalNonNull[slot] = contracts;
    }

    internal void CopyContracts(PlanSlot source, PlanSlot destination)
    {
        if (conditionalNonNull.TryGetValue(source, out var contracts))
            conditionalNonNull[destination] = contracts;
    }

    internal void ApplyCondition(OperationArgument condition, bool branch)
    {
        if (condition.Variable is not Variable variable || !conditionalNonNull.TryGetValue(variable.Slot, out var contracts))
            return;
        foreach (var contract in contracts)
        {
            if (contract.When == branch && variables.TryGetValue(contract.Slot, out VariableBinding current) && current.Write == contract.Write)
                variables[contract.Slot] = variables[contract.Slot] with
                {
                    State = Availability.Available
                };
        }
    }
}
