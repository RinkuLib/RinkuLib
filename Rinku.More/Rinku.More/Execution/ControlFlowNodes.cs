using static Rinku.OperationBindingContext;
using System.Reflection;
using System.Reflection.Emit;
using static Rinku.ExecutorCompiler;

namespace Rinku;

internal sealed class LogicalConditionNode(OperationArgument left, ExecutorLogicalOperator @operator, PlanSequence right, OperationArgument rightResult, PlanValue result) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Right;
        }
    }

    internal OperationArgument Left { get; set; } = left;
    internal ExecutorLogicalOperator Operator { get; set; } = @operator;
    internal PlanSequence Right { get; set; } = right;
    internal OperationArgument RightResult { get; set; } = rightResult;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new LogicalConditionNode(context.Argument(Left), Operator, context.Sequence(Right), context.Argument(RightResult), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Left]);
        Left = context.ResolveReferenceOperand(Left);
        RightResult = context.ResolveReferenceOperand(RightResult);
        context.EnsureNotNull(Left);
        FlowState beforeRight = context.CaptureState();
        context.ApplyCondition(Left, Operator == ExecutorLogicalOperator.AndAlso);
        PlanSequence logicalBody = context.BindBranch(Right);
        context.EnsureAvailable(RightResult);
        context.EnsureNotNull(RightResult);
        context.MergeState(beforeRight, context.CaptureState());
        context.Define(Result, typeof(bool));
        return new LogicalConditionNode(Left, Operator, logicalBody, context.ResolveReferenceOperand(RightResult), Result);
    }
    public override void Compile(OperationEmissionContext context)
    {
        LocalBuilder result = context.il.DeclareLocal(typeof(bool));
        Label shortCircuit = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.EmitArgument(Left, typeof(bool));
        context.il.Emit(Operator == ExecutorLogicalOperator.AndAlso ? OpCodes.Brfalse : OpCodes.Brtrue, shortCircuit);
        context.EmitSequence(Right, null, null, context.inExceptionBlock);
        context.EmitArgument(RightResult, typeof(bool));
        context.il.Emit(OpCodes.Stloc, result);
        context.il.Emit(OpCodes.Br, end);
        context.il.MarkLabel(shortCircuit);
        context.il.Emit(Operator == ExecutorLogicalOperator.AndAlso ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
        context.il.Emit(OpCodes.Stloc, result);
        context.il.MarkLabel(end);
        context.values[Result] = new(typeof(bool), result, 0);
    }
}

internal sealed class ConditionNode(OperationArgument condition, PlanSequence whenTrue, PlanSequence? whenFalse) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return WhenTrue;
            if (WhenFalse is not null)
            {
                yield return WhenFalse;
            }
        }
    }

    internal OperationArgument Condition { get; set; } = condition;
    internal PlanSequence WhenTrue { get; set; } = whenTrue;
    internal PlanSequence? WhenFalse { get; set; } = whenFalse;
    public override IPlanNode Copy(OperationCopyContext context) => new ConditionNode(context.Argument(Condition), context.Sequence(WhenTrue), WhenFalse is null ? null : context.Sequence(WhenFalse));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Condition]);
        Condition = context.ResolveReferenceOperand(Condition);

        context.EnsureNotNull(Condition);
        FlowState incoming = context.CaptureState();
        context.ApplyCondition(Condition, true);
        PlanSequence whenTrue = context.BindBranch(WhenTrue);
        FlowState trueState = context.CaptureState();
        context.RestoreState(incoming);
        context.ApplyCondition(Condition, false);
        PlanSequence? whenFalse = WhenFalse is null ? null : context.BindBranch(WhenFalse);
        context.MergeState(trueState, context.CaptureState());
        return new ConditionNode(Condition, whenTrue, whenFalse);
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitArgument(Condition, typeof(bool));
        Label whenFalse = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.il.Emit(OpCodes.Brfalse, whenFalse);
        context.EmitSequence(WhenTrue, context.breakLabel, context.continueLabel, context.inExceptionBlock);
        context.il.Emit(OpCodes.Br, end);
        context.il.MarkLabel(whenFalse);
        if (WhenFalse is not null)
            context.EmitSequence(WhenFalse, context.breakLabel, context.continueLabel, context.inExceptionBlock);
        context.il.MarkLabel(end);
    }
}

