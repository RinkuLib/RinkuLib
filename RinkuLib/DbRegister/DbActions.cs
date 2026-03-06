using System.Data;
using System.Data.Common;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbRegister;
internal static class SharedLock {
    internal static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Lock = new();
}
/// <summary>
/// Provide a dictionary of possible db actions to make on a type instance
/// </summary>
public static class DbActions<T> {
    static DbActions() {
        var type = typeof(T);
        var actionMakers = type.GetCustomAttributes<ActionMaker>();
        List<string> names = [];
        List<IDbAction<T>> actions = [];
        List<bool> isDefaults = [];
        foreach (var actionMaker in actionMakers) {
            var (name, action, def) = actionMaker.MakeAction<T>(null);
            names.Add(name);
            actions.Add(action);
            isDefaults.Add(def);
        }

        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        foreach (var member in members) {
            var actionMaker = member.GetCustomAttribute<ActionMaker>();
            if (actionMaker is null)
                continue;
            var (name, action, def) = actionMaker.MakeAction<T>(member);
            names.Add(name);
            actions.Add(action);
            isDefaults.Add(def);
        }
        var mapper = Mapper.GetMapper(names);
        if (mapper.Count != names.Count)
            for (var i = 0; i < names.Count; i++)
                if (mapper.GetKey(i) != names[i])
                    throw new DuplicateNameException($"the name {names[i]} exist twice or more");
        Mapper = mapper;
        _actions = [.. actions];
        IsDefault = [.. isDefaults];
    }
    /// <summary>
    /// The mapper of the actions
    /// </summary>
    public static Mapper Mapper { get; private set; } = Mapper.GetEmptyMapper();
    /// <summary>
    /// Tha actual actions
    /// </summary>
    public static ReadOnlySpan<IDbAction<T>> Actions => _actions;
    internal static IDbAction<T>[] _actions = [];
    internal static bool[] IsDefault = [];
    /// <inheritdoc/>
    public static ReadOnlySpan<string> Keys => Mapper.Keys;
    /// <inheritdoc/>
    public static int Count => Mapper.Count;
    /// <inheritdoc/>
    public static void UpdateAll(Mapper mapper, IDbAction<T>[] actions, bool[] isDefault) {
        if (mapper.Count != actions.Length || mapper.Count != isDefault.Length)
            throw new Exception($"all parameters must be of the same length {nameof(mapper)} : {mapper.Count}, {nameof(actions)} : {actions.Length}, {nameof(isDefault)} : {isDefault.Length}");

        lock (SharedLock.Lock) {
            Mapper.Dispose();
            Mapper = mapper;
            _actions = actions;
            IsDefault = isDefault;
        }
    }
    /// <inheritdoc/>
    public static void UpdateIsDefault(int idx, bool isDefault) {
        if (idx < 0 || idx >= Mapper.Count)
            throw new IndexOutOfRangeException();
        IsDefault[idx] = isDefault;
    }
    /// <inheritdoc/>
    public static void AddOrUpdate(string key, IDbAction<T> action, bool isDefault) {
        lock (SharedLock.Lock) {
            var idx = Mapper.GetIndex(key);
            if (idx >= 0) {
                _actions[idx] = action;
                IsDefault[idx] = isDefault;
                return;
            }
            var mapper = Mapper.GetMapper([..Mapper.Keys, key]);
            Mapper.Dispose();
            Mapper = mapper;
            _actions = [.. _actions, action];
            IsDefault = [.. IsDefault, isDefault];
        }
    }
    /// <inheritdoc/>
    public static IDbAction<T> RemoveAt(int idx) {
        lock (SharedLock.Lock) {
            if (idx < 0 || idx >= Mapper.Count)
                throw new IndexOutOfRangeException();
            var oldKeys = Mapper.Keys;
            int newCount = oldKeys.Length - 1;

            string[] newKeys = new string[newCount];
            var newActions = new IDbAction<T>[newCount];
            var newIsDefault = new bool[newCount];

            if (idx > 0) {
                oldKeys[..idx].CopyTo(newKeys);
                _actions.AsSpan(0, idx).CopyTo(newActions);
                IsDefault.AsSpan(0, idx).CopyTo(newIsDefault);
            }
            if (idx < newCount) {
                oldKeys[(idx + 1)..].CopyTo(newKeys.AsSpan(idx));
                _actions.AsSpan(idx + 1).CopyTo(newActions.AsSpan(idx));
                IsDefault.AsSpan(idx + 1).CopyTo(newIsDefault.AsSpan(idx));
            }

            var newMapper = Mapper.GetMapper(newKeys);
            Mapper.Dispose();

            var oldVal = _actions[idx];
            Mapper = newMapper;
            _actions = newActions;
            IsDefault = newIsDefault;

            return oldVal;
        }
    }
}
/// <summary>Provide extensions to execute actions an intance(s)</summary>
public static class DbActions {
    /// <summary>Execute the set of actions to do using the instance, will use the default actions</summary>
    public static void ExecuteDBActions<T>(ref T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            actions[i].ExecuteOnOne(ref instance, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instance</summary>
    public static void ExecuteDBActions<T>(ref T instance, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            actions[idx].ExecuteOnOne(ref instance, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instance, will use the default actions
    /// <para>Use the static ref version for value types</para>
    /// </summary>
    public static void ExecuteDBActions<T>(this T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where T : class
        => ExecuteDBActions(ref instance, cnn, transaction, timeout);
    /// <summary>Execute the set of actions to do using the instance
    /// <para>Use the static ref version for value types</para>
    /// </summary>
    public static void ExecuteDBActions<T>(this T instance, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null) where T : class
        => ExecuteDBActions(ref instance, cnn, actionsToDo, transaction, timeout);
    /// <summary>Execute the set of actions to do using the instances, will use the default actions</summary>
    public static void ExecuteDBActions<T>(this List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            actions[i].ExecuteOnMany(instances, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instances</summary>
    public static void ExecuteDBActions<T>(this List<T> instances, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            actions[idx].ExecuteOnMany(instances, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where T : class {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions using the instance</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this T instance, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where T : class {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions to do using the instances, will use the default actions</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnManyAsync(instances, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions to do using the instances</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this List<T> instances, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnManyAsync(instances, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }


    /// <summary>Execute the set of actions to do using the instance, will use the default actions</summary>
    public static void ExecuteDBActions<T>(ref T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            actions[i].ExecuteOnOne(ref instance, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instance</summary>
    public static void ExecuteDBActions<T>(ref T instance, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            actions[idx].ExecuteOnOne(ref instance, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instance, will use the default actions
    /// <para>Use the static ref version for value types</para>
    /// </summary>
    public static void ExecuteDBActions<T>(this T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) where T : class
        => ExecuteDBActions(ref instance, cnn, transaction, timeout);
    /// <summary>Execute the set of actions to do using the instance
    /// <para>Use the static ref version for value types</para>
    /// </summary>
    public static void ExecuteDBActions<T>(this T instance, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null) where T : class
        => ExecuteDBActions(ref instance, cnn, actionsToDo, transaction, timeout);
    /// <summary>Execute the set of actions to do using the instances, will use the default actions</summary>
    public static void ExecuteDBActions<T>(this List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            actions[i].ExecuteOnMany(instances, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions to do using the instances</summary>
    public static void ExecuteDBActions<T>(this List<T> instances, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            actions[idx].ExecuteOnMany(instances, cnn, transaction, timeout);
        }
    }
    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where T : class {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this T instance, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where T : class {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions to do using the instances, will use the default actions</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnManyAsync(instances, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <summary>Execute the set of actions to do using the instances</summary>
    public static async ValueTask ExecuteDBActionsAsync<T>(this List<T> instances, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnManyAsync(instances, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }



    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        if (typeof(T).IsValueType) {
            ExecuteDBActions(ref instance, cnn, transaction, timeout);
            return instance;
        }
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instance;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        if (typeof(T).IsValueType) {
            ExecuteDBActions(ref instance, cnn, transaction, timeout);
            return instance;
        }
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instance;
    }

    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        if (typeof(T).IsValueType) {
            ExecuteDBActions(ref instance, cnn, transaction, timeout);
            return instance;
        }
        var actions = DbActions<T>._actions;
        var isDefault = DbActions<T>.IsDefault;
        for (int i = 0; i < actions.Length; i++) {
            if (!isDefault[i])
                continue;
            await actions[i].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instance;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<T?> ExecuteDBActionsAsync<T>(this Task<T?> instanceTask, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instance = await instanceTask.ConfigureAwait(false);
        if (instance is null)
            return default;
        if (typeof(T).IsValueType) {
            ExecuteDBActions(ref instance, cnn, transaction, timeout);
            return instance;
        }
        var actions = DbActions<T>._actions;
        var mapper = DbActions<T>.Mapper;
        foreach (var item in actionsToDo) {
            var idx = mapper.GetIndex(item);
            if (idx < 0)
                continue;
            await actions[idx].ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
        return instance;
    }
    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        await instances.ExecuteDBActionsAsync(cnn, transaction, timeout, ct).ConfigureAwait(false);
        return instances;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, DbConnection cnn, IEnumerable<string> actionsToDo, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        await instances.ExecuteDBActionsAsync(cnn, transaction, timeout, ct).ConfigureAwait(false);
        return instances;
    }

    /// <summary>Execute the set of actions using the instance. Uses the default actions</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        await instances.ExecuteDBActionsAsync(cnn, transaction, timeout, ct).ConfigureAwait(false);
        return instances;
    }
    /// <summary>Execute the set of actions using the instance.</summary>
    public static async Task<List<T>> ExecuteDBActionsAsync<T>(this Task<List<T>> instancesTask, IDbConnection cnn, IEnumerable<string> actionsToDo, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instances = await instancesTask.ConfigureAwait(false);
        await instances.ExecuteDBActionsAsync(cnn, transaction, timeout, ct).ConfigureAwait(false);
        return instances;
    }
}