using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.DbRegister;

/// <summary>
/// The default implementation without a cache nor an allready set function
/// </summary>
public struct CacheOneItem<T>(IWithParserAndParam<T> p) : ISchemaParser<T> {
    private readonly IWithParserAndParam<T> p = p;
    private Func<DbDataReader, T> parser = null!;
    /// <inheritdoc/>
    public readonly CommandBehavior Behavior => default;
    /// <inheritdoc/>
    public readonly bool IsInit => false;
    /// <inheritdoc/>
    public void Init(DbDataReader reader, IDbCommand cmd) {
        var schema = reader.GetColumnsFast();
        parser = p.Parser = TypeParser<T>.GetParserFunc(ref schema, out var b);
        p.Behavior = b;
        var makers = CollectionsMarshal.AsSpan(IDbParamInfoGetter.ParamGetterMakers);
        for (int i = 0; i < makers.Length; i++) {
            if (!makers[i](cmd, out var getter))
                continue;
            foreach (var item in getter.EnumerateParameters()) {
                if (item.Key == ActionNameCache.BindParamName)
                    continue;
                p.ParamInfo = getter.MakeInfoAt(item.Value);
            }
            return;
        }
        var parameters = cmd.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++) {
            if (parameters[i] is not IDbDataParameter pa || pa.ParameterName == ActionNameCache.BindParamName)
                continue;
            p.ParamInfo = DefaultParamCache.MakeInfo(pa);
            break;
        }
    }
    /// <inheritdoc/>
    public readonly T Parse(DbDataReader reader) => parser(reader);
}

/// <summary>
/// Basic interface to indicate a maping with one parser
/// </summary>
public interface IWithParser<T> {
    /// <inheritdoc/>
    public Func<DbDataReader, T>? Parser { set; }
    /// <inheritdoc/>
    public CommandBehavior Behavior { set; }
}
/// <summary>
/// Basic interface to indicate a maping with one param
/// </summary>
public interface IWithParam {
    /// <inheritdoc/>
    public DbParamInfo ParamInfo { set; }
}
/// <summary>
/// Basic interface to indicate a maping with one parser and one param
/// </summary>
public interface IWithParserAndParam<T> : IWithParser<T>, IWithParam;