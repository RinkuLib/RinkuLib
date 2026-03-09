using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.DbRegister;
using RinkuLib.Queries;

namespace RinkuDemo;

public enum Role { User = 0, Employee = 1, Admin = 2 }
public static class Registry {
    public const string GetAll = "GetAll";
    public static string ConnStr { get; private set; } = null!;
    public static DbConnection GetConnection() => new SqliteConnection(ConnStr);
    public static CrudCommands<Artist> Artists { get; private set; } = null!;
    public static CrudCommands<Album> Albums { get; private set; } = null!;
    public static CrudCommands<Track> Tracks { get; private set; } = null!;
    public static CrudCommands<KeyValuePair<int, string>> Genres { get; private set; } = null!;
    public static CrudCommands<Reference> MediaTypes { get; private set; } = null!;
    public static CrudCommands<Employee> Employees { get; private set; } = null!;
    public static CrudCommands<Customer> Customers { get; private set; } = null!;
    public static CrudCommands<Invoice> Invoices { get; private set; } = null!;
    public static CrudCommands<InvoiceLine> InvoiceLines { get; private set; } = null!;
    public static async IAsyncEnumerable<T> Stream<T>(HttpContext ctx, CrudCommands<T> commands) {
        using var db = GetConnection();
        var b = GetBuilder(ctx, commands.Read, ctx.Request.Query, out var actions);
        b.Use(GetAll);
        if (actions.Length <= 0) {
            await foreach (var item in b.QueryAllAsync<T>(db))
                yield return item;
            yield break;
        }
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

    public static void Initialize(IConfiguration config) {
        var info = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));
        info.AddAltName("key", "id");
        info.AddAltName("value", "name");
        info.SetInvalidOnNull("key", true);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<SqliteCommand>);

        var fileName = config["DbFile"] ?? throw new InvalidOperationException("DbFile is missing.");
        ConnStr = $"Data Source={Path.Combine(AppContext.BaseDirectory, fileName)}";
        Artists = new(config, "Artist");
        Albums = new(config, "Album");
        Tracks = new(config, "Track");
        Genres = new(config, "Genre");
        MediaTypes = new(config, "MediaType");
        Employees = new(config, "Employee");
        Customers = new(config, "Customer");
        Invoices = new(config, "Invoice");
        InvoiceLines = new(config, "InvoiceLine");
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
        b.Use(GetAll);
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
    public static QueryBuilder GetBuilder(HttpContext ctx, QueryCommand command, IEnumerable<KeyValuePair<string, StringValues>> parameters, out string[] actions) {
        var b = command.StartBuilder();
        b.Use(GetAll);
        UseRole(GetRole(ctx), b);
        actions = [];
        foreach (var (k, v) in parameters) {
            if (string.IsNullOrEmpty(k) || k[0] == '#')
                continue;
            if (k.Equals("Uses", StringComparison.InvariantCultureIgnoreCase)) {
                foreach (var useValue in v)
                    if (!string.IsNullOrEmpty(useValue) && useValue[0] != '#')
                        b.Use(useValue);
            }
            if (k.Equals("Actions", StringComparison.InvariantCultureIgnoreCase)) {
                if (v.Count > 0)
                    actions = v.ToArray()!;
            }
            else
                b.Use('@', k, v.ToInferredObject());
        }
        return b;
    }
    public static ValueTask ExecuteDBActionAsync<T>(this T instance, DbConnection cnn, string actionName, DbTransaction? transaction = null, int? timeout = null, CancellationToken ct = default) {
        if (!DbActions<T>.TryGetAction(0, actionName, out var action, out var startNext))
            return default;
        if (startNext == 0)
            return action.ExecuteOnOneAsync(instance, cnn, transaction, timeout, ct);
        return action.FowardExecuteOnOneAsync(startNext, actionName, instance, cnn, transaction, timeout, ct);
    }
}

public class CrudCommands<T>(IConfiguration config, string key) {
    public QueryCommand Create { get; } = new(config[$"SQLStrings:{key}:Create"] ?? throw new Exception($"{key} : Create does not exist"));
    public QueryCommand Read { get; } = new(config[$"SQLStrings:{key}:Read"] ?? throw new Exception($"{key} : Read does not exist"));
    public QueryCommand Update { get; } = new(config[$"SQLStrings:{key}:Update"] ?? throw new Exception($"{key} : Update does not exist"));
    public QueryCommand Delete { get; } = new(config[$"SQLStrings:{key}:Delete"] ?? throw new Exception($"{key} : Delete does not exist"));
}