internal sealed class ForEachNode(OperationArgument source, PlanValue item, PlanSequence body) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Body;
        }
    }

    internal OperationArgument Source { get; set; } = source;
    internal PlanValue Item { get; set; } = item;
    internal PlanSequence Body { get; set; } = body;
    public override IPlanNode Copy(OperationCopyContext context) => new ForEachNode(context.Argument(Source), context.Value(Item)!, context.Sequence(Body));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Source]);
        Source = context.ResolveReferenceOperand(Source);
        context.loopBindings++;
        context.scopes.Push([]);
        try
        {

            context.EnsureNotNull(Source);
            FlowState incoming = context.CaptureState();
            context.Define(Item, GetEnumerableElementType(context.GetArgumentType(Source)) ?? Item.LogicalType);
            LoopFlow flow = new();
            PlanSequence body = context.BindLoopBody(Body, flow);
            context.MergeContinues(flow);
            context.MergeState(incoming, context.CaptureState());
            context.MergeBreaks(flow);
            return new ForEachNode(Source, Item, body);

        }
        finally
        {
            context.scopes.Pop();
            context.loopBindings--;
        }
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type sourceType = context.GetArgumentType(Source);
        Type elementType = context.binding.TypeOf(Item);
        MethodInfo getEnumerator = sourceType.GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public) ?? typeof(IEnumerable<>).MakeGenericType(elementType).GetMethod("GetEnumerator") ?? throw new InvalidOperationException($"Enumerable type {sourceType} has no GetEnumerator method.");
        Type enumeratorType = getEnumerator.ReturnType;
        LocalBuilder enumerator = context.il.DeclareLocal(enumeratorType);
        Type sourceReceiver = context.EmitInstanceArgument(Source, getEnumerator.DeclaringType ?? sourceType);
        context.EmitInvocation(getEnumerator, sourceReceiver);
        context.il.Emit(OpCodes.Stloc, enumerator);
        Label check = context.il.DefineLabel();
        Label continueLabel = context.il.DefineLabel();
        Label loopEnd = context.il.DefineLabel();
        Label afterFinally = context.il.DefineLabel();
        context.il.BeginExceptionBlock();
        context.il.MarkLabel(check);
        ValueLocation enumeratorValue = new(enumeratorType, enumerator, 0);
        MethodInfo moveNext = enumeratorType.GetMethod("MoveNext") ?? typeof(System.Collections.IEnumerator).GetMethod(nameof(System.Collections.IEnumerator.MoveNext)) ?? throw new InvalidOperationException($"Enumerator {enumeratorType} has no MoveNext method.");
        context.EmitInstanceMethodCall(enumeratorValue, moveNext);
        context.il.Emit(OpCodes.Brfalse, loopEnd);
        MethodInfo current = enumeratorType.GetProperty("Current")?.GetMethod ?? typeof(System.Collections.IEnumerator).GetProperty(nameof(System.Collections.IEnumerator.Current))?.GetMethod ?? throw new InvalidOperationException($"Enumerator {enumeratorType} has no Current method.");
        context.EmitInstanceMethodCall(enumeratorValue, current);
        Type actualItemType = elementType;
        LocalBuilder itemLocal = context.il.DeclareLocal(actualItemType);
        context.il.Emit(OpCodes.Stloc, itemLocal);
        context.values[Item] = new(actualItemType, itemLocal, 0);
        context.EmitSequence(Body, loopEnd, continueLabel, true);
        context.il.MarkLabel(continueLabel);
        context.il.Emit(OpCodes.Br, check);
        context.il.MarkLabel(loopEnd);
        context.il.Emit(OpCodes.Leave, afterFinally);
        context.il.BeginFinallyBlock();
        if (typeof(IDisposable).IsAssignableFrom(enumeratorType))
        {
            MethodInfo dispose = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose)) ?? throw new InvalidOperationException("IDisposable.Dispose is missing.");
            context.EmitInstanceMethodCall(enumeratorValue, dispose);
        }
        context.il.EndExceptionBlock();
        context.il.MarkLabel(afterFinally);
    }
}

internal sealed class WhileNode(PlanSequence condition, OperationArgument conditionResult, PlanSequence body) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Condition;
            yield return Body;
        }
    }

    internal PlanSequence Condition { get; set; } = condition;
    internal OperationArgument ConditionResult { get; set; } = conditionResult;
    internal PlanSequence Body { get; set; } = body;
    public override IPlanNode Copy(OperationCopyContext context) => new WhileNode(context.Sequence(Condition), context.Argument(ConditionResult), context.Sequence(Body));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        ConditionResult = context.ResolveReferenceOperand(ConditionResult);
        context.loopBindings++;
        context.scopes.Push([]);
        try
        {

            PlanSequence condition = context.BindSequence(Condition);
            context.EnsureAvailable(ConditionResult);
            context.EnsureNotNull(ConditionResult);
            FlowState incoming = context.CaptureState();
            context.ApplyCondition(ConditionResult, true);
            LoopFlow flow = new();
            PlanSequence body = context.BindLoopBody(Body, flow);
            context.MergeContinues(flow);
            context.MergeState(incoming, context.CaptureState());
            context.ApplyCondition(ConditionResult, false);
            context.MergeBreaks(flow);
            return new WhileNode(condition, context.ResolveReferenceOperand(ConditionResult), body);

        }
        finally
        {
            context.scopes.Pop();
            context.loopBindings--;
        }
    }
    public override void Compile(OperationEmissionContext context)
    {
        Label check = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.il.MarkLabel(check);
        context.EmitSequence(Condition, null, null, context.inExceptionBlock);
        context.EmitArgument(ConditionResult, typeof(bool));
        context.il.Emit(OpCodes.Brfalse, end);
        context.EmitSequence(Body, end, check, context.inExceptionBlock);
        context.il.Emit(OpCodes.Br, check);
        context.il.MarkLabel(end);
    }
}

