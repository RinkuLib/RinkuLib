using System.Diagnostics;
using System.Runtime.InteropServices;
using Rinku.Internal;

namespace Rinku.Querying;

/// <summary>
/// Renders a template down to the SQL a single run sends, dropping the parts whose values are absent and
/// filling in the handler spots.
/// </summary>
public interface IQueryText {
    /// <summary>
    /// The SQL for one run, taking the values from an array. A part stays when its key has a value and drops
    /// when it does not. When nothing is optional the original template is returned untouched.
    /// </summary>
    /// <param name="variables">The values for this run, one slot per key.</param>
    /// <returns>The rendered SQL, or the original template when no part was dropped.</returns>
    /// <exception cref="RequiredHandlerValueException">A required handler spot had no value.</exception>
    public string Parse(object?[] variables);
    /// <summary>Whether a key controls any optional part of the template.</summary>
    public bool IsInCondition(int varIndex);
    /// <summary>
    /// The SQL for one run, taking which keys are present from <paramref name="usageMap"/> and the values the
    /// handler spots render from <paramref name="handlerValues"/>.
    /// </summary>
    /// <param name="usageMap">Which keys are present this run.</param>
    /// <param name="handlerValues">
    /// One slot per handled key, in key order from the first handled one, holding the value the binding pass
    /// left. Empty for a template with no handler, which needs no values at all.
    /// </param>
    /// <returns>The rendered SQL, or the original template when no part was dropped.</returns>
    /// <exception cref="RequiredHandlerValueException">A required handler spot had no value.</exception>
    public string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues);
    /// <summary>How many slots <see cref="Parse(Span{bool}, ReadOnlySpan{object})"/> reads values from.</summary>
    public int HandlerValuesLength { get; }
}
/// <summary>Thrown when a required part of the query needs a handler value that the run did not supply.</summary>
public class RequiredHandlerValueException : RinkuBindingException {
    /// <summary>The key slot whose value was missing, or -1 when the refusal came from the handler itself.</summary>
    public int Index;
    /// <summary>The key was absent and the segment that needed it was kept.</summary>
    public RequiredHandlerValueException(int Index)
        : base(ErrorCodes.RequiredHandlerValue, $"The variable at index {Index} should be set")
        => this.Index = Index;
    /// <summary>
    /// The segment was kept and the handler still had nothing to write. This is the refusal a handler makes
    /// for itself, which is reached when the value counted as supplied and the handler cannot render it.
    /// </summary>
    public RequiredHandlerValueException(string variableName)
        : base(ErrorCodes.RequiredHandlerValue, $"the query keeps \"{variableName}\" and its value renders nothing")
        => Index = -1;
}
/// <summary>
/// Renders a query template for one run. Use <see cref="IQueryText.Parse(object[])"/> to inspect the SQL that
/// a set of values produces without running a command.
/// </summary>
public abstract class QueryText : IQueryText {
    /// <summary>Gets the template with conditional markers removed.</summary>
    public readonly string QueryString;
    /// <summary>Gets the literal text and handler parts of the template.</summary>
    public readonly QuerySegment[] Segments;
    /// <summary>Gets the optional parts and their controlling keys.</summary>
    public readonly Condition[] Conditions;
    /// <summary>The number of key slots a run's values array must carry, checked by <see cref="Parse(object[])"/>.</summary>
    public readonly int RequiredVariablesLength;
    /// <inheritdoc/>
    public int HandlerValuesLength => NbHandlers;
    private protected readonly int HandlersStart;
    private protected readonly int NbHandlers;
    private protected int AverageLengthChunk;
    private int NbExecuted;
    private const int MaxExecution = 1024;

    private protected QueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers) {
        QueryString = queryString;
        AverageLengthChunk = queryString.Length;
        Segments = segments;
        Conditions = conditions;
        RequiredVariablesLength = conditions[^1].CondIndex;
        HandlersStart = handlersStart;
        NbHandlers = nbHandlers;
    }
    internal static QueryText Create(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers) {
        if (conditions.Length == 1 && segments.Length == 1)
            return new StaticQueryText(queryString, segments, conditions, handlersStart, nbHandlers);
        if (nbHandlers <= 0)
            return new ConditionalQueryText(queryString, segments, conditions, handlersStart, nbHandlers);
        return new HandledQueryText(queryString, segments, conditions, handlersStart, nbHandlers);
    }
    /// <inheritdoc/>
    public bool IsInCondition(int varIndex) {
        for (int i = 0; i < Conditions.Length; i++)
            if (Conditions[i].CondIndex == varIndex)
                return true;
        return false;
    }
    /// <inheritdoc/>
    public abstract string Parse(object?[] variables);
    /// <inheritdoc/>
    public abstract string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues);
    private protected ValueStringBuilder StartBuilder()
        => AverageLengthChunk <= 512 ? new ValueStringBuilder(512) : new ValueStringBuilder(AverageLengthChunk);
    private protected void UpdateAvg(int length) {
        if (NbExecuted > MaxExecution)
            return;
        NbExecuted++;
        AverageLengthChunk += (length - AverageLengthChunk) / NbExecuted;
        int estimated = (AverageLengthChunk + 128) & ~64;
        AverageLengthChunk = estimated == 512 ? 576 : estimated;
    }
}

internal sealed class StaticQueryText : QueryText {
    internal StaticQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }
    public override string Parse(object?[] variables) => QueryString;
    public override string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues) => QueryString;
}

internal sealed class ConditionalQueryText : QueryText {
    internal ConditionalQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }

    public override unsafe string Parse(object?[] variables) {
        Debug.Assert(variables.Length == RequiredVariablesLength);
        ref object? pVarBase = ref MemoryMarshal.GetArrayDataReference(variables);
        var sb = StartBuilder();
        var start = 0;
        var length = 0;
        var prevExcess = 0;
        fixed (char* ptr = &MemoryMarshal.GetReference(QueryString.AsSpan()))
        fixed (Condition* conditions = &MemoryMarshal.GetReference(Conditions.AsSpan())) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;
                Restart:
                    if ((System.Runtime.CompilerServices.Unsafe.Add(ref pVarBase, (*cond).CondIndex) is null) == (*cond).IsNeeded) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if ((System.Runtime.CompilerServices.Unsafe.Add(ref pVarBase, (*(cond + j)).CondIndex) is not null) == (*(cond + j)).IsNeeded)
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
                if (length == 0) {
                    if (seg.IsSection)
                        sb.Length -= prevExcess;
                    start = seg.Start;
                }
                length += seg.Length;
                prevExcess = seg.ExcessOrInd;
                i++;
            }
            if (length == QueryString.Length) {
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

    public override unsafe string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);
        var sb = StartBuilder();
        var start = 0;
        var length = 0;
        var prevExcess = 0;
        fixed (char* ptr = &MemoryMarshal.GetReference(QueryString.AsSpan()))
        fixed (Condition* conditions = &MemoryMarshal.GetReference(Conditions.AsSpan())) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;
                Restart:
                    if (usageMap[(*cond).CondIndex] != (*cond).IsNeeded) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if (usageMap[(*(cond + j)).CondIndex] == (*(cond + j)).IsNeeded)
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
                if (length == 0) {
                    if (seg.IsSection)
                        sb.Length -= prevExcess;
                    start = seg.Start;
                }
                length += seg.Length;
                prevExcess = seg.ExcessOrInd;
                i++;
            }
            if (length == QueryString.Length) {
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
}

internal sealed class HandledQueryText : QueryText {
    internal HandledQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }

    public override unsafe string Parse(object?[] variables) {
        Debug.Assert(variables.Length == RequiredVariablesLength);
        ref object? pVarBase = ref MemoryMarshal.GetArrayDataReference(variables);
        var sb = StartBuilder();
        var start = 0;
        var length = 0;
        var prevExcess = 0;
        fixed (char* ptr = &MemoryMarshal.GetReference(QueryString.AsSpan()))
        fixed (Condition* conditions = &MemoryMarshal.GetReference(Conditions.AsSpan())) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;
                Restart:
                    if ((System.Runtime.CompilerServices.Unsafe.Add(ref pVarBase, (*cond).CondIndex) is null) == (*cond).IsNeeded) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if ((System.Runtime.CompilerServices.Unsafe.Add(ref pVarBase, (*(cond + j)).CondIndex) is not null) == (*(cond + j)).IsNeeded)
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
                    var val = System.Runtime.CompilerServices.Unsafe.Add(ref pVarBase, seg.ExcessOrInd)
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
            if (length > 0)
                sb.Append(ptr + start, length);
            else
                sb.Length -= prevExcess;
        }
        UpdateAvg(sb.Length);
        return sb.ToStringAndDispose();
    }

    public override unsafe string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);
        Debug.Assert(handlerValues.Length == NbHandlers);
        var sb = StartBuilder();
        var start = 0;
        var length = 0;
        var prevExcess = 0;
        fixed (char* ptr = &MemoryMarshal.GetReference(QueryString.AsSpan()))
        fixed (Condition* conditions = &MemoryMarshal.GetReference(Conditions.AsSpan())) {
            var cond = conditions;
            int i = 0;
            while (true) {
                if ((*cond).SegmentInd == i) {
                    if ((*cond).Length < 0)
                        break;
                Restart:
                    if (usageMap[(*cond).CondIndex] != (*cond).IsNeeded) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++)
                                if (usageMap[(*(cond + j)).CondIndex] == (*(cond + j)).IsNeeded)
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
                    var val = handlerValues[seg.ExcessOrInd - HandlersStart]
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
            if (length > 0)
                sb.Append(ptr + start, length);
            else
                sb.Length -= prevExcess;
        }
        UpdateAvg(sb.Length);
        return sb.ToStringAndDispose();
    }
}
