using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using RinkuLib.DbParsing;
using RinkuLib.DbRegister;
using RinkuLib.Queries;

namespace RinkuDemo;

public enum Role { User = 0, Employee = 1, Admin = 2 }
public interface IHasID<TId> {
    public TId ID { get; set; }
}
public static class Registry {
    public static string ConnStr { get; private set; } = null!;
    public static DbConnection GetConnection() => new SqliteConnection(ConnStr);
    public static Controller<T, int> MapController<T>(this IEndpointRouteBuilder app, IConfiguration config, string key) where T : IHasID<int> {
        var controller = new Controller<T, int>(config, key);
        app.MapController(controller);
        return controller;
    }
    public static void MapController<T>(this IEndpointRouteBuilder app, Controller<T, int> controller) where T : IHasID<int> {
        TypeParsingInfo.GetOrAdd<T>();
        var g = app.MapGroup($"/{controller.Name.ToLower()}");
        g.MapGet("/", (HttpContext context) => controller.GetAll(context));
        g.MapGet("/{id:int}", async (int id) => {
            var result = await controller.GetOne(id);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });
        g.MapPost("/", async (T item) => {
            var res = await controller.Create(item);
            return Results.Created($"/{controller.Name.ToLower()}/{res.ID}", res);
        });
        g.MapPost("/{id:int}", async (int id, HttpContext context) => {
            var success = await controller.Update(id, context);
            if (!success)
                return Results.NotFound();
            var result = await controller.GetOne(id);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });
        g.MapDelete("/{id:int}", async (int id) => {
            var success = await controller.Delete(id);
            return success ? Results.NoContent() : Results.NotFound();
        });
    }

    public static void Initialize(IConfiguration config) {
        var info = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));
        info.AddAltName("key", "id");
        info.AddAltName("value", "name");
        info.SetInvalidOnNull("key", true);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<SqliteCommand>);
        // to change things a bit, the relation is added manualy
        DbActions.AddOrUpdateToManyRelation<Employee>("ManagingEmployees", "ID", "SELECT EmployeeId AS ID, FirstName, LastName, Title, BirthDate, HireDate, Address, City, State, Country, PostalCode, Phone, Fax, Email FROM employees WHERE ReportsTo = @ID");

        var fileName = config["DbFile"] ?? throw new InvalidOperationException("DbFile is missing.");
        ConnStr = $"Data Source={Path.Combine(AppContext.BaseDirectory, fileName)}";
        SQLitePCL.Batteries.Init();
    }
    public static object? ToInferredObject(this StringValues sv) {
        int count = sv.Count;
        if (count == 0)
            return null;
        if (count > 1)
            return sv.ToArray();
        string val = sv[0]!;
        ReadOnlySpan<char> span = val.AsSpan();
        if (bool.TryParse(val, out bool b))
            return b;
        if (long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) {
            if (l is >= int.MinValue and <= int.MaxValue)
                return (int)l;
            return l;
        }
        if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d;
        return val;
    }
    public static Role GetRole(HttpContext context) {
        // Use safer auth in real applications
        string? header = context.Request.Headers["X-Role"];
        if (header?.Length != 1)
            return Role.User;
        int val = header[0] - '0';
        return val is >= 0 and <= 2 ? (Role)val : Role.User;
    }
    public static void UseRole<T>(Role role, T builder) where T : IQueryBuilder {
        if (role >= Role.Employee) {
            builder.Use("#Emp");
            if (role >= Role.Admin)
                builder.Use("#Adm");
        }
    }
    public static QueryBuilder GetBuilder(HttpContext ctx, QueryCommand command, IEnumerable<KeyValuePair<string, StringValues>> parameters) {
        var b = command.StartBuilder();
        UseRole(GetRole(ctx), b);
        foreach (var (k, v) in parameters) {
            if (string.IsNullOrEmpty(k) || k[0] == '#')
                continue;
            if (k.Equals("Uses", StringComparison.InvariantCultureIgnoreCase)) {
                foreach (var useValue in v)
                    if (!string.IsNullOrEmpty(useValue) && useValue[0] != '#')
                        b.Use(useValue);
            }
            else
                b.Use('@', k, v.ToInferredObject());
        }
        return b;
    }
    public static void FillQueryBuilder(HttpContext ctx, QueryBuilder builder, IEnumerable<KeyValuePair<string, StringValues>> parameters, out string[] actions) {
        UseRole(GetRole(ctx), builder);
        actions = [];
        foreach (var (k, v) in parameters) {
            if (string.IsNullOrEmpty(k) || k[0] == '#')
                continue;
            if (k.Equals("Uses", StringComparison.InvariantCultureIgnoreCase)) {
                foreach (var useValue in v)
                    if (!string.IsNullOrEmpty(useValue) && useValue[0] != '#')
                        builder.Use(useValue);
            }
            if (k.Equals("Actions", StringComparison.InvariantCultureIgnoreCase)) {
                if (v.Count > 0)
                    actions = v.ToArray()!;
            }
            else
                builder.Use('@', k, v.ToInferredObject());
        }
    }
    public static void ExecuteDBAction<T>(ref T instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return;
        if (startNext == 0) {
            action.ExecuteOnOne(ref instance, cnn, transaction, timeout);
            return;
        }
        action.FowardExecuteOnOne(startNext, actionName, ref instance, cnn, transaction, timeout);
    }
}