internal sealed class DoWhileNode(PlanSequence body, PlanSequence condition, OperationArgument conditionResult) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Body;
            yield return Condition;
        }
    }

    internal PlanSequence Body { get; set; } = body;
    internal PlanSequence Condition { get; set; } = condition;
    internal OperationArgument ConditionResult { get; set; } = conditionResult;
    public override IPlanNode Copy(OperationCopyContext context) => new DoWhileNode(context.Sequence(Body), context.Sequence(Condition), context.Argument(ConditionResult));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        ConditionResult = context.ResolveReferenceOperand(ConditionResult);
        context.loopBindings++;
        context.scopes.Push([]);
        try
        {

            LoopFlow flow = new();
            PlanSequence body = context.BindLoopBody(Body, flow);
            context.MergeContinues(flow);
            PlanSequence condition = context.BindSequence(Condition);
            OperationArgument result = OperationArgument.Constant(false);
            if (context.reachable)
            {
                context.EnsureAvailable(ConditionResult);
                context.EnsureNotNull(ConditionResult);
                result = context.ResolveReferenceOperand(ConditionResult);
                context.ApplyCondition(result, false);
            }
            context.MergeBreaks(flow);
            return new DoWhileNode(body, condition, result);

        }
        finally
        {
            context.scopes.Pop();
            context.loopBindings--;
        }
    }
    public override void Compile(OperationEmissionContext context)
    {
        Label bodyStart = context.il.DefineLabel();
        Label condition = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.il.MarkLabel(bodyStart);
        context.EmitSequence(Body, end, condition, context.inExceptionBlock);
        context.il.MarkLabel(condition);
        context.EmitSequence(Condition, null, null, context.inExceptionBlock);
        context.EmitArgument(ConditionResult, typeof(bool));
        context.il.Emit(OpCodes.Brtrue, bodyStart);
        context.il.MarkLabel(end);
    }
}

internal sealed class ForNode(PlanSequence setup, PlanSequence condition, OperationArgument conditionResult, PlanSequence increment, PlanSequence body) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Setup;
            yield return Condition;
            yield return Increment;
            yield return Body;
        }
    }

    internal PlanSequence Setup { get; set; } = setup;
    internal PlanSequence Condition { get; set; } = condition;
    internal OperationArgument ConditionResult { get; set; } = conditionResult;
    internal PlanSequence Increment { get; set; } = increment;
    internal PlanSequence Body { get; set; } = body;
    public override IPlanNode Copy(OperationCopyContext context) => new ForNode(context.Sequence(Setup), context.Sequence(Condition), context.Argument(ConditionResult), context.Sequence(Increment), context.Sequence(Body));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        ConditionResult = context.ResolveReferenceOperand(ConditionResult);
        context.loopBindings++;
        context.scopes.Push([]);
        try
        {

            PlanSequence setup = context.BindSequence(Setup);
            if (!context.reachable)
                return new ScopeNode(setup);
            PlanSequence condition = context.BindSequence(Condition);
            context.EnsureAvailable(ConditionResult);
            context.EnsureNotNull(ConditionResult);
            FlowState incoming = context.CaptureState();
            context.ApplyCondition(ConditionResult, true);
            LoopFlow flow = new();
            PlanSequence body = context.BindLoopBody(Body, flow);
            context.MergeContinues(flow);
            PlanSequence increment = context.BindSequence(Increment);
            context.MergeState(incoming, context.CaptureState());
            context.ApplyCondition(ConditionResult, false);
            context.MergeBreaks(flow);
            return new ForNode(setup, condition, context.ResolveReferenceOperand(ConditionResult), increment, body);

        }
        finally
        {
            context.scopes.Pop();
            context.loopBindings--;
        }
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitSequence(Setup, null, null, context.inExceptionBlock);
        Label check = context.il.DefineLabel();
        Label increment = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.il.MarkLabel(check);
        context.EmitSequence(Condition, null, null, context.inExceptionBlock);
        context.EmitArgument(ConditionResult, typeof(bool));
        context.il.Emit(OpCodes.Brfalse, end);
        context.EmitSequence(Body, end, increment, context.inExceptionBlock);
        context.il.MarkLabel(increment);
        context.EmitSequence(Increment, null, null, context.inExceptionBlock);
        context.il.Emit(OpCodes.Br, check);
        context.il.MarkLabel(end);
    }
}

