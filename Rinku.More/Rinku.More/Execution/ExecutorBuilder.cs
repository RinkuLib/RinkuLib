using System.Linq.Expressions;
using System.Reflection;

namespace Rinku;

/// <summary>Builds an editable executor plan for one or more ordered inputs.</summary>

public partial class ExecutorBuilder
{
    internal ExecutorScope Data
    {
        get;
    }
    public ExecutorScope ToScope() => Data;
    private PlanSequence _sequence;
    private int _loopDepth;
    private bool _loopControlsAllowed;

    private readonly Stack<TypeTestMatch> _activeTypeMatches = [];

    /// <summary>Creates an empty executor builder.</summary>
    public ExecutorBuilder(params Type[] authoringInputTypes)
    {
        Data = new(authoringInputTypes);
        _sequence = Data.Body;
    }

    public ExecutorBuilder(ExecutorScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Data = scope;
        _sequence = Data.Body;
    }

    /// <summary>Gets the number of direct operations in the current recording sequence.</summary>
    public int Count => _sequence.Items.Count;

    /// <summary>Groups operations that must all bind before any operation in the group is emitted.</summary>
    /// <remarks>Rejection preserves existing variables. Group-local names remain private.</remarks>
    public ExecutorBuilder Scope(Action<ExecutorBuilder> operations, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        PlanSequence outer = _sequence;
        PlanSequence body = new();
        _sequence = body;
        try
        {
            operations(this);
            outer.Add(new ScopeNode(body), requirement);
        }
        catch
        {
            _sequence = outer;
            Rollback(checkpoint);
            throw;
        }
        finally
        {
            _sequence = outer;
        }
        return this;
    }

    /// <summary>Gets an ordered input value by index and optionally names it.</summary>
    public Variable<T> Input<T>(int index, string? name = null)
    {
        if (index < 0 || index >= Data.Inputs.Count || Data.Inputs[index].LogicalType != typeof(T))
            throw new ArgumentException($"Input {index} is not of type {typeof(T)}.", nameof(index));
        PlanValue value = Data.Inputs[index];
        Data.PublishValue(value, name);
        return new(Data, value);
    }

    public Variable Input(int index, string? name = null) {
        if ((uint)index >= (uint)Data.Inputs.Count) throw new ArgumentOutOfRangeException(nameof(index));
        PlanValue value = Data.Inputs[index];
        Data.PublishValue(value, name);
        return global::Rinku.Variable.Create(Data, value);
    }

    /// <summary>Gets a named or unnamed variable of the requested logical type.</summary>
    public bool TryGetVariable<T>(out Variable<T>? variable) => TryGetVariable(null, out variable);

    /// <summary>Gets a variable by its logical type and name.</summary>
    public bool TryGetVariable<T>(string? name, out Variable<T>? variable)
    {
        PlanSlot? match = null;
        foreach (PlanSlot slot in Data.Slots)
        {
            if (!slot.IsPublished || slot.LogicalType != typeof(T) || !string.Equals(slot.Name, name, StringComparison.Ordinal))
                continue;
            if (match is not null)
                throw new InvalidOperationException($"More than one authored variable matches {typeof(T)} and '{name}'. Use its variable reference directly.");
            match = slot;
        }
        if (match is not null)
        {
            variable = match.Value is PlanValue value ? new(Data, value) : new(Data, match.Local!);
            return true;
        }
        variable = null;
        return false;
    }
    /// <summary>Enumerates plan items in structural order.</summary>
    public IEnumerable<ExecutorNodeInfo> Nodes => EnumerateNodes(this, Data.Body, []);

    /// <summary>Adds a side-effect operation from reflection metadata.</summary>
    public ExecutorBuilder Then(MethodBase method, IReadOnlyList<object?> operands, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        AddCall(_sequence, method, operands, null, requirement);
        return this;
    }

    /// <summary>Evaluates a reflected method and stores its result in a new variable.</summary>
    public ExecutorBuilder Then<TResult>(out Variable<TResult> destination, MethodBase method, IReadOnlyList<object?> operands, string? name = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        PlanValue value = AddCall(_sequence, method, operands, typeof(TResult), requirement);
        Data.PublishValue(value, name);
        destination = new(Data, value);
        return this;
    }

    /// <summary>Evaluates a reflected method and stores its result in an existing variable.</summary>
    public ExecutorBuilder Then<TResult>(Variable<TResult> destination, MethodBase method, IReadOnlyList<object?> operands, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint recording = CaptureCheckpoint();
        try
        {
            ValidateValue(destination);
            PlanValue value = AddCall(method, operands, typeof(TResult));
            Store(destination, OperationArgument.FromValue(value));
            CompleteRecording(recording, requirement, destination.Slot);
            return this;
        }
        catch
        {
            Rollback(recording);
            throw;
        }
    }

    /// <summary>Reads a reflected member and stores its result in a new variable.</summary>
    public ExecutorBuilder Then<TResult>(out Variable<TResult> destination, MemberInfo member, IReadOnlyList<object?> operands, string? name = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        OperationArgument[] planOperands = ToOperationArguments(operands, MemberOperandTypes(member));
        PlanValue value = AddMemberRead(_sequence, member, planOperands, typeof(TResult), requirement);
        Data.PublishValue(value, name);
        destination = new(Data, value);
        return this;
    }

    /// <summary>Reads a reflected member and stores its result in an existing variable.</summary>
    public ExecutorBuilder Then<TResult>(Variable<TResult> destination, MemberInfo member, IReadOnlyList<object?> operands, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint recording = CaptureCheckpoint();
        try
        {
            ValidateValue(destination);
            OperationArgument[] planOperands = ToOperationArguments(operands, MemberOperandTypes(member));
            PlanValue value = AddMemberRead(member, planOperands, typeof(TResult));
            Store(destination, OperationArgument.FromValue(value));
            CompleteRecording(recording, requirement, destination.Slot);
            return this;
        }
        catch
        {
            Rollback(recording);
            throw;
        }
    }
    /// <summary>Adds a parameterless expression action.</summary>
    internal ExecutorBuilder RecordAction(Expression<Action> expression, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        AddExpressionAction(expression, [], requirement);
        return this;
    }

    /// <summary>Adds an expression action with plan-value arguments for an extension-defined arity.</summary>
    internal ExecutorBuilder ThenExpression(LambdaExpression expression, params Variable[] arguments)
    {
        AddExpressionAction(expression, arguments);
        return this;
    }

    internal ExecutorBuilder Then<TResult>(LambdaExpression expression, IReadOnlyList<Variable> arguments, out Variable<TResult> result, string? name = null)
    {
        result = AddExpressionValue<TResult>(expression, arguments, name);
        return this;
    }

    internal ExecutorBuilder Then<TResult>(LambdaExpression target, IReadOnlyList<Variable> arguments, Variable<TResult> value)
    {
        AddExpressionAssignment(target, arguments, value);
        return this;
    }

    internal ExecutorBuilder RecordValue<TResult>(Expression<Func<TResult>> expression, Variable<TResult> result, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateValue(result);
        ReadExpressionValue(expression, [], typeof(TResult), result, requirement);
        return this;
    }

    /// <summary>Adds a member assignment from reflection metadata.</summary>
    public ExecutorBuilder Then(MemberInfo member, object? value, IReadOnlyList<object?> operands, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        OperationArgument[] planOperands = ToOperationArguments(operands, MemberOperandTypes(member));
        AddAssignment(_sequence, member, planOperands, ToOperationArgument(value, GetMemberType(member)), requirement);
        return this;
    }

    /// <summary>Adds a parameterless member assignment.</summary>
    internal ExecutorBuilder RecordAssignment<TDestination>(Expression<Func<TDestination>> target, object? value, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        AddExpressionAssignment(target, [], value, requirement);
        return this;
    }

    internal ExecutorBuilder RecordAssignment<TDestination, TValue>(Expression<Func<TDestination>> target, Expression<Func<TValue>> value, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        AddExpressionAssignment(target, value, requirement);
        return this;
    }

