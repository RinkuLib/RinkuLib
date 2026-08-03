using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Queries;
/// <summary>
/// A marker that both binds database parameters and writes SQL, for a variable that expands into several
/// parameters, such as a list spread into an <c>IN</c> clause. Subclass it and register a letter in
/// <see cref="SpecialHandlerGetter"/> to add your own.
/// </summary>
/// <remarks>
/// It works in two passes each run, and an implementation must respect the order. First the binding pass,
/// <see cref="Use(IDbCommand, ref object?)"/>, <see cref="SaveUse"/>, or <see cref="Update"/>, sets the command's
/// parameters and may rewrite the value in place (a list, for instance, becomes its element count). Then the
/// render pass, <see cref="Handle"/>, writes the SQL from that rewritten value, so it never re-reads the
/// original input.
/// </remarks>
public abstract class SpecialHandler : IQuerySegmentHandler {
    /// <summary>Builds a command's special handlers, one per claimed variable, from its parsed template.</summary>
    public static SpecialHandler[] GetHandlers(int startSpecialHandlers, int startBaseHandlers, Mapper mapper, string queryString, QuerySegment[] segments) {
        if (startSpecialHandlers == startBaseHandlers)
            return [];
        var handlers = new SpecialHandler[startBaseHandlers - startSpecialHandlers];
        for (int i = 0; i < segments.Length; i++) {
            ref var seg = ref segments[i];
            var h = seg.Handler;
            if (h is null || h != IQuerySegmentHandler.NotSet)
                continue;
            var last = seg.Start + seg.Length - 1;
            var ind = mapper.GetIndex(queryString[seg.Start..(last - 1)]);
            ref var handler = ref handlers[ind - startSpecialHandlers];
            if (handler is null) {
                var suffix = queryString[last];
                if (!SpecialHandlerGetter.TryGetValue(suffix, out var getter))
                    throw new RinkuTemplateException(ErrorCodes.UnknownHandlerSuffix, $"unknown handler suffix \"_{suffix}\" on variable \"{mapper.GetKey(ind)}\"");
                handler = getter(mapper.GetKey(ind));
            }
            seg.Handler = handler;
            seg.ExcessOrInd = ind;
        }
        return handlers;
    }
    /// <summary>
    /// The suffix letters that map to a special handler, <c>X</c> for list spread to start. Add a letter here
    /// to register your own handler for every command.
    /// </summary>
    public static readonly LetterMap<HandlerGetter<SpecialHandler>> SpecialHandlerGetter = new() {
        ['X'] = MultiVariableHandler.Build,
    };
    /// <summary>
    /// Whether this handler has settled its parameter metadata, so it binds without further learning.
    /// </summary>
    public bool IsCached;
    /// <summary>
    /// Whether the handler can render anything from the value it was handed, asked once a value has reached
    /// it and answered by the handler alone. What decides whether a value gets this far is a separate matter
    /// settled earlier, a <see langword="null"/> slot, a member the parameter object does not count as
    /// supplied, or a <c>Use</c> the caller never called. None of those are the handler being overruled, they
    /// are a value never arriving. Once one does, this is the last word on it, and answering
    /// <see langword="false"/> prunes the footprint the way an unsupplied variable's would.
    /// </summary>
    /// <remarks>
    /// Nothing upstream is a guarantee. A value reaches this from a <c>Use</c>, from a member the parameter
    /// object counted as supplied, or from a presence rule the caller wrote, and none of them owe the handler
    /// a shape. The complete job belongs here, including walking a sequence that will not answer any other
    /// way. The slot is the handler's to rewrite for that, so what the walk cost can be handed to the bind.
    /// </remarks>
    /// <param name="value">
    /// The value, never <see langword="null"/> on the way in, in a slot the handler may rewrite.
    /// </param>
    public virtual bool CanHandle(ref object? value) => true;
    /// <summary>
    /// The part of <see cref="CanHandle"/> that is cheap enough to fold into the presence check a parameter
    /// object is read with, turning a key away before its value is ever fetched. This is an optimization and
    /// carries no promise, so whatever it lets through is asked of <see cref="CanHandle"/> in full.
    /// Returning <see langword="null"/> keeps the member on the ordinary presence check.
    /// </summary>
    /// <param name="targetType">The parameter object's type.</param>
    /// <param name="member">The member holding this handler's variable.</param>
    public virtual IAccessorEmiter? GetUsageEmitter(Type targetType, System.Reflection.MemberInfo member) => null;
    /// <summary>
    /// Re-binds for a new run from the rewritten value a previous <see cref="SaveUse"/> left, adding, changing,
    /// or dropping parameters to match, and keeping the bound-command road warm.
    /// </summary>
    /// <param name="cmd">The command to update.</param>
    /// <param name="currentValue">The rewritten value from the previous <see cref="SaveUse"/>.</param>
    /// <param name="newValue">The new value for this run.</param>
    /// <returns><see langword="true"/> when the parameters were updated.</returns>
    public abstract bool Update(IDbCommand cmd, ref object? currentValue, object? newValue);
    /// <summary>
    /// Binds the value's parameters and rewrites <paramref name="value"/> to what a later <see cref="Update"/>
    /// needs, so reusing the same command can update in place.
    /// </summary>
    /// <param name="cmd">The command to bind onto.</param>
    /// <param name="value">On the way in, the value to bind. On the way out, the state <see cref="Update"/> reuses.</param>
    /// <returns><see langword="true"/> when the parameters were bound.</returns>
    public abstract bool SaveUse(IDbCommand cmd, ref object? value);
    /// <summary>
    /// Binds the value's parameters for a single run, without keeping the state a later <see cref="Update"/>
    /// would need. When the value cannot report a count without being walked again, the implementation
    /// rewrites it to what the render pass needs, so a lazy sequence is enumerated exactly once.
    /// </summary>
    /// <param name="cmd">The command to bind onto.</param>
    /// <param name="value">On the way in, the value to bind. On the way out, what <see cref="Handle"/> reads.</param>
    /// <returns><see langword="true"/> when the parameters were bound.</returns>
    public abstract bool Use(IDbCommand cmd, ref object? value);
    /// <summary> The <see cref="DbCommand"/> form of <see cref="Use(IDbCommand, ref object?)"/>. </summary>
    public abstract bool Use(DbCommand cmd, ref object? value);
    /// <summary>
    /// Writes the SQL for this variable, reading the value the binding pass rewrote rather than the original input.
    /// </summary>
    /// <param name="sb">The builder the query is being assembled in.</param>
    /// <param name="value">The value as the binding pass left it.</param>
    public abstract void Handle(ref ValueStringBuilder sb, object value);
    /// <summary>
    /// Settles this handler's parameter metadata from a provider reader, so later runs bind without inferring.
    /// </summary>
    public abstract bool UpdateCache<T>(T infoGetter) where T : IDbParamInfoGetter;
}
/// <summary>
/// The handler behind the <c>_X</c> marker. It spreads a collection into numbered parameters, so
/// <c>@Items</c> becomes <c>@Items_1, @Items_2, ... @Items_N</c>, one per element, ready for an <c>IN</c> clause.
/// </summary>
public class MultiVariableHandler(string ParameterName) : SpecialHandler {
    /// <summary> The base name the numbered parameters are built from. </summary>
    public string ParameterName = ParameterName;

