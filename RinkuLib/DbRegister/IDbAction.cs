using System.Data;
using System.Data.Common;

namespace RinkuLib.DbRegister;

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
    public virtual void ExecuteOnMany(List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
        => ExecuteOnMany(instances, (IDbConnection)cnn, transaction, timeout);
    /// <summary>Execute the action for many instances in an async way</summary>
    public virtual ValueTask ExecuteOnManyAsync(List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
        => ExecuteOnManyAsync(instances, (IDbConnection)cnn, transaction, timeout, ct);
    /// <summary>Execute the action for one instance in a sync way</summary>
    public abstract void ExecuteOnOne(ref T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public abstract ValueTask ExecuteOnOneAsync(T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Execute the action for many instances in a sync way</summary>
    public abstract void ExecuteOnMany(List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for many instances in an async way</summary>
    public abstract ValueTask ExecuteOnManyAsync(List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
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
    public virtual void FowardExecuteOnMany(int nameStart, string actionName, List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null)
        => FowardExecuteOnMany(nameStart, actionName, instances, cnn, transaction, timeout);
    /// <summary>Foward and execute the action for many instances in an async way</summary>
    public virtual ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default)
        => FowardExecuteOnManyAsync(nameStart, actionName, instances, cnn, transaction, timeout, ct);
    /// <summary>Foward and execute the action for many instances in a sync way</summary>
    public abstract void FowardExecuteOnMany(int nameStart, string actionName, List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Foward and execute the action for many instances in an async way</summary>
    public abstract ValueTask FowardExecuteOnManyAsync(int nameStart, string actionName, List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
}
