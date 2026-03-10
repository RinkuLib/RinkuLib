using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.DbRegister;
/// <inheritdoc/>
public class ToManyRelation<TObj, TItem, TParam, TColAccessor> : DbAction<TObj> where TColAccessor : notnull, ICollectionRefAccessor<TItem> {
    private object Getter;
    private object AccessorGetter;
    private object Setter;
    private readonly string Query;
    private readonly int IndexAfterVar;
    private readonly bool KeepingRefToList;
    /// <inheritdoc/>
    private ToManyRelation(string Query, object Getter, object ListGetter, object Setter, bool KeepingRefToList) {
        this.Query = SanitizeQuery(Query, out IndexAfterVar);
        this.Getter = Getter;
        this.AccessorGetter = ListGetter;
        this.Setter = Setter;
        this.KeepingRefToList = KeepingRefToList;
    }

    private static string SanitizeQuery(string Query, out int indexAfterVar) {
        var indexOfVar = Query.IndexOf(ActionNameCache.BindParamName);
        if (indexOfVar < 0 || Query.IndexOf(ActionNameCache.BindParamName, indexOfVar + 1) > 0)
            throw new ArgumentException($"The {nameof(Query)} string must contains exactly one {ActionNameCache.BindParamName} variable, currently having : {Query}");
        int startOfWhitespaceInFrontOfVar = indexOfVar - 1;
        if (indexOfVar > 0) {
            for (; startOfWhitespaceInFrontOfVar >= 0; startOfWhitespaceInFrontOfVar--) {
                if (!char.IsWhiteSpace(Query[startOfWhitespaceInFrontOfVar]))
                    break;
            }
            if (startOfWhitespaceInFrontOfVar == 0 || Query[startOfWhitespaceInFrontOfVar] != '=')
                throw new Exception($"The {nameof(Query)} string must contains exactly one {ActionNameCache.BindParamName} variable and nave an '=' preceding it (x = {ActionNameCache.BindParamName}), currently having : {Query}");
            startOfWhitespaceInFrontOfVar++;
        }
        var lastCharIdx = Query.Length - 1;
        for (; lastCharIdx >= 0; lastCharIdx--) {
            if (!char.IsWhiteSpace(Query[lastCharIdx]))
                break;
        }
        bool alreadyHasSemicolon = lastCharIdx >= 0 && Query[lastCharIdx] == ';';
        int effectiveLastIdx = alreadyHasSemicolon ? lastCharIdx : lastCharIdx + 1;

        int finalLength = Query.Length - (indexOfVar - startOfWhitespaceInFrontOfVar) - (Query.Length - 1 - lastCharIdx);
        if (!alreadyHasSemicolon)
            finalLength++;
        indexAfterVar = startOfWhitespaceInFrontOfVar + ActionNameCache.BindParamName.Length;
        if (alreadyHasSemicolon && finalLength == Query.Length)
            return Query;
        return string.Create(finalLength, (Query, startOfWhitespaceInFrontOfVar, indexOfVar, effectiveLastIdx), static (dest, param) => {
            var (src, varStart, varIdx, lastIdx) = param;
            src.AsSpan(0, varStart).CopyTo(dest);
            int remainingContentLen = lastIdx - varIdx;
            src.AsSpan(varIdx, remainingContentLen).CopyTo(dest[varStart..]);
            dest[^1] = ';';
        });
    }

