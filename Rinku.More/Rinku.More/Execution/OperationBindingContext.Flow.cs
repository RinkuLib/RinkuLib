using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    internal FlowState CaptureState() => new(new(variables), new(constants), new(conditionalNonNull), reachable);

    internal void RestoreState(FlowState state) => AdoptState(new(new(state.Variables), new(state.Constants), new(state.Contracts), state.Reachable));

    internal void AdoptState(FlowState state)
    {
        variables = state.Variables;
        constants = state.Constants;
        conditionalNonNull = state.Contracts;
        reachable = state.Reachable;
        RestoreLocalTypes();
    }

    internal void MergeState(FlowState left, FlowState right) => AdoptState(MergeFlows(left, right));

    internal FlowState MergeFlows(FlowState left, FlowState right)
    {
        Dictionary<PlanSlot, VariableBinding> merged = [];
        foreach (PlanSlot slot in left.Variables.Keys.Union(right.Variables.Keys))
        {
            bool hasLeft = left.Variables.TryGetValue(slot, out VariableBinding first);
            bool hasRight = right.Variables.TryGetValue(slot, out VariableBinding second);
            if (hasLeft && hasRight && first.Bound && second.Bound && first.Type != second.Type)
                throw new ExecutorBindingException(ExecutorBindingFailure.TypeMismatch, $"Variable {slot.Name ?? slot.LogicalType.Name} has incompatible branch types {first.Type} and {second.Type}.");
            VariableBinding binding = hasLeft && first.Bound ? first : second;
            Availability state = !left.Reachable ? right.Reachable && hasRight ? second.State : Availability.Unavailable
                : !right.Reachable ? hasLeft ? first.State : Availability.Unavailable
                : !hasLeft || !hasRight || first.State == Availability.Unavailable || second.State == Availability.Unavailable ? Availability.Unavailable
                : first.State == Availability.KnownNull && second.State == Availability.KnownNull ? Availability.KnownNull : Availability.Available;
            int write = !left.Reachable && right.Reachable && hasRight ? second.Write
                : !right.Reachable && left.Reachable && hasLeft ? first.Write
                : hasLeft && hasRight && first.Write == second.Write ? first.Write : ++nextWrite;
            merged.Add(slot, binding with
            {
                State = state,
                Write = write
            });
        }
        return new(merged, MergeFacts(left.Constants, right.Constants, left.Reachable, right.Reachable), MergeFacts(left.Contracts, right.Contracts, left.Reachable, right.Reachable), left.Reachable || right.Reachable);
    }

    internal static Dictionary<PlanSlot, T> MergeFacts<T>(Dictionary<PlanSlot, T> left, Dictionary<PlanSlot, T> right, bool leftReachable, bool rightReachable)
    {
        if (!leftReachable)
            return rightReachable ? new(right) : [];
        if (!rightReachable)
            return new(left);
        Dictionary<PlanSlot, T> common = [];
        foreach (var (slot, value) in left)
        {
            if (right.TryGetValue(slot, out T? other) && EqualityComparer<T>.Default.Equals(value, other))
                common.Add(slot, value);
        }
        return common;
    }

    internal void RecordLoopExit(bool continuing)
    {
        LoopFlow loop = loops.Peek();
        FlowState state = CaptureState();
        if (continuing)
            loop.Continues = loop.Continues is FlowState previous ? MergeFlows(previous, state) : state;
        else
            loop.Breaks = loop.Breaks is FlowState previous ? MergeFlows(previous, state) : state;
        reachable = false;
    }

    internal PlanSequence BindLoopBody(PlanSequence source, LoopFlow flow)
    {
        loops.Push(flow);
        try
        {
            return BindSequence(source);
        }
        finally
        {
            loops.Pop();
        }
    }

    internal void MergeContinues(LoopFlow flow)
    {
        if (flow.Continues is FlowState continuing)
            MergeState(CaptureState(), continuing);
    }

    internal void MergeBreaks(LoopFlow flow)
    {
        if (flow.Breaks is FlowState exiting)
            MergeState(CaptureState(), exiting);
    }

}
