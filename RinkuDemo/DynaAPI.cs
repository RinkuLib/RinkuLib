using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
using RinkuLib.DBActions;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuDemo;

public enum HttpMethod { Get, Post, Put, Delete }
public abstract class DynaEndpoint(string name, HttpMethod method, QueryCommand command) {
    protected readonly QueryCommand Command = command;
    public string Name { get; } = name;
    public HttpMethod Method { get; } = method;

    public abstract Task<IResult> ExecuteAsync(HttpContext context);
    public IEnumerable<KeyValuePair<string, StringValues>> GetParams(HttpContext ctx)
        => Method == HttpMethod.Get || Method == HttpMethod.Delete
            ? ctx.Request.Query : ctx.Request.Form;
}

public sealed class QueryOneEndpoint(string n, HttpMethod m, QueryCommand c) : DynaEndpoint(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var b = Command.StartBuilder();
        Registry.FillQueryBuilder(context, b, GetParams(context));
        var task = b.QueryOneAsync<DynaObject>(cnn);
        return Results.Ok(await task);
    }
}
public sealed class ExecuteEndpoint(string n, HttpMethod m, QueryCommand c) : DynaEndpoint(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var b = Command.StartBuilder();
        Registry.FillQueryBuilder(context, b, GetParams(context));
        return Results.Ok(await b.ExecuteAsync(cnn));
    }
}
public sealed class ExecuteScalarEndpoint(string n, HttpMethod m, QueryCommand c) : DynaEndpoint(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var b = Command.StartBuilder();
        Registry.FillQueryBuilder(context, b, GetParams(context));
        return Results.Ok(await b.ExecuteScalarAsync<object>(cnn));
    }
}

public sealed class QueryAllEndpoint(string n, HttpMethod m, QueryCommand c) : DynaEndpoint(n, m, c) {
    public override Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var b = Command.StartBuilder();
        Registry.FillQueryBuilder(context, b, GetParams(context));
        var stream = b.QueryAllAsync<DynaObject>(cnn);
        return Task.FromResult(Results.Ok(stream));
    }
}

public sealed class QueryAllWithDBActionsEndpoint : DynaEndpoint {
    public DynaAction[] Actions = [];
    public QueryAllWithDBActionsEndpoint(string n, HttpMethod m, QueryCommand c) : base(n, m, c) {
        var mapper = c.Mapper;
        var actionNames = DBActionHelper.GetStrippedActions(mapper);
        if (actionNames.Length > 0) {
            Actions = new DynaAction[actionNames.Length];
            for (int i = 0; i < actionNames.Length; i++) {
                var actionName = actionNames[i];
                Actions[i] = new DynaAction(actionName, DBActionHelper.GetCondIndexes(actionName, mapper));
            }
        }
    }
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var db = Registry.GetConnection();
        if (!context.Request.Query.TryGetValue(Registry.ActionRequestParameterName, out StringValues actionNames)
            || actionNames.Count <= 0) {
            var b = Command.StartBuilder();
            Registry.FillQueryBuilder(context, b, context.Request.Query);
            using var cmd = db.CreateCommand();
            return Results.Ok(b.QueryAllAsync<DynaObject>(db));
        }
        return Results.Ok(await GetAll(context, db, actionNames).ConfigureAwait(false));
    }
    private async Task<DynaListWrapper> GetAll(HttpContext context, DbConnection db, StringValues actionNames) {
        using var cmd = db.CreateCommand();
        var b = Command.StartBuilder(cmd);
        Registry.FillQueryBuilder(context, b, context.Request.Query);
        var items = await b.QueryAllBufferedAsync<DynaObject>().ConfigureAwait(false);
        DynaListWrapper res = new(items, []);
        if (items.Count > 0) {
            b.Use(Registry.UsingActionCondName);
            if (db.State != ConnectionState.Open)
                await db.OpenAsync().ConfigureAwait(false);
            QueryCommandBuilderCommand getter = new(b);
            foreach (var an in actionNames) {
                var actionName = an;
                if (actionName is null)
                    continue;
                foreach (var action in Actions) {
                    if (!action.Matches(actionName))
                        continue;
                    await action.DynaExecute(res, b).ConfigureAwait(false);
                }
            }
        }
        return res;
    }
}

