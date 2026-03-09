using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.DbRegister;
/// <inheritdoc/>
public class CollectionRelation<TObj, TItem, TParam, TColAccessor> : DbAction<TObj>, IWithParserAndParam<DBPair<TParam, TItem>>, IWithParserAndParam<TItem> where TColAccessor : notnull, ICollectionRefAccessor<TItem> {
    private object Getter;
    private object AccessorGetter;
    private object Setter;
    private readonly string Query;
    private readonly string KeyColumnName;
    private readonly int IndexOfVariable;
    private readonly int IndexOfKeyInSelect;
    /// <inheritdoc/>
    private CollectionRelation(string Query, string KeyColumnName, object Getter, object ListGetter, object Setter) {
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
        this.AccessorGetter = ListGetter;
        this.Setter = Setter;
        this.KeyColumnName = $" {KeyColumnName}, ";
    }
    /// <inheritdoc/>
    public CollectionRelation(string Query, string KeyColumnName, Getter<TObj, TParam> Getter, Getter<TObj, TColAccessor> AccessorGetter, Setter<TObj, List<TItem>> Setter)
        : this(Query, KeyColumnName, (object)Getter, AccessorGetter, Setter) { }
    /// <inheritdoc/>
    public CollectionRelation(string Query, string KeyColumnName, StructGetter<TObj, TParam> Getter, StructGetter<TObj, TColAccessor> AccessorGetter, StructSetter<TObj, List<TItem>> Setter)
        : this(Query, KeyColumnName, (object)Getter, AccessorGetter, Setter) { }
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
    private TColAccessor GetAccess(ref TObj inst) => typeof(TObj).IsValueType
        ? Unsafe.As<object, StructGetter<TObj, TColAccessor>>(ref AccessorGetter)(ref inst)
        : Unsafe.As<object, Getter<TObj, TColAccessor>>(ref AccessorGetter)(inst);
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private void SetCollection(ref TObj inst, List<TItem> list) {
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
        SetCollection(ref instance, items);
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
        SetCollection(ref instance, items);
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
        SetCollection(ref instance, items);
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
        SetCollection(ref instance, items);
    }
    /// <inheritdoc/>
    public override void ExecuteOnMany<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            ExecuteOnOne(ref instances.GetAt(0), cnn, transaction, timeout);
            return;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        var items = ParserPair is null
            ? cmd.QueryAll<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false)
            : cmd.QueryAll<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false);

        using var enumerator = items.GetEnumerator();
        bool hasMore = enumerator.MoveNext();
        var comparer = EqualityComparer<TParam>.Default;
        for (int i = 0; i < count; i++) {
            ref TObj instance = ref instances.GetAt(i);
            TParam currentKey = GetParam(ref instance)!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = enumerator.MoveNext();
            }
            SetCollection(ref instance, list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override void ExecuteOnMany<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            ExecuteOnOne(ref instances.GetAt(0), cnn, transaction, timeout);
            return;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        var items = ParserPair is null
            ? cmd.QueryAll<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false)
            : cmd.QueryAll<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false);

        using var enumerator = items.GetEnumerator();
        bool hasMore = enumerator.MoveNext();
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < count; i++) {
            ref TObj instance = ref instances.GetAt(i);
            TParam currentKey = GetParam(ref instance)!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = enumerator.MoveNext();
            }
            SetCollection(ref instance, list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            using var cmdd = cnn.GetCommand(transaction, timeout);
            cmdd.CommandText = Query;
            var param = GetParam(ref instances.GetAt(0))!;
            ParamInfo.Use(ActionNameCache.BindParamName, cmdd, param);
            var list = await (Parser is null
                ? cmdd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
                : cmdd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
            SetCollection(ref instances.GetAt(0), list);
            return;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        var items = ParserPair is null
            ? cmd.QueryAllAsync<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false, ct)
            : cmd.QueryAllAsync<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false, ct);

        await using var enumerator = items.GetAsyncEnumerator(ct);
        bool hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < count; i++) {
            TParam currentKey = GetParam(ref instances.GetAt(i))!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            SetCollection(ref instances.GetAt(i), list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            using var cmdd = cnn.GetCommand(transaction, timeout);
            cmdd.CommandText = Query;
            var param = GetParam(ref instances.GetAt(0))!;
            ParamInfo.Use(ActionNameCache.BindParamName, cmdd, param);
            var list = await (Parser is null
                ? cmdd.QueryAllBufferedAsync<CacheOneItem<TItem>, TItem>(new(this), false, ct)
                : cmdd.QueryAllBufferedAsync<SchemaParser<TItem>, TItem>(new(Parser, Behavior), false, ct)).ConfigureAwait(false);
            SetCollection(ref instances.GetAt(0), list);
            return;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.BuildInClause(Query, KeyColumnName, IndexOfKeyInSelect, IndexOfVariable, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        var items = ParserPair is null
            ? cmd.QueryAllAsync<CacheOneItem<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(this), false, ct)
            : cmd.QueryAllAsync<SchemaParser<DBPair<TParam, TItem>>, DBPair<TParam, TItem>>(new(ParserPair, Behavior), false, ct);

        await using var enumerator = items.GetAsyncEnumerator(ct);
        bool hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
        var comparer = EqualityComparer<TParam>.Default;

        for (int i = 0; i < count; i++) {
            TParam currentKey = GetParam(ref instances.GetAt(i))!;
            var list = new List<TItem>();
            while (hasMore && comparer.Equals(enumerator.Current.Key, currentKey)) {
                list.Add(enumerator.Current.Value);
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            SetCollection(ref instances.GetAt(i), list);
        }
        if (hasMore)
            throw new InvalidOperationException("Database returned keys not present in the input collection.");
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnOne(int nameStart, string actionName, ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var access = GetAccess(ref instance);
        if (!access.HasValues || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        if (startNextSegment == 0) {
            action.ExecuteOnMany(access, cnn, transaction, timeout);
            return;
        }
        action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
    }
    /// <inheritdoc/>
    public override ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var access = GetAccess(ref instance);
        if (!access.HasValues || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return default;
        if (startNextSegment == 0)
            return action.ExecuteOnManyAsync(access, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct);
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnOne(int nameStart, string actionName, ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var access = GetAccess(ref instance);
        if (!access.HasValues || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        if (startNextSegment == 0) {
            action.ExecuteOnMany(access, cnn, transaction, timeout);
            return;
        }
        action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
    }
    /// <inheritdoc/>
    public override ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var access = GetAccess(ref instance);
        if (!access.HasValues || !DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return default;
        if (startNextSegment == 0)
            return action.ExecuteOnManyAsync(access, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct);
    }

    /// <inheritdoc/>
    public override void FowardExecuteOnMany<TAccess>(int nameStart, string actionName, TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var count = instances.Length;
        for (int i = 0; i < count; i++) {
            var access = GetAccess(ref instances.GetAt(i));
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                return;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
        }
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnMany<TAccess>(int nameStart, string actionName, TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var count = instances.Length;
        for (int i = 0; i < count; i++) {
            var access = GetAccess(ref instances.GetAt(i));
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                return;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync<TAccess>(int nameStart, string actionName, TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var count = instances.Length;
        for (int i = 0; i < count; i++) {
            var access = GetAccess(ref instances.GetAt(i));
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                return;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync<TAccess>(int nameStart, string actionName, TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        var count = instances.Length;
        for (int i = 0; i < count; i++) {
            var access = GetAccess(ref instances.GetAt(i));
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                return;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>A factory builder to make a collection relation</summary>
public static class CollectionRelationActionFactory {
    /// <summary>A factory builder to make a collection relation</summary>
    public static object Build(Type tObj, MemberInfo colMember, MemberInfo idMember, string query, string keyColumnName, MemberInfo? colGetterMember = null) {
        Type tParam = GetMemberType(idMember);

        Type setterStorageType = colMember switch {
            FieldInfo f => f.FieldType,
            PropertyInfo p => p.PropertyType,
            MethodInfo m => GetColTypeFromMethod(m, tObj),
            _ => throw new NotSupportedException($"Unsupported List member: {colMember.MemberType}")
        };
        Type tItem;
        if (setterStorageType.IsArray)
            tItem = setterStorageType.GetElementType()!;
        else if (setterStorageType.IsGenericType && setterStorageType.GetGenericTypeDefinition() == typeof(List<>))
            tItem = setterStorageType.GetGenericArguments()[0];
        else
            throw new ArgumentException($"Member must be List<T> or T[]. Found: {setterStorageType.Name}");
        colGetterMember ??= colMember;
        Type getterReturnType = GetMemberType(colGetterMember);

        Type accessorType;
        bool needsWrapping = false;

        Type expectedInterface = typeof(ICollectionRefAccessor<>).MakeGenericType(tItem);
        if (expectedInterface.IsAssignableFrom(getterReturnType)) {
            accessorType = getterReturnType;
        }
        else if (getterReturnType == typeof(List<>).MakeGenericType(tItem)) {
            accessorType = typeof(ListAccess<>).MakeGenericType(tItem);
            needsWrapping = true;
        }
        else if (getterReturnType == tItem.MakeArrayType()) {
            accessorType = typeof(ArrayAccess<>).MakeGenericType(tItem);
            needsWrapping = true;
        }
        else {
            throw new NotSupportedException($"Getter type {getterReturnType.Name} cannot be mapped to TItem {tItem.Name}");
        }
        Type actionType = typeof(CollectionRelation<,,,>).MakeGenericType(tObj, tItem, tParam, accessorType);

        object idGetter = AccessorFactory.CreateGetter(tObj, tParam, idMember);
        object setter = !setterStorageType.IsArray
            ? AccessorFactory.CreateSetter(tObj, setterStorageType, colMember)
            : CreateArraySetter(tObj, tItem, colMember);
        object accessGetter = needsWrapping
            ? CreateWrappingGetter(tObj, getterReturnType, accessorType, colGetterMember)
            : AccessorFactory.CreateGetter(tObj, accessorType, colGetterMember);

        return Activator.CreateInstance(actionType, query, keyColumnName, idGetter, accessGetter, setter)!;
    }

    internal static Type GetColTypeFromMethod(MethodInfo m, Type tObj) {
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
    private static Type GetMemberType(MemberInfo member) => member switch {
        FieldInfo f => f.FieldType,
        PropertyInfo p => p.PropertyType,
        MethodInfo m => m.ReturnType, // Assumes getter-style method
        _ => throw new NotSupportedException()
    };
    private static object CreateArraySetter(Type tObj, Type tItem, MemberInfo member) {
        bool isStruct = tObj.IsValueType;
        Type listInputType = typeof(List<>).MakeGenericType(tItem);
        Type delegateType = isStruct
            ? typeof(StructSetter<,>).MakeGenericType(tObj, listInputType)
            : typeof(Setter<,>).MakeGenericType(tObj, listInputType);

        var dm = new DynamicMethod("SmartSet", null, [isStruct ? tObj.MakeByRefType() : tObj, listInputType], tObj.Module, true);
        var il = dm.GetILGenerator();
        var toArrayMethod = listInputType.GetMethod(nameof(List<>.ToArray))!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, toArrayMethod);

        if (member is FieldInfo f) {
            if (f.IsStatic)
                throw new NotSupportedException();
            il.Emit(OpCodes.Stfld, f);
        }
        else {
            MethodInfo? m = (member as MethodInfo 
                ?? (member as PropertyInfo)?.SetMethod) 
                ?? throw new NotSupportedException();
            il.Emit(m.IsStatic || isStruct ? OpCodes.Call : OpCodes.Callvirt, m);
        }

        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate(delegateType);
    }
    private static object CreateWrappingGetter(Type objType, Type storageType, Type accessorType, MemberInfo member) {
        bool isStruct = objType.IsValueType;
        Type delegateType = isStruct
            ? typeof(StructGetter<,>).MakeGenericType(objType, accessorType)
            : typeof(Getter<,>).MakeGenericType(objType, accessorType);

        var dm = new DynamicMethod("WrapGet", accessorType, [isStruct ? objType.MakeByRefType() : objType], objType.Module, true);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (member is FieldInfo f)
            il.Emit(OpCodes.Ldfld, f);
        else {
            MethodInfo m = (member as MethodInfo ?? (member as PropertyInfo)?.GetMethod)
                ?? throw new NotSupportedException($"Member {member.Name} has no readable getter.");
            il.Emit(m.IsStatic || isStruct ? OpCodes.Call : OpCodes.Callvirt, m);
        }
        var ctor = accessorType.GetConstructor([storageType])
            ?? throw new InvalidOperationException($"No constructor found for {accessorType.Name} taking {storageType.Name}");

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate(delegateType);
    }
}