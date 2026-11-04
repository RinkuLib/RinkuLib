using System.Reflection;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    internal enum Availability : byte
    {
        Unavailable, Available, KnownNull
    }
    internal readonly record struct VariableBinding(Type Type, Availability State, bool Bound, int Write = 0);

    internal readonly record struct FlowState(Dictionary<PlanSlot, VariableBinding> Variables, Dictionary<PlanSlot, OperationArgument> Constants, Dictionary<PlanSlot, (PlanSlot Slot, bool When, int Write)[]> Contracts, bool Reachable);

    internal sealed class LoopFlow
    {
        internal FlowState? Breaks;
        internal FlowState? Continues;
    }

    internal readonly ExecutorScope plan;
    internal ExecutorScope Plan => plan;
    internal ExecutorMethodPool methodPool;
    internal Dictionary<PlanSlot, VariableBinding> variables = [];
    internal readonly NullabilityInfoContext nullability = new();
    internal Dictionary<PlanSlot, (PlanSlot Slot, bool When, int Write)[]> conditionalNonNull = [];
    internal bool reachable = true;
    internal int nextWrite;
    internal readonly Stack<LoopFlow> loops = [];
    internal int loopBindings;
    internal Dictionary<PlanLocal, Type> LocalTypes { get; } = [];
    internal HashSet<Type> AccessTypes { get; } = [];
    internal Dictionary<PlanSlot, (string? Name, Type Type)> InstanceFields { get; } = [];
    internal Dictionary<Type, Type?> inputTypeBindings = [];

    internal PlanSequence Body { get; set; } = new();

    internal OperationBindingContext(ExecutorScope plan, ExecutorBuildConfigurationSnapshot registry, IReadOnlyList<Type> rootTypes)
        : this(plan, new ExecutorMethodPool(registry), rootTypes)
    {
    }

    internal OperationBindingContext(ExecutorScope plan, ExecutorMethodPool pool, IReadOnlyList<Type> rootTypes)
    {
        this.plan = plan;
        methodPool = pool;
        for (int i = 0; i < plan.Inputs.Count; i++)
        {
            BindInputType(plan.Inputs[i].LogicalType, rootTypes[i]);
            Define(plan.Inputs[i], rootTypes[i]);
        }
    }

    internal Type TypeOf(PlanValue value) => variables[value.Slot].Type;
    internal Type GetArgumentType(OperationArgument argument) => argument.IsValue ? GetSlotType(argument.Value.Slot) : argument.IsVariable ? GetSlotType(argument.Local.Slot) : argument.Type;
    internal Type GetSlotType(PlanSlot slot) => references.TryGetValue(slot, out OperationArgument reference) ? GetArgumentType(reference) : variables.TryGetValue(slot, out VariableBinding value) ? value.Type : slot.LogicalType;
    internal Availability State(PlanSlot slot) => references.TryGetValue(slot, out OperationArgument reference) ? State(reference) : variables.TryGetValue(slot, out VariableBinding value) ? value.State : Availability.Unavailable;
    internal Availability State(OperationArgument argument) => argument.IsValue ? State(argument.Value.Slot) : argument.IsVariable ? State(argument.Local.Slot) : argument.Data is null ? Availability.KnownNull : Availability.Available;

    internal void Define(PlanValue? value, Type type, Availability state = Availability.Available)
    {
        if (value is not null)
            Define(value.Slot, type, state);
    }

    internal void Define(PlanSlot slot, Type type, Availability state, bool publish = true)
    {
        bool declared = variables.TryGetValue(slot, out VariableBinding previous) && previous.Bound;
        if (declared && previous.Type != type)
            throw new ExecutorBindingException(ExecutorBindingFailure.TypeMismatch, $"Variable {slot.Name ?? slot.LogicalType.Name} was already bound to {previous.Type}; it cannot be rebound to {type}.");
        ValidatePlanType(type, "bound value");
        if (!type.IsVisible) AccessTypes.Add(type);
        variables[slot] = new(type, state, true, ++nextWrite);
        conditionalNonNull.Remove(slot);
        if (slot.Local is PlanLocal local)
            LocalTypes[local] = type;
        methodPool.Include(type);
        if (publish && !declared)
            Publish(slot);
    }

    internal PlanSequence BindBranch(PlanSequence sequence)
    {
        scopes.Push([]);
        try
        {
            return BindSequence(sequence);
        }
        finally
        {
            scopes.Pop();
        }
    }

    internal PlanSequence BindSequence(PlanSequence source)
    {
        PlanSequence result = new();
        foreach (PlanInstruction instruction in source.Items)
        {
            IPlanNode item = instruction.Node;
            if (!reachable)
            {
                break;
            }
            bool canFallback = instruction.Requirement != ExecutorRequirement.Required;
            FlowState? checkpoint = canFallback ? CaptureState() : null;
            var exits = canFallback ? loops.Select(loop => (Loop: loop, loop.Breaks, loop.Continues)).ToArray() : null;
            ExecutorMethodPool? pool = canFallback ? methodPool.Clone() : null;
            List<PlanSlot>? savedNames = canFallback ? new(scopes.Peek()) : null;
            Dictionary<PlanSlot, OperationArgument>? savedReferences = canFallback ? new(references) : null;
            try
            {
                IPlanNode? resolved = item.Resolve(this);
                if (resolved is not null)
                {
                    result.Add(resolved);
                }
            }
            catch (ExecutorBindingException) when (canFallback)
            {
                RestoreState(checkpoint!.Value);
                foreach (var exit in exits!)
                {
                    exit.Loop.Breaks = exit.Breaks;
                    exit.Loop.Continues = exit.Continues;
                }
                methodPool = pool!;
                scopes.Pop();
                scopes.Push(savedNames!);
                references.Clear();
                foreach (var reference in savedReferences!)
                {
                    references.Add(reference.Key, reference.Value);
                }
                HashSet<PlanSlot> exports = instruction.Exports.Select(value => value.Slot).ToHashSet();
                foreach (PlanSlot output in instruction.AllOutputs.Select(value => value.Slot).Distinct())
                {
                    if (variables.TryGetValue(output, out VariableBinding existing) && existing.Bound)
                    {
                        continue;
                    }
                    if (instruction.Requirement == ExecutorRequirement.Nullable)
                    {
                        Type type = GetSlotType(output);
                        if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        {
                            type = typeof(Nullable<>).MakeGenericType(type);
                        }
                        Define(output, type, Availability.KnownNull, exports.Contains(output));
                        result.Add(new NullOperation(output, type));
                    }
                    else
                    {
                        variables[output] = new(output.LogicalType, Availability.Unavailable, false);
                    }
                }
                foreach (PlanSlot output in exports)
                {
                    if (variables.TryGetValue(output, out var bound) && bound.Bound)
                    {
                        Publish(output);
                    }
                }
            }
        }
        return result;
    }

    internal void RestoreLocalTypes()
    {
        LocalTypes.Clear();
        foreach (var (slot, value) in variables)
        {
            if (slot.Local is PlanLocal local && value.Bound)
                LocalTypes.Add(local, value.Type);
        }
    }

    internal void EnsureAvailable(OperationArgument value)
    {
        if (value.Variable is Variable variable)
        {
            EnsureAvailable(variable.Slot);
        }
    }

    internal void EnsureAvailable(PlanSlot slot)
    {
        if (State(slot) == Availability.Unavailable)
            throw new ExecutorBindingException(ExecutorBindingFailure.UnavailableInput, $"Variable {slot.Name ?? slot.LogicalType.Name} has no available value.");
    }

    internal void EnsureNotNull(OperationArgument argument)
    {
        if (State(argument) == Availability.KnownNull)
            throw new ExecutorBindingException(ExecutorBindingFailure.NullNotAccepted, "This operation cannot consume a known null value.");
    }

}
