namespace Rinku.Querying;
/// <summary>
/// One part of a query template. It contains literal text or a handler that writes a value.
/// </summary>
/// <param name="Start">The absolute start position within the normalized <c>Query</c> string.</param>
/// <param name="Length">The total length of the segment (including potential excess).</param>
/// <param name="ExcessOrInd">
/// The value index when <paramref name="Handler"/> is present.
/// Otherwise the number of trailing characters to remove when the segment ends its section.
/// </param>
/// <param name="IsSection">Whether this segment starts a SQL section such as <c>WHERE</c> or <c>SELECT</c>.</param>
/// <param name="Handler">
/// The handler that writes a value into the SQL.
/// Use <see langword="null"/> for literal text.
/// </param>
public record struct QuerySegment(int Start, int Length, int ExcessOrInd, bool IsSection, IQuerySegmentHandler? Handler);
/// <summary>
/// One optional part of a query template and the segments its key turns on or off.
/// </summary>
/// <param name="CondIndex">
/// The value index that decides whether the SQL part is included.
/// </param>
/// <param name="SegmentInd">The starting index in the <see cref="QueryFactory.Segments"/> array controlled by this condition.</param>
/// <param name="Length">The number of contiguous <see cref="QuerySegment"/>s tied to this logical footprint.</param>
/// <param name="NbConditionSkip">
/// A positive value is the number of following conditions skipped when this condition fails.
/// A negative value marks an OR group and its absolute value is the size of that group.
/// </param>
/// <param name="IsNeeded">Whether the value must be present rather than absent.</param>
public record struct Condition(int CondIndex, int SegmentInd, int Length, int NbConditionSkip, bool IsNeeded) : IComparable<Condition> {
    /// <summary>Orders conditions by their SQL segment.</summary>
    public readonly int CompareTo(Condition other) {
        int c = SegmentInd.CompareTo(other.SegmentInd);
        if (c != 0)
            return c;
        return other.Length.CompareTo(Length);
    }
}
