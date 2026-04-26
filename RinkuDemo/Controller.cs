using RinkuLib.Commands;
using RinkuLib.Queries;

namespace RinkuDemo;

public class Controller<T, TId> where T : IHasID<TId> {
    public QueryCommand InsertQuery;
    public QueryCommand SelectQuery;
    public QueryCommand UpdateQuery;
    public QueryCommand DeleteQuery;
    public string Name;
    public Controller(IConfiguration config, string key) {
        InsertQuery = new(config[$"SQLStrings:{key}:Insert"] ?? throw new Exception($"{key} : Insert does not exist"));
        SelectQuery = new(config.GetFlatString($"SQLStrings:{key}:Select"));
        UpdateQuery = new(config[$"SQLStrings:{key}:Update"] ?? throw new Exception($"{key} : Update does not exist"));
        DeleteQuery = new(config[$"SQLStrings:{key}:Delete"] ?? throw new Exception($"{key} : Delete does not exist"));
        var mapper = SelectQuery.Mapper;
        this.Name = key;
        if (!mapper.ContainsKey("@ID"))
            throw new Exception($"The select should have a @ID variable");
        if (!DeleteQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The delete should have a @ID variable");
        if (!UpdateQuery.Mapper.ContainsKey("@ID"))
            throw new Exception($"The update should have a @ID variable");
        // When using a builder, when we use, we can check if the bind was successful
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
        var b = SelectQuery.StartBuilder();
        Registry.FillQueryBuilder(context, b, context.Request.Query);
        using var cmd = db.CreateCommand();
        return Results.Ok(b.StreamQueryAsync<T>(db));
    }
    public async Task<T?> GetOne(TId id) {
        using var db = Registry.GetConnection();
        var item = await SelectQuery.QueryAsync<T>(db, new { ID = id }).ConfigureAwait(false);
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
