using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.InteropServices;
using RinkuLib.Commands;
using RinkuLib.Queries;

namespace RinkuLib.DbRegister;
/// <inheritdoc/>
public class PopulateListInClassAction<TObj, TItem, TParam> : IWithParserAndParam<TItem>, IDbAction<TObj> where TObj : class {
    private readonly Getter<TObj, TParam> Getter;
    private readonly Setter<TObj, List<TItem>> Setter;
    private readonly string Query;
    /// <inheritdoc/>
    public PopulateListInClassAction(string Query, Getter<TObj, TParam> Getter, Setter<TObj, List<TItem>> Setter) {
        if (!Query.Contains(ActionNameCache.BindParamName))
            throw new ArgumentException($"The {nameof(Query)} string must contains a variable named {ActionNameCache.BindParamName}");
        this.Getter = Getter;
        this.Setter = Setter;
        this.Query = Query;
    }
    /// <inheritdoc/>
    public Func<DbDataReader, TItem>? Parser { get; set; }
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; set; }
    /// <inheritdoc/>
    public DbParamInfo ParamInfo { get; set; } = InferedDbParamCache.Instance;
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(instance, items);
    }
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(instance, items);
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnOneAsync(TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(instance, items);
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnOneAsync(TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(instance, items);
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        if (instancesSpan.Length == 0)
            return;
        ref TObj instance = ref instancesSpan[0];
        object? param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(instance, items);
        for (var i = 1; i < instancesSpan.Length; i++) {
            instance = ref instancesSpan[i];
            var p = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false);
            Setter(instance, items);
        }
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        if (instancesSpan.Length == 0)
            return;
        ref TObj instance = ref instancesSpan[0];
        object? param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(instance, items);
        for (var i = 1; i < instancesSpan.Length; i++) {
            instance = ref instancesSpan[i];
            var p = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false);
            Setter(instance, items);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (instances.Count == 0)
            return;
        TObj instance = instances[0];
        object? param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(instance, items);
        for (var i = 1; i < instances.Count; i++) {
            instance = instances[i];
            var p = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = await cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false, ct).ConfigureAwait(false);
            Setter(instance, items);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (instances.Count == 0)
            return;
        TObj instance = instances[0];
        object? param = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(instance, items);
        for (var i = 1; i < instances.Count; i++) {
            instance = instances[i];
            var p = Getter(instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = await cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false, ct).ConfigureAwait(false);
            Setter(instance, items);
        }
    }
}
/// <inheritdoc/>
public class PopulateListInStructAction<TObj, TItem, TParam> : IWithParserAndParam<TItem>, IDbAction<TObj> where TObj : struct {
    private readonly StructGetter<TObj, TParam> Getter;
    private readonly StructSetter<TObj, List<TItem>> Setter;
    private readonly string Query;
    /// <inheritdoc/>
    public PopulateListInStructAction(string Query, StructGetter<TObj, TParam> Getter, StructSetter<TObj, List<TItem>> Setter) {
        if (!Query.Contains(ActionNameCache.BindParamName))
            throw new ArgumentException($"The {nameof(Query)} string must contains a variable named {ActionNameCache.BindParamName}");
        this.Getter = Getter;
        this.Setter = Setter;
        this.Query = Query;
    }
    /// <inheritdoc/>
    public Func<DbDataReader, TItem>? Parser { get; set; }
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; set; }
    /// <inheritdoc/>
    public DbParamInfo ParamInfo { get; set; } = InferedDbParamCache.Instance;
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(ref instance, items);
    }
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(ref instance, items);
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnOneAsync(TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(ref instance, items);
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnOneAsync(TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(ref instance, items);
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        if (instancesSpan.Length == 0)
            return;
        ref TObj instance = ref instancesSpan[0];
        object? param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(ref instance, items);
        for (var i = 1; i < instancesSpan.Length; i++) {
            instance = ref instancesSpan[i];
            var p = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false);
            Setter(ref instance, items);
        }
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        if (instancesSpan.Length == 0)
            return;
        ref TObj instance = ref instancesSpan[0];
        object? param = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        Setter(ref instance, items);
        for (var i = 1; i < instancesSpan.Length; i++) {
            instance = ref instancesSpan[i];
            var p = Getter(ref instance) ?? throw new Exception($"The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false);
            Setter(ref instance, items);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (instances.Count == 0)
            return;
        object? param = Getter(ref CollectionsMarshal.AsSpan(instances)[0])
            ?? throw new Exception("The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(ref CollectionsMarshal.AsSpan(instances)[0], items);
        for (var i = 1; i < instances.Count; i++) {
            var p = Getter(ref CollectionsMarshal.AsSpan(instances)[i])
            ?? throw new Exception("The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = await cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false, ct).ConfigureAwait(false);
            Setter(ref CollectionsMarshal.AsSpan(instances)[i], items);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (instances.Count == 0)
            return;
        object? param = Getter(ref CollectionsMarshal.AsSpan(instances)[0])
            ?? throw new Exception("The \"ID\" value was null");
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        Setter(ref CollectionsMarshal.AsSpan(instances)[0], items);
        for (var i = 1; i < instances.Count; i++) {
            var p = Getter(ref CollectionsMarshal.AsSpan(instances)[i])
            ?? throw new Exception("The \"ID\" value was null");
            ParamInfo.Update(cmd, ref param, p);
            items = await cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser!, Behavior), false, ct).ConfigureAwait(false);
            Setter(ref CollectionsMarshal.AsSpan(instances)[i], items);
        }
    }
}

/// <summary>A factory builder to make a populate list</summary>
public static class PopulateListActionFactory {
    /// <summary>A factory builder to make a populate list</summary>
    public static object Build(Type tObj, MemberInfo listMember, MemberInfo idMember, string query) {
        Type tParam = idMember switch {
            FieldInfo fi => fi.FieldType,
            PropertyInfo p => p.PropertyType,
            MethodInfo m => m.ReturnType,
            _ => throw new NotSupportedException($"Unsupported ID member: {idMember.MemberType}")
        };

        Type listType = listMember switch {
            FieldInfo f => f.FieldType,
            PropertyInfo p => p.PropertyType,
            MethodInfo m => GetListTypeFromMethod(m, tObj),
            _ => throw new NotSupportedException($"Unsupported List member: {listMember.MemberType}")
        };

        if (!listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(List<>))
            throw new ArgumentException($"Member must be List<T>. Found: {listType.Name}");

        Type tItem = listType.GetGenericArguments()[0];

        Type actionType = (tObj.IsValueType
            ? typeof(PopulateListInStructAction<,,>)
            : typeof(PopulateListInClassAction<,,>))
            .MakeGenericType(tObj, tItem, tParam);

        object getter = AccessorFactory.CreateGetter(tObj, tParam, idMember);
        object setter = AccessorFactory.CreateSetter(tObj, listType, listMember);

        return Activator.CreateInstance(actionType, query, getter, setter)!;
    }

    internal static Type GetListTypeFromMethod(MethodInfo m, Type tObj) {
        ParameterInfo[] pars = m.GetParameters();
        if (m.IsStatic) {
            Type expectedFirstParam = tObj.IsValueType ? tObj.MakeByRefType() : tObj;
            if (pars.Length != 2 || pars[0].ParameterType != expectedFirstParam) 
                throw new ArgumentException($"Static setter {m.Name} must have 2 params: ({(tObj.IsValueType ? "ref " : "")}{tObj.Name}, List<T>)");
            return pars[1].ParameterType;
        }
        if (pars.Length != 1)
            throw new ArgumentException($"Instance setter {m.Name} must have exactly 1 param: (List<T>)");
        return pars[0].ParameterType;
    }
}