using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace RinkuLib.Queries;

/// <summary>
/// Tries to make a metadata reader for a given command, taking a look at its concrete type first. Returns
/// <see langword="false"/> to pass, letting the next maker try.
/// </summary>
public delegate bool ParamInfoGetterMaker(IDbCommand cmd, [MaybeNullWhen(false)] out IDbParamInfoGetter getter);
/// <summary>
/// Reads a provider's real parameter metadata, its exact types and sizes, off a command, so a query binds
/// parameters the way the database expects rather than inferring from the values. Register one for your
/// provider in <see cref="ParamGetterMakers"/>, and the command uses it while learning its cache.
/// </summary>
public interface IDbParamInfoGetter {
    /// <summary>
    /// The registered metadata readers, tried in turn against a command until one claims it. Add your
    /// provider's reader here to pin parameter types across the app.
    /// </summary>
    public static readonly List<ParamInfoGetterMaker> ParamGetterMakers = [];
    /// <summary>Reads the metadata for one named parameter off <paramref name="cmd"/>, through a registered reader when one claims the command, otherwise from the parameter as-is.</summary>
    public static bool TryGetParamInfo(IDbCommand cmd, string paramName, [MaybeNullWhen(false)] out DbParamInfo param) {
        var makers = CollectionsMarshal.AsSpan(ParamGetterMakers);
        for (int i = 0; i < makers.Length; i++) {
            if (!makers[i](cmd, out var getter))
                continue;
            foreach (var item in getter.EnumerateParameters()) {
                if (item.Key == paramName) {
                    param = getter.MakeInfoAt(item.Value);
                    return true;
                }
            }
            param = null;
            return false;
        }
        var parameters = cmd.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++) {
            if (parameters[i] is not IDbDataParameter pa || pa.ParameterName != paramName)
                continue;
            param = DefaultParamCache.MakeInfo(pa);
            return true;
        }
        param = null;
        return false;
    }
    /// <summary>
    /// The metadata for one named parameter, from the provider's schema when it has it.
    /// </summary>
    public bool TryGetInfo(string paramName, [MaybeNullWhen(false)] out DbParamInfo info);
    /// <summary>
    /// The command's parameters, as name and position pairs, for learning them all in one pass.
    /// </summary>
    public IEnumerable<KeyValuePair<string, int>> EnumerateParameters();
    /// <summary>
    /// The binding metadata for the parameter at position <paramref name="i"/>.
    /// </summary>
    public DbParamInfo MakeInfoAt(int i);
}
/// <summary>How a query keeps, per parameter, the learned strategy for binding it.</summary>
public interface IDbParamCache {
    /// <summary>Whether the parameter at <paramref name="ind"/> already has a settled binding strategy.</summary>
    public bool IsCached(int ind);
    /// <summary>Pins the binding strategy for the parameter at <paramref name="ind"/>.</summary>
    public bool UpdateCache(int ind, DbParamInfo info);
    /// <summary>
    /// Offers <paramref name="infoGetter"/> to the special handlers that have not settled yet, so each can
    /// work out its own binding from the provider's metadata.
    /// </summary>
    public bool UpdateSpecialHandlers<T>(T infoGetter) where T : IDbParamInfoGetter;

    /// <summary>
    /// Refreshes the split between settled and unsettled parameters after a round of learning.
    /// </summary>
    public void UpdateCachedIndexes();
}
/// <summary>
/// How one parameter is bound onto a command, add, set, update, remove. Subclass it to take over binding for
/// a parameter, for a provider quirk or a custom type the default path handles wrong.
/// </summary>
public abstract class DbParamInfo(bool IsCached) {
    /// <summary>
    /// Whether this strategy is settled. While <see langword="false"/>, the command may still replace it with
    /// a more exact one learned from the provider.
    /// </summary>
    public bool IsCached = IsCached;
    /// <summary>
    /// Changes an already-bound parameter's value. <paramref name="currentValue"/> is the parameter reference
    /// a previous <see cref="SaveUse"/> handed back.
    /// </summary>
    public abstract bool Update(IDbCommand cmd, ref object? currentValue, object? newValue);
    /// <summary>
    /// Binds a value and hands back the parameter reference in <paramref name="value"/>, so a later
    /// <see cref="Update"/> can change it without a lookup.
    /// </summary>
    public abstract bool SaveUse(string paramName, IDbCommand cmd, ref object value);
    /// <summary> Binds a value for a single run, without keeping a reference for reuse. </summary>
    public abstract bool Use(string paramName, IDbCommand cmd, object value);
    /// <summary> Drops the parameter reference from the command. </summary>
    public abstract void Remove(IDbCommand cmd, object currentValue);
    /// <summary> Drops a parameter from the command by name. </summary>
    public static bool RemoveSingle(string paramName, IDbCommand cmd) {
        var parameters = cmd.Parameters;
        for (int i = parameters.Count - 1; i >= 0; i--) {
            if (parameters[i] is IDataParameter p && p.ParameterName == paramName) {
                parameters.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
    /// <summary> Binds a value to the command for a single execution. </summary>
    public abstract bool Use(string paramName, DbCommand cmd, object value);
    /// <summary> Removes a parameter by name from the command collection. </summary>
    public static bool RemoveSingle(string paramName, DbCommand cmd) {
        var parameters = cmd.Parameters;
        for (int i = parameters.Count - 1; i >= 0; i--) {
            if (parameters[i].ParameterName == paramName) {
                parameters.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}