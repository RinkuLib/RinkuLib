using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Queries;

namespace RinkuLib.DbRegister; 
internal class SubItemAction<TObj, TItem> : IDbAction<TObj> {
    public SubItemAction(Getter<TObj, List<TItem>?> Getter) : this((object)Getter) {
        if (typeof(TObj).IsValueType)
            throw new Exception("typeof TObj must be a reference type when using Getter");
    }
    public SubItemAction(StructGetter<TObj, List<TItem>?> Getter) : this((object)Getter) {
        if (!typeof(TObj).IsValueType)
            throw new Exception("typeof TObj must be a value type when using StructGetter");
    }
    private SubItemAction(object Getter) {
        this.Getter = Getter;
    }
    private object Getter;
    /// <inheritdoc/>
    public Func<DbDataReader, TItem>? Parser { get; set; }
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; set; }
    /// <inheritdoc/>
    public DbParamInfo ParamInfo { get; set; } = InferedDbParamCache.Instance;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<TItem>? GetList(ref TObj instance) => typeof(TObj).IsValueType
        ? Unsafe.As<object, StructGetter<TObj, List<TItem>?>>(ref Getter)(ref instance)
        : Unsafe.As<object, Getter<TObj, List<TItem>?>>(ref Getter)(instance);
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
        => GetList(ref instance)?.ExecuteDBActions(cnn, transaction, timeout);
    /// <inheritdoc/>
    public void ExecuteOnOne(ref TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null)
        => GetList(ref instance)?.ExecuteDBActions(cnn, transaction, timeout);
    /// <inheritdoc/>
    public ValueTask ExecuteOnOneAsync(TObj instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var list = GetList(ref instance);
        if (list is null)
            return default;
        return list.ExecuteDBActionsAsync(cnn, transaction, timeout, ct);
    }
    /// <inheritdoc/>
    public ValueTask ExecuteOnOneAsync(TObj instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        var list = GetList(ref instance);
        if (list is null)
            return default;
        return list.ExecuteDBActionsAsync(cnn, transaction, timeout, ct);
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) {
        var listSpan = CollectionsMarshal.AsSpan(instances);
        for (int i = 0; i < listSpan.Length; i++) {
            var list = GetList(ref listSpan[i]);
            if (list is null)
                continue;
            list.ExecuteDBActions(cnn, transaction, timeout);
        }
    }
    /// <inheritdoc/>
    public void ExecuteOnMany(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) {
        var listSpan = CollectionsMarshal.AsSpan(instances);
        for (int i = 0; i < listSpan.Length; i++) {
            var list = GetList(ref listSpan[i]);
            if (list is null)
                continue;
            list.ExecuteDBActions(cnn, transaction, timeout);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        for (int i = 0; i < instances.Count; i++) {
            var list = GetList(ref CollectionsMarshal.AsSpan(instances)[i]);
            if (list is null)
                continue;
            await DbActions.ImplExecuteDBActionsAsync(list, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public async ValueTask ExecuteOnManyAsync(List<TObj> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        for (int i = 0; i < instances.Count; i++) {
            var list = GetList(ref CollectionsMarshal.AsSpan(instances)[i]);
            if (list is null)
                continue;
            await DbActions.ImplExecuteDBActionsAsync(list, cnn, transaction, timeout, ct).ConfigureAwait(false);
        }
    }
}