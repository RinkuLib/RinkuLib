using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
using RinkuLib.DBActions;
using RinkuLib.Queries;

namespace RinkuDemo;

public class Controller<T, TId> where T : IHasID<TId> {
    public (string, int[], DbAction<T>)[] Actions = [];
    public QueryCommand InsertQuery;
    public QueryCommand SelectQuery;
    public QueryCommand UpdateQuery;
    public QueryCommand DeleteQuery;
    public string Name;
    public Controller(IConfiguration config, string key) {
        InsertQuery = new(config[$"SQLStrings:{key}:Insert"] ?? throw new Exception($"{key} : Insert does not exist"));
        SelectQuery = new(GetFlatString(config, $"SQLStrings:{key}:Select"));
        UpdateQuery = new(config[$"SQLStrings:{key}:Update"] ?? throw new Exception($"{key} : Update does not exist"));
        DeleteQuery = new(config[$"SQLStrings:{key}:Delete"] ?? throw new Exception($"{key} : Delete does not exist"));
        var mapper = SelectQuery.Mapper;
        var actionNames = DBActionHelper.GetStrippedActions(mapper);
        if (actionNames.Length > 0) {
            Actions = new (string, int[], DbAction<T>)[actionNames.Length];
            for (int i = 0; i < actionNames.Length; i++)
                Actions[i] = DBActionHelper.MakeAction<T>(mapper, actionNames[i]);
        }
        this.Name = key;
        if (!mapper.ContainsKey("@ID"))
            throw new Exception($"The select should have a @ID variable");
        if (!DeleteQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The delete should have a @ID variable");
        if (!UpdateQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The update should have a @ID variable");
        // When using a builder, when we use, we can check if the bind was successful
    }

    public static string GetFlatString(IConfiguration config, string key) {
        var section = config.GetSection(key);
        var parts = section.Get<string[]>();
        if (parts is not null)
            return string.Concat(parts);
        return section.Value ?? throw new Exception($"{key} does not exist");
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
    public async Task<IResult> GetAll(HttpContext context) {
        using var db = Registry.GetConnection();
        if (context.Request.Query.TryGetValue(Registry.ActionRequestParameterName, out StringValues actionNames))
        if (actionNames.Count <= 0) {
            var b = SelectQuery.StartBuilder();
            Registry.FillQueryBuilder(context, b, context.Request.Query);
            using var cmd = db.CreateCommand();
            return Results.Ok(b.QueryAllAsync<T>(db));
        }
        return Results.Ok(await GetAll(context, db, actionNames).ConfigureAwait(false));
    }
    private async Task<List<T>> GetAll(HttpContext context, DbConnection db, StringValues actionNames) {
        using var cmd = db.CreateCommand();
        var b = SelectQuery.StartBuilder(cmd);
        Registry.FillQueryBuilder(context, b, context.Request.Query);
        var items = await b.QueryAllBufferedAsync<T>().ConfigureAwait(false);
        if (items.Count > 0) {
            b.Use(Registry.UsingActionCondName);
            var access = new ListAccess<T>(items);
            if (db.State != ConnectionState.Open)
                await db.OpenAsync().ConfigureAwait(false);
            QueryCommandBuilderCommand getter = new(b);
            foreach (var an in actionNames) {
                var actionName = an;
                if (actionName is null)
                    continue;
                foreach (var (name, indexes, action) in Actions) {
                    if (!string.Equals(name, actionName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    b.UseFrom(indexes);
                    await action.HandleAsync(access, getter).ConfigureAwait(false);
                    b.UnUseFrom(indexes);
                    break;
                }
            }
        }
        return items;
    }
    public async Task<T?> GetOne(TId id) {
        using var db = Registry.GetConnection();
        var item = await SelectQuery.QueryOneAsync<T>(db, new { ID = id }).ConfigureAwait(false);
        if (Actions.Length > 0 && item is not null) {
            if (db.State != ConnectionState.Open)
                await db.OpenAsync().ConfigureAwait(false);
            using var cmd = db.CreateCommand();
            var b = SelectQuery.StartBuilder(cmd);
            b.Use(Registry.UsingActionCondName);
            if (!b.Use("@ID", id))
                throw new Exception();
            QueryCommandBuilderCommand getter = new(b);
            foreach (var (name, indexes, action) in Actions) {
                b.UseFrom(indexes);
                if (!typeof(T).IsValueType)
                    await action.HandleAsync(item, getter).ConfigureAwait(false);
                else
                    action.Handle(ref item, getter);
                b.UnUseFrom(indexes);
            }
        }
        return item;
    }
    public async Task<bool> Update(TId id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = UpdateQuery.StartBuilder();
        if (!b.Use("@ID", id))
            throw new Exception("Could not bind the ID");
        Registry.FillQueryBuilder(context, b, context.Request.Form);
        return await b.ExecuteAsync(db) > 0;
    }
}
