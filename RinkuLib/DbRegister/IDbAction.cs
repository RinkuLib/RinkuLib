using System.Data;
using System.Data.Common;

namespace RinkuLib.DbRegister;

/// <summary>
/// A db action that may be executed using a type instance
/// </summary>
public interface IDbAction<T> {
    /// <summary>Execute the action for one instance in a sync way</summary>
    public void ExecuteOnOne(ref T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public ValueTask ExecuteOnOneAsync(T instance, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Execute the action for many instances in a sync way</summary>
    public void ExecuteOnMany(List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for many instances in an async way</summary>
    public ValueTask ExecuteOnManyAsync(List<T> instances, DbConnection cnn, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Execute the action for one instance in a sync way</summary>
    public void ExecuteOnOne(ref T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for one instance in an async way, should probably not be called with a struct type since would only modify the copy</summary>
    public ValueTask ExecuteOnOneAsync(T instance, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
    /// <summary>Execute the action for many instances in a sync way</summary>
    public void ExecuteOnMany(List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null);
    /// <summary>Execute the action for many instances in an async way</summary>
    public ValueTask ExecuteOnManyAsync(List<T> instances, IDbConnection cnn, IDbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default);
}
