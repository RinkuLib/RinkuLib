using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;

/// <summary>
/// Defines a two-phase execution contract for processing database results and command state.
/// </summary>
/// <remarks>
/// <para><b>The Preparation Phase:</b></para>
/// <para>The <see cref="Prepare"/> method is called once the reader is open but before data 
/// is consumed. It provides access to the active <see cref="IDbCommand"/> and <see cref="DbDataReader"/> 
/// to finalize state, synchronize metadata, or update caching information for subsequent executions.</para>
/// 
/// <para><b>The Parsing Phase:</b></para>
/// <para>The <see cref="Parse"/> method performs the actual data transformation or extraction 
/// from the current state of the reader.</para>
/// </remarks>
public interface IParser<T> {
    /// <summary> The requested execution behavior for the command. </summary>
    CommandBehavior DefaultBehavior { get; }

    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    void Prepare(DbDataReader reader, IDbCommand cmd);

    /// <summary> Processes the current result set state into <typeparamref name="T"/>. </summary>
    T Parse(DbDataReader reader);
}
/// <summary>
/// Defines a two-phase asynchronous execution contract for processing database results 
/// and command state.
/// </summary>
/// <remarks>
/// This interface follows the same structural pattern as <see cref="IParser{T}"/>, 
/// separating the context synchronization from the data transformation to allow 
/// for non-blocking I/O during metadata resolution and record processing.
/// </remarks>
public interface IAsyncParser<T> {
    /// <summary> 
    /// The requested execution behavior for the command. 
    /// </summary>
    CommandBehavior DefaultBehavior { get; }

    /// <summary> 
    /// Asynchronously synchronizes the parser with the active execution context.
    /// </summary>
    /// <remarks>
    /// Identical in purpose to <see cref="IParser{T}.Prepare"/>, this hook is used 
    /// to update parameter caching, synchronize metadata, or initialize parsing 
    /// logic without blocking the calling thread.
    /// </remarks>
    Task Prepare(DbDataReader reader, IDbCommand cmd);

    /// <summary> 
    /// Asynchronously processes the current result set state into <typeparamref name="T"/>. 
    /// </summary>
    Task<T> Parse(DbDataReader reader);
}