    /// <inheritdoc/>
    public ToManyRelation(string Query, Getter<TObj, TParam> Getter, Getter<TObj, TColAccessor> AccessorGetter, Setter<TObj, List<TItem>> Setter, bool KeepingRefToList = true)
        : this(Query, (object)Getter, AccessorGetter, Setter, KeepingRefToList) { }
    /// <inheritdoc/>
    public ToManyRelation(string Query, StructGetter<TObj, TParam> Getter, StructGetter<TObj, TColAccessor> AccessorGetter, StructSetter<TObj, List<TItem>> Setter, bool KeepingRefToList = true)
        : this(Query, (object)Getter, AccessorGetter, Setter, KeepingRefToList) { }
    /// <inheritdoc/>
    public Func<DbDataReader, TItem>? Parser;
    /// <inheritdoc/>
    public CommandBehavior Behavior;
    /// <inheritdoc/>
    public DbParamInfo ParamInfo = InferedDbParamCache.Instance;

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
    [MemberNotNull(nameof(Parser))]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private void EnsureParser(IDbCommand cmd, DbDataReader reader) {
        if (Parser is null) {
            var schema = reader.GetColumns();
            Parser = TypeParser<TItem>.GetParserFunc(ref schema, out Behavior);
            if (IDbParamInfoGetter.TryGetParamInfo(cmd, ActionNameCache.BindParamNames[0], out var p))
                ParamInfo = p;
        }
    }
    #region Execute on One
    /// <inheritdoc/>
    public override void ExecuteOnOne(ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        using var reader = cmd.ExecuteReader(b);
        EnsureParser(cmd, reader);
        List<TItem> res = [];
        while (reader.Read())
            res.Add(Parser(reader));
        SetCollection(ref instance, res);
    }
    /// <inheritdoc/>
    public override void ExecuteOnOne(ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        using var r = cmd.ExecuteReader(b);
        var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
        EnsureParser(cmd, reader);
        List<TItem> res = [];
        while (reader.Read())
            res.Add(Parser(reader));
        SetCollection(ref instance, res);
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnOneAsync(TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = Query;
        var param = GetParam(ref instance)!;
        ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
        using var reader = await cmd.ExecuteReaderAsync(b, ct).ConfigureAwait(false);
        EnsureParser(cmd, reader);
        List<TItem> res = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            res.Add(Parser(reader));
        SetCollection(ref instance, res);
    }
    /// <inheritdoc/>
    public override ValueTask ExecuteOnOneAsync(TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (cnn is DbConnection conn && (transaction is null || transaction is DbTransaction))
            return ExecuteOnOneAsync(instance, conn, Unsafe.As<IDbTransaction?, DbTransaction?>(ref transaction), timeout, ct);
        ExecuteOnOne(ref instance, cnn, transaction, timeout);
        return default;
    }
    #endregion
    #region Execute on many using access
    /// <inheritdoc/>
    public override void ExecuteOnMany<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            ExecuteOnOne(ref instances.GetAt(0), cnn, transaction, timeout);
            return;
        }
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.GenerateMultiQuery(Query, IndexAfterVar, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        using var reader = cmd.ExecuteReader(b);
        EnsureParser(cmd, reader);
        List<TItem> res = [];
        int ind = 0;
        do {
            while (reader.Read())
                res.Add(Parser(reader));
            SetCollection(ref instances.GetAt(ind), res);
            if (KeepingRefToList)
                res = [];
            else
                res.Clear();
            ind++;
        } while (reader.NextResult());
    }