public static class DynaLoader {
    public static List<DynaEndpoint> LoadActions(this IConfiguration config) {
        var actions = new List<DynaEndpoint>();
        var section = config.GetSection("DynaEndpoints");

        foreach (var entry in section.GetChildren()) {
            var type = entry["Type"]!;
            var name = entry["Name"]!;
            var template = entry.GetFlatString("Template");

            var method = entry["Method"]?.ToUpperInvariant() switch {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                _ => HttpMethod.Get
            };

            actions.Add(type switch {
                "QueryOne" => new QueryOneEndpoint(name, method, new(template)),
                "QueryAll" => new QueryAllEndpoint(name, method, new(template)),
                "QueryAllWithActions" => new QueryAllWithDBActionsEndpoint(name, method, new(template)),
                "Execute" => new ExecuteEndpoint(name, method, new(template)),
                "ExecuteScalar" => new ExecuteScalarEndpoint(name, method, new(template)),
                _ => throw new NotSupportedException(type)
            });
        }
        return actions;
    }

    public static void MapDynaApi(this IEndpointRouteBuilder app, List<DynaEndpoint> actions) {
        foreach (var action in actions) {
            _ = action.Method switch {
                HttpMethod.Get => app.MapGet($"/api/{action.Name}", (Delegate)action.ExecuteAsync),
                HttpMethod.Post => app.MapPost($"/api/{action.Name}", (Delegate)action.ExecuteAsync),
                HttpMethod.Put => app.MapPut($"/api/{action.Name}", (Delegate)action.ExecuteAsync),
                HttpMethod.Delete => app.MapDelete($"/api/{action.Name}", (Delegate)action.ExecuteAsync),
                _ => throw new NotImplementedException()
            };
        }
    }
}
public class DynaAction(string Name, int[] IndexesToUse) {
    public string Name = Name;
    public int[] IndexesToUse = IndexesToUse;
    public IDynaRelation? Relation = null!;
    public bool Matches(string name) => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase);
    public async ValueTask DynaExecute(DynaListWrapper parents, QueryBuilderCommand<DbCommand> builder, CancellationToken ct = default) {
        builder.Use(Registry.UsingActionCondName);
        builder.UseFrom(IndexesToUse);
        if (Relation is null) {
            builder.Command.CommandText = builder.QueryCommand.QueryText.Parse(builder.Variables);
            var reader = await builder.Command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct).ConfigureAwait(false);
            Relation = InitRelation(reader);
            await Relation.InitHandleAsync(parents, reader, builder.QueryCommand, builder.Variables, builder.Command, ct).ConfigureAwait(false);
            return;
        }
        await Relation.HandleAsync(parents, new QueryCommandBuilderCommand(builder), ct).ConfigureAwait(false);
        builder.UnUseFrom(IndexesToUse);
    }
    private IDynaRelation InitRelation(DbDataReader reader) {
        ReadOnlySpan<char> nameSpan = Name.AsSpan();
        int dotIndex = nameSpan.IndexOf('.');
        if (dotIndex == -1) {
            Type dynaType = typeof(DynaRelation<>).MakeGenericType(reader.GetFieldType(0));
            return (IDynaRelation)Activator.CreateInstance(dynaType, Name, reader.GetName(0))!;
        }
        if (nameSpan[(dotIndex + 1)..].Contains('.'))
            throw new Exception("Only supports 2 level");

        string parentProp = nameSpan[..dotIndex].ToString();
        string transientProp = nameSpan[(dotIndex + 1)..].ToString();
        Type twoLevelType = typeof(DynaRelationTwoLevel<,>).MakeGenericType(reader.GetFieldType(0), reader.GetFieldType(1));

        return (IDynaRelation)Activator.CreateInstance(twoLevelType, parentProp, reader.GetName(0), transientProp, reader.GetName(1))!;
    }
}