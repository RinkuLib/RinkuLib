using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
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
        var task = b.QueryAsync<DynaObject>(cnn);
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
        var stream = b.StreamQueryAsync<DynaObject>(cnn);
        return Task.FromResult(Results.Ok(stream));
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