    /// <summary>Creates a mutable variable slot.</summary>
    internal Variable<T> Variable<T>(string? name = null) => new(Data, Data.CreateLocal(typeof(T), name));

    /// <summary>Evaluates a captured expression and stores its result in a new variable.</summary>
    public ExecutorBuilder Then<T>(out Variable<T> destination, Expression<Func<T>> expression, string? name = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        destination = AddExpressionValue<T>(expression, [], name, requirement);
        return this;
    }

    /// <summary>Declares an uninitialized mutable variable.</summary>
    public ExecutorBuilder Declare<T>(out Variable<T> destination, string? name = null)
    {
        destination = Variable<T>(name);
        Data.Add(_sequence, new DeclarationOperation(destination.Local));
        return this;
    }

    /// <summary>Declares and executes a mutable variable with a constant.</summary>
    public ExecutorBuilder Then<T>(out Variable<T> destination, T value, string? name = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        destination = Variable<T>(name);
        return StoreValue(destination, value, requirement);
    }

    internal ExecutorBuilder StoreValue<T>(Variable<T> destination, T value, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => Store(destination, ToOperationArgument(value, typeof(T)), requirement);

    internal ExecutorBuilder StoreValue<T>(Variable<T> destination, Variable<T> value, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => Store(destination, ToOperationArgument(value), requirement);

    private ExecutorBuilder Store<T>(Variable<T> variable, OperationArgument argument, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        ValidateValue(variable);
        EnsureCompatible(typeof(T), argument);
        IOperation operation = variable.IsLocal ? new StoreOperation(OperationArgument.FromLocal(variable.Local), ToRawOperand(argument)) : new StoreOperation(OperationArgument.FromValue(variable.Value), ToRawOperand(argument));
        Data.Add(_sequence, operation, requirement, variable);
        return this;
    }

    internal ExecutorBuilder Store(Variable variable, OperationArgument argument)
    {
        ValidateValue(variable);
        EnsureCompatible(variable.LogicalType, argument);
        Data.Add(_sequence, (variable.IsLocal ? (IOperation)new StoreOperation(OperationArgument.FromLocal(variable.Local), ToRawOperand(argument)) : new StoreOperation(OperationArgument.FromValue(variable.Value), ToRawOperand(argument))), outputs: [variable]);
        return this;
    }

    /// <summary>Creates an if/else operation with nested instruction sequences.</summary>
    public ExecutorBuilder If(Variable<bool> condition, Action<ExecutorBuilder> whenTrue, Action<ExecutorBuilder>? whenFalse = null)
    {
        ValidateValue(condition);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            var yes = RecordSequence(whenTrue);
            var no = whenFalse is null ? null : RecordSequence(whenFalse);
            Data.Add(_sequence, new ConditionNode(ToRawOperand(ToOperationArgument(condition)), yes, no));
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
        return this;
    }

    /// <summary>Creates an if/else block from a captured boolean expression.</summary>
    /// <remarks>The expression is recorded immediately before the conditional. Its result remains an internal plan value; callers do not need to declare a condition variable.</remarks>
    public ExecutorBuilder If(Expression<Func<bool>> condition, Action<ExecutorBuilder> whenTrue, Action<ExecutorBuilder>? whenFalse = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(whenTrue);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            PlanValue value = ReadExpressionValue(condition, [], typeof(bool));
            if (ExecutorExpressionReader.TryGetGuaranteedTypeTest(condition.Body, out Variable source, out Type matchType))
            {
                ValidateValue(source);
                TypeTestMatch branchMatch = new(source, matchType);
                return If(new Variable<bool>(Data, value), branch =>
                {
                    _activeTypeMatches.Push(branchMatch);
                    try
                    {
                        whenTrue(branch);
                    }
                    finally
                    {
                        _activeTypeMatches.Pop();
                    }
                }, whenFalse);
            }
            return If(new Variable<bool>(Data, value), whenTrue, whenFalse);
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    /// <summary>Creates a negated type guard and exposes the type proven after the guard returns.</summary>
    public ExecutorBuilder If<T>(Expression<Func<bool>> condition, Action<ExecutorBuilder> whenTrue, out Variable<T> matched)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(whenTrue);
        if (!ExecutorExpressionReader.TryGetGuaranteedNegatedTypeTest(condition.Body, out Variable source, out Type matchType) || matchType != typeof(T))
            throw new ArgumentException($"The condition must be a direct negated 'is {typeof(T)}' test over a captured variable.", nameof(condition));

        If(condition, whenTrue);
        PlanValue value = Data.CreateValue(typeof(T));
        Data.Add(_sequence, new TypeAsOperation(ToRawOperand(ToOperationArgument(source)), value), outputs: [global::Rinku.Variable.Create(Data, value)]);
        matched = new(Data, value);
        return this;
    }
    /// <summary>Retrieves the one type-test variable currently proven by the enclosing true branch.</summary>
    public Variable<T> GetVariable<T>()
    {
        TypeTestMatch? match = _activeTypeMatches.FirstOrDefault(x => x.Type == typeof(T));
        if (match is null)
            throw new InvalidOperationException($"No active true-branch type test provides a {typeof(T)} variable.");
        if (match.Variable is null)
        {
            PlanValue value = Data.CreateValue(match.Type);
            Data.Add(_sequence, new TypeAsOperation(ToRawOperand(ToOperationArgument(match.Source)), value), outputs: [global::Rinku.Variable.Create(Data, value)]);
            match.Variable = global::Rinku.Variable.Create(Data, value);
        }
        return GetVariable<T>(match.Variable);
    }

    /// <summary>Retrieves a branch variable with its proven logical type.</summary>
    public Variable<T> GetVariable<T>(Variable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ValidateValue(variable);
        if (variable.LogicalType != typeof(T))
            throw new ArgumentException($"Variable {variable.Name ?? variable.LogicalType.Name} has logical type {variable.LogicalType}, not {typeof(T)}.", nameof(variable));
        return variable.IsLocal ? new Variable<T>(Data, variable.Local) : new Variable<T>(Data, variable.Value);
    }

    private sealed class TypeTestMatch(Variable source, Type type)
    {
        internal Variable Source { get; } = source;
        internal Type Type { get; } = type;
        internal Variable? Variable
        {
            get; set;
        }
    }
    /// <summary>Creates a foreach block over an enumerable source.</summary>
    public ExecutorBuilder ForEach<TItem>(Variable<IEnumerable<TItem>> source, Action<ExecutorBuilder, Variable<TItem>> body)
        => AddForEach<IEnumerable<TItem>, TItem>(source, body, out _);

    /// <summary>Creates a foreach block over a concrete enumerable source.</summary>
    public ExecutorBuilder ForEach<TSource, TItem>(Variable<TSource> source, Action<ExecutorBuilder, Variable<TItem>> body) where TSource : IEnumerable<TItem>
        => AddForEach<TSource, TItem>(source, body, out _);

    /// <summary>Creates a foreach block from a captured enumerable expression and exposes the scoped item variable to its body.</summary>
    public ExecutorBuilder ForEach<TItem>(Expression<Func<IEnumerable<TItem>>> source, Action<ExecutorBuilder, Variable<TItem>> body)
    {
        ArgumentNullException.ThrowIfNull(source);
        PlanValue value = ReadExpressionValue(source, [], typeof(IEnumerable<TItem>));
        return AddForEach<IEnumerable<TItem>, TItem>(new Variable<IEnumerable<TItem>>(Data, value), body, out _);
    }

    private ExecutorBuilder AddForEach<TSource, TItem>(Variable<TSource> source, Action<ExecutorBuilder, Variable<TItem>> body, out Variable<TItem> item)
    {
        ArgumentNullException.ThrowIfNull(body);
        ValidateValue(source);
        Type sourceElementType = GetEnumerableElementType(typeof(TSource)) ?? throw new ArgumentException($"Enumerable type {typeof(TSource)} does not expose a generic element type.", nameof(source));
        EnsureCompatible(typeof(TItem), sourceElementType);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        bool previousLoopControlsAllowed = _loopControlsAllowed;
        bool enteredLoop = false;
        PlanValue itemValue = Data.CreateValue(typeof(TItem));
        item = new(Data, itemValue);
        try
        {
            _loopDepth++;
            enteredLoop = true;
            _loopControlsAllowed = true;
            var loopItem = item;
            var instructions = RecordSequence(plan => body(plan, loopItem));
            Data.Add(_sequence, new ForEachNode(ToRawOperand(ToOperationArgument(source)), itemValue, instructions), outputs: [global::Rinku.Variable.Create(Data, itemValue)]);
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
        finally
        {
            if (enteredLoop)
            {
                _loopDepth--;
            }
            _loopControlsAllowed = previousLoopControlsAllowed;
        }
        return this;
    }

    /// <summary>Creates a while block.</summary>
    public ExecutorBuilder While(Func<ExecutorBuilder, Variable<bool>> condition, Action<ExecutorBuilder> body)
    {
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        bool previousLoopControlsAllowed = _loopControlsAllowed;
        bool enteredLoop = false;
        try
        {
            _loopControlsAllowed = false;
            Variable<bool> test = null!;
            var conditionBody = RecordSequence(plan => test = condition(plan));
            ValidateValue(test);
            _loopDepth++;
            enteredLoop = true;
            _loopControlsAllowed = true;
            var instructions = RecordSequence(body);
            Data.Add(_sequence, new WhileNode(conditionBody, ToRawOperand(ToOperationArgument(test)), instructions));
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
        finally
        {
            if (enteredLoop)
            {
                _loopDepth--;
            }
            _loopControlsAllowed = previousLoopControlsAllowed;
        }
        return this;
    }

    /// <summary>Creates a while block from a captured boolean expression.</summary>
    public ExecutorBuilder While(Expression<Func<bool>> condition, Action<ExecutorBuilder> body)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(body);
        return While(_ => new Variable<bool>(Data, ReadExpressionValue(condition, [], typeof(bool))), body);
    }

    /// <summary>Creates a do/while block.</summary>
    public ExecutorBuilder DoWhile(Func<ExecutorBuilder, Variable<bool>> condition, Action<ExecutorBuilder> body)
    {
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        bool previousLoopControlsAllowed = _loopControlsAllowed;
        bool enteredLoop = false;
        try
        {
            _loopDepth++;
            enteredLoop = true;
            _loopControlsAllowed = true;
            var instructions = RecordSequence(body);
            _loopDepth--;
            enteredLoop = false;
            _loopControlsAllowed = false;
            Variable<bool> test = null!;
            var conditionBody = RecordSequence(plan => test = condition(plan));
            ValidateValue(test);
            Data.Add(_sequence, new DoWhileNode(instructions, conditionBody, ToRawOperand(ToOperationArgument(test))));
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
        finally
        {
            if (enteredLoop)
            {
                _loopDepth--;
            }
            _loopControlsAllowed = previousLoopControlsAllowed;
        }
        return this;
    }

    /// <summary>Creates a do/while block from a captured boolean expression.</summary>
    public ExecutorBuilder DoWhile(Expression<Func<bool>> condition, Action<ExecutorBuilder> body)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(body);
        return DoWhile(_ => new Variable<bool>(Data, ReadExpressionValue(condition, [], typeof(bool))), body);
    }

    /// <summary>Creates a C#-style for block.</summary>
    public ExecutorBuilder For(Action<ExecutorBuilder> setup, Func<ExecutorBuilder, Variable<bool>> condition, Action<ExecutorBuilder> increment, Action<ExecutorBuilder> body)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(increment);
        ArgumentNullException.ThrowIfNull(body);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        bool previousLoopControlsAllowed = _loopControlsAllowed;
        bool enteredLoop = false;
        try
        {
            var initialization = RecordSequence(setup);
            _loopControlsAllowed = false;
            Variable<bool> test = null!;
            var conditionBody = RecordSequence(plan => test = condition(plan));
            ValidateValue(test);
            _loopDepth++;
            enteredLoop = true;
            _loopControlsAllowed = true;
            var instructions = RecordSequence(body);
            _loopDepth--;
            enteredLoop = false;
            _loopControlsAllowed = false;
            var step = RecordSequence(increment);
            Data.Add(_sequence, new ForNode(initialization, conditionBody, ToRawOperand(ToOperationArgument(test)), step, instructions));
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
        finally
        {
            if (enteredLoop)
            {
                _loopDepth--;
            }
            _loopControlsAllowed = previousLoopControlsAllowed;
        }
        return this;
    }

    /// <summary>Creates a C#-style for block without a setup region.</summary>
    public ExecutorBuilder For(Func<ExecutorBuilder, Variable<bool>> condition, Action<ExecutorBuilder> increment, Action<ExecutorBuilder> body)
        => For(static _ => { }, condition, increment, body);

    /// <summary>Creates a C#-style for block with a captured boolean condition expression.</summary>
    public ExecutorBuilder For(Action<ExecutorBuilder> setup, Expression<Func<bool>> condition, Action<ExecutorBuilder> increment, Action<ExecutorBuilder> body)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(increment);
        ArgumentNullException.ThrowIfNull(body);
        return For(setup, _ => new Variable<bool>(Data, ReadExpressionValue(condition, [], typeof(bool))), increment, body);
    }

    /// <summary>Creates a C#-style for block without a setup region from a captured boolean expression.</summary>
    public ExecutorBuilder For(Expression<Func<bool>> condition, Action<ExecutorBuilder> increment, Action<ExecutorBuilder> body)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return For(static _ => { }, condition, increment, body);
    }

    /// <summary>Creates a for block with one scoped variable initialized from a literal or retained value.</summary>
    public ExecutorBuilder For<T>(T initialValue, Expression<Func<T, bool>> condition, Action<ExecutorBuilder, Variable<T>> increment, Action<ExecutorBuilder, Variable<T>> body, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(increment);
        ArgumentNullException.ThrowIfNull(body);
        Variable<T> variable = Variable<T>(name);
        return For(loop => loop.Then(variable, initialValue), _ => new Variable<bool>(Data, ReadExpressionValue(condition, [variable], typeof(bool))), loop => increment(loop, variable), loop => body(loop, variable));
    }

    /// <summary>Creates a for block with one scoped variable initialized from another variable.</summary>
    public ExecutorBuilder For<T>(Variable<T> initialValue, Expression<Func<T, bool>> condition, Action<ExecutorBuilder, Variable<T>> increment, Action<ExecutorBuilder, Variable<T>> body, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(initialValue);
        ValidateValue(initialValue);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(increment);
        ArgumentNullException.ThrowIfNull(body);
        Variable<T> variable = Variable<T>(name);
        return For(loop => loop.Then(variable, initialValue), _ => new Variable<bool>(Data, ReadExpressionValue(condition, [variable], typeof(bool))), loop => increment(loop, variable), loop => body(loop, variable));
    }

    /// <summary>Adds a loop break.</summary>
    public ExecutorBuilder Break()
    {
        if (_loopDepth <= 0 || !_loopControlsAllowed)
            throw new InvalidOperationException("Break can only be used inside a loop.");
        Data.Add(_sequence, new BreakOperation());
        return this;
    }

    /// <summary>Adds a loop continue.</summary>
    public ExecutorBuilder Continue()
    {
        if (_loopDepth <= 0 || !_loopControlsAllowed)
            throw new InvalidOperationException("Continue can only be used inside a loop.");
        Data.Add(_sequence, new ContinueOperation());
        return this;
    }

    /// <summary>Adds an early executor return.</summary>
    public ExecutorBuilder Return()
    {
        Data.Add(_sequence, new ReturnOperation());
        return this;
    }
    /// <summary>Composes a nested scope whose inputs resolve from enclosing variables during compilation.</summary>
    public ExecutorBuilder Merge(ExecutorScope source, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        ImportMethods(source);
        Data.Add(_sequence, source, requirement);
        return this;
    }

    /// <summary>Composes a nested scope and exposes its result.</summary>
    public ExecutorBuilder Merge<T>(out Variable<T> result, ExecutorScope source, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        PlanValue value = Data.CreateValue(typeof(T));
        AddComposition(source, value, requirement);
        result = new(Data, value);
        return this;
    }

    /// <summary>References a named value from an enclosing compilation scope.</summary>
    public ExecutorBuilder Reference<T>(string name, out Variable<T> result, bool defaultWhenMissing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddReference(name, out result, defaultWhenMissing);
    }

    /// <summary>References a value by type from an enclosing compilation scope.</summary>
    public ExecutorBuilder Reference<T>(out Variable<T> result, bool defaultWhenMissing = false) => AddReference(null, out result, defaultWhenMissing);

    private ExecutorBuilder AddReference<T>(string? name, out Variable<T> result, bool defaultWhenMissing)
    {
        PlanSlot slot = new(Data, typeof(T), PlanSlotKind.Reference)
        {
            Name = name,
            DefaultWhenMissing = defaultWhenMissing
        };
        PlanValue value = new(slot);
        slot.Value = value;
        Data.AddSlot(slot);
        Data.Add(_sequence, new ReferenceOperation(value), outputs: [global::Rinku.Variable.Create(Data, value)]);
        result = new(Data, value);
        return this;
    }

    /// <summary>Exposes the result returned by an executor or a composed scope.</summary>
    public ExecutorBuilder Result(Variable value)
    {
        ValidateValue(value);
        Data.Output = value;
        return this;
    }

    private void AddComposition(ExecutorScope source, PlanValue? result, ExecutorRequirement requirement)
    {
        ValidateRequirement(requirement);
        ArgumentNullException.ThrowIfNull(source);
        ImportMethods(source);
        Data.Add(_sequence, new CompositionNode(source, result), requirement, result is null ? [] : [global::Rinku.Variable.Create(Data, result)]);
    }

    private void ImportMethods(ExecutorScope source)
    {
        foreach (var contract in source.MethodContracts)
        {
            Data.MethodContracts.Add(contract);
        }
        foreach (var method in source.Methods)
        {
            if (Data.Methods.TryGetValue(method.Key, out var existing))
            {
                new ExecutorBuilder(existing).Merge(method.Value);
            }
            else
            {
                Data.Methods.Add(method.Key, ExecutorCompiler.CloneScope(method.Value));
            }
        }
    }

    /// <summary>Creates an independent builder copy.</summary>
    public ExecutorBuilder Clone() => new(ExecutorCompiler.CloneScope(Data));

    /// <summary>Removes a plan item represented by <paramref name="node"/>.</summary>
    public ExecutorBuilder Remove(ExecutorNodeInfo node)
    {
        if (!ReferenceEquals(node.Plan, this))
        {
            if (!Nodes.Any(candidate => ReferenceEquals(candidate.Plan, node.Plan) && ReferenceEquals(candidate.Item, node.Item)))
                throw new ArgumentException("The node does not belong to this builder.", nameof(node));
            node.Plan.Remove(node);
            return this;
        }
        if (!TryFind(Data.Body, node.Item, out PlanSequence? sequence, out int index) || sequence is null)
            throw new ArgumentException("The node does not belong to this builder.", nameof(node));
        sequence.RemoveAt(index);
        return this;
    }

    /// <summary>Records a call to be resolved from its name and bound operands during compilation.</summary>
    public ExecutorBuilder Call(string methodName, IReadOnlyList<object?> operands, IReadOnlyList<ExecutorArgumentPassing>? passing = null, IReadOnlyList<Type>? genericArguments = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        return Call([methodName], operands, passing, genericArguments, requirement);
    }

    /// <summary>Records a named call and exposes its result as a variable.</summary>
    public ExecutorBuilder Call<TResult>(out Variable<TResult> result, string methodName, IReadOnlyList<object?> operands, string? name = null, IReadOnlyList<ExecutorArgumentPassing>? passing = null, IReadOnlyList<Type>? genericArguments = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        return Call(out result, [methodName], operands, name, passing, genericArguments, requirement);
    }

    public ExecutorBuilder Call(IReadOnlyList<string> names, IReadOnlyList<object?> operands, IReadOnlyList<ExecutorArgumentPassing>? passing = null, IReadOnlyList<Type>? genericArguments = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        AddResolvedCall(names, operands, passing, genericArguments, null, requirement);
        return this;
    }

    public ExecutorBuilder Call<TResult>(out Variable<TResult> result, IReadOnlyList<string> names, IReadOnlyList<object?> operands, string? name = null, IReadOnlyList<ExecutorArgumentPassing>? passing = null, IReadOnlyList<Type>? genericArguments = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        PlanValue value = AddResolvedCall(names, operands, passing, genericArguments, typeof(TResult), requirement)!;
        Data.PublishValue(value, name);
        result = new(Data, value);
        return this;
    }

    private PlanValue? AddResolvedCall(IReadOnlyList<string> names, IReadOnlyList<object?> operands, IReadOnlyList<ExecutorArgumentPassing>? passing, IReadOnlyList<Type>? genericArguments, Type? resultType, ExecutorRequirement requirement)
    {
        ValidateRequirement(requirement);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(operands);
        ExecutorArgumentPassing[] modes = passing?.ToArray() ?? new ExecutorArgumentPassing[operands.Count];
        if (modes.Length != operands.Count || modes.Any(mode => !Enum.IsDefined(mode)))
            throw new ArgumentException("Argument passing modes must match the call operands.", nameof(passing));
        for (int index = 0; index < operands.Count; index++)
        {
            if (operands[index] is Variable variable)
                ValidateValue(variable);
            else if (modes[index] != ExecutorArgumentPassing.Value)
                throw new ArgumentException("Address operands require a plan variable.", nameof(operands));
        }
        PlanValue? result = resultType is null ? null : Data.CreateValue(resultType);
        OperationArgument[] arguments = operands.Select(OperationArgument.FromRaw).ToArray();
        Data.Add(_sequence, new CallOperation(names, arguments, result, modes, genericArguments?.ToArray() ?? [], null), requirement, CallOutputs(result, arguments, modes));
        return result;
    }

    private static Variable[] CallOutputs(PlanValue? result, IReadOnlyList<OperationArgument> operands, IReadOnlyList<ExecutorArgumentPassing>? passing)
    {
        List<Variable> outputs = [];
        if (result is not null)
        {
            outputs.Add(global::Rinku.Variable.Create(result.Plan, result));
        }
        if (passing is not null)
        {
            for (int index = 0; index < operands.Count; index++)
            {
                if (passing[index] == ExecutorArgumentPassing.Out && operands[index].Variable is Variable variable)
                {
                    outputs.Add(variable);
                }
            }
        }
        return outputs.ToArray();
    }

    public ExecutorBuilder Add(IPlanNode operation, ExecutorRequirement requirement = ExecutorRequirement.Required, params Variable[] outputs)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Data.Add(_sequence, operation, requirement, outputs);
        return this;
    }

