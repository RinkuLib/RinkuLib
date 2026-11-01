using System.Data.Common;
using System.Reflection;
using System.Linq.Expressions;
using Rinku.Mapping.Conversion;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Committers;

/// <summary>A processing outcome, optionally carrying a value for postprocessing and pending parameters.</summary>
public readonly struct CommitDecision<T> {
    private CommitDecision(bool accepted, bool hasValue, T value) { Accepted = accepted; HasValue = hasValue; Value = value; }
    public bool Accepted { get; }
    public bool HasValue { get; }
    public T Value { get; }
    public static CommitDecision<T> Reject() => default;
    public static CommitDecision<T> Continue() => new(true, false, default!);
    public static CommitDecision<T> Success(T value) => new(true, true, value);
}

/// <summary>A typed operation composed from processing and postprocessing delegates.</summary>
public sealed class CommitOperation<TItem, TResult>(Func<TItem, DbCommitState, CommitDecision<TResult>> process) {
    private ResultParameter[]? _parameters = [];
    internal CommitOperation(Func<TItem, DbCommitState, CommitDecision<TResult>> process, ResultParameter[]? parameters) : this(process) => _parameters = parameters;
    internal CommitOperation<TItem, TResult> WithUnnamedOutputs() { _parameters = null; return this; }
    internal CommitOperation<TItem, TResult> WithOutputs(params CommandOutput[] values) {
        _parameters = new ResultParameter[values.Length];
        for (int index = 0; index < values.Length; index++) _parameters[index] = new(values[index].Name, values[index].ValueType, "Item" + (index + 2));
        return this;
    }
    public CommitOperation<TItem, TResult> Named(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _parameters = [new(name, typeof(TResult), null)];
        return this;
    }
    private ResultParameter[] MappingParameters() => _parameters ?? throw new InvalidOperationException("Name command outputs before attaching the operation to a committer.");
    internal Func<TItem, DbCommitState, bool> CompileProcessing() {
        Action<TItem, TResult>? write = null;
        var parameters = MappingParameters();
        if (parameters.Length != 0) {
            if (!typeof(ITrackingParameters).IsAssignableFrom(typeof(TItem)))
                throw new ArgumentException($"{typeof(TItem)} must expose pending tracking parameters.");
            var item = Expression.Parameter(typeof(TItem), "item");
            var result = Expression.Parameter(typeof(TResult), "result");
            var bag = Expression.Variable(typeof(TrackingParameters), "bag");
            var pending = Expression.Property(Expression.Convert(item, typeof(ITrackingParameters)), nameof(ITrackingParameters.PendingParameters));
            var statements = new List<Expression> { Expression.Assign(bag, Expression.Coalesce(pending, Expression.Assign(pending, Expression.New(bag.Type)))) };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters) {
                if (!names.Add(parameter.Name)) throw new ArgumentException($"Duplicate pending value '{parameter.Name}'.");
                var value = parameter.MemberName is null ? (Expression)result : Expression.Field(result, parameter.MemberName);
                statements.Add(Expression.Call(bag, bag.Type.GetMethod("Set")!.MakeGenericMethod(parameter.ValueType), Expression.Constant(parameter.Name), value));
            }
            statements.Add(Expression.Empty());
            write = Expression.Lambda<Action<TItem, TResult>>(Expression.Block([bag], statements), item, result).Compile();
        }
        return (item, state) => {
            var result = Execute(item, state);
            if (!result.Accepted) return false;
            if (result.HasValue) write?.Invoke(item, result.Value);
            return true;
        };
    }
    internal CommitDecision<TResult> Execute(TItem item, DbCommitState connection) => process(item, connection);
    public CommitOperation<TItem, TResult> BeforeEach(Action<TItem> before) => BeforeEach(item => { before(item); return true; });
    public CommitOperation<TItem, TResult> BeforeEach(Func<TItem, bool> before) => new((item, connection) => before(item) ? Execute(item, connection) : CommitDecision<TResult>.Reject(), _parameters);
    public CommitOperation<TItem, TResult> AfterEach(Action<TItem, TResult> after) => AfterEach((item, result) => { after(item, result); return true; });
    public CommitOperation<TItem, TResult> AfterEach(Func<TItem, TResult, bool> after) => new((item, connection) => {
        var result = Execute(item, connection);
        return result.Accepted && result.HasValue && !after(item, result.Value) ? CommitDecision<TResult>.Reject() : result;
    }, _parameters);
    public CommitOperation<TItem, TNext> Then<TNext>(Func<TItem, TResult, CommitDecision<TNext>> next) => new((item, connection) => {
        var result = Execute(item, connection);
        return !result.Accepted ? CommitDecision<TNext>.Reject() : result.HasValue ? next(item, result.Value) : CommitDecision<TNext>.Continue();
    });
    public CommitOperation<TItem, TNext> Select<TNext>(Func<TItem, TResult, TNext> select) => Then<TNext>((item, result) => CommitDecision<TNext>.Success(select(item, result)));
}


public static class CommitOperation {
    private delegate DbCommand OneOutput<TItem>(TItem item, DbConnection connection, out DbParameter output);
    private delegate DbCommand TwoOutputs<TItem>(TItem item, DbConnection connection, out DbParameter first, out DbParameter second);
    private delegate DbCommand ThreeOutputs<TItem>(TItem item, DbConnection connection, out DbParameter first, out DbParameter second, out DbParameter third);

    public static CommitOperation<TItem, (TResult Result, T1? Output)> Command<TItem, TResult, T1>(Delegate factory, Func<DbCommand, TResult> read, CommandOutput<T1> output) => Command<TItem, TResult, T1>(factory, read, OutputSources(factory.Method, [output])).WithOutputs(output);
    public static CommitOperation<TItem, (TResult Result, T1? Output1, T2? Output2)> Command<TItem, TResult, T1, T2>(Delegate factory, Func<DbCommand, TResult> read, CommandOutput<T1> first, CommandOutput<T2> second) => Command<TItem, TResult, T1, T2>(factory, read, OutputSources(factory.Method, [first, second])).WithOutputs(first, second);
    public static CommitOperation<TItem, (TResult Result, T1? Output1, T2? Output2, T3? Output3)> Command<TItem, TResult, T1, T2, T3>(Delegate factory, Func<DbCommand, TResult> read, CommandOutput<T1> first, CommandOutput<T2> second, CommandOutput<T3> third) => Command<TItem, TResult, T1, T2, T3>(factory, read, OutputSources(factory.Method, [first, second, third])).WithOutputs(first, second, third);

    /// <summary>Maps a command factory and reads its first output as the requested value type after execution.</summary>
    public static CommitOperation<TItem, (TResult Result, T1? Output)> Command<TItem, TResult, T1>(Delegate factory, Func<DbCommand, TResult> read, params string[] outputNames) {
        var mapped = MethodCaller.Create<OneOutput<TItem>>(factory, OutputBindings(factory.Method, 1, outputNames));
        return Process<TItem, (TResult, T1?)>((item, state) => {
            using var command = mapped(item, state.Connection, out var output);
            command.Transaction = state.Transaction;
            TResult result = read(command);
            return (result, output.Value.Parse<T1>());
        }).WithUnnamedOutputs();
    }
    /// <summary>Maps a command factory and reads two typed output values after execution.</summary>
    public static CommitOperation<TItem, (TResult Result, T1? Output1, T2? Output2)> Command<TItem, TResult, T1, T2>(Delegate factory, Func<DbCommand, TResult> read, params string[] outputNames) {
        var mapped = MethodCaller.Create<TwoOutputs<TItem>>(factory, OutputBindings(factory.Method, 2, outputNames));
        return Process<TItem, (TResult, T1?, T2?)>((item, state) => {
            using var command = mapped(item, state.Connection, out var first, out var second);
            command.Transaction = state.Transaction;
            TResult result = read(command);
            return (result, first.Value.Parse<T1>(), second.Value.Parse<T2>());
        }).WithUnnamedOutputs();
    }
    /// <summary>Maps a command factory and reads three typed output values after execution.</summary>
    public static CommitOperation<TItem, (TResult Result, T1? Output1, T2? Output2, T3? Output3)> Command<TItem, TResult, T1, T2, T3>(Delegate factory, Func<DbCommand, TResult> read, params string[] outputNames) {
        var mapped = MethodCaller.Create<ThreeOutputs<TItem>>(factory, OutputBindings(factory.Method, 3, outputNames));
        return Process<TItem, (TResult, T1?, T2?, T3?)>((item, state) => {
            using var command = mapped(item, state.Connection, out var first, out var second, out var third);
            command.Transaction = state.Transaction;
            TResult result = read(command);
            return (result, first.Value.Parse<T1>(), second.Value.Parse<T2>(), third.Value.Parse<T3>());
        }).WithUnnamedOutputs();
    }
    private static string[] OutputSources(MethodInfo factory, CommandOutput[] outputs) {
        var declared = new List<ParameterInfo>();
        foreach (var parameter in factory.GetParameters()) if (parameter.IsOut) declared.Add(parameter);
        var names = new string[outputs.Length];
        for (int index = 0; index < outputs.Length; index++) {
            if (outputs[index].SourceName is null && outputs.Length != declared.Count)
                throw new ArgumentException($"Select source names when choosing a subset of outputs from {factory}.", nameof(outputs));
            names[index] = outputs[index].SourceName ?? declared[index].Name!;
        }
        return names;
    }

    private static CallerParameter[] OutputBindings(MethodInfo method, int count, string[] names) {
        if (names.Length != 0 && names.Length != count)
            throw new ArgumentException($"Expected {count} output names.", nameof(names));
        var outputs = new List<ParameterInfo>();
        foreach (var parameter in method.GetParameters())
            if (parameter.IsOut) outputs.Add(parameter);
        if (names.Length == 0 && outputs.Count != count)
            throw new ArgumentException($"Factory {method} has {outputs.Count} outputs; select {count} explicitly.", nameof(method));
        var bindings = new CallerParameter[count + 1];
        bindings[0] = CallerParameter<DbConnection>.ByType();
        for (int index = 0; index < count; index++) {
            string name = names.Length == 0 ? outputs[index].Name! : names[index];
            ParameterInfo? output = null;
            foreach (var candidate in outputs)
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) { output = candidate; break; }
            if (output is null || output.ParameterType.GetElementType() != typeof(DbParameter))
                throw new ArgumentException($"'{name}' must identify a DbParameter output on {method}.", nameof(names));
            for (int previous = 1; previous <= index; previous++)
                if (string.Equals(bindings[previous].TargetName, name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Output '{name}' was selected more than once.", nameof(names));
            bindings[index + 1] = CallerParameter<DbParameter>.Named(name);
        }
        return bindings;
    }

    /// <summary>Runs a command factory as a non-query operation.</summary>
    public static CommitOperation<TItem, int> Command<TItem>(Delegate factory) => Command<TItem, int>(factory, static command => command.ExecuteNonQuery());

    public static CommitOperation<TItem, TResult> Command<TItem, TResult>(Delegate factory, Func<DbCommand, TResult> read) => Command(MethodCaller.Create<Func<TItem, DbConnection, DbCommand>>(factory), read);

    public static CommitOperation<TItem, TResult> Changes<TItem, TResult>(QueryCommand query, Func<DbCommand, TResult> read) where TItem : ITrackingChanges => Command<TItem, TResult>((item, connection) => query.GetChangesCommand(item, connection), read);
    public static CommitOperation<TItem, TResult> Process<TItem, TResult>(Func<TItem, DbCommitState, TResult> process) => new((item, connection) => CommitDecision<TResult>.Success(process(item, connection)));
    public static CommitOperation<TItem, TResult> Process<TItem, TInput, TResult>(Func<TItem, TInput> before, Func<TItem, TInput, DbCommitState, TResult> process) => Process<TItem, TResult>((item, connection) => process(item, before(item), connection));
    public static CommitOperation<TItem, TResult> Command<TItem, TResult>(Func<TItem, DbConnection, DbCommand> factory, Func<DbCommand, TResult> read) => Process<TItem, TResult>((item, connection) => { using var command = factory(item, connection.Connection); command.Transaction = connection.Transaction; return read(command); });
}