    /// <inheritdoc/>
    public override void ExecuteOnMany<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        if (cnn is DbConnection conn && (transaction is null || transaction is DbTransaction)) {
            ExecuteOnMany(instances, conn, Unsafe.As<IDbTransaction?, DbTransaction?>(ref transaction), timeout);
            return;
        }
        var count = instances.Length;
        if (count == 0)
            return;
        if (count == 1) {
            ExecuteOnOne(ref instances.GetAt(0), cnn, transaction, timeout);
            return;
        }
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        using var cmd = cnn.GetCommand(transaction, timeout);
        cmd.CommandText = ActionNameCache.GenerateMultiQuery(Query, IndexAfterVar, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        using var r = cmd.ExecuteReader(b);
        var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
        EnsureParser(cmd, reader);
        List<TItem> res = [];
        int ind = 0;
        do {
            while (reader.Read())
                res.Add(Parser(reader));
            SetCollection(ref instances.GetAt(ind), res);
            if (KeepingRefToList)
                res = [];
            else
                res.Clear();
            ind++;
        } while (reader.NextResult());
    }
    /// <inheritdoc/>
    public override async ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var count = instances.Length;
        if (count == 0)
            return;
        using var cmd = cnn.GetCommand(transaction, timeout);
        List<TItem> res = [];
        if (count == 1) {
            cmd.CommandText = Query;
            var param = GetParam(ref instances.GetAt(0))!;
            ParamInfo.Use(ActionNameCache.BindParamName, cmd, param);
            using var rd = await cmd.ExecuteReaderAsync(Behavior, ct).ConfigureAwait(false);
            EnsureParser(cmd, rd);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                res.Add(Parser(rd));
            SetCollection(ref instances.GetAt(0), res);
            return;
        }
        var b = Behavior;
        if (cnn.State != ConnectionState.Open) {
            cnn.Open();
            b |= CommandBehavior.CloseConnection;
        }
        cmd.CommandText = ActionNameCache.GenerateMultiQuery(Query, IndexAfterVar, count);
        if (count > 100) {
            for (var i = 0; i < 100; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
            for (int i = 100; i < count; i++)
                ParamInfo.Use(ActionNameCache.GetParamName(i), cmd, GetParam(ref instances.GetAt(i))!);
        }
        else
            for (var i = 0; i < count; i++)
                ParamInfo.Use(ActionNameCache.BindParamNames[i], cmd, GetParam(ref instances.GetAt(i))!);
        using var reader = await cmd.ExecuteReaderAsync(b, ct).ConfigureAwait(false);
        EnsureParser(cmd, reader);
        int ind = 0;
        do {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                res.Add(Parser(reader));
            SetCollection(ref instances.GetAt(ind), res);
            if (KeepingRefToList)
                res = [];
            else
                res.Clear();
            ind++;
        } while (await reader.NextResultAsync(ct).ConfigureAwait(false));
    }
    /// <inheritdoc/>
    public override ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (cnn is DbConnection conn && (transaction is null || transaction is DbTransaction))
            return ExecuteOnManyAsync(instances, conn, Unsafe.As<IDbTransaction?, DbTransaction?>(ref transaction), timeout, ct);
        ExecuteOnMany(instances, cnn, transaction, timeout);
        return default;
    }
    #endregion
    #region Execute on many using enumerable
    /// <inheritdoc/>
    public override void ExecuteOnMany(IEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        using var enumerator = instances.GetEnumerator();
        if (!enumerator.MoveNext())
            return;
        var wasClosed = cnn.State != ConnectionState.Open;
        if (wasClosed)
            cnn.Open();
        try {
            using var cmd = cnn.GetCommand(transaction, timeout);
            cmd.CommandText = Query;
            List<TItem> res = [];
            TObj current = enumerator.Current;
            object? param = GetParam(ref current)!;
            ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
            using (var reader = cmd.ExecuteReader(Behavior)) {
                EnsureParser(cmd, reader);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
            while (enumerator.MoveNext()) {
                current = enumerator.Current;
                ParamInfo.Update(cmd, ref param, GetParam(ref current));
                if (KeepingRefToList)
                    res = [];
                else
                    res.Clear();
                using var reader = cmd.ExecuteReader(Behavior);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <inheritdoc/>
    public async override ValueTask ExecuteOnManyAsync(IEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        using var enumerator = instances.GetEnumerator();
        if (!enumerator.MoveNext())
            return;
        var wasClosed = cnn.State != ConnectionState.Open;
        if (wasClosed)
            await cnn.OpenAsync(ct).ConfigureAwait(false);
        try {
            using var cmd = cnn.GetCommand(transaction, timeout);
            cmd.CommandText = Query;
            List<TItem> res = [];
            TObj current = enumerator.Current;
            object? param = GetParam(ref current)!;
            ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
            await using (var reader = await cmd.ExecuteReaderAsync(Behavior, ct).ConfigureAwait(false)) {
                EnsureParser(cmd, reader);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
            while (enumerator.MoveNext()) {
                current = enumerator.Current;
                ParamInfo.Update(cmd, ref param, GetParam(ref current));
                if (KeepingRefToList)
                    res = [];
                else
                    res.Clear();
                using var reader = await cmd.ExecuteReaderAsync(Behavior, ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
        }
        finally {
            if (wasClosed)
                await cnn.CloseAsync().ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public override void ExecuteOnMany(IEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        using var enumerator = instances.GetEnumerator();
        if (!enumerator.MoveNext())
            return;
        var wasClosed = cnn.State != ConnectionState.Open;
        if (wasClosed)
            cnn.Open();
        try {
            using var cmd = cnn.GetCommand(transaction, timeout);
            cmd.CommandText = Query;
            List<TItem> res = [];
            TObj current = enumerator.Current;
            object? param = GetParam(ref current)!;
            ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
            using (var r = cmd.ExecuteReader(Behavior)) {
                var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                EnsureParser(cmd, reader);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
            while (enumerator.MoveNext()) {
                current = enumerator.Current;
                ParamInfo.Update(cmd, ref param, GetParam(ref current));
                if (KeepingRefToList)
                    res = [];
                else
                    res.Clear();
                using var r = cmd.ExecuteReader(Behavior);
                var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <inheritdoc/>
    public override ValueTask ExecuteOnManyAsync(IEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (cnn is DbConnection conn && (transaction is null || transaction is DbTransaction))
            return ExecuteOnManyAsync(instances, conn, Unsafe.As<IDbTransaction?, DbTransaction?>(ref transaction), timeout, ct);
        ExecuteOnMany(instances, cnn, transaction, timeout);
        return default;
    }
    /// <inheritdoc/>
    public async override ValueTask ExecuteOnManyAsync(IAsyncEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        await using var enumerator = instances.GetAsyncEnumerator(ct);
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            return;
        var wasClosed = cnn.State != ConnectionState.Open;
        if (wasClosed)
            await cnn.OpenAsync(ct).ConfigureAwait(false);
        try {
            using var cmd = cnn.GetCommand(transaction, timeout);
            cmd.CommandText = Query;
            List<TItem> res = [];
            TObj current = enumerator.Current;
            object? param = GetParam(ref current)!;
            ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
            await using (var reader = await cmd.ExecuteReaderAsync(Behavior, ct).ConfigureAwait(false)) {
                EnsureParser(cmd, reader);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
            while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                current = enumerator.Current;
                ParamInfo.Update(cmd, ref param, GetParam(ref current));
                if (KeepingRefToList)
                    res = [];
                else
                    res.Clear();
                using var reader = await cmd.ExecuteReaderAsync(Behavior, ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
        }
        finally {
            if (wasClosed)
                await cnn.CloseAsync().ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public async override ValueTask ExecuteOnManyAsync(IAsyncEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        await using var enumerator = instances.GetAsyncEnumerator(ct);
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            return;
        var wasClosed = cnn.State != ConnectionState.Open;
        if (wasClosed)
            cnn.Open();
        try {
            using var cmd = cnn.GetCommand(transaction, timeout);
            cmd.CommandText = Query;
            List<TItem> res = [];
            TObj current = enumerator.Current;
            object? param = GetParam(ref current)!;
            ParamInfo.SaveUse(ActionNameCache.BindParamName, cmd, ref param);
            using (var r = cmd.ExecuteReader(Behavior)) {
                var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                EnsureParser(cmd, reader);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
            while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                current = enumerator.Current;
                ParamInfo.Update(cmd, ref param, GetParam(ref current));
                if (KeepingRefToList)
                    res = [];
                else
                    res.Clear();
                using var r = cmd.ExecuteReader(Behavior);
                var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                while (reader.Read())
                    res.Add(Parser(reader));
                SetCollection(ref current, res);
            }
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    #endregion
    #region Foward on one
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
    #endregion
    #region Foward on many using access
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
                continue;
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
                continue;
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
                continue;
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
                continue;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    #endregion
    #region Foward on many using enumerable
    /// <inheritdoc/>
    public override void FowardExecuteOnMany(int nameStart, string actionName, IEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        using var enumerator = instances.GetEnumerator();
        while (enumerator.MoveNext()) {
            var current = enumerator.Current;
            var access = GetAccess(ref current);
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                continue;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
        }
    }
    /// <inheritdoc/>
    public override void FowardExecuteOnMany(int nameStart, string actionName, IEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        using var enumerator = instances.GetEnumerator();
        while (enumerator.MoveNext()) {
            var current = enumerator.Current;
            var access = GetAccess(ref current);
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                action.ExecuteOnMany(access, cnn, transaction, timeout);
                continue;
            }
            action.FowardExecuteOnMany(startNextSegment, actionName, access, cnn, transaction, timeout);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, IEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        using var enumerator = instances.GetEnumerator();
        while (enumerator.MoveNext()) {
            var current = enumerator.Current;
            var access = GetAccess(ref current);
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                await action.ExecuteOnManyAsync(access, cnn, transaction, timeout, ct).ConfigureAwait(false);
                continue;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, IEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (cnn is DbConnection conn && (transaction is null || transaction is DbTransaction))
            return FowardExecuteOnManyAsync(nameStart, actionName, instances, conn, Unsafe.As<IDbTransaction?, DbTransaction?>(ref transaction), timeout, ct);
        FowardExecuteOnMany(nameStart, actionName, instances, cnn, transaction, timeout);
        return default;
    }

    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, IAsyncEnumerable<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        await using var enumerator = instances.GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
            var current = enumerator.Current;
            var access = GetAccess(ref current);
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                await action.ExecuteOnManyAsync(access, cnn, transaction, timeout, ct).ConfigureAwait(false);
                continue;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public override async ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, IAsyncEnumerable<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<TItem>.TryGetAction(nameStart, actionName, out var action, out var startNextSegment))
            return;
        await using var enumerator = instances.GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync().ConfigureAwait(false)) {
            var current = enumerator.Current;
            var access = GetAccess(ref current);
            if (!access.HasValues)
                continue;
            if (startNextSegment == 0) {
                await action.ExecuteOnManyAsync(access, cnn, transaction, timeout, ct).ConfigureAwait(false);
                continue;
            }
            await action.FowardExecuteOnManyAsync(startNextSegment, actionName, access, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    #endregion
}

/// <summary>A factory builder to make a collection relation</summary>
public static class ToManyRelationActionFactory {
    /// <summary>A factory builder to make a collection relation</summary>
    public static object Build(Type tObj, MemberInfo colMember, MemberInfo idMember, string query, MemberInfo? colGetterMember = null) {
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
        bool needWrapping = false;

        Type expectedInterface = typeof(ICollectionRefAccessor<>).MakeGenericType(tItem);
        if (expectedInterface.IsAssignableFrom(getterReturnType)) {
            accessorType = getterReturnType;
        }
        else if (getterReturnType == typeof(List<>).MakeGenericType(tItem)) {
            accessorType = typeof(ListAccess<>).MakeGenericType(tItem);
            needWrapping = true;
        }
        else if (getterReturnType == tItem.MakeArrayType()) {
            accessorType = typeof(ArrayAccess<>).MakeGenericType(tItem);
            needWrapping = true;
        }
        else {
            throw new NotSupportedException($"Getter type {getterReturnType.Name} cannot be mapped to TItem {tItem.Name}");
        }
        Type actionType = typeof(ToManyRelation<,,,>).MakeGenericType(tObj, tItem, tParam, accessorType);

        object idGetter = AccessorFactory.CreateGetter(tObj, tParam, idMember);
        object setter = !setterStorageType.IsArray
            ? AccessorFactory.CreateSetter(tObj, setterStorageType, colMember)
            : CreateArraySetter(tObj, tItem, colMember);
        object accessGetter = needWrapping
            ? CreateWrappingGetter(tObj, getterReturnType, accessorType, colGetterMember)
            : AccessorFactory.CreateGetter(tObj, accessorType, colGetterMember);

        return Activator.CreateInstance(actionType, query, idGetter, accessGetter, setter, !setterStorageType.IsArray)!;
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
public static partial class DbActions {
    /// <summary>
    /// Lets you manualy add a to many relation
    /// </summary>
    /// <param name="actionName">The action name</param>
    /// <param name="idMemberName"></param>
    /// <param name="query"></param>
    /// <param name="collectionMemberName">Will use the action name as the collection parameter name if not provided</param>
    /// <param name="differentCollectionGetterMemberName">Will use the collection member if not provided</param>
    /// <returns></returns>
    public static DbAction<T> AddOrUpdateToManyRelation<T>(string actionName, string idMemberName, string query, string? collectionMemberName = null, string? differentCollectionGetterMemberName = null) {
        Type tObj = typeof(T);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        MemberInfo colMember = tObj.GetMember(collectionMemberName ?? actionName, flags)[0];
        MemberInfo idMember = tObj.GetMember(idMemberName, flags)[0];

        MemberInfo? colGetterMember = differentCollectionGetterMemberName is not null
            ? tObj.GetMember(differentCollectionGetterMemberName, flags)[0]
            : null;

        var relation = (DbAction<T>)ToManyRelationActionFactory.Build(tObj, colMember, idMember, query, colGetterMember);
        DbActions<T>.AddOrUpdate(actionName, relation);
        return relation;
    }
}