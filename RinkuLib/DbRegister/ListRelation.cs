using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.DbRegister;
/// <inheritdoc/>
public class ListRelation<TObj, TItem, TParam> : DbAction<TObj>, IWithParserAndParam<DBPair<TParam, TItem>>, IWithParserAndParam<TItem> {
    private object Getter;
    private object ListGetter;
    private object Setter;
    private readonly string Query;
    private readonly string KeyColumnName;
    private readonly int IndexOfVariable;
    private readonly int IndexOfKeyInSelect;
    /// <inheritdoc/>
    private ListRelation(string Query, string KeyColumnName, object Getter, object ListGetter, object Setter) {
        IndexOfKeyInSelect = Query.IndexOf("{0}");
        IndexOfVariable = Query.IndexOf("{1}");
        if (IndexOfKeyInSelect < 0 || IndexOfVariable < 0)
            throw new ArgumentException($"The {nameof(Query)} string must contains a placeholder \"{{0}}\" to insert the key column name : {KeyColumnName}, also need a placeholder \"{{1}}\" to insert the \" IN (@ID00,@ID01...)\" and to insert the \"= {ActionNameCache.BindParamName}\" logic, currently having : {Query}");
        if (IndexOfVariable > IndexOfKeyInSelect) {
            this.Query = $"{Query.AsSpan(0, IndexOfKeyInSelect)}{Query.AsSpan(IndexOfKeyInSelect + 3, IndexOfVariable - (IndexOfKeyInSelect + 3))}={ActionNameCache.BindParamName}{Query.AsSpan(IndexOfVariable + 3)}";
            IndexOfVariable -= 3;
        }
        else {
            this.Query = $"{Query.AsSpan(0, IndexOfVariable)}={ActionNameCache.BindParamName}{Query.AsSpan(IndexOfVariable + 3)}{Query.AsSpan(IndexOfVariable + 3, IndexOfKeyInSelect - (IndexOfVariable + 3))}";
            IndexOfKeyInSelect -= ActionNameCache.BindParamName.Length + 1;
        }
        this.Getter = Getter;
        this.ListGetter = ListGetter;
        this.Setter = Setter;
        this.KeyColumnName = $" {KeyColumnName}, ";
    }
    /// <inheritdoc/>
    public ListRelation(string Query, string KeyColumnName, Getter<TObj, TParam> Getter, Getter<TObj, List<TItem>> ListGetter, Setter<TObj, List<TItem>> Setter)
        : this(Query, KeyColumnName, (object)Getter, ListGetter, Setter) { }
    /// <inheritdoc/>
    public ListRelation(string Query, string KeyColumnName, StructGetter<TObj, TParam> Getter, StructGetter<TObj, List<TItem>> ListGetter, StructSetter<TObj, List<TItem>> Setter)
        : this(Query, KeyColumnName, (object)Getter, ListGetter, Setter) { }
    /// <inheritdoc/>
    public Func<DbDataReader, TItem>? Parser { get; set; }
    Func<DbDataReader, DBPair<TParam, TItem>>? IWithParser<DBPair<TParam, TItem>>.Parser { set => ParserPair = value; }
    /// <inheritdoc/>
    public Func<DbDataReader, DBPair<TParam, TItem>>? ParserPair { get; set; }
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; set; }
    CommandBehavior IWithParser<DBPair<TParam, TItem>>.Behavior { set => Behavior = value; }
    /// <inheritdoc/>
    public DbParamInfo ParamInfo { get; set; } = InferedDbParamCache.Instance;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private TParam GetParam(ref TObj inst) => (typeof(TObj).IsValueType
        ? Unsafe.As<object, StructGetter<TObj, TParam>>(ref Getter)(ref inst)
        : Unsafe.As<object, Getter<TObj, TParam>>(ref Getter)(inst)) ?? throw new Exception($"The \"ID\" value was null");
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private List<TItem> GetList(ref TObj inst) => typeof(TObj).IsValueType
        ? Unsafe.As<object, StructGetter<TObj, List<TItem>>>(ref ListGetter)(ref inst)
        : Unsafe.As<object, Getter<TObj, List<TItem>>>(ref ListGetter)(inst);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private void SetList(ref TObj inst, List<TItem> list) {
        if (typeof(TObj).IsValueType)
            Unsafe.As<object, StructSetter<TObj, List<TItem>>>(ref Setter)(ref inst, list);
        else
            Unsafe.As<object, Setter<TObj, List<TItem>>>(ref Setter)(inst, list);
    }
    /// <inheritdoc/>
    public override void ExecuteOnOne(ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        SetList(ref instance, items);
    }
    /// <inheritdoc/>
    public override void ExecuteOnOne(ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = Parser is null
            ? cmd.QueryAllBuffered<CacheOneItem<TItem>, TItem>(new(this), false)
            : cmd.QueryAllBuffered<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false);
        SetList(ref instance, items);
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnOneAsync(TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        SetList(ref instance, items);
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnOneAsync(TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        var items = await (Parser is null
            ? cmd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
            : cmd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
        SetList(ref instance, items);
    }
    /// <inheritdoc/>
    public override void ExecuteOnMany(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        var count = instancesSpan.Length;
        if (count == 0)
            return;
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, instancesSpan.Length);
        if (count > 100)
            count = 100;
        for (var i = 0; i < count; i++)
            ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instancesSpan[i])!);
        if (instancesSpan.Length > 100)
            for (int i = 100; i < instancesSpan.Length; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instancesSpan[i])!);
        var items = ParserPair is null
            ? cmd.QueryAll<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false)
            : cmd.QueryAll<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false);

        using var enumerator = items.GetEnumerator();
        bool hasMore = enumerator.MoveNext();
        var comparer = EqualityComparer<TParam>.Default;
        for (int i = 0; i < instancesSpan.Length; i++) {
            ref TObj instance = ref instancesSpan[i];
            TParam currentKey = GetParam(ref instance)!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = enumerator.MoveNext();
            }
            SetList(ref instance, list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override void ExecuteOnMany(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        var count = instancesSpan.Length;
        if (count == 0)
            return;
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, instancesSpan.Length);
        if (count > 100)
            count = 100;
        for (var i = 0; i < count; i++)
            ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instancesSpan[i])!);
        if (instancesSpan.Length > 100)
            for (int i = 100; i < instancesSpan.Length; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instancesSpan[i])!);
        var items = ParserPair is null
            ? cmd.QueryAll<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false)
            : cmd.QueryAll<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false);

        using var enumerator = items.GetEnumerator();
        bool hasMore = enumerator.MoveNext();
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < instancesSpan.Length; i++) {
            ref TObj instance = ref instancesSpan[i];
            TParam currentKey = GetParam(ref instance)!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = enumerator.MoveNext();
            }
            SetList(ref instance, list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnManyAsync(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        var count = instancesSpan.Length;
        if (count == 0)
            return;
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, instancesSpan.Length);
        if (count > 100)
            count = 100;
        for (var i = 0; i < count; i++)
            ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instancesSpan[i])!);
        if (instancesSpan.Length > 100)
            for (int i = 100; i < instancesSpan.Length; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instancesSpan[i])!);
        var items = ParserPair is null
            ? cmd.QueryAllAsync<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false, ct)
            : cmd.QueryAllAsync<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false, ct);

        await using var enumerator = items.GetAsyncEnumerator(ct);
        bool hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < instances.Count; i++) {
            TParam currentKey = GetParam(ref CollectionsMarshal.AsSpan(instances)[i])!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            SetList(ref CollectionsMarshal.AsSpan(instances)[i], list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnManyAsync(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        var count = instancesSpan.Length;
        if (count == 0)
            return;
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, instancesSpan.Length);
        if (count > 100)
            count = 100;
        for (var i = 0; i < count; i++)
            ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instancesSpan[i])!);
        if (instancesSpan.Length > 100)
            for (int i = 100; i < instancesSpan.Length; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instancesSpan[i])!);
        var items = ParserPair is null
            ? cmd.QueryAllAsync<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false, ct)
            : cmd.QueryAllAsync<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false, ct);

        await using var enumerator = items.GetAsyncEnumerator(ct);
        bool hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < instances.Count; i++) {
            TParam currentKey = GetParam(ref CollectionsMarshal.AsSpan(instances)[i])!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            SetList(ref CollectionsMarshal.AsSpan(instances)[i], list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnOne(int nameStart, string actionName, ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var list = GetList(ref instance);
        if (list is null || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        if (startNextSegment == 0) {
            action.ExecuteOnMany(list, cnn, transaction, timeout);
            return;
        }
        action.FowardExecuteOnMany(startNextSegment, actionName, list, cnn, transaction, timeout);
    }
    /// <inheritdoc/>
    public override ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var list = GetList(ref instance);
        if (list is null || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return default;
        if (startNextSegment == 0)
            return action.ExecuteOnManyAsync(list, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNextSegment, actionName, list, cnn, transaction, timeout, ct);
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnOne(int nameStart, string actionName, ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var list = GetList(ref instance);
        if (list is null || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        if (startNextSegment == 0) {
            action.ExecuteOnMany(list, cnn, transaction, timeout);
            return;
        }
        action.FowardExecuteOnMany(startNextSegment, actionName, list, cnn, transaction, timeout);
    }
    /// <inheritdoc/>
    public override ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var list = GetList(ref instance);
        if (list is null || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return default;
        if (startNextSegment == 0)
            return action.ExecuteOnManyAsync(list, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNextSegment, actionName, list, cnn, transaction, timeout, ct);
    }

    /// <inheritdoc/>
    public override void FowardExecuteOnMany(int nameStart, string actionName, List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        for (int i = 0; i < instancesSpan.Length; i++) {
            var list = GetList(ref instancesSpan[i]);
            if (list is null)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(list, cnn, transaction, timeout);
                return;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, list, cnn, transaction, timeout);
        }
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnMany(int nameStart, string actionName, List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var instancesSpan = CollectionsMarshal.AsSpan(instances);
        for (int i = 0; i < instancesSpan.Length; i++) {
            var list = GetList(ref instancesSpan[i]);
            if (list is null)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(list, cnn, transaction, timeout);
                return;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, list, cnn, transaction, timeout);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        for (int i = 0; i < instances.Count; i++) {
            var list = GetList(ref CollectionsMarshal.AsSpan(instances)[i]);
            if (list is null)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(list, cnn, transaction, timeout);
                return;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, list, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        for (int i = 0; i < instances.Count; i++) {
            var list = GetList(ref CollectionsMarshal.AsSpan(instances)[i]);
            if (list is null)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(list, cnn, transaction, timeout);
                return;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, list, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>A factory builder to make a populate list</summary>
public static class PopulateListActionFactory {
    /// <summary>A factory builder to make a populate list</summary>
    public static object Build(Type tObj, MemberInfo listMember, MemberInfo idMember, string query, string keyColumnName, MemberInfo? listGetterMember = null) {
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
        listGetterMember ??= listMember;
        if (listType != (listGetterMember switch {
            FieldInfo fi => fi.FieldType,
            PropertyInfo p => p.PropertyType,
            MethodInfo m => m.ReturnType,
            _ => throw new NotSupportedException($"Unsupported List member: {listGetterMember.MemberType}")
        }))
            throw new NotSupportedException("the type of the setter and getter of the list does not match");


        if (!listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(List<>))
            throw new ArgumentException($"Member must be List<T>. Found: {listType.Name}");

        Type tItem = listType.GetGenericArguments()[0];

        Type actionType = typeof(ListRelation<,,>).MakeGenericType(tObj, tItem, tParam);

        object getter = AccessorFactory.CreateGetter(tObj, tParam, idMember);
        object listGetter = AccessorFactory.CreateGetter(tObj, listType, listGetterMember);
        object setter = AccessorFactory.CreateSetter(tObj, listType, listMember);

        return Activator.CreateInstance(actionType, query, keyColumnName, getter, listGetter, setter)!;
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