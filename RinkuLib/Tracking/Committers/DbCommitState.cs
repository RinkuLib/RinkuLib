using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Owns the connection and transaction used by one database commit.</summary>
public readonly struct DbCommitState : IDisposable
{
    private readonly DbConnection? _connection;
    private readonly DbTransaction? _transaction;
    private readonly bool _closeConnection;

    /// <summary>Gets the commit connection.</summary>
    public readonly DbConnection Connection => _connection ?? throw new InvalidOperationException("The commit state was not initialized.");
    /// <summary>Gets the commit transaction.</summary>
    public readonly DbTransaction Transaction => _transaction ?? throw new InvalidOperationException("The commit state was not initialized.");
    /// <summary>Gets the command timeout.</summary>
    public int? Timeout { get; }

    /// <summary>Starts a database commit state.</summary>
    public DbCommitState(DbConnection connection, IsolationLevel? isolationLevel = null, int? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        _transaction = null;
        _closeConnection = connection.State != ConnectionState.Open;
        Timeout = timeout;

        if (_closeConnection)
            connection.Open();

        try
        {
            _transaction = isolationLevel is IsolationLevel level ? connection.BeginTransaction(level) : connection.BeginTransaction();
        }
        catch
        {
            if (_closeConnection)
                connection.Close();

            throw;
        }
    }

    /// <summary>Commits the transaction.</summary>
    public readonly void Commit() => Transaction.Commit();
    /// <summary>Rolls the transaction back.</summary>
    public readonly void Rollback() => Transaction.Rollback();

    /// <inheritdoc/>
    public readonly void Dispose()
    {
        _transaction?.Dispose();

        if (_closeConnection)
            _connection?.Close();
    }
}
