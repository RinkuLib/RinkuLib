using System.Data;
using System.Data.Common;

namespace Rinku.Mapping.Parsers;

/// <summary>
/// Returns a result that continues using the reader while it is consumed. Implement this for a custom
/// streamed result so the caller can finish or release the reader at the correct time.
/// </summary>
public interface IReaderHoldingParser<T> : ITypeParser<T> {
    /// <summary>
    /// Reads from a reader the caller owns, running <paramref name="onDone"/> once the rows are walked out
    /// or the walk is left early.
    /// </summary>
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