internal sealed class ConditionalValueNode(OperationArgument condition, PlanSequence whenTrue, OperationArgument trueResult, PlanSequence whenFalse, OperationArgument falseResult, PlanValue result) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return WhenTrue;
            yield return WhenFalse;
        }
    }

    internal OperationArgument Condition { get; set; } = condition;
    internal PlanSequence WhenTrue { get; set; } = whenTrue;
    internal OperationArgument TrueResult { get; set; } = trueResult;
    internal PlanSequence WhenFalse { get; set; } = whenFalse;
    internal OperationArgument FalseResult { get; set; } = falseResult;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new ConditionalValueNode(context.Argument(Condition), context.Sequence(WhenTrue), context.Argument(TrueResult), context.Sequence(WhenFalse), context.Argument(FalseResult), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Condition]);
        Condition = context.ResolveReferenceOperand(Condition);
        TrueResult = context.ResolveReferenceOperand(TrueResult);
        FalseResult = context.ResolveReferenceOperand(FalseResult);
        context.EnsureNotNull(Condition);
        FlowState beforeBranches = context.CaptureState();
        context.ApplyCondition(Condition, true);
        PlanSequence whenTrue = context.BindBranch(WhenTrue);
        context.EnsureAvailable(TrueResult);
        Availability trueResult = context.State(TrueResult);
        FlowState trueState = context.CaptureState();
        context.RestoreState(beforeBranches);
        context.ApplyCondition(Condition, false);
        PlanSequence whenFalse = context.BindBranch(WhenFalse);
        context.EnsureAvailable(FalseResult);
        Availability falseResult = context.State(FalseResult);
        context.MergeState(trueState, context.CaptureState());
        context.Define(Result, context.SpecializeType(Result.LogicalType), trueResult == Availability.KnownNull && falseResult == Availability.KnownNull ? Availability.KnownNull : Availability.Available);
        return new ConditionalValueNode(Condition, whenTrue, context.ResolveReferenceOperand(TrueResult), whenFalse, context.ResolveReferenceOperand(FalseResult), Result);
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type resultType = context.plan.GetValueType(Result);
        LocalBuilder result = context.il.DeclareLocal(resultType);
        Label whenFalse = context.il.DefineLabel();
        Label end = context.il.DefineLabel();
        context.EmitArgument(Condition, typeof(bool));
        context.il.Emit(OpCodes.Brfalse, whenFalse);
        context.EmitSequence(WhenTrue, null, null, context.inExceptionBlock);
        context.EmitArgument(TrueResult, resultType);
        context.il.Emit(OpCodes.Stloc, result);
        context.il.Emit(OpCodes.Br, end);
        context.il.MarkLabel(whenFalse);
        context.EmitSequence(WhenFalse, null, null, context.inExceptionBlock);
        context.EmitArgument(FalseResult, resultType);
        context.il.Emit(OpCodes.Stloc, result);
        context.il.MarkLabel(end);
        context.values[Result] = new(resultType, result, 0);
    }
}

internal sealed class BreakOperation() : Operation
{
    public override IPlanNode Copy(OperationCopyContext context) => new BreakOperation();
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RecordLoopExit(false);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        if (context.breakLabel is not Label currentBreak)
        {
            throw new InvalidOperationException("Break was emitted outside a loop.");
        }
        context.il.Emit(OpCodes.Br, currentBreak);
    }
}

internal sealed class ContinueOperation() : Operation
{
    public override IPlanNode Copy(OperationCopyContext context) => new ContinueOperation();
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RecordLoopExit(true);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        if (context.continueLabel is not Label currentContinue)
        {
            throw new InvalidOperationException("Continue was emitted outside a loop.");
        }
        context.il.Emit(OpCodes.Br, currentContinue);
    }
}

internal sealed class ReturnOperation() : Operation
{
    public override IPlanNode Copy(OperationCopyContext context) => new ReturnOperation();
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.ValidateResults();
        context.reachable = false;
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.il.Emit(context.inExceptionBlock ? OpCodes.Leave : OpCodes.Br, context.returnLabel);
    }
}

