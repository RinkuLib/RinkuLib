using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Queries;

public delegate bool ParamInfoGetterMaker(IDbCommand cmd, out IDbParamInfoGetter getter);
#if !NET8_0_OR_GREATER
public static class DbParamInfoGetter {
    /// <summary>
    /// The global discovery hub for metadata providers.
    /// </summary>
    /// <remarks>
    /// This collection stores negotiation delegates that are evaluated via linear search. 
    /// This design choice prioritizes versatility over strict type-mapping, allowing a 
    /// single maker to match multiple related command types (e.g., legacy vs. modern 
    /// drivers or inheritance) through pattern matching.
    /// </remarks>
    public static readonly List<ParamInfoGetterMaker> ParamGetterMakers = [];
}
#endif
/// <summary>
/// Defines a contract for accessing parameter metadata, used to transition from 
/// inferred types to explicit database-specific definitions.
/// </summary>
/// <remarks>
/// <para><b>Negotiation Pattern:</b></para>
/// <para>The <see cref="ParamGetterMakers"/> allows for provider-specific implementations. 
/// A maker can inspect the <see cref="IDbCommand"/> (e.g., checking if it is a <c>SqlCommand</c>) 
/// to return a getter that accesses actual database metadata instead of relying on the 
/// default inference logic.</para>
/// 
/// <para><b>Optimization:</b></para>
/// <para>When a specific implementation is found, the <c>UpdateCache</c> workflow uses it 
/// to lock in the exact types and sizes required by the database, preventing query 
/// plan fragmentation and reducing driver overhead.</para>
/// </remarks>
public interface IDbParamInfoGetter {
#if NET8_0_OR_GREATER
    /// <summary>
    /// The global discovery hub for metadata providers.
    /// </summary>
    /// <remarks>
    /// This collection stores negotiation delegates that are evaluated via linear search. 
    /// This design choice prioritizes versatility over strict type-mapping, allowing a 
    /// single maker to match multiple related command types (e.g., legacy vs. modern 
    /// drivers or inheritance) through pattern matching.
    /// </remarks>
    public static readonly List<ParamInfoGetterMaker> ParamGetterMakers = [];
#endif
    /// <summary>
    /// Attempts to resolve a <see cref="DbParamInfo"/> for a specific parameter name, 
    /// ideally leveraging provider-specific schema details.
    /// </summary>
    public bool TryGetInfo(string paramName,
#if NET8_0_OR_GREATER
    [MaybeNullWhen(false)]
# endif
    out DbParamInfo info);
    /// <summary>
    /// Enumerates all parameters in the source command to facilitate bulk cache updates.
    /// </summary>
    public IEnumerable<KeyValuePair<string, int>> EnumerateParameters();
    /// <summary>
    /// Creates a metadata resolution object for the parameter at the specified index.
    /// </summary>
    public DbParamInfo MakeInfoAt(int i);
}
public interface IDbParamCache {
    /// <summary> 
    /// Returns true if a stable metadata strategy (inferred or explicit) 
    /// has been established for this parameter index. 
    /// </summary>
    public bool IsCached(int ind);
    /// <summary> 
    /// Assigns a finalized metadata strategy to a specific variable index.
    /// </summary>
    public bool UpdateCache(int ind, DbParamInfo info);
    /// <summary> 
    /// Provides the <paramref name="infoGetter"/> to all non-cached special handlers, 
    /// allowing them to internally resolve their own <see cref="DbParamInfo"/> 
    /// (using either provider-specific or inferred metadata).
    /// </summary>
    public bool UpdateSpecialHandlers<T>(T infoGetter) where T : IDbParamInfoGetter;

    /// <summary>
    /// Synchronizes the internal tracking of cached vs. non-cached parameters.
    /// </summary>
    public void UpdateNbCached();
}
public abstract class DbParamInfo(bool IsCached) {
    /// <summary> 
    /// Indicates if this instance represents a finalized, reusable metadata strategy.
    /// When false, the system may still attempt to "upgrade" this info to a more 
    /// specific implementation (e.g., via provider-specific metadata).
    /// </summary>
    public bool IsCached = IsCached;
    /// <summary> 
    /// Efficiently updates the value of an existing parameter. 
    /// Expects <paramref name="currentValue"/> to be the parameter reference 
    /// captured by a previous <see cref="SaveUse"/>.
    /// </summary>
    public abstract bool Update(IDbCommand cmd, ref object currentValue, object newValue);
    /// <summary> 
    /// Binds the value to the command and replaces <paramref name="value"/> with 
    /// the parameter reference to enable high-speed updates in the future. 
    /// </summary>
    public abstract bool SaveUse(string paramName, IDbCommand cmd, ref object value);
    /// <summary> Binds a value to the command for a single execution. </summary>
    public abstract bool Use(string paramName, IDbCommand cmd, object value);
    /// <summary> Removes the specific parameter reference from the command. </summary>
    public abstract void Remove(IDbCommand cmd, object currentValue);
    /// <summary> Removes a parameter by name from the command collection. </summary>
    public abstract bool Remove(string paramName, IDbCommand cmd);
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
    public abstract bool Use(string paramName, DbCommand cmd, object value);
    public abstract bool Remove(string paramName, DbCommand cmd);
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