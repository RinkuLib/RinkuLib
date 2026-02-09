using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Defines a contract for handlers that participate in both the database command configuration 
/// and the SQL string generation phases.
/// </summary>
/// <remarks>
/// <para><b>Implementation Requirements:</b></para>
/// <para>This class facilitates a <b>Sequential Dual-Phase Lifecycle</b>. Developers must implement 
/// logic with the strict understanding that Command Synchronization (parameter binding) 
/// occurs before Query Assembly (string generation).</para>
/// 
/// <para><b>Sequential Lifecycle:</b></para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Phase 1: Command Synchronization.</b> Methods such as <see cref="Use(IDbCommand, object)"/>, <see cref="SaveUse"/>, 
/// or <see cref="Update"/> are invoked to synchronize the <see cref="IDbCommand"/> state. 
/// The architecture <b>specifically allows for destructive value transformation</b> during this phase. 
/// For example, an implementation may replace a raw <c>IEnumerable</c> with an <c>int</c> count 
/// in the state array.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Phase 2: Query Assembly.</b> The <see cref="Handle"/> method is subsequently invoked. 
/// It is designed to receive the <b>transformed value</b> produced in Phase 1, allowing for 
/// optimized string rendering without re-processing the original input data.
/// </description>
/// </item>
/// </list>
/// </remarks>
public abstract class SpecialHandler : IQuerySegmentHandler {
    /// <summary>
    /// Global registry mapping variable type-suffix characters (e.g., 'X' for Multi-Variable) 
    /// to their corresponding <see cref="SpecialHandler"/> factory delegates.
    /// </summary>
    public static readonly LetterMap<HandlerGetter<SpecialHandler>> SpecialHandlerGetter = new() {
        ['X'] = MultiVariableHandler.Build,
    };
    /// <summary>
    /// Indicates whether the handler has successfully resolved and cached its 
    /// parameter metadata (e.g., via <see cref="UpdateCache"/>).
    /// </summary>
    public bool IsCached;
    /// <summary>
    /// Synchronizes existing parameters within the <see cref="IDbCommand"/> using the 
    /// transformation state preserved by a previous <see cref="SaveUse"/> call.
    /// </summary>
    /// <param name="cmd">The command whose parameters require synchronization.</param>
    /// <param name="currentValue">The transformed state stored from a previous <see cref="SaveUse"/>.</param>
    /// <param name="newValue">The new raw data provided for this execution.</param>
    /// <returns><c>true</c> if the parameters were successfully synchronized.</returns>
    public abstract bool Update(IDbCommand cmd, ref object currentValue, object newValue);
    /// <summary>
    /// Binds data to the <see cref="IDbCommand"/> and preserves a detailed transformation 
    /// state to allow for subsequent <see cref="Update"/> calls on this command instance.
    /// </summary>
    /// <param name="cmd">The command to receive the parameters.</param>
    /// <param name="value">
    /// [In/Out] The value to bind. Replaced with the <b>detailed transformed state</b> 
    /// (e.g., an array of bound parameter objects) required for future differential updates.
    /// </param>
    /// <returns><c>true</c> if the parameters were successfully created and saved.</returns>
    public abstract bool SaveUse(IDbCommand cmd, ref object value);
    /// <summary>
    /// Binds data to the <see cref="IDbCommand"/> for a single execution pass.
    /// </summary>
    /// <remarks>
    /// This method is intended for one-off bindings where no subsequent <see cref="Update"/> 
    /// will be called on this command instance. It preserves only the minimal state 
    /// required for string assembly.
    /// </remarks>
    /// <param name="cmd">The command to receive the parameters.</param>
    /// <param name="value">The raw value to bind.</param>
    /// <returns><c>true</c> if the parameters were successfully bound.</returns>
    public abstract bool Use(IDbCommand cmd, object value);
    /// <summary>
    /// Removes parameters associated with this handler from the <see cref="IDbCommand"/>.
    /// </summary>
    /// <param name="cmd">The command to prune.</param>
    /// <param name="value">The transformed state value used to identify the parameters to remove.</param>
    /// <returns><c>true</c> if the removal was successful.</returns>
    public abstract bool Remove(IDbCommand cmd, object value);
    /// <summary> Performs the same logic as <see cref="Use(IDbCommand, object)"/> but specialized for <see cref="DbCommand"/> to reduce interface dispatch overhead. </summary>
    public abstract bool Use(DbCommand cmd, object value);

    /// <summary> Performs the same logic as <see cref="Remove(IDbCommand, object)"/> but specialized for <see cref="DbCommand"/> to reduce interface dispatch overhead. </summary>
    public abstract bool Remove(DbCommand cmd, object value);
    /// <summary>
    /// Generates the SQL fragment for this variable. This is called after the 
    /// command synchronization phase has transformed the input <paramref name="value"/>.
    /// </summary>
    /// <param name="sb">The builder used to assemble the final query string.</param>
    /// <param name="value">The <b>transformed value</b> produced by a synchronization method.</param>
    public abstract void Handle(ref ValueStringBuilder sb, object value);
    /// <summary>
    /// Resolves and caches database-specific parameter metadata via an external provider.
    /// </summary>
    public abstract bool UpdateCache<T>(T infoGetter) where T : IDbParamInfoGetter;
}
/// <summary>
/// A specialized handler that expands a collection into a sequence of numbered database parameters.
/// (e.g., <c>@Items</c> becomes <c>@Items_1, @Items_2, ... @Items_N</c>).
/// </summary>
/// <remarks>
/// This implementation fulfills the dual-phase contract by:
/// <list type="number">
/// <item><b>Command Phase:</b> Binding collection elements to numbered parameters and 
/// transforming the input <c>IEnumerable</c> into an <c>int</c> count or <c>object[]</c> state.</item>
/// <item><b>Query Phase:</b> Using that transformed state to render a comma-separated 
/// string of parameter names in the SQL.</item>
/// </list>
/// </remarks>
public class MultiVariableHandler(string ParameterName) : SpecialHandler {
    /// <summary> The base name used to generate numbered parameters. </summary>
    public string ParameterName = ParameterName;

    /// <summary> 
    /// Cached metadata for generating <see cref="IDataParameter"/> instances efficiently. 
    /// </summary>
    public DbParamInfo CachedParam = InferedDbParamCache.Instance;
    /// <summary>
    /// Used to create a <see cref="HandlerGetter{SpecialHandler}"/> delegate.
    /// </summary>
    public static MultiVariableHandler Build(string Name)
        => new(Name);
    /// <summary>
    /// Performs a differential update on the command. Adds, updates, or prunes 
    /// parameters based on the change in collection size since the last <see cref="SaveUse"/>.
    /// </summary>
    public override bool Update(IDbCommand cmd, ref object currentValue, object newValue) {
        if (newValue is not System.Collections.IEnumerable e) return false;
        if (currentValue is not object[] arr)
            throw new Exception("the value was not set or not saved");
        object[] array = [.. e];
        var cached = CachedParam;
        if (array.Length == arr.Length) {
            for (int i = 0; i < array.Length; i++)
                if (!cached.Update(cmd, ref arr[i], array[i]))
                    return false;
            currentValue = arr;
            return true;
        }
        if (array.Length < arr.Length) {
            for (int i = 0; i < array.Length; i++) {
                if (!cached.Update(cmd, ref arr[i], array[i]))
                    return false;
                array[i] = arr[i];
            }
            for (int i = array.Length; i < arr.Length; i++)
                cached.Remove(cmd, arr[i]);
            currentValue = array;
            return true;
        }
        if (array.Length > arr.Length) {
            for (int i = 0; i < arr.Length; i++) {
                if (!cached.Update(cmd, ref arr[i], array[i]))
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
        return false;
    }
    /// <inheritdoc/>
    public override bool UpdateCache<T>(T infoGetter) {
        if (!infoGetter.TryGetInfo(ParameterName, out var info))
            return false;
        CachedParam = info;
        IsCached = CachedParam.IsCached;
        return true;
    }
    /// <inheritdoc/>
    public override bool Remove(IDbCommand cmd, object oldValue) {
        if (oldValue is not int nb) {
            if (oldValue is not object[] arr)
                return false;
            RemoveArray(cmd, arr);
            return true;
        }
        if (nb == 0)
            return false;
        var ps = cmd.Parameters;
        var name = ParameterName;
        var nameLen = name.Length;
        var nameSpan = name.AsSpan();
        for (int i = ps.Count - 1; i >= 0; i--) {
            var p = ((IDbDataParameter)ps[i]!).ParameterName;
            if (p.Length <= nameLen || p[nameLen] != '_')
                continue;
            if (!p.AsSpan(0, nameLen).SequenceEqual(nameSpan))
                continue;
            ps.RemoveAt(i);
        }
        return true;
    }

    private void RemoveArray(IDbCommand cmd, object[] oldArray) {
        if (oldArray.Length == 0)
            return;
        var cached = CachedParam;
        for (int i = 0; i < oldArray.Length; i++)
            cached.Remove(cmd, oldArray[i]);
    }

    private void RemoveArray(DbCommand cmd, object[] oldArray) {
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
    public override bool SaveUse(IDbCommand cmd, ref object value) {
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
    /// Binds the collection for a single pass and replaces <paramref name="value"/> 
    /// with an <c>int</c> representing the count of bound items.
    /// </summary>
    public override bool Use(IDbCommand cmd, object value) {
        if (value is not System.Collections.IEnumerable e) return false;
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
        return true;
    }
    /// <summary>
    /// Removes all numbered parameters matching the <see cref="ParameterName"/> pattern 
    /// from the command's parameter collection.
    /// </summary>
    public override bool Remove(DbCommand cmd, object oldValue) {
        if (oldValue is not int nb) {
            if (oldValue is not object[] arr)
                return false;
            RemoveArray(cmd, arr);
            return true;
        }
        if (nb == 0)
            return false;
        var ps = cmd.Parameters;
        var name = ParameterName;
        var nameLen = name.Length;
        var nameSpan = name.AsSpan();
        for (int i = ps.Count - 1; i >= 0; i--) {
            var p = ps[i].ParameterName;
            if (p.Length <= nameLen || p[nameLen] != '_')
                continue;
            if (!p.AsSpan(0, nameLen).SequenceEqual(nameSpan))
                continue;
            ps.RemoveAt(i);
        }
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(DbCommand cmd, object value) {
        if (value is not System.Collections.IEnumerable e) return false;
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
        return true;
    }
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
            else if (value is System.Collections.IEnumerable e && e.TryGetNonEnumeratedCount(out nb)) { }
            else if (value is int c)
                nb = c;
            else
                throw new ArgumentException("The value must provide a count");
        }
        if (nb == 0) {
            var lastInd = sb.Length - 1;
            var lastChar = sb[lastInd];
            while (lastChar == ',' || char.IsWhiteSpace(lastChar))
                lastChar = sb[--lastInd];
            sb.Length = lastInd + 1;
            return;
        }
        var nameSpan = ParameterName.AsSpan();
        int nameLen = nameSpan.Length;
        sb.EnsureCapacity(ComputeTotalLength(nb) + nb * (nameLen + 3));
        for (int i = 1; i <= nb; i++) {
            sb.Append(nameSpan);
            sb.Append('_');
            sb.Append(i);
            sb.Append(',');
            sb.Append(' ');
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
