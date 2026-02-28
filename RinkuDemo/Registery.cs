using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuDemo;

public static class Registry {
    public const string AllJoin = "AllJ";
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
        var b = commands.Read.StartBuilder();
        b.Use(AllJoin);
        foreach (var (k, v) in ctx.Request.Query)
            b.Use('@', k, v.ToInferredObject());
        await foreach (var item in b.QueryAllAsync<T>(db))
            yield return item;
    }

    public static void Initialize(IConfiguration config) {
        var info = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));
        info.AddAltName("key", "id");
        info.AddAltName("value", "name");
        info.SetInvalidOnNull("key", true);
        IDbParamInfoGetter.ParamGetterMakers.Add(ForceInferedParamCache.GetInfoGetterMaker<SqliteCommand>);

        ConnStr = config["ConnStr"]
            ?? throw new InvalidOperationException("ConnStr is missing from configuration.");
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
}

public class CrudCommands<T>(IConfiguration config, string key) {
    public QueryCommand Create { get; } = new(config[$"SQLStrings:{key}:Create"]!);
    public QueryCommand Read { get; } = new(config[$"SQLStrings:{key}:Read"]!);
    public QueryCommand Update { get; } = new(config[$"SQLStrings:{key}:Update"]!);
    public QueryCommand Delete { get; } = new(config[$"SQLStrings:{key}:Delete"]!);
}