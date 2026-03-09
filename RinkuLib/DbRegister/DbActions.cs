using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbRegister;
/// <summary>
/// Provide a dictionary of possible db actions to make on a type instance
/// </summary>
public static class DbActions<T> {
    static DbActions() {
        var type = typeof(T);
        var actionMakers = type.GetCustomAttributes<ActionMaker>();
        List<string> names = [];
        List<DbAction<T>> actions = [];
        foreach (var actionMaker in actionMakers) {
            var (name, action) = actionMaker.MakeAction<T>(null);
            names.Add(name);
            actions.Add(action);
        }

        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        foreach (var member in members) {
            actionMakers = member.GetCustomAttributes<ActionMaker>();
            foreach (var actionMaker in actionMakers) {
                var (name, action) = actionMaker.MakeAction<T>(member);
                names.Add(name);
                actions.Add(action);
            }
        }
        var mapper = Mapper.GetMapper(names);
        if (mapper.Count != names.Count)
            for (var i = 0; i < names.Count; i++)
                if (mapper.GetKey(i) != names[i])
                    throw new DuplicateNameException($"the name {names[i]} exist twice or more");
        Mapper = mapper;
        _actions = [.. actions];
    }
    /// <summary>
    /// The mapper of the actions
    /// </summary>
    public static Mapper Mapper { get; private set; } = Mapper.GetEmptyMapper();
    /// <summary>
    /// Tha actual actions
    /// </summary>
    public static ReadOnlySpan<DbAction<T>> Actions => _actions;
    internal static DbAction<T>[] _actions = [];
    /// <inheritdoc/>
    public static ReadOnlySpan<string> Keys => Mapper.Keys;
    /// <inheritdoc/>
    public static int Count => Mapper.Count;
    /// <inheritdoc/>
    public static void UpdateAll(Mapper mapper, DbAction<T>[] actions, bool[] isDefault) {
        lock (DbActions.Lock) {
            if (mapper.Count != actions.Length || mapper.Count != isDefault.Length)
                throw new Exception($"all parameters must be of the same length {nameof(mapper)} : {mapper.Count}, {nameof(actions)} : {actions.Length}, {nameof(isDefault)} : {isDefault.Length}");
            Mapper.Dispose();
            Mapper = mapper;
            _actions = actions;
        }
    }
    /// <inheritdoc/>
    public static void AddOrUpdate(string key, DbAction<T> action) {
        lock (DbActions.Lock) {
            var idx = Mapper.GetIndex(key);
            if (idx >= 0) {
                _actions[idx] = action;
                return;
            }
            var mapper = Mapper.GetMapper([..Mapper.Keys, key]);
            Mapper.Dispose();
            Mapper = mapper;
            _actions = [.. _actions, action];
        }
    }
    /// <inheritdoc/>
    public static DbAction<T> RemoveAt(int idx) {
        lock (DbActions.Lock) {
            if (idx < 0 || idx >= Mapper.Count)
                throw new IndexOutOfRangeException();
            var oldKeys = Mapper.Keys;
            int newCount = oldKeys.Length - 1;

            string[] newKeys = new string[newCount];
            var newActions = new DbAction<T>[newCount];

            if (idx > 0) {
                oldKeys[..idx].CopyTo(newKeys);
                _actions.AsSpan(0, idx).CopyTo(newActions);
            }
            if (idx < newCount) {
                oldKeys[(idx + 1)..].CopyTo(newKeys.AsSpan(idx));
                _actions.AsSpan(idx + 1).CopyTo(newActions.AsSpan(idx));
            }

            var newMapper = Mapper.GetMapper(newKeys);
            Mapper.Dispose();

            var oldVal = _actions[idx];
            Mapper = newMapper;
            _actions = newActions;

            return oldVal;
        }
    }
    /// <inheritdoc/>
    public static bool TryGetAction(string actionName, [MaybeNullWhen(false)] out DbAction<T> action) {
        var idx = Mapper.GetIndex(actionName);
        if (idx < 0) {
            action = null;
            return false;
        }
        action = _actions[idx];
        return true;
    }
    /// <summary>Try to get an action using a name and if its not a perfect match but a path using '.' will return a startNextSegment to the start of the next segment while returning the action that makes the fowarding</summary>
    public static bool TryGetAction(int nameStart, string actionName, [MaybeNullWhen(false)] out DbAction<T> action, out int startNextSegment) {
        var span = actionName.AsSpan(nameStart);
        var idx = Mapper.GetIndex(span);
        if (idx >= 0) {
            startNextSegment = 0;
            action = _actions[idx];
            return true;
        }
        var dotIdx = span.IndexOf('.');
        if (dotIdx < 0) {
            startNextSegment = 0;
            action = null;
            return false;
        }
        idx = Mapper.GetIndex(span[..dotIdx]);
        if (idx < 0) {
            startNextSegment = 0;
            action = null;
            return false;
        }
        startNextSegment = dotIdx + 1;
        action = _actions[idx];
        return true;
    }
}
/// <summary>Provide extensions to execute actions an intance(s)</summary>
public static class DbActions {
    internal static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Lock = new();

    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, DbConnection cnn, string[] actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        foreach (var item in actionsToDo) {
            if (!DbActions<T>.TryGetAction(0, item, out var action, out var startNext))
                continue;
            if (startNext == 0) {
                if (typeof(T).IsValueType)
                    action.ExecuteOnOne(ref instance, cnn, transaction, timeout);
                else
                    await action.ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
            }
            else {
                if (typeof(T).IsValueType)
                    action.FowardExecuteOnOne(startNext, item, ref instance, cnn, transaction, timeout);
                else
                    await action.FowardExecuteOnOneAsync(startNext, item, instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
            }
        }
        return instance;
    }

    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, IDbConnection cnn, string[] actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        foreach (var item in actionsToDo) {
            if (!DbActions<T>.TryGetAction(0, item, out var action, out var startNext))
                continue;
            if (startNext == 0) {
                if (typeof(T).IsValueType)
                    action.ExecuteOnOne(ref instance, cnn, transaction, timeout);
                else
                    await action.ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
            }
            else {
                if (typeof(T).IsValueType)
                    action.FowardExecuteOnOne(startNext, item, ref instance, cnn, transaction, timeout);
                else
                    await action.FowardExecuteOnOneAsync(startNext, item, instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
            }
        }
        return instance;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, DbConnection cnn, string[] actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        foreach (var item in actionsToDo) {
            if (!DbActions<T>.TryGetAction(0, item, out var action, out var startNext))
                continue;
            if (startNext == 0)
                await action.ExecuteOnManyAsync(new ListAccess<T>(instances), cnn, transaction, timeout, ct).ConfigureAwait(false);
            else
                await action.FowardExecuteOnManyAsync(startNext, item, new ListAccess<T>(instances), cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instances;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, IDbConnection cnn, string[] actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        foreach (var item in actionsToDo) {
            if (!DbActions<T>.TryGetAction(0, item, out var action, out var startNext))
                continue;
            if (startNext == 0)
                await action.ExecuteOnManyAsync(new ListAccess<T>(instances), cnn, transaction, timeout, ct).ConfigureAwait(false);
            else
                await action.FowardExecuteOnManyAsync(startNext, item, new ListAccess<T>(instances), cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instances;
    }
    /// <summary>Execute the set of actions using the instance. (Value types will act on copy only)</summary>
    public static void ExecuteDBAction<T>(this T instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return;
        if (startNext == 0)
            action.ExecuteOnOne(ref instance, cnn, transaction, timeout);
        else
            action.FowardExecuteOnOne(startNext, actionName, ref instance, cnn, transaction, timeout);
    }
    /// <summary>Execute the set of actions using the instance. (Value types will act on copy only)</summary>
    public static ValueTask ExecuteDBActionAsync<T>(this T instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return default;
        if (startNext == 0)
            return action.ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnOneAsync(startNext, actionName, instance, cnn, transaction, timeout, ct);
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static void ExecuteDBAction<T>(this List<T> instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return;
        if (startNext == 0)
            action.ExecuteOnMany(new ListAccess<T>(instance), cnn, transaction, timeout);
        else
            action.FowardExecuteOnMany(startNext, actionName, new ListAccess<T>(instance), cnn, transaction, timeout);
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static ValueTask ExecuteDBActionAsync<T>(this List<T> instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return default;
        if (startNext == 0)
            return action.ExecuteOnManyAsync(new ListAccess<T>(instance), cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNext, actionName, new ListAccess<T>(instance), cnn, transaction, timeout, ct);
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static void ExecuteDBAction<T>(this T[] instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return;
        if (startNext == 0)
            action.ExecuteOnMany(new ArrayAccess<T>(instance), cnn, transaction, timeout);
        else
            action.FowardExecuteOnMany(startNext, actionName, new ArrayAccess<T>(instance), cnn, transaction, timeout);
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static ValueTask ExecuteDBActionAsync<T>(this T[] instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return default;
        if (startNext == 0)
            return action.ExecuteOnManyAsync(new ArrayAccess<T>(instance), cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNext, actionName, new ArrayAccess<T>(instance), cnn, transaction, timeout, ct);
    }
}