using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuDemo;

public enum HttpMethod { Get, Post, Put, Delete }
public abstract class DynaAction(string name, HttpMethod method, QueryCommand command) {
    protected readonly QueryCommand Command = command;
    public string Name { get; } = name;
    public HttpMethod Method { get; } = method;

    public abstract Task<IResult> ExecuteAsync(HttpContext context);
    public IEnumerable<KeyValuePair<string, StringValues>> GetParams(HttpContext ctx)
        => Method == HttpMethod.Get || Method == HttpMethod.Delete
            ? ctx.Request.Query : ctx.Request.Form;
}

public sealed class QueryOneAction(string n, HttpMethod m, QueryCommand c) : DynaAction(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var builder = Registry.GetBuilder(context, Command, GetParams(context));
        var task = builder.QueryOneAsync<DynaObject>(cnn);
        return Results.Ok(await task);
    }
}
public sealed class ExecuteAction(string n, HttpMethod m, QueryCommand c) : DynaAction(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var builder = Registry.GetBuilder(context, Command, GetParams(context));
        return Results.Ok(await builder.ExecuteAsync(cnn));
    }
}
public sealed class ExecuteScalarAction(string n, HttpMethod m, QueryCommand c) : DynaAction(n, m, c) {
    public override async Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var builder = Registry.GetBuilder(context, Command, GetParams(context));
        return Results.Ok(await builder.ExecuteScalarAsync<object>(cnn));
    }
}

public sealed class QueryAllAction(string n, HttpMethod m, QueryCommand c) : DynaAction(n, m, c) {
    public override Task<IResult> ExecuteAsync(HttpContext context) {
        using var cnn = Registry.GetConnection();
        var builder = Registry.GetBuilder(context, Command, GetParams(context));
        var stream = builder.QueryAllAsync<DynaObject>(cnn);
        return Task.FromResult(Results.Ok(stream));
    }
}

public static class DynaLoader {
    public static List<DynaAction> LoadActions(this IConfiguration config) {
        var actions = new List<DynaAction>();
        var section = config.GetSection("DynaEndpoints");

        foreach (var entry in section.GetChildren()) {
            var type = entry["Type"]!;
            var name = entry["Name"]!;
            var template = entry["Template"]!;

            var method = entry["Method"]?.ToUpperInvariant() switch {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                _ => HttpMethod.Get
            };

            actions.Add(type switch {
                "QueryOne" => new QueryOneAction(name, method, new(template)),
                "QueryAll" => new QueryAllAction(name, method, new(template)),
                "Execute" => new ExecuteAction(name, method, new(template)),
                "ExecuteScalar" => new ExecuteScalarAction(name, method, new(template)),
                _ => throw new NotSupportedException(type)
            });
        }
        return actions;
    }

    public static void MapDynaApi(this IEndpointRouteBuilder app, List<DynaAction> actions) {
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