using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DBActions;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuDemo;
public interface IDynaRelation {
    public ValueTask HandleAsync<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter, object[] itemsForAction, CancellationToken ct = default)
        where TAccess : notnull, ICollectionRefAccessor<DynaObject>
        where TParserGetter : IParserGetter;
    public Task<object> HandleAsync<TParserGetter>(DynaObject parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter;
    public ValueTask InitHandleAsync<TAccess>(TAccess parents, object[] itemsForAction, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) where TAccess : notnull, ICollectionRefAccessor<DynaObject>;
    public Task<object> InitHandleAsync(DynaObject parent, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default);
}
/// <summary></summary>
public class DynaRelation<TID>(string CompareColumn) : IDynaRelation {
    public readonly string CompareColumn = CompareColumn;
    public async ValueTask InitHandleAsync<TAccess>(TAccess parents, object[] itemsForAction, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) where TAccess : notnull, ICollectionRefAccessor<DynaObject> {
        if (query.TryGetCache<DBPair<TID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else 
            parser = QueryCommandUsingObjectParam.UpdateCache<DBPair<TID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        await HandleAsync(parents, itemsForAction, reader, parser, ct).ConfigureAwait(false);
    }
    public async Task<object> InitHandleAsync(DynaObject parent, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) {
        if (query.TryGetCache<DBPair<TID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else
            parser = QueryCommandUsingObjectParam.UpdateCache<DBPair<TID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        return await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public ValueTask HandleAsync<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter, object[] itemsForAction, CancellationToken ct = default) where TParserGetter : IParserGetter where TAccess : notnull, ICollectionRefAccessor<DynaObject> {
        var reader = parserGetter.GetParserAndReader<DBPair<TID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        return HandleAsync(parents, itemsForAction, reader, parser, ct);
    }
    public async ValueTask HandleAsync<TAccess>(TAccess parents, object[] itemsForAction, DbDataReader reader, SchemaParser<DBPair<TID, DynaObject>> parser, CancellationToken ct) where TAccess : notnull, ICollectionRefAccessor<DynaObject> {
        using (reader) {
            var comparer = EqualityComparer<TID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;
            var count = parents.Length;
            for (int i = 0; i < count; i++) {
                var parentId = parents.GetAt(i).Get<TID>(CompareColumn);
                while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                    sharedArray.Add(currentPair.Object);
                    hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (hasData) {
                        currentPair = parser.Parse(reader);
                    }
                }
                itemsForAction[i] = sharedArray.ToArray();
                sharedArray.Clear();
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
        }
    }

    /// <inheritdoc/>
    public async Task<object> HandleAsync<TParserGetter>(DynaObject parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter {
        var reader = parserGetter.GetParserAndReader<DBPair<TID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        return await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }

    public static async Task<object> HandleAsync(DynaObject parent, DbDataReader reader, SchemaParser<DBPair<TID, DynaObject>> parser, CancellationToken ct) {
        using (reader) {
            var comparer = EqualityComparer<TID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;

            var parentId = parent.Get<TID>(reader.GetName(0));
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);
                hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
            return sharedArray.ToArray();
        }
    }
}
