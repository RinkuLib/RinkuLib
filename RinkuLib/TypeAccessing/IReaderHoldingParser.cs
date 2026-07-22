using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// A parser whose result keeps reading after it has returned, so the reader it was given is still being
/// used once the call is over.
/// </summary>
/// <remarks>
/// A caller that owns the reader has to know when such a result is finished with it, and the result itself
/// is the only thing that knows. <see cref="Commands.MultiReader"/> needs this to step to the next set once
/// the rows of this one have been walked, and a run that opened the reader before handing the rows over
/// needs it to let go.
/// </remarks>
public interface IReaderHoldingParser<T> : ITypeParser<T> {
    /// <summary>
    /// Reads from a reader the caller owns, running <paramref name="onDone"/> once the rows are walked out
    /// or the walk is left early.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TDone"/> is a type parameter rather than a delegate so a struct passed here is
    /// specialized into the read and costs no allocation, which matters because the caller is on the road
    /// that reads every row.
    /// </remarks>
    public T ParseThen<TDone>(DbDataReader reader, TDone onDone) where TDone : IReaderDone;
}

/// <summary>What to do with a reader once the result that held it is finished.</summary>
public interface IReaderDone {
    /// <summary>Runs once the rows are walked out or the walk is left early.</summary>
    public void Invoke(DbDataReader reader);
}

/// <summary>Steps to the next result set, for a reader that carries more than one.</summary>
public readonly struct GoToNextResultSet : IReaderDone {
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) => reader.NextResult();
}

/// <summary>Lets go of the reader, for a run that opened it and left the command to its caller.</summary>
public readonly struct LetGoOfReader : IReaderDone {
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) => reader.Dispose();
}

/// <summary>Lets go of the reader and the command, for a run that owns both.</summary>
public readonly struct LetGoOfReaderAndCommand(IDbCommand command) : IReaderDone {
    private readonly IDbCommand _command = command;
    /// <inheritdoc/>
    public readonly void Invoke(DbDataReader reader) {
        reader.Dispose();
        _command.Parameters.Clear();
        _command.Dispose();
    }
}
