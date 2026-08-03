using System.Diagnostics;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

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
/// The compiled form of a template, ready to render the SQL for each run. Built once from the template and
/// held on the <see cref="QueryCommand"/>, it drops the parts a run leaves out and fills the handler spots,
/// returning the original template untouched when nothing was optional.
/// </summary>
/// <remarks>
/// What a template is made of settles when it is read and never changes after, so the kind of render it needs
/// is picked once, here, rather than asked again on every run. <see cref="Create"/> returns the one that fits,
/// and each carries only the work its own templates call for.
/// </remarks>
public abstract class QueryText : IQueryText {
    /// <summary> The template as written, with the markers stripped. </summary>
    public readonly string QueryString;
    /// <summary> The template broken into the runs of text and handler spots a render walks. </summary>
    public readonly QuerySegment[] Segments;
    /// <summary> The optional parts and the keys that switch them on or off. </summary>
    public readonly Condition[] Conditions;
    /// <summary>The number of key slots a run's values array must carry, checked by <see cref="Parse(object[])"/>.</summary>
    public readonly int RequiredVariablesLength;
    /// <inheritdoc/>
    public int HandlerValuesLength => NbHandlers;
    /// <summary>The first key a handler renders, the offset the values span is indexed from.</summary>
    protected readonly int HandlersStart;
    /// <summary>How many keys a handler renders.</summary>
    protected readonly int NbHandlers;
    /// <summary>The buffer size a render is expected to grow to, learned from the runs so far.</summary>
    protected int AverageLengthChunk;
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
    /// <summary>
    /// The render for this template, chosen from what the template turned out to hold.
    /// </summary>
    /// <param name="queryString">The template with its markers stripped.</param>
    /// <param name="segments">The runs of text and handler spots.</param>
    /// <param name="conditions">The optional parts and their keys.</param>
    /// <param name="handlersStart">The first key a handler renders.</param>
    /// <param name="nbHandlers">How many keys a handler renders.</param>
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
    /// <summary>Opens the builder a render writes into, sized from what the runs so far have needed.</summary>
    private protected ValueStringBuilder StartBuilder()
        => AverageLengthChunk <= 512 ? new ValueStringBuilder(512) : new ValueStringBuilder(AverageLengthChunk);
    /// <summary>Folds a render's length into the size the next one starts at.</summary>
    private protected void UpdateAvg(int length) {
        if (NbExecuted > MaxExecution)
            return;
        NbExecuted++;
        AverageLengthChunk += (length - AverageLengthChunk) / NbExecuted;
        int estimated = (AverageLengthChunk + 128) & ~64;
        AverageLengthChunk = estimated == 512 ? 576 : estimated;
    }
}

/// <summary>
/// A template with nothing optional and no handler spot. Every run sends the same SQL, so a render is the
/// template itself.
/// </summary>
public sealed class StaticQueryText : QueryText {
    internal StaticQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }
    /// <inheritdoc/>
    public override string Parse(object?[] variables) => QueryString;
    /// <inheritdoc/>
    public override string Parse(Span<bool> usageMap, ReadOnlySpan<object?> handlerValues) => QueryString;
}

/// <summary>
/// A template with optional parts and no handler spot. A render decides what to keep and copies it, and needs
/// to know only which keys a run supplied, never their values.
/// </summary>
public sealed class ConditionalQueryText : QueryText {
    internal ConditionalQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

/// <summary>
/// A template carrying at least one handler spot. A render writes those spots from the values the binding
/// pass left, so this is the only kind that reads values at all.
/// </summary>
public sealed class HandledQueryText : QueryText {
    internal HandledQueryText(string queryString, QuerySegment[] segments, Condition[] conditions, int handlersStart, int nbHandlers)
        : base(queryString, segments, conditions, handlersStart, nbHandlers) { }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
