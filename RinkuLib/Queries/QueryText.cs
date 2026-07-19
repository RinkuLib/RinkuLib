using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

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
    /// The SQL for one run, taking which keys are present from <paramref name="usageMap"/> and any handler
    /// values through <paramref name="accessor"/>.
    /// </summary>
    /// <param name="usageMap">Which keys are present this run.</param>
    /// <param name="accessor">Reads a value for a handler spot when one is needed.</param>
    /// <returns>The rendered SQL, or the original template when no part was dropped.</returns>
    /// <exception cref="RequiredHandlerValueException">A required handler spot had no value.</exception>
#if NET9_0_OR_GREATER
    public string Parse<T>(Span<bool> usageMap, T accessor) where T : ITypeAccessor, allows ref struct;
#else
    public string Parse(Span<bool> usageMap, NoTypeAccessor accessor);
#endif

#if !NET9_0_OR_GREATER
    /// <inheritdoc cref="Parse(Span{bool}, NoTypeAccessor)"/>
    public string Parse(Span<bool> usageMap, TypeAccessor accessor);
#endif
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
public sealed class QueryText : IQueryText {
    /// <summary> The template as written, with the markers stripped. </summary>
    public readonly string QueryString;
    /// <summary> The template broken into the runs of text and handler spots a render walks. </summary>
    public readonly QuerySegment[] Segments;
    /// <summary> The optional parts and the keys that switch them on or off. </summary>
    public readonly Condition[] Conditions;
    /// <summary>The number of key slots a run's values array must carry, checked by <see cref="Parse(object[])"/>.</summary>
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
    /// <inheritdoc/>
    public bool IsInCondition(int varIndex) {
        for (int i = 0; i < Conditions.Length; i++)
            if (Conditions[i].CondIndex == varIndex)
                return true;
        return false;
    }
    /// <inheritdoc/>
#if NET9_0_OR_GREATER
    public unsafe string Parse<T>(Span<bool> usageMap, T accessor) where T : ITypeAccessor, allows ref struct { 
#else
    public unsafe string Parse(Span<bool> usageMap, NoTypeAccessor accessor) {
#endif
        Debug.Assert(usageMap.Length == RequiredVariablesLength);
        if (Conditions.Length == 1 && Segments.Length == 1)
            return QueryString;
        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
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

                    if (!usageMap[seg.ExcessOrInd])
                        throw new RequiredHandlerValueException(seg.ExcessOrInd);
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
#if !NET9_0_OR_GREATER
    /// <inheritdoc/>
    public unsafe string Parse(Span<bool> usageMap, TypeAccessor accessor) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
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

                    if (!usageMap[seg.ExcessOrInd])
                        throw new RequiredHandlerValueException(seg.ExcessOrInd);
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
    /// <inheritdoc cref="Parse(Span{bool}, NoTypeAccessor)"/>
    public unsafe string Parse<T>(Span<bool> usageMap, TypeAccessor<T> accessor) {
        Debug.Assert(usageMap.Length == RequiredVariablesLength);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
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

                    if (!usageMap[seg.ExcessOrInd])
                        throw new RequiredHandlerValueException(seg.ExcessOrInd);
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
#endif
    /// <inheritdoc/>
    public unsafe string Parse(object?[] variables) {
        Debug.Assert(variables.Length == RequiredVariablesLength);
        ref object? pVarBase = ref MemoryMarshal.GetArrayDataReference(variables);

        ValueStringBuilder sb = AverageLengthChunk <= 512
                ? new ValueStringBuilder(stackalloc char[512])
                : new ValueStringBuilder(AverageLengthChunk);
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
                    if ((Unsafe.Add(ref pVarBase, (*cond).CondIndex) is null) == (*cond).IsNeeded) {
                        if (length > 0) {
                            sb.Append(ptr + start, length);
                            length = 0;
                        }
                        var skip = (*cond).NbConditionSkip;
                        if (skip < 0) {
                            var orCount = (*(cond + 1)).NbConditionSkip;
                            int j = 1;
                            for (; j <= orCount; j++) 
                                if ((Unsafe.Add(ref pVarBase, (*(cond + j)).CondIndex) is not null) == (*(cond + j)).IsNeeded)
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
    private void UpdateAvg(int length) {
        if (NbExecuted > MaxExecution)
            return;
        NbExecuted++;
        AverageLengthChunk += (length - AverageLengthChunk) / NbExecuted;
        int estimated = (AverageLengthChunk + 128) & ~64;
        AverageLengthChunk = estimated == 512 ? 576 : estimated;
    }
}