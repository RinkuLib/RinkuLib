using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.DbRegister;
/// <summary>Wrap a collection and provide acces to the ref of the items</summary>
public interface ICollectionRefAccessor<T> {
    /// <summary>Is the collection is not null and has at least one value</summary>
    public bool HasValues { get; }
    /// <summary>The length / count of the underlying item</summary>
    public int Length { get; }
    /// <summary>Access the ref of an item at the index</summary>
    public ref T GetAt(int i);
}
/// <summary>The ref accessor for a list</summary>
public readonly struct ListAccess<T>(List<T> instances) : ICollectionRefAccessor<T> {
    /// <inheritdoc/>
    public bool HasValues => instances is not null && instances.Count > 0;
    private readonly List<T> instances = instances;
    /// <inheritdoc/>
    public int Length => instances.Count;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref T GetAt(int i) => ref CollectionsMarshal.AsSpan(instances)[i];
}
/// <summary>The ref accessor for a list</summary>
public readonly struct ArrayAccess<T>(T[] instances) : ICollectionRefAccessor<T> {
    private readonly T[] instances = instances;
    /// <inheritdoc/>
    public bool HasValues => instances is not null && instances.Length > 0;
    /// <inheritdoc/>
    public int Length => instances.Length;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref T GetAt(int i) => ref instances[i];
}
/// <summary>
/// A db action that may be executed using a type instance
/// </summary>
public abstract class DbAction<T> {
    /// <summary>Execute the action for one instance in a sync way</summary>
    public virtual void ExecuteOnOne(ref T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
        => ExecuteOnOne(ref instance, (IDbConnection)cnn, transaction, timeout);
    /// <summary>Execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public virtual ValueTask ExecuteOnOneAsync(T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
        => ExecuteOnOneAsync(instance, (IDbConnection)cnn, transaction, timeout, ct);
    /// <summary>Execute the action for many instances in a sync way</summary>
    public virtual void ExecuteOnMany<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where TAccess : ICollectionRefAccessor<T>
        => ExecuteOnMany(instances, (IDbConnection)cnn, transaction, timeout);
    /// <summary>Execute the action for many instances in an async way</summary>
    public virtual ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TAccess : ICollectionRefAccessor<T>
        => ExecuteOnManyAsync(instances, (IDbConnection)cnn, transaction, timeout, ct);
    /// <summary>Execute the action for one instance in a sync way</summary>
    public abstract void ExecuteOnOne(ref T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public abstract ValueTask ExecuteOnOneAsync(T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Execute the action for many instances in a sync way</summary>
    public abstract void ExecuteOnMany<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) where TAccess : ICollectionRefAccessor<T>;
    /// <summary>Execute the action for many instances in an async way</summary>
    public abstract ValueTask ExecuteOnManyAsync<TAccess>(TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TAccess : ICollectionRefAccessor<T>;
    /// <summary>Foward and execute the action for one instance in a sync way</summary>
    public virtual void FowardExecuteOnOne(int nameStart, string actionName, ref T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
        => FowardExecuteOnOne(nameStart, actionName, ref instance, cnn, transaction, timeout);
    /// <summary>Foward and execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public virtual ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
        => FowardExecuteOnOneAsync(nameStart, actionName, instance, cnn, transaction, timeout, ct);
    /// <summary>Foward and execute the action for one instance in a sync way</summary>
    public abstract void FowardExecuteOnOne(int nameStart, string actionName, ref T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Foward and execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public abstract ValueTask FowardExecuteOnOneAsync(int nameStart, string actionName, T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Foward and execute the action for many instances in a sync way</summary>
    public virtual void FowardExecuteOnMany<TAccess>(int nameStart, string actionName, TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null) where TAccess : ICollectionRefAccessor<T>
        => FowardExecuteOnMany(nameStart, actionName, instances, cnn, transaction, timeout);
    /// <summary>Foward and execute the action for many instances in an async way</summary>
    public virtual ValueTask FowardExecuteOnManyAsync<TAccess>(int nameStart, string actionName, TAccess instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TAccess : ICollectionRefAccessor<T>
        => FowardExecuteOnManyAsync(nameStart, actionName, instances, cnn, transaction, timeout, ct);
    /// <summary>Foward and execute the action for many instances in a sync way</summary>
    public abstract void FowardExecuteOnMany<TAccess>(int nameStart, string actionName, TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null) where TAccess : ICollectionRefAccessor<T>;
    /// <summary>Foward and execute the action for many instances in an async way</summary>
    public abstract ValueTask FowardExecuteOnManyAsync<TAccess>(int nameStart, string actionName, TAccess instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) where TAccess : ICollectionRefAccessor<T>;
}