    /// <summary> How each element parameter is bound. </summary>
    public DbParamInfo CachedParam = InferedDbParamCache.Instance;
    /// <summary>
    /// The factory used to register this handler under a letter in <see cref="SpecialHandler.SpecialHandlerGetter"/>.
    /// </summary>
    public static MultiVariableHandler Build(string Name)
        => new(Name);

    /// <summary>
    /// A spread writes one parameter per element, so a collection with no elements leaves it nothing to
    /// write. A sequence is walked to find out, and the slot keeps what the walk took. A value that is not a
    /// collection is left alone, a count among them.
    /// </summary>
    public override bool CanHandle(ref object? value) => SpreadUsage.HasElement(ref value);
    /// <inheritdoc/>
    public override IAccessorEmiter? GetUsageEmitter(Type targetType, System.Reflection.MemberInfo member)
        => new SpreadUsageEmitter(targetType, member);
    /// <summary>
    /// Performs a differential update on the command. Adds, updates, or prunes 
    /// parameters based on the change in collection size since the last <see cref="SaveUse"/>.
    /// </summary>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (newValue is not System.Collections.IEnumerable e) {
            if (newValue is not null)
                return false;
            if (currentValue is null)
                return true;
            if (currentValue is not object[] currentArr)
                return false;
            RemoveArray(cmd, currentArr);
            currentValue = null!;
            return true;
        }
        if (currentValue is not object[] arr)
            throw new RinkuBindingException(ErrorCodes.ValueNotSet,
                $"the spread for \"{ParameterName}\" was updated before a bind saved its state");
        object[] array = [.. e];
        var cached = CachedParam;
        if (array.Length == arr.Length) {
            for (int i = 0; i < array.Length; i++)
                if (!cached.Update(cmd, ref arr[i]!, array[i]))
                    return false;
            currentValue = arr;
            return true;
        }
        if (array.Length < arr.Length) {
            for (int i = 0; i < array.Length; i++) {
                if (!cached.Update(cmd, ref arr[i]!, array[i]))
                    return false;
                array[i] = arr[i];
            }
            for (int i = array.Length; i < arr.Length; i++)
                cached.Remove(cmd, arr[i]);
            currentValue = array.Length <= 0 ? null : array;
            return true;
        }
        System.Diagnostics.Debug.Assert(array.Length > arr.Length);
        for (int i = 0; i < arr.Length; i++) {
            if (!cached.Update(cmd, ref arr[i]!, array[i]))
                return false;
            array[i] = arr[i];
        }
        int nbDigits = ValueStringBuilder.DigitCount(arr.Length);
        int lastWithSameNbDidgit = 1;
        for (int j = 0; j < nbDigits; j++) lastWithSameNbDidgit *= 10;
        lastWithSameNbDidgit -= 1;
        for (int i = arr.Length; i < array.Length; i++) {
            if (i >= lastWithSameNbDidgit) {
                nbDigits++;
                lastWithSameNbDidgit = ((lastWithSameNbDidgit + 1) * 10) - 1;
            }
            if (!cached.SaveUse(BuildName(ParameterName, i+1, nbDigits), cmd, ref array[i]))
                return false;
        }
        currentValue = array;
        return true;
    }
    /// <inheritdoc/>
    public override bool UpdateCache<T>(T infoGetter) {
        if (!infoGetter.TryGetInfo(ParameterName, out var info))
            return false;
        CachedParam = info;
        IsCached = CachedParam.IsCached;
        return true;
    }
    private void RemoveArray(IDbCommand cmd, object[] oldArray) {
        if (oldArray.Length == 0)
            return;
        var cached = CachedParam;
        for (int i = 0; i < oldArray.Length; i++)
            cached.Remove(cmd, oldArray[i]);
    }
    /// <summary>
    /// Binds the collection and replaces <paramref name="value"/> with an <c>object[]</c> 
    /// to enable subsequent differential <see cref="Update"/> calls.
    /// </summary>
    public override bool SaveUse(IDbCommand cmd, ref object? value) {
        if (value is not System.Collections.IEnumerable e) return false;
        object[] array = [.. e];
        if (array.Length == 0) {
            value = null!;
            return true;
        }
        int nbDigits = 1;
        int lastWithSameNbDidgit = 9;
        var cached = CachedParam;
        for (int i = 0; i < array.Length; i++) {
            if (i >= lastWithSameNbDidgit) {
                nbDigits++;
                lastWithSameNbDidgit = ((lastWithSameNbDidgit + 1) * 10) - 1;
            }
            if (!cached.SaveUse(BuildName(ParameterName, i+1, nbDigits), cmd, ref array[i]))
                return false;
        }
        value = array;
        return true;
    }
    /// <summary>
    /// Binds the collection for a single pass. A collection that can report its count is left in place for
    /// the render pass to count; a lazy sequence is walked only here, and <paramref name="value"/> becomes
    /// the <c>int</c> count of bound items instead.
    /// </summary>
    public override bool Use(IDbCommand cmd, ref object? value) {
        if (value is not System.Collections.IEnumerable e) return false;
        bool cheapCount = HasCheapCount(e);
        int i = 1;
        int nbDigits = 1;
        int nextPow10 = 10;
        var cached = CachedParam;
        foreach (var item in e) {
            if (i >= nextPow10) {
                nbDigits++;
                nextPow10 *= 10;
            }
            cached.Use(BuildName(ParameterName, i, nbDigits), cmd, item);
            i++;
        }
        if (!cheapCount)
            value = i - 1;
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(DbCommand cmd, ref object? value) {
        if (value is not System.Collections.IEnumerable e) return false;
        bool cheapCount = HasCheapCount(e);
        int i = 1;
        int nbDigits = 1;
        int nextPow10 = 10;
        var cached = CachedParam;
        foreach (var item in e) {
            if (i >= nextPow10) {
                nbDigits++;
                nextPow10 *= 10;
            }
            cached.Use(BuildName(ParameterName, i, nbDigits), cmd, item);
            i++;
        }
        if (!cheapCount)
            value = i - 1;
        return true;
    }
    private static bool HasCheapCount(System.Collections.IEnumerable value)
        => value is System.Collections.ICollection
        || (value is IEnumerable<object> g && g.TryGetNonEnumeratedCount(out _))
        || value.TryGetNonEnumeratedCount(out _);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string BuildName(string parameterName, int index, int digitCount) {
        return string.Create(
            parameterName.Length + 1 + digitCount,
            (parameterName, index, digitCount),
            static (span, s) => {
                s.parameterName.AsSpan().CopyTo(span);
                int pos = s.parameterName.Length;
                span[pos++] = '_';
                int v = s.index;
                int i = pos + s.digitCount - 1;
                do {
                    span[i--] = (char)('0' + (v % 10));
                    v /= 10;
                } while (v != 0);
            });
    }
    /// <summary>
    /// Renders the SQL fragment (e.g., <c>@P_1, @P_2</c>). 
    /// Requires <paramref name="value"/> to be the <c>int</c> or <c>object[].Length</c> 
    /// produced during the synchronization phase.
    /// </summary>
    public override void Handle(ref ValueStringBuilder sb, object value) {
        if (value is not IEnumerable<object> enumerable || !enumerable.TryGetNonEnumeratedCount(out var nb)) {
            if (value is System.Collections.ICollection collection)
                nb = collection.Count;
            else if (value is int c)
                nb = c;
            else if (value is System.Collections.IEnumerable e) {
                if (!e.TryGetNonEnumeratedCount(out nb)) {
                    nb = 0;
                    var en = e.GetEnumerator();
                    while (en.MoveNext())
                        nb++;
                    (en as IDisposable)?.Dispose();
                }
            }
            else
                throw new RinkuBindingException(ErrorCodes.HandlerValueType, "the value handed to a spread must be a collection that can report a count");
        }
        if (nb <= 0)
            throw new RequiredHandlerValueException(ParameterName);
        var nameSpan = ParameterName.AsSpan();
        int nameLen = nameSpan.Length;
        var dst = sb.AppendSpan(ComputeTotalLength(nb) + (nb * (nameLen + 3)));
        int pos = 0;
        int digits = 1;
        int nextPow10 = 10;
        for (int i = 1; i <= nb; i++) {
            if (i == nextPow10) {
                digits++;
                nextPow10 *= 10;
            }
            nameSpan.CopyTo(dst.Slice(pos, nameLen));
            pos += nameLen;
            dst[pos++] = '_';
            int last = pos + digits - 1;
            for (int v = i, d = last; d >= pos; d--) {
                dst[d] = (char)('0' + (v % 10));
                v /= 10;
            }
            pos = last + 1;
            dst[pos++] = ',';
            dst[pos++] = ' ';
        }
        sb.Length -= 2;
    }
    static int ComputeTotalLength(int nb) {
        int total = 0;
        int next = 10;
        var currentNb = nb;
        while (true) {
            total += currentNb;
            if (next > nb)
                break;
            currentNb = 1 + nb - next;
            next *= 10;
        }
        return total;
    }
}
