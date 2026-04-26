namespace RinkuLib.DbParsing;

/// <summary>A simple struct used track the usage of the columns</summary>
public ref struct ColumnUsage(Span<bool> Span) {
    
    private readonly Span<bool> Span = Span;
    /// <summary>The index of the last column that was used</summary>
    public int LastIndexUsed { get; private set; } = -1;
    /// <summary>The amount of columns</summary>
    public readonly int Length => Span.Length;
    /// <summary>
    /// Save a snapshot of the current usage into a checkpoint <see cref="Span{Boolean}"/>
    /// </summary>
    public readonly void InitCheckpoint(Span<bool> checkpoint, out int lastUsed) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            checkpoint[i] = Span[i];
        lastUsed = LastIndexUsed;
    }
    /// <summary>
    /// Reset the column usage to the checkpoint state
    /// </summary>
    public void Rollback(scoped Span<bool> checkpoint, int lastUsed) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            Span[i] = checkpoint[i];
        LastIndexUsed = lastUsed;
    }
    /// <summary>
    /// Check if a column has been marked as used
    /// </summary>
    public readonly bool IsUsed(int ind) => Span[ind];
    /// <summary>
    /// Mark a column as used
    /// </summary>
    public void Use(int ind) {
        Span[ind] = true;
        LastIndexUsed = ind;
    }
}
