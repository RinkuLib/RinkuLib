namespace Rinku.Mapping;

/// <summary>Tracks which result columns a mapping plan has claimed.</summary>
public ref struct ColumnUsage(Span<bool> Span) {
    /// <summary>Gets the number of distinct columns consumed by ordinary slots.</summary>
    public readonly int NbUsed { get {
            var nb = 0;
            for (int i = 0; i < Span.Length; i++)
                if (Span[i])
                    nb++;
            return nb;
        } }
    /// <summary>
    /// Gets the number of successful column claims, including reusable claims that did not consume their column.
    /// </summary>
    public int NbClaims { get; private set; }
    private readonly Span<bool> Span = Span;
    /// <summary>Gets the index of the last column claimed.</summary>
    public int LastIndexUsed { get; private set; } = -1;
    /// <summary>Gets the number of available columns.</summary>
    public readonly int Length => Span.Length;
    /// <summary>
    /// Copies the current usage into a checkpoint that can be passed to <see cref="Rollback"/>.
    /// </summary>
    public readonly void InitCheckpoint(Span<bool> checkpoint, out int lastUsed, out int nbClaims) {
        if (checkpoint.Length != Span.Length)
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            checkpoint[i] = Span[i];
        lastUsed = LastIndexUsed;
        nbClaims = NbClaims;
    }
    /// <summary>
    /// Restores a checkpoint created by <see cref="InitCheckpoint"/>.
    /// </summary>
    public void Rollback(scoped Span<bool> checkpoint, int lastUsed, int nbClaims) {
        if (checkpoint.Length != Span.Length)
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            Span[i] = checkpoint[i];
        LastIndexUsed = lastUsed;
        NbClaims = nbClaims;
    }
    /// <summary>
    /// Returns whether a column has been marked as used.
    /// </summary>
    public readonly bool IsUsed(int ind) => Span[ind];
    /// <summary>
    /// Claims a column and prevents an ordinary later claim.
    /// </summary>
    public void Use(int ind) {
        Span[ind] = true;
        LastIndexUsed = ind;
        NbClaims++;
    }
    /// <summary>
    /// Records a reusable claim without consuming the column, leaving it available to a later ordinary slot.
    /// </summary>
    public void Reuse(int ind) {
        _ = Span[ind];
        NbClaims++;
    }
}
