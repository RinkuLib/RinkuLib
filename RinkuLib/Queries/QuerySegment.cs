namespace RinkuLib.Queries;
/// <summary>
/// A data-only descriptor for a fragment of the query string.
/// </summary>
/// <param name="Start">The absolute start position within the normalized <c>Query</c> string.</param>
/// <param name="Length">The total length of the segment (including potential excess).</param>
/// <param name="ExcessOrInd">
/// <list type="bullet">
/// <item><b>If Handler exists:</b> Acts as the <c>Mapper</c> index to fetch the variable value.</item>
/// <item><b>If Handler is null:</b> The count of trailing characters to trim if this segment is the last one rendered in its section.</item>
/// </list>
/// </param>
/// <param name="IsSection">If <c>true</c>, this segment marks the boundary of a new structural block (e.g., a WHERE or SELECT clause).</param>
/// <param name="Handler">
/// The processing logic for this segment.
/// <list type="bullet">
/// <item><b>null:</b> No complex logic; the segment is used as-is (passthrough).</item>
/// <item><b>Concrete Implementation:</b> Required for segments that must transform an external value 
/// (at <paramref name="ExcessOrInd"/>) into a SQL-ready string.</item>
/// </list>
/// <i>Note: <c>IQuerySegmentHandler.NotSet</c> is a transient state used during factory construction; 
/// it must be replaced with a valid handler before the segment is passed to an execution engine.</i>
/// </param>
public record struct QuerySegment(int Start, int Length, int ExcessOrInd, bool IsSection, IQuerySegmentHandler? Handler);
/// <summary>
/// A data-only descriptor defining logical gates and forward-jumps for query assembly.
/// </summary>
/// <param name="CondIndex">
/// The index in the external state array (defined by the <c>Mapper</c> contract) used to 
/// determine if this logical branch should be included.
/// </param>
/// <param name="SegmentInd">The starting index in the <see cref="QueryFactory.Segments"/> array controlled by this condition.</param>
/// <param name="Length">The number of contiguous <see cref="QuerySegment"/>s tied to this logical footprint.</param>
/// <param name="NbConditionSkip">
/// <para><b>If Positive (> 0):</b> Represents a forward-jump offset for AND/Nested logic. 
/// If the condition fails, the engine skips this many subsequent <see cref="Condition"/> entries.</para>
/// <para><b>If Negative (&lt; 0):</b> Represents an OR-group relationship. The absolute value 
/// indicates the total number of conditions (including itself) that form the OR block. 
/// If this condition is met (True), the engine can short-circuit and skip the remaining members of the OR-group.</para>
/// </param>
public record struct Condition(int CondIndex, int SegmentInd, int Length, int NbConditionSkip) : IComparable<Condition> {
    /// <summary>The logic for sorting</summary>
    public readonly int CompareTo(Condition other) {
        int c = SegmentInd.CompareTo(other.SegmentInd);
        if (c != 0)
            return c;
        c = other.Length.CompareTo(Length);
        if (c != 0)
            return c;
        return other.NbConditionSkip.CompareTo(NbConditionSkip);
    }
}