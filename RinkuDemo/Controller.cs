using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbRegister;
using RinkuLib.Queries;

namespace RinkuDemo;

public delegate void SetID<T, TId>(ref T item, TId ID);
public delegate TId GetID<T, TId>(ref T item);

public class Controller<T, TId> where T : IHasID<TId> {
    public string[] ActionsOnGetOne;
    public QueryCommand InsertQuery;
    public QueryCommand SelectQuery;
    public QueryCommand UpdateQuery;
    public QueryCommand DeleteQuery;
    public string Name;
    public Controller(IConfiguration config, string key) {
        InsertQuery = new(config[$"SQLStrings:{key}:Insert"] ?? throw new Exception($"{key} : Insert does not exist"));
        SelectQuery = new(config[$"SQLStrings:{key}:Select"] ?? throw new Exception($"{key} : Select does not exist"));
        UpdateQuery = new(config[$"SQLStrings:{key}:Update"] ?? throw new Exception($"{key} : Update does not exist"));
        DeleteQuery = new(config[$"SQLStrings:{key}:Delete"] ?? throw new Exception($"{key} : Delete does not exist"));
        ActionsOnGetOne = config.GetSection($"SQLStrings:{key}:ActionsOnGetOne").Get<string[]>() ?? [];
        this.Name = key;
        if (!SelectQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The select should have a @ID variable");
        if (!DeleteQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The delete should have a @ID variable");
        /* To change things up, we check when binding the ID in the update
        if (!UpdateQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The update should have a @ID variable");*/
    }
    public async Task<T> Create(T item) {
        using var db = Registry.GetConnection();
        var ID = await InsertQuery.ExecuteScalarAsync<TId>(db, item);
        item.ID = ID;
        return item;
    }
    public async Task<bool> Delete(TId id) {
        using var db = Registry.GetConnection();
        db.Open();
        using var tx = db.BeginTransaction();
        try {
            var rows = await DeleteQuery.ExecuteAsync(db, new { ID = id }, tx);
            tx.Commit();
            return rows > 0;
        }
        catch { tx.Rollback(); throw; }
    }
    public IAsyncEnumerable<T> GetAll(HttpContext context) {
        using var db = Registry.GetConnection();
        return GetAll(db, context);
    }
    private IAsyncEnumerable<T> GetAll(DbConnection db, HttpContext context) {
        var b = SelectQuery.StartBuilder();
        Registry.FillQueryBuilder(context, b, context.Request.Query, out var actions);
        if (actions.Length <= 0)
            return b.QueryAllAsync<T>(db);
        return GetAll(db, b, actions);
    }
    private static async IAsyncEnumerable<T> GetAll(DbConnection db, QueryBuilder b, string[] actions) {
        if (typeof(T).IsValueType) {
            foreach (var item in await b.QueryAllBufferedAsync<T>(db).ExecuteDBActionsAsync(db, actions).ConfigureAwait(false))
                yield return item;
            yield break;
        }
        await foreach (var item in b.QueryAllAsync<T>(db)) {
            foreach (var actionName in actions)
                await item.ExecuteDBActionAsync(db, actionName).ConfigureAwait(false);
            yield return item;
        }
    }
    public async Task<T?> GetOne(TId id) {
        using var db = Registry.GetConnection();
        var task = SelectQuery.QueryOneAsync<T>(db, new { ID = id });
        if (ActionsOnGetOne.Length <= 0)
            return await task;
        return await task.ExecuteDBActionsAsync(db, ActionsOnGetOne);
    }
    public async Task<bool> Update(TId id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = UpdateQuery.StartBuilder();
        if (!b.Use("@ID", id))
            throw new Exception("Could not bind the ID");
        Registry.FillQueryBuilder(context, b, context.Request.Form, out var actions);
        return await b.ExecuteAsync(db) > 0;
    }
}