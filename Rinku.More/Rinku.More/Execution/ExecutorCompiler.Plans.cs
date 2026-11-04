namespace Rinku;

internal static partial class ExecutorCompiler
{
    internal static ExecutorScope CloneScope(ExecutorScope source)
    {
        Dictionary<ExecutorScope, ExecutorScope> copies = [];
        return Copy(source);
        ExecutorScope Copy(ExecutorScope original)
        {
            if (copies.TryGetValue(original, out ExecutorScope? existing))
            {
                return existing;
            }
            ExecutorScope clone = new(original.Inputs.Select(input => input.LogicalType).ToArray());
            copies.Add(original, clone);
            for (int index = 0; index < original.Inputs.Count; index++)
            {
                PlanValue input = original.Inputs[index];
                if (input.IsVariable)
                {
                    clone.PublishValue(clone.Inputs[index], input.Name);
                }
            }
            clone.Output = ImportGraph(clone, original, clone.Inputs.Select(OperationArgument.FromValue).ToArray(), clone.Body, Copy);
            clone.MethodContracts.UnionWith(original.MethodContracts);
            foreach (var method in original.Methods)
            {
                clone.Methods.Add(method.Key, Copy(method.Value));
            }
            return clone;
        }
    }

    internal static Variable? ImportGraph(ExecutorScope target, ExecutorScope source, IReadOnlyList<OperationArgument> inputArguments, PlanSequence destination, Func<ExecutorScope, ExecutorScope>? scopes = null)
    {
        if (source.Inputs.Count != inputArguments.Count)
            throw new ArgumentException("The source input count does not match the supplied input values.", nameof(inputArguments));

        Dictionary<PlanValue, OperationArgument> values = [];
        for (int i = 0; i < source.Inputs.Count; i++)
        {
            values[source.Inputs[i]] = inputArguments[i];
        }
        Dictionary<PlanLocal, PlanLocal> locals = [];
        foreach (PlanSlot definition in source.Slots)
        {
            if (definition.IsInput)
                continue;
            PlanSlot slot = new(target, definition.LogicalType, definition.Kind)
            {
                Name = definition.Name,
                IsPublished = definition.IsPublished,
                DefaultWhenMissing = definition.DefaultWhenMissing
            };
            target.AddSlot(slot);
            if (definition.Value is PlanValue value)
            {
                PlanValue copy = new(slot);
                slot.Value = copy;
                values[value] = OperationArgument.FromValue(copy);
            }
            else if (definition.Local is PlanLocal local)
            {
                PlanLocal copy = new(slot, definition.Name);
                slot.Local = copy;
                locals[local] = copy;
            }
        }
        OperationCopyContext context = new(target, values, locals, scopes ?? (static scope => scope));
        foreach (PlanInstruction instruction in source.Body.Items)
        {
            destination.Add(instruction.Copy(context));
        }
        if (source.Output is not Variable output)
            return null;
        OperationArgument mapped = output.IsLocal ? OperationArgument.FromLocal(locals[output.Local]) : values[output.Value];
        if (!mapped.IsValue && !mapped.IsVariable)
        {
            PlanValue constant = target.CreateValue(mapped.Type);
            destination.Add(new CapturedValueOperation(mapped.Data, constant));
            return global::Rinku.Variable.Create(target, constant);
        }
        return mapped.IsValue ? global::Rinku.Variable.Create(target, mapped.Value) : global::Rinku.Variable.Create(target, mapped.Local);
    }

}
