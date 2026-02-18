using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// The execution contract responsible for assembling the final query string.
/// </summary>
public interface IQueryText {
    /// <summary>Processes the variables to genereate the SQL query string</summary>
    public unsafe string Parse(object?[] variables);
}
/// <summary>Thrown when a handled variable needs to be used in the final SQL query, but the value was not provided</summary>
public class RequiredHandlerValueException(int Index) : Exception($"The variable at index {Index} should be set") {
    /// <summary>Index of the missing handled variable</summary>
    public int Index = Index;
}
/// <summary>
/// The execution engine responsible for assembling the final query string.
/// </summary>
/// <remarks>
/// This engine utilizes a non-recursive, pointer-based traversal of <see cref="Condition"/> 
/// and <see cref="QuerySegment"/> arrays to minimize CPU overhead and memory pressure.
/// </remarks>
public sealed class QueryText : IQueryText {
    /// <summary> The original normalized template string. </summary>
    public readonly string QueryString;
    /// <summary> The structural metadata defining the query fragments. </summary>
    public readonly QuerySegment[] Segments;
    /// <summary> The jump-table metadata defining the logical branching. </summary>
    public readonly Condition[] Conditions;
    /// <summary>Specifies the expected length of the variables array on <see cref="Parse(object[])"/></summary>
    public readonly int RequiredVariablesLength;
    private int AverageLengthChunk;
    private int NbExecuted;
    private const int MaxExecution = 1024;
    private readonly bool ContainsHandlers;
    internal QueryText(string QueryString, QuerySegment[] Segments, Condition[] Conditions) {
        this.QueryString = QueryString;
        this.AverageLengthChunk = QueryString.Length;
        this.Segments = Segments;
        this.Conditions = Conditions;
        this.RequiredVariablesLength = Conditions[^1].CondIndex;
        ContainsHandlers = Segments.Any(s => s.Handler is not null);
    }
    /// <summary>
    /// Processes the input state array to synthesize the final query string based on segment logic.
    /// </summary>
    /// <remarks>
    /// The execution follows a high-performance linear path:
    /// <list type="bullet">
    /// <item>Evaluates the <paramref name="usageMap"/> against the jump-table to determine segment visibility.</item>
    /// <item>Invokes <see cref="IQuerySegmentHandler.Handle"/> for dynamic segments, passing the current state value.</item>
    /// <item>Bypasses string allocation and returns <see cref="QueryString"/> if the logic resolves to the full original template.</item>
    /// <item>Adapts the underlying memory buffer based on previous execution metrics to minimize reallocations.</item>
    /// </list>
    /// </remarks>
    /// <param name="usageMap">The state array containing logical indicators</param>
    /// <param name="accessor">The accessor to get the values</param>
    /// <returns>The assembled SQL string or the original template if no modifications occurred.</returns>
    /// <exception cref="RequiredHandlerValueException">Thrown when a required variable is null during handler execution.</exception>
    public unsafe string Parse(Span<bool> usageMap, TypeAccessor accessor) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
        var start = 0;
        var length = 0;
        var prevExcess = 0;

        fixed (char* ptr = QueryString)
        fixed (Condition* conditions = Conditions) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;

                Restart:
                    if (!usageMap[(*cond).CondIndex]) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if (usageMap[(*(cond + j)).CondIndex])
                                    break;
                            if (j <= orCount) {
                                cond += orCount + 1;
                                continue;
                            }
                            skip = -skip;
                        }
                        i += (*cond).Length;
                        cond += skip;
                        continue;
                    }
                    else {
                        cond++;
                        if ((*cond).SegmentInd == i)
                            goto Restart;
                    }
                }

                var seg = Segments[i];
                if (seg.Handler is not null) {
                    if (length > 0) {
                        sb.Append(ptr + start, length);
                        length = 0;
                    }
                    prevExcess = 0;
                    start = seg.Start + seg.Length;

                    var val = accessor.GetValue(seg.ExcessOrInd)
                        ?? throw new RequiredHandlerValueException(seg.ExcessOrInd);

                    seg.Handler.Handle(ref sb, val);
                    i++;
                    continue;
                }

                if (length == 0) {
                    if (seg.IsSection)
                        sb.Length -= prevExcess;
                    start = seg.Start;
                }
                length += seg.Length;
                prevExcess = seg.ExcessOrInd;
                i++;
            }

            if (length == QueryString.Length && !ContainsHandlers) {
                sb.Dispose();
                return QueryString;
            }
            if (length > 0)
                sb.Append(ptr + start, length);
            else
                sb.Length -= prevExcess;
        }
        UpdateAvg(sb.Length);
        return sb.ToStringAndDispose();
    }
    /// <summary>
    /// Processes the input state array to synthesize the final query string based on segment logic.
    /// </summary>
    /// <remarks>
    /// The execution follows a high-performance linear path:
    /// <list type="bullet">
    /// <item>Evaluates the <paramref name="usageMap"/> against the jump-table to determine segment visibility.</item>
    /// <item>Invokes <see cref="IQuerySegmentHandler.Handle"/> for dynamic segments, passing the current state value.</item>
    /// <item>Bypasses string allocation and returns <see cref="QueryString"/> if the logic resolves to the full original template.</item>
    /// <item>Adapts the underlying memory buffer based on previous execution metrics to minimize reallocations.</item>
    /// </list>
    /// </remarks>
    /// <param name="usageMap">The state array containing logical indicators</param>
    /// <param name="accessor">The accessor to get the values</param>
    /// <returns>The assembled SQL string or the original template if no modifications occurred.</returns>
    /// <exception cref="RequiredHandlerValueException">Thrown when a required variable is null during handler execution.</exception>
    public unsafe string Parse<T>(Span<bool> usageMap, TypeAccessor<T> accessor) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
        var start = 0;
        var length = 0;
        var prevExcess = 0;

        fixed (char* ptr = QueryString)
        fixed (Condition* conditions = Conditions) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;

                Restart:
                    if (!usageMap[(*cond).CondIndex]) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if (usageMap[(*(cond + j)).CondIndex])
                                    break;
                            if (j <= orCount) {
                                cond += orCount + 1;
                                continue;
                            }
                            skip = -skip;
                        }
                        i += (*cond).Length;
                        cond += skip;
                        continue;
                    }
                    else {
                        cond++;
                        if ((*cond).SegmentInd == i)
                            goto Restart;
                    }
                }

                var seg = Segments[i];
                if (seg.Handler is not null) {
                    if (length > 0) {
                        sb.Append(ptr + start, length);
                        length = 0;
                    }
                    prevExcess = 0;
                    start = seg.Start + seg.Length;

                    var val = accessor.GetValue(seg.ExcessOrInd)
                        ?? throw new RequiredHandlerValueException(seg.ExcessOrInd);

                    seg.Handler.Handle(ref sb, val);
                    i++;
                    continue;
                }

                if (length == 0) {
                    if (seg.IsSection)
                        sb.Length -= prevExcess;
                    start = seg.Start;
                }
                length += seg.Length;
                prevExcess = seg.ExcessOrInd;
                i++;
            }

            if (length == QueryString.Length && !ContainsHandlers) {
                sb.Dispose();
                return QueryString;
            }
            if (length > 0)
                sb.Append(ptr + start, length);
            else
                sb.Length -= prevExcess;
        }
        UpdateAvg(sb.Length);
        return sb.ToStringAndDispose();
    }
    /// <summary>
    /// Processes the input state array to synthesize the final query string based on segment logic.
    /// </summary>
    /// <remarks>
    /// The execution follows a high-performance linear path:
    /// <list type="bullet">
    /// <item>Evaluates the nullability of <paramref name="variables"/> against the jump-table to determine segment visibility.</item>
    /// <item>Invokes <see cref="IQuerySegmentHandler.Handle"/> for dynamic segments, passing the current state value.</item>
    /// <item>Bypasses string allocation and returns <see cref="QueryString"/> if the logic resolves to the full original template.</item>
    /// <item>Adapts the underlying memory buffer based on previous execution metrics to minimize reallocations.</item>
    /// </list>
    /// </remarks>
    /// <param name="variables">The state array containing logical indicators and data values.</param>
    /// <returns>The assembled SQL string or the original template if no modifications occurred.</returns>
    /// <exception cref="RequiredHandlerValueException">Thrown when a required variable is null during handler execution.</exception>
    public unsafe string Parse(object?[] variables) {
        Debug.Assert(variables.Length == RequiredVariablesLength);
        ref object? pVarBase = ref MemoryMarshal.GetArrayDataReference(variables);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
        var start = 0;
        var length = 0;
        var prevExcess = 0;

        fixed (char* ptr = QueryString)
        fixed (Condition* conditions = Conditions) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;

                Restart:
                    if (Unsafe.Add(ref pVarBase, (*cond).CondIndex) is null) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++) 
                                if (Unsafe.Add(ref pVarBase, (*(cond + j)).CondIndex) is not null)
                                    break;
                            if (j <= orCount) {
                                cond += orCount + 1;
                                continue;
                            }
                            skip = -skip;
                        }
                        i += (*cond).Length;
                        cond += skip;
                        continue;
                    }
                    else {
                        cond++;
                        if ((*cond).SegmentInd == i)
                            goto Restart;
                    }
                }

                var seg = Segments[i];
                if (seg.Handler is not null) {
                    if (length > 0) {
                        sb.Append(ptr + start, length);
                        length = 0;
                    }
                    prevExcess = 0;
                    start = seg.Start + seg.Length;

                    var val = Unsafe.Add(ref pVarBase, seg.ExcessOrInd)
                        ?? throw new RequiredHandlerValueException(seg.ExcessOrInd);

                    seg.Handler.Handle(ref sb, val);
                    i++;
                    continue;
                }

                if (length == 0) {
                    if (seg.IsSection)
                        sb.Length -= prevExcess;
                    start = seg.Start;
                }
                length += seg.Length;
                prevExcess = seg.ExcessOrInd;
                i++;
            }

            if (length == QueryString.Length && !ContainsHandlers) {
                sb.Dispose();
                return QueryString;
            }
            if (length > 0)
                sb.Append(ptr + start, length);
            else
                sb.Length -= prevExcess;
        }
        UpdateAvg(sb.Length);
        return sb.ToStringAndDispose();
    }
    /// <summary>
    /// Dynamically adjusts the initial buffer allocation size based on a moving average of 
    /// previously generated query lengths.
    /// </summary>
    /// <remarks>
    /// This optimization reduces the frequency of internal array resizing within <see cref="ValueStringBuilder"/>.
    /// It ceases adjustment after <see cref="MaxExecution"/> to stabilize performance in long-running processes.
    /// </remarks>
    /// <param name="length">The length of the most recently generated query.</param>
    private void UpdateAvg(int length) {
        if (NbExecuted > MaxExecution)
            return;
        NbExecuted++;
        AverageLengthChunk += (length - AverageLengthChunk) / NbExecuted;
        int estimated = (AverageLengthChunk + 128) & ~64;
        AverageLengthChunk = estimated == 512 ? 576 : estimated;
    }
}