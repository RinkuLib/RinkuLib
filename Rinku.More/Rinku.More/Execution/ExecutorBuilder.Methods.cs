namespace Rinku;

public partial class ExecutorBuilder
{
    public ExecutorBuilder Implement<TContract>(string methodName, ExecutorBuilder method)
    {
        ArgumentNullException.ThrowIfNull(method);
        Type contract = typeof(TContract);
        ValidateMethod(contract, methodName, method.Data.Inputs.Count);
        Data.MethodContracts.Add(contract);
        if (!Data.Methods.TryAdd((methodName, method.Data.Inputs.Count), method.Data))
        {
            throw new ArgumentException("A plan is already recorded for this method. Edit its builder or merge explicitly.", nameof(methodName));
        }
        return this;
    }

    public ExecutorBuilder DefineMethod<TContract>(string methodName, params Type[] authoringInputTypes)
    {
        Type contract = typeof(TContract);
        ValidateMethod(contract, methodName, authoringInputTypes.Length);
        Data.MethodContracts.Add(contract);
        var key = (methodName, authoringInputTypes.Length);
        if (!Data.Methods.TryGetValue(key, out var method))
        {
            method = new ExecutorScope(authoringInputTypes);
            Data.Methods.Add(key, method);
        }
        return new ExecutorBuilder(method);
    }

    private static void ValidateMethod(Type contract, string name, int arity)
    {
        if (!contract.IsInterface || !contract.GetInterfaces().Prepend(contract).SelectMany(type => type.GetMethods()).Any(method => method.Name == name && !method.IsStatic && method.GetParameters().Length == arity))
        {
            throw new ArgumentException("The contract must declare a matching instance method.", nameof(name));
        }
    }

    public ExecutorBuilder Callback<TDelegate>(out Variable<TDelegate> callback, ExecutorScope body, params Variable[] captures) where TDelegate : Delegate {
        callback = new(Data, RecordCallback(typeof(TDelegate), body, captures));
        return this;
    }
    public ExecutorBuilder Callback(Type delegateType, out Variable callback, ExecutorScope body, params Variable[] captures) {
        callback = global::Rinku.Variable.Create(Data, RecordCallback(delegateType, body, captures));
        return this;
    }
    private PlanValue RecordCallback(Type delegateType, ExecutorScope body, Variable[] captures) {
        ArgumentNullException.ThrowIfNull(body);
        if (!typeof(Delegate).IsAssignableFrom(delegateType) || delegateType.GetMethod(nameof(Action.Invoke)) is not { } signature)
            throw new ArgumentException("A concrete delegate type is required.", nameof(delegateType));
        var nested = new ExecutorBuilder(captures.Select(value => value.LogicalType).Concat(signature.GetParameters().Select(parameter => parameter.ParameterType)).ToArray());
        nested.Data.Output = ExecutorCompiler.ImportGraph(nested.Data, body, nested.Data.Inputs.Take(body.Inputs.Count).Select(OperationArgument.FromValue).ToArray(), nested.Data.Body);
        PlanValue result = Data.CreateValue(delegateType);
        Data.PublishValue(result, null);
        Data.Add(_sequence, new DelegateOperation(delegateType, nested.Data, captures.Select(value => ToOperationArgument(value)).ToArray(), result), outputs: [global::Rinku.Variable.Create(Data, result)]);
        return result;
    }

    public ExecutorBuilder Field<T>(out Variable<T> field) {
        field = Variable<T>();
        Data.Add(_sequence, new InstanceFieldOperation(null, field.Local), outputs: [field]);
        return this;
    }

    public ExecutorBuilder Field<T>(out Variable<T> field, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        field = Variable<T>();
        Data.Add(_sequence, new InstanceFieldOperation(name, field.Local), outputs: [field]);
        return this;
    }
}