    /// <summary>Builds the plan into an emitted executor type.</summary>
    public IExecutor Build(ExecutorBuildConfiguration? configuration = null) => (IExecutor)Activator.CreateInstance(ExecutorCompiler.BuildType(this, (configuration ?? ExecutorBuildConfiguration.Global).Snapshot()))!;
    public TConcreteExecutor Build<TConcreteExecutor>(ExecutorBuildConfiguration? configuration = null) where TConcreteExecutor : IExecutor => ExecutorCompiler.Build<TConcreteExecutor>(this, (configuration ?? ExecutorBuildConfiguration.Global).Snapshot());
    public Func<TExecutor> Compile<TExecutor>(ExecutorBuildConfiguration? configuration = null) where TExecutor : IExecutor => ExecutorCompiler.Compile<TExecutor>(this, (configuration ?? ExecutorBuildConfiguration.Global).Snapshot());

    internal PlanValue AddCall(MethodBase method, IReadOnlyList<OperationArgument> operands, Type? resultType)
        => AddCall(_sequence, method, operands, resultType);

    internal PlanValue AddCall(PlanSequence sequence, MethodBase method, IReadOnlyList<OperationArgument> operands, Type? resultType)
        => AddTypedCall(sequence, method, operands, resultType, ExecutorRequirement.Required);

    internal PlanValue AddCall(PlanSequence sequence, MethodBase method, IReadOnlyList<object?> operands, Type? resultType, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => AddTypedCall(sequence, method, operands.Select(OperationArgument.FromRaw).ToArray(), resultType, requirement);

    private PlanValue AddTypedCall(PlanSequence sequence, MethodBase method, IReadOnlyList<OperationArgument> operands, Type? resultType, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        ValidateTypedCall(method, operands);
        PlanValue? result = resultType is null ? null : Data.CreateValue(resultType);
        int offset = method is MethodInfo { IsStatic: false } ? 1 : 0;
        ParameterInfo[] parameters = method.GetParameters();
        ExecutorArgumentPassing[] passing = new ExecutorArgumentPassing[operands.Count];
        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            passing[index + offset] = !parameter.ParameterType.IsByRef ? ExecutorArgumentPassing.Value : parameter.IsOut ? ExecutorArgumentPassing.Out : parameter.IsIn ? ExecutorArgumentPassing.In : ExecutorArgumentPassing.Ref;
        }
        Type[] genericArguments = method is MethodInfo { IsGenericMethod: true, ContainsGenericParameters: false } generic && !generic.GetGenericMethodDefinition().GetParameters().Any(parameter => parameter.ParameterType.ContainsGenericParameters) ? generic.GetGenericArguments() : [];
        Data.Add(sequence, new CallOperation(method is ConstructorInfo ? [] : [method.Name], operands.ToArray(), result, passing, genericArguments, method is ConstructorInfo ? method.DeclaringType : null), requirement, CallOutputs(result, operands, passing));
        return result!;
    }

    internal PlanValue AddMemberRead(MemberInfo member, IReadOnlyList<OperationArgument> operands, Type resultType)
        => AddMemberRead(_sequence, member, operands, resultType);

    internal PlanValue AddMemberRead(PlanSequence sequence, MemberInfo member, IReadOnlyList<OperationArgument> operands, Type resultType, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        ValidateMemberRead(member, operands);
        PlanValue result = Data.CreateValue(resultType);
        Data.Add(sequence, new CallOperation([member.Name], operands.Select(ToRawOperand).ToArray(), result, new ExecutorArgumentPassing[operands.Count], [], null), requirement, CallOutputs(result, operands, null));
        return result;
    }

    internal void AddIncrement(Variable local, int amount)
    {
        ValidateValue(local);
        if (!local.IsLocal)
            throw new InvalidOperationException("Increment requires a mutable variable.");
        PlanLocal localId = local.Local;
        Type localType = local.LogicalType;
        ValidateNumericType(localType);
        OperationArgument current = OperationArgument.FromLocal(localId);

        OperationArgument amountArgument = OperationArgument.FromConstant(typeof(int), amount);
        Type resultType = GetBinaryResultType(localType, amountArgument, ExecutorBinaryOperator.Add);
        PlanValue result = AddBinary(_sequence, current, ExecutorBinaryOperator.Add, amountArgument, resultType);
        OperationArgument stored = ConvertForAssignment(result, resultType, localType);
        Data.Add(_sequence, new StoreOperation(OperationArgument.FromLocal(localId), ToRawOperand(stored)), outputs: [global::Rinku.Variable.Create(Data, localId)]);
    }

    internal void AddIncrement(MemberInfo member, int amount, IReadOnlyList<object?> operands)
    {
        OperationArgument[] planOperands = ToOperationArguments(operands, MemberOperandTypes(member));
        ValidateMemberRead(member, planOperands);
        Type memberType = GetMemberType(member);
        PlanValue current = AddMemberRead(_sequence, member, planOperands, memberType);
        OperationArgument amountArgument = OperationArgument.FromConstant(typeof(int), amount);
        Type resultType = GetBinaryResultType(memberType, amountArgument, ExecutorBinaryOperator.Add);
        PlanValue result = AddBinary(_sequence, OperationArgument.FromValue(current), ExecutorBinaryOperator.Add, amountArgument, resultType);
        AddAssignment(_sequence, member, planOperands, ConvertForAssignment(result, resultType, memberType));
    }

    private OperationArgument ConvertForAssignment(PlanValue value, Type sourceType, Type targetType)
    {
        if (sourceType == targetType)
            return OperationArgument.FromValue(value);
        if (!CanUseConversion(sourceType, targetType))
            throw new ArgumentException($"Values of type {sourceType} cannot be assigned to {targetType}.", nameof(targetType));
        PlanValue converted = Data.CreateValue(targetType);
        Data.Add(_sequence, new ConvertOperation(ToRawOperand(OperationArgument.FromValue(value)), converted, false), outputs: [global::Rinku.Variable.Create(Data, converted)]);
        return OperationArgument.FromValue(converted);
    }
    internal PlanValue AddArrayRead(OperationArgument array, OperationArgument index, PlanSequence sequence)
    {
        Type arrayType = GetArgumentLogicalType(array);
        Type? elementType = arrayType.IsArray && arrayType.GetArrayRank() == 1 ? arrayType.GetElementType() : null;
        if (elementType is null)
            throw new ArgumentException($"Argument type {arrayType} is not a one-dimensional array.", nameof(array));
        EnsureCompatible(arrayType, array);
        EnsureCompatible(typeof(int), index);
        PlanValue result = Data.CreateValue(elementType);
        Data.Add(sequence, new ArrayReadOperation(ToRawOperand(array), ToRawOperand(index), result), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    internal PlanValue AddArrayCreate(PlanSequence sequence, IReadOnlyList<OperationArgument> elements, Type resultType)
    {
        Type? elementType = resultType.IsArray && resultType.GetArrayRank() == 1 ? resultType.GetElementType() : null;
        if (elementType is null)
            throw new ArgumentException($"Result type {resultType} is not a one-dimensional array.", nameof(resultType));
        foreach (OperationArgument element in elements)
            EnsureCompatible(elementType, element);
        PlanValue result = Data.CreateValue(resultType);
        Data.Add(sequence, new ArrayCreateOperation(elements.Select(ToRawOperand).ToArray(), result), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    internal void AddAssignment(MemberInfo member, IReadOnlyList<OperationArgument> operands, OperationArgument value)
        => AddAssignment(_sequence, member, operands, value);

    internal void AddAssignment(PlanSequence sequence, MemberInfo member, IReadOnlyList<OperationArgument> operands, OperationArgument value, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        ValidateMemberRead(member, operands);
        OperationArgument[] arguments = [.. operands.Select(ToRawOperand), ToRawOperand(value)];
        Data.Add(sequence, new CallOperation([member.Name], arguments, null, new ExecutorArgumentPassing[arguments.Length], [], null), requirement);
    }

    internal void AddArrayAssignment(OperationArgument array, OperationArgument index, OperationArgument value, PlanSequence sequence)
    {
        Type arrayType = GetArgumentLogicalType(array);
        Type? elementType = arrayType.IsArray && arrayType.GetArrayRank() == 1 ? arrayType.GetElementType() : null;
        if (elementType is null)
            throw new ArgumentException($"Argument type {arrayType} is not a one-dimensional array.", nameof(array));
        EnsureCompatible(arrayType, array);
        EnsureCompatible(typeof(int), index);
        EnsureCompatible(elementType, value);
        Data.Add(sequence, new ArrayAssignmentOperation(ToRawOperand(array), ToRawOperand(index), ToRawOperand(value)));
    }

    internal OperationArgument ToOperationArgument(Variable value)
    {
        ValidateValue(value);
        return value.IsLocal ? OperationArgument.FromLocal(value.Local) : OperationArgument.FromValue(value.Value);
    }
    internal OperationArgument ToOperationArgument(object? argument, Type expectedType)
    {
        if (argument is OperationArgument prepared)
        {
            return prepared;
        }
        if (argument is Variable value)
            return ToOperationArgument(value);
        Type type = expectedType.ContainsGenericParameters || Nullable.GetUnderlyingType(expectedType) is null ? argument?.GetType() ?? expectedType : expectedType;
        return OperationArgument.FromConstant(type, argument);
    }

    internal OperationArgument ToRawOperand(OperationArgument argument) => argument;

    internal void EnsureArgumentCompatible(Type expected, OperationArgument argument) => EnsureCompatible(expected, argument);

    internal void EnsureResultCompatible(Type expected, Type actual)
    {
        if (!CanUseResult(expected, actual))
            throw new ArgumentException($"Value of type {actual} cannot be used as {expected}.", nameof(actual));
    }

    internal PlanValue AddCompare(PlanSequence sequence, OperationArgument left, ExecutorComparison comparison, OperationArgument right)
    {
        Type leftType = GetArgumentLogicalType(left);
        ValidateComparison(comparison);
        EnsureCompatible(leftType, left);
        EnsureComparable(leftType, right, comparison);
        PlanValue result = Data.CreateValue(typeof(bool));
        Data.Add(sequence, new CompareOperation(ToRawOperand(left), comparison, ToRawOperand(right), result), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    internal PlanValue AddBinary(PlanSequence sequence, OperationArgument left, ExecutorBinaryOperator @operator, OperationArgument right, Type resultType, bool isChecked = false)
    {
        Type leftType = GetArgumentLogicalType(left);
        ValidateBinaryOperator(@operator);
        EnsureCompatible(leftType, left);
        EnsureBinaryOperands(leftType, right, @operator);
        Type operationResultType = GetBinaryResultType(leftType, right, @operator);
        if (resultType != operationResultType)
            throw new ArgumentException($"The {@operator} operation produces {operationResultType}; use that result type instead of {resultType}.", nameof(resultType));
        PlanValue result = Data.CreateValue(resultType);
        Data.Add(sequence, new BinaryOperation(ToRawOperand(left), @operator, ToRawOperand(right), result, isChecked), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    internal PlanValue AddUnary(PlanSequence sequence, OperationArgument value, ExecutorUnaryOperator @operator, Type resultType, bool isChecked = false)
    {
        Type valueType = GetArgumentLogicalType(value);
        ValidateUnaryOperator(@operator);
        EnsureCompatible(valueType, value);
        ValidateUnaryType(valueType, @operator);
        Type operationResultType = GetUnaryResultType(valueType, @operator);
        if (resultType != operationResultType)
            throw new ArgumentException($"The {@operator} operation produces {operationResultType}; use that result type instead of {resultType}.", nameof(resultType));
        PlanValue result = Data.CreateValue(resultType);
        Data.Add(sequence, new UnaryOperation(ToRawOperand(value), @operator, result, isChecked), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    private static void ValidateRequirement(ExecutorRequirement requirement)
    {
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
    }

    private void CompleteRecording(BuilderCheckpoint recording, ExecutorRequirement requirement, PlanSlot? output = null)
    {
        PlanSequence sequence = recording.Sequence;
        int count = sequence.Items.Count - recording.ItemCount;
        if (count == 0)
            return;
        if (count == 1)
        {
            sequence.Items[recording.ItemCount].Requirement = requirement;
            return;
        }
        PlanSequence body = new(count);
        for (int index = recording.ItemCount; index < sequence.Items.Count; index++)
        {
            body.Add(sequence.Items[index]);
        }
        sequence.RemoveFrom(recording.ItemCount);
        sequence.Add(new ScopeNode(body, output), requirement);
    }

    internal BuilderCheckpoint CaptureCheckpoint() => new(_sequence, _sequence.Items.Count, Data.Slots.Count);

    internal void Rollback(BuilderCheckpoint checkpoint)
    {
        Data.Rollback(checkpoint.Sequence, checkpoint.ItemCount, checkpoint.SlotCount);
    }

    private void AddExpressionAction(LambdaExpression expression, IReadOnlyList<object?> arguments, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            new ExecutorExpressionReader(this, expression, arguments, _sequence).ReadAction();
            CompleteRecording(checkpoint, requirement);
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    private Variable<TResult> AddExpressionValue<TResult>(LambdaExpression expression, IReadOnlyList<object?> arguments, string? name = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        PlanValue value = ReadExpressionValue(expression, arguments, typeof(TResult), requirement: requirement);
        Data.PublishValue(value, name);
        return new(Data, value);
    }

    private PlanValue ReadExpressionValue(LambdaExpression expression, IReadOnlyList<object?> arguments, Type resultType, Variable? destination = null, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            PlanValue value = new ExecutorExpressionReader(this, expression, arguments, _sequence).ReadValue(resultType);
            if (destination is not null)
                Store(destination, OperationArgument.FromValue(value));
            CompleteRecording(checkpoint, requirement, destination?.Slot ?? value.Slot);
            return value;
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    private void AddExpressionAssignment(LambdaExpression expression, IReadOnlyList<object?> arguments, object? value, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            new ExecutorExpressionReader(this, expression, arguments, _sequence).ReadAssignment(value);
            CompleteRecording(checkpoint, requirement);
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    private void AddExpressionAssignment(LambdaExpression target, LambdaExpression value, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        ValidateRequirement(requirement);
        BuilderCheckpoint checkpoint = CaptureCheckpoint();
        try
        {
            new ExecutorExpressionReader(this, target, [], _sequence).ReadAssignment(value);
            CompleteRecording(checkpoint, requirement);
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    private PlanValue AddCall(MethodBase method, IReadOnlyList<object?> operands, Type? resultType)
        => AddCall(_sequence, method, operands, resultType);

    private OperationArgument[] ToOperationArguments(IReadOnlyList<object?> arguments, IReadOnlyList<Type> expectedTypes)
    {
        OperationArgument[] result = new OperationArgument[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            result[i] = ToOperationArgument(arguments[i], i < expectedTypes.Count ? expectedTypes[i] : typeof(object));
        return result;
    }

    private static IReadOnlyList<Type> MemberOperandTypes(MemberInfo member)
        => IsStatic(member) ? [] : [member.DeclaringType ?? typeof(object)];

    private void ValidateTypedCall(MethodBase method, IReadOnlyList<OperationArgument> operands)
    {
        int expected = method.GetParameters().Length + (method is MethodInfo { IsStatic: false } ? 1 : 0);
        if (operands.Count != expected)
        {
            throw new ArgumentException($"The authored call to {method} expects {expected} arguments.", nameof(operands));
        }
        ParameterInfo[] parameters = method.GetParameters();
        int offset = method is MethodInfo { IsStatic: false } ? 1 : 0;
        for (int index = 0; index < operands.Count; index++)
        {
            if (operands[index].Variable is Variable variable)
            {
                ValidateValue(variable);
            }
            else if (index >= offset && parameters[index - offset].ParameterType.IsByRef)
            {
                throw new ArgumentException("An address argument requires a variable.", nameof(operands));
            }
        }
    }

    private void ValidateMemberRead(MemberInfo member, IReadOnlyList<OperationArgument> operands)
    {
        MemberOperandTypes(member);
        if (operands.Count != (IsStatic(member) ? 0 : 1))
            throw new ArgumentException($"Member {member} has an incorrect number of operands.", nameof(operands));
    }

    private static bool IsStatic(MemberInfo member)
        => member switch
        {
            FieldInfo field => field.IsStatic,
            PropertyInfo property => property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic == true,
            _ => false
        };

    private static Type GetMemberType(MemberInfo member)
        => member switch
        {
            FieldInfo field when !field.FieldType.IsByRef => field.FieldType,
            PropertyInfo property when !property.PropertyType.IsByRef => property.PropertyType,
            FieldInfo or PropertyInfo => throw new NotSupportedException("By-reference members are not supported by executor plans."),
            _ => throw new ArgumentException($"Member {member} is not a field or property.", nameof(member))
        };

    private void ValidateLocal<T>(Variable<T> variable) => ValidateValue(variable);
    private void ValidateValue(Variable value)
    {
        if (value.IsLocal)
            Data.EnsurePlan(value.Local);
        else
            Data.EnsurePlan(value.Value);
    }
    private void ValidateNumericType(Type type)
    {
        if (!IsNumeric(type))
            throw new ArgumentException($"Type {type} is not numeric.", nameof(type));
    }

    private static void ValidateComparison(ExecutorComparison comparison)
    {
        if (comparison is not (ExecutorComparison.Equal or ExecutorComparison.NotEqual or ExecutorComparison.LessThan or ExecutorComparison.LessThanOrEqual or ExecutorComparison.GreaterThan or ExecutorComparison.GreaterThanOrEqual))
            throw new ArgumentOutOfRangeException(nameof(comparison), comparison, "The comparison operator is not supported.");
    }

    private static void ValidateBinaryOperator(ExecutorBinaryOperator @operator)
    {
        if (@operator is not (ExecutorBinaryOperator.Add or ExecutorBinaryOperator.Subtract or ExecutorBinaryOperator.Multiply or ExecutorBinaryOperator.Divide or ExecutorBinaryOperator.Modulo or ExecutorBinaryOperator.And or ExecutorBinaryOperator.Or or ExecutorBinaryOperator.ExclusiveOr or ExecutorBinaryOperator.LeftShift or ExecutorBinaryOperator.RightShift))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The binary operator is not supported.");
    }

    private void EnsureComparable(Type leftType, OperationArgument right, ExecutorComparison comparison)
    {
        Type rightType = right.IsValue ? Data.GetValueType(right.Value) : right.IsVariable ? Data.GetLocalType(right.Local) : right.Type;
        Type leftComparableType = Nullable.GetUnderlyingType(leftType) ?? leftType;
        Type rightComparableType = Nullable.GetUnderlyingType(rightType) ?? rightType;
        if (IsNumeric(leftComparableType) && IsNumeric(rightComparableType))
        {
            EnsureNumericPromotion(leftComparableType, rightComparableType);
            return;
        }
        if (leftComparableType == rightComparableType || leftComparableType.IsAssignableFrom(rightComparableType) || rightComparableType.IsAssignableFrom(leftComparableType))
        {
            if (comparison is ExecutorComparison.Equal or ExecutorComparison.NotEqual)
                return;
        }
        throw new ArgumentException($"Values of type {leftType} and {rightType} cannot be compared with {comparison}.");
    }

    private void EnsureBinaryOperands(Type leftType, OperationArgument right, ExecutorBinaryOperator @operator)
    {
        Type rightType = right.IsValue ? Data.GetValueType(right.Value) : right.IsVariable ? Data.GetLocalType(right.Local) : right.Type;
        if (@operator == ExecutorBinaryOperator.Add && (leftType == typeof(string) || rightType == typeof(string)))
            return;
        if (@operator is ExecutorBinaryOperator.Add or ExecutorBinaryOperator.Subtract or ExecutorBinaryOperator.Multiply or ExecutorBinaryOperator.Divide or ExecutorBinaryOperator.Modulo)
        {
            if (IsNumeric(leftType) && IsNumeric(rightType))
            {
                EnsureNumericPromotion(leftType, rightType);
                return;
            }
        }
        else if (@operator is ExecutorBinaryOperator.And or ExecutorBinaryOperator.Or or ExecutorBinaryOperator.ExclusiveOr)
        {
            if (leftType == typeof(bool) && rightType == typeof(bool) || leftType.IsEnum && leftType == rightType || IsIntegral(leftType) && IsIntegral(rightType))
                return;
        }
        else if (@operator is ExecutorBinaryOperator.LeftShift or ExecutorBinaryOperator.RightShift)
        {
            if (IsIntegral(leftType) && IsIntegral(rightType))
                return;
        }
        throw new ArgumentException($"Values of type {leftType} and {rightType} cannot be combined with {@operator}.");
    }

    private static void ValidateUnaryOperator(ExecutorUnaryOperator @operator)
    {
        if (@operator is not (ExecutorUnaryOperator.Negate or ExecutorUnaryOperator.OnesComplement))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The unary operator is not supported.");
    }

    private void ValidateUnaryType(Type type, ExecutorUnaryOperator @operator)
    {
        if (@operator == ExecutorUnaryOperator.Negate && !IsNumeric(type) || @operator == ExecutorUnaryOperator.OnesComplement && !IsIntegral(type) && !type.IsEnum)
            throw new ArgumentException($"Type {type} is not supported by {@operator}.", nameof(type));
    }

    internal static Type GetUnaryResultType(Type type, ExecutorUnaryOperator @operator)
    {
        if (@operator == ExecutorUnaryOperator.OnesComplement)
            return type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(char) ? typeof(int) : type;
        return type switch
        {
            _ when type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(char) || type == typeof(int) => typeof(int),
            _ when type == typeof(uint) => typeof(long),
            _ when type == typeof(ulong) => typeof(decimal),
            _ => type
        };
    }

    private static void ValidateLogicalOperator(ExecutorLogicalOperator @operator)
    {
        if (@operator is not (ExecutorLogicalOperator.AndAlso or ExecutorLogicalOperator.OrElse))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The logical operator is not supported.");
    }

    private Type GetArgumentLogicalType(OperationArgument argument)
        => argument.IsValue ? Data.GetValueType(argument.Value) : argument.IsVariable ? Data.GetLocalType(argument.Local) : argument.Type;

    private void EnsureCompatible(Type expected, OperationArgument argument)
    {
        Type actual = GetArgumentLogicalType(argument);
        if (expected.ContainsGenericParameters || expected == actual || expected.IsAssignableFrom(actual))
            return;
        if (IsNullableCompatible(expected, actual))
            return;
        if (CanBoxNullableTo(actual, expected))
            return;
        if (actual.IsValueType && expected.IsValueType && IsNumeric(actual) && IsNumeric(expected))
            return;
        if (argument.Kind == OperationArgumentKind.Constant && argument.Data is null && (!expected.IsValueType || Nullable.GetUnderlyingType(expected) is not null))
            return;
        if (!expected.IsValueType && !actual.IsValueType)
            return;

        throw new ArgumentException($"Value of type {actual} cannot be used as {expected}.");
    }

    private static void EnsureCompatible(Type expected, Type actual)
    {
        if (expected.ContainsGenericParameters || expected == actual || expected.IsAssignableFrom(actual))
            return;
        if (IsNullableCompatible(expected, actual))
            return;
        if (CanBoxNullableTo(actual, expected))
            return;
        if (actual.IsValueType && expected.IsValueType && IsNumeric(actual) && IsNumeric(expected))
            return;
        if (!expected.IsValueType && !actual.IsValueType)
            return;

        throw new ArgumentException($"Value of type {actual} cannot be used as {expected}.");
    }

    private static bool CanUseResult(Type expected, Type actual)
    {
        if (expected.ContainsGenericParameters || actual.ContainsGenericParameters || expected == actual || expected.IsAssignableFrom(actual))
            return true;
        if (IsNullableCompatible(expected, actual))
            return true;
        if (CanBoxNullableTo(actual, expected))
            return true;
        if (IsNumeric(actual) && IsNumeric(expected))
            return true;
        if (!expected.IsValueType && !actual.IsValueType)
            return true;
        return false;
    }

    private static bool CanUseConversion(Type source, Type target)
    {
        if (ExecutorCompiler.HasImplicitConversion(source, target))
        {
            return true;
        }
        if (source.ContainsGenericParameters || target.ContainsGenericParameters || source == target || target.IsAssignableFrom(source))
            return true;
        Type? sourceNullable = Nullable.GetUnderlyingType(source);
        Type? targetNullable = Nullable.GetUnderlyingType(target);
        if (IsNullableCompatible(target, source))
            return true;
        if (CanBoxNullableTo(source, target))
            return true;
        if (sourceNullable is not null && (target == sourceNullable || IsNumeric(sourceNullable) && IsNumeric(target)))
            return true;
        if (sourceNullable is not null && targetNullable is null)
            return false;
        if (source.IsEnum || target.IsEnum)
        {
            Type sourceNumeric = source.IsEnum ? Enum.GetUnderlyingType(source) : source;
            Type targetNumeric = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
            if (IsNumeric(sourceNumeric) && IsNumeric(targetNumeric))
                return true;
        }
        if (IsNumeric(source) && IsNumeric(target))
            return true;

        if (!source.IsValueType && !target.IsValueType)
            return source.IsAssignableFrom(target) || target.IsAssignableFrom(source) || source.IsInterface || target.IsInterface;
        if (!source.IsValueType && target.IsValueType)
            return source == typeof(object) || source.IsInterface && source.IsAssignableFrom(target);
        return false;
    }

    private static bool CanUseTypeAs(Type source, Type target)
        => !source.IsValueType && (!target.IsValueType && (source.IsAssignableFrom(target) || target.IsAssignableFrom(source) || source.IsInterface || target.IsInterface));

    private static bool IsNumeric(Type type)
    {
        return IsIntegral(type) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];
        return type.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private static bool IsNullableCompatible(Type expected, Type actual)
    {
        Type? expectedUnderlying = Nullable.GetUnderlyingType(expected);
        Type? actualUnderlying = Nullable.GetUnderlyingType(actual);
        if (expectedUnderlying is null)
            return false;
        if (actualUnderlying is null)
            return expectedUnderlying == actual || IsNumeric(expectedUnderlying) && IsNumeric(actual);
        return expectedUnderlying == actualUnderlying || IsNumeric(expectedUnderlying) && IsNumeric(actualUnderlying);
    }

    private static bool CanBoxNullableTo(Type source, Type target)
    {
        Type? underlying = Nullable.GetUnderlyingType(source);
        return underlying is not null && !target.IsValueType && (target == typeof(object) || target == typeof(ValueType) || target.IsAssignableFrom(underlying));
    }

    private static bool IsIntegral(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(char);

    private static void EnsureNumericPromotion(Type left, Type right)
    {
        if ((left == typeof(decimal) || right == typeof(decimal)) && (left == typeof(float) || left == typeof(double) || right == typeof(float) || right == typeof(double)) || (left == typeof(ulong) && IsSignedIntegral(right)) || (right == typeof(ulong) && IsSignedIntegral(left)) || (left == typeof(uint) && right == typeof(long)) || (left == typeof(long) && right == typeof(uint)))
            throw new ArgumentException($"Values of type {left} and {right} have no C# numeric promotion.");
    }

    internal Type GetBinaryResultType(Type leftType, OperationArgument right, ExecutorBinaryOperator @operator)
    {
        Type rightType = right.IsValue ? Data.GetValueType(right.Value) : right.IsVariable ? Data.GetLocalType(right.Local) : right.Type;
        if (@operator == ExecutorBinaryOperator.Add && (leftType == typeof(string) || rightType == typeof(string)))
            return typeof(string);
        if (@operator is ExecutorBinaryOperator.Add or ExecutorBinaryOperator.Subtract or ExecutorBinaryOperator.Multiply or ExecutorBinaryOperator.Divide or ExecutorBinaryOperator.Modulo)
            return GetNumericPromotionType(leftType, rightType);
        if (@operator is ExecutorBinaryOperator.LeftShift or ExecutorBinaryOperator.RightShift)
            return GetShiftResultType(leftType);
        if (@operator is ExecutorBinaryOperator.And or ExecutorBinaryOperator.Or or ExecutorBinaryOperator.ExclusiveOr)
        {
            if (leftType == typeof(bool) && rightType == typeof(bool) || leftType.IsEnum && leftType == rightType)
                return leftType;
            return GetNumericPromotionType(leftType, rightType);
        }
        throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The binary operator is not supported.");
    }

    private static Type GetShiftResultType(Type left)
        => left == typeof(sbyte) || left == typeof(byte) || left == typeof(short) || left == typeof(ushort) || left == typeof(char) ? typeof(int) : left;

    private static Type GetNumericPromotionType(Type left, Type right)
    {
        if (left == typeof(decimal) || right == typeof(decimal))
        {
            if (left == typeof(float) || left == typeof(double) || right == typeof(float) || right == typeof(double))
                throw new ArgumentException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(decimal);
        }
        if (left == typeof(double) || right == typeof(double))
            return typeof(double);
        if (left == typeof(float) || right == typeof(float))
            return typeof(float);
        if (left == typeof(ulong) || right == typeof(ulong))
        {
            Type other = left == typeof(ulong) ? right : left;
            if (IsSignedIntegral(other))
                throw new ArgumentException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(ulong);
        }
        if (left == typeof(long) || right == typeof(long))
        {
            if (left == typeof(uint) || right == typeof(uint))
                throw new ArgumentException($"The numeric operation between {left} and {right} has no C# numeric promotion.");
            return typeof(long);
        }
        if (left == typeof(uint) || right == typeof(uint))
        {
            Type other = left == typeof(uint) ? right : left;
            return other == typeof(sbyte) || other == typeof(short) || other == typeof(int) ? typeof(long) : typeof(uint);
        }
        return typeof(int);
    }

    private static bool IsSignedIntegral(Type type)
        => type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long);

    private static bool TryFind(PlanSequence sequence, PlanInstruction target, out PlanSequence? owner, out int index)
    {
        for (int i = 0; i < sequence.Items.Count; i++)
        {
            if (ReferenceEquals(sequence.Items[i], target))
            {
                owner = sequence;
                index = i;
                return true;
            }
            foreach (PlanSequence child in sequence.Items[i].Node is IPlanGroup container ? container.Sequences : [])
            {
                if (TryFind(child, target, out owner, out index))
                    return true;
            }
        }
        owner = null;
        index = -1;
        return false;
    }

    private PlanSequence RecordSequence(Action<ExecutorBuilder> record)
    {
        PlanSequence outer = _sequence;
        var child = new PlanSequence();
        _sequence = child;
        try
        {
            record(this);
            return child;
        }
        finally
        {
            _sequence = outer;
        }
    }

    private static IEnumerable<ExecutorNodeInfo> EnumerateNodes(ExecutorBuilder plan, PlanSequence sequence, HashSet<IPlanNode> ancestors)
    {
        foreach (PlanInstruction instruction in sequence.Items)
        {
            if (!ancestors.Add(instruction.Node))
            {
                throw new InvalidOperationException("Plan groups cannot recursively contain themselves.");
            }
            yield return new(plan, instruction, instruction.Node.GetType().Name, instruction.Node is CallOperation call ? call.Name : null);
            if (instruction.Node is IPlanGroup group)
            {
                foreach (PlanSequence child in group.Sequences)
                {
                    foreach (ExecutorNodeInfo node in EnumerateNodes(plan, child, ancestors))
                    {
                        yield return node;
                    }
                }
            }
            ancestors.Remove(instruction.Node);
        }
    }

    internal readonly record struct BuilderCheckpoint(PlanSequence Sequence, int ItemCount, int SlotCount);
}

/// <summary>Describes one item in an executor plan and can be passed back to the builder for editing.</summary>
public sealed class ExecutorNodeInfo
{
    internal ExecutorNodeInfo(ExecutorBuilder plan, PlanInstruction item, string kind, string? method)
    {
        Plan = plan;
        Item = item;
        Kind = kind;
        Name = method;
    }

    /// <summary>Gets the plan item kind.</summary>
    public string Kind
    {
        get;
    }

    /// <summary>Gets the symbolic operation name, when applicable.</summary>
    public string? Name
    {
        get;
    }

    /// <summary>Gets or sets the operation's binding-failure policy.</summary>
    public ExecutorRequirement Requirement
    {
        get => Item.Requirement;
        set => Item.Requirement = value;
    }

    public IPlanNode Node => Item.Node;
    internal ExecutorBuilder Plan
    {
        get;
    }
    internal PlanInstruction Item
    {
        get;
    }
}
