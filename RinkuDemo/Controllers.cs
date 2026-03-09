using RinkuLib.Commands;
using RinkuLib.DbRegister;
using RinkuLib.Queries;

namespace RinkuDemo;

public class ArtistModule : IApiModule<Artist> {
    public static string Name => "artist";
    public static async Task<Artist> Create(Artist a) {
        using var db = Registry.GetConnection();
        return a with {
            ID = await Registry.Artists.Create.ExecuteScalarAsync<int>(db, a)
        };
    }
    public static int GetID(Artist a) => a.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        db.Open();
        using var tx = db.BeginTransaction();
        try {
            var rows = await Registry.Artists.Delete.ExecuteAsync(db, new { ID = id });
            tx.Commit();
            return rows > 0;
        }
        catch {
            tx.Rollback();
            throw;
        }
    }
    public static IAsyncEnumerable<Artist> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Artists);
    public static async Task<Artist?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Artists.Read.QueryOneAsync<Artist>(db, new { ID = id })
            .ExecuteDBActionsAsync(db, ["Albums", "Albums.Tracks"]);
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Artists.Update.StartBuilder();
        b.Use("@ID", id);

        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);

        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Artists.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Artist select should have a @ID variable");
        if (!Registry.Albums.Read.Mapper.ContainsKey("@ArtistID"))
            throw new Exception("Albums select should have a @ArtistID variable");
        if (!Registry.Tracks.Read.Mapper.ContainsKey("@AlbumID"))
            throw new Exception("Tracks select should have a @AlbumID variable");
        if (!Registry.Artists.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Artist delete should have a @ID variable");
        if (!Registry.Artists.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Artist update should have a @ID variable");
    }
}
public class AlbumModule : IApiModule<Album> {
    public static string Name => "album";
    public static async Task<Album> Create(Album a) {
        using var db = Registry.GetConnection();
        return a with {
            ID = await Registry.Albums.Create.ExecuteScalarAsync<int>(db, a)
        };
    }
    public static int GetID(Album a) => a.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        db.Open();
        using var tx = db.BeginTransaction();
        try {
            var rows = await Registry.Albums.Delete.ExecuteAsync(db, new { ID = id });
            tx.Commit();
            return rows > 0;
        }
        catch { tx.Rollback(); throw; }
    }
    public static IAsyncEnumerable<Album> GetAll(HttpContext context) 
        => Registry.Stream(context, Registry.Albums);
    public static async Task<Album?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Albums.Read.QueryOneAsync<Album>(db, new { ID = id })
            .ExecuteDBActionsAsync(db, ["Tracks"]);
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Albums.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Albums.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Albums select should have a @ID variable");
        if (!Registry.Tracks.Read.Mapper.ContainsKey("@AlbumID"))
            throw new Exception("Tracks select should have a @AlbumID variable");
        if (!Registry.Albums.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Albums delete should have a @ID variable");
        if (!Registry.Albums.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Albums update should have a @ID variable");
    }
}
public class TrackModule : IApiModule<Track> {
    public static string Name => "track";
    public static async Task<Track> Create(Track t) {
        using var db = Registry.GetConnection();
        return t with {
            ID = await Registry.Tracks.Create.ExecuteScalarAsync<int>(db, t)
        };
    }
    public static int GetID(Track t) => t.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        var rows = await Registry.Tracks.Delete.ExecuteAsync(db, new { ID = id });
        return rows > 0;
    }
    public static IAsyncEnumerable<Track> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Tracks);
    public static async Task<Track?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Tracks.Read.QueryOneAsync<Track>(db, new { ID = id });
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Tracks.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Tracks.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Tracks select should have a @ID variable");
        if (!Registry.Tracks.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Tracks delete should have a @ID variable");
        if (!Registry.Tracks.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Tracks update should have a @ID variable");
    }
}
public class MediaTypeModule : IApiModule<Reference> {
    public static string Name => "mediatype";
    public static async Task<Reference> Create(Reference mt) {
        using var db = Registry.GetConnection();
        return mt with {
            ID = await Registry.MediaTypes.Create.ExecuteScalarAsync<int>(db, mt)
        };
    }
    public static int GetID(Reference mt) => mt.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        var rows = await Registry.MediaTypes.Delete.ExecuteAsync(db, new { ID = id });
        return rows > 0;
    }
    public static IAsyncEnumerable<Reference> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.MediaTypes);
    public static async Task<Reference> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.MediaTypes.Read.QueryOneAsync<Reference>(db, new { ID = id });
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.MediaTypes.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.MediaTypes.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("MediaTypes select should have a @ID variable");
        if (!Registry.MediaTypes.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("MediaTypes delete should have a @ID variable");
        if (!Registry.MediaTypes.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("MediaTypes update should have a @ID variable");
    }
}
public class GenreModule : IApiModule<KeyValuePair<int, string>> {
    public static string Name => "genre";
    public static async Task<KeyValuePair<int, string>> Create(KeyValuePair<int, string> g) {
        using var db = Registry.GetConnection();
        return new(await Registry.Genres.Create.ExecuteScalarAsync<int>(db, g), g.Value);
    }
    public static int GetID(KeyValuePair<int, string> g) => g.Key;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        var rows = await Registry.Genres.Delete.ExecuteAsync(db, new { ID = id });
        return rows > 0;
    }
    public static IAsyncEnumerable<KeyValuePair<int, string>> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Genres);
    public static async Task<KeyValuePair<int, string>> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Genres.Read.QueryOneAsync<KeyValuePair<int, string>>(db, new { ID = id });
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Genres.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Genres.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Genres select should have a @ID variable");
        if (!Registry.Genres.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Genres delete should have a @ID variable");
        if (!Registry.Genres.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Genres update should have a @ID variable");
    }
}
public class EmployeeModule : IApiModule<Employee> {
    public static string Name => "employee";
    public static async Task<Employee> Create(Employee e) {
        using var db = Registry.GetConnection();
        return e with {
            ID = await Registry.Employees.Create.ExecuteScalarAsync<int>(db, e)
        };
    }
    public static int GetID(Employee e) => e.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        var rows = await Registry.Employees.Delete.ExecuteAsync(db, new { ID = id });
        return rows > 0;
    }
    public static IAsyncEnumerable<Employee> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Employees);
    public static async Task<Employee?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Employees.Read.QueryOneAsync<Employee>(db, new { ID = id })
            .ExecuteDBActionsAsync(db, ["ManagingEmployees"]);
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Employees.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Employees.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Employees select should have a @ID variable");
        if (!Registry.Employees.Read.Mapper.ContainsKey("@ReportsTo"))
            throw new Exception("Employees select should have a @ReportsTo variable");
        if (!Registry.Employees.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Employees delete should have a @ID variable");
        if (!Registry.Employees.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Employees update should have a @ID variable");
    }
}
public class CustomerModule : IApiModule<Customer> {
    public static string Name => "customer";
    public static async Task<Customer> Create(Customer c) {
        using var db = Registry.GetConnection();
        return c with {
            ID = await Registry.Customers.Create.ExecuteScalarAsync<int>(db, c)
        };
    }
    public static int GetID(Customer c) => c.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        db.Open();
        using var tx = db.BeginTransaction();
        try {
            await Registry.InvoiceLines.Delete.ExecuteAsync(db, new { CustomerId = id });
            await Registry.Invoices.Delete.ExecuteAsync(db, new { CustomerId = id });
            var rows = await Registry.Customers.Delete.ExecuteAsync(db, new { ID = id });
            tx.Commit();
            return rows > 0;
        }
        catch {
            tx.Rollback();
            throw;
        }
    }
    public static IAsyncEnumerable<Customer> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Customers);
    public static async Task<Customer?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Customers.Read.QueryOneAsync<Customer>(db, new { ID = id })
            .ExecuteDBActionsAsync(db, ["Invoices", "Invoices.Lines"]);
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Customers.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Customers.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Customers select should have a @ID variable");
        if (!Registry.Invoices.Read.Mapper.ContainsKey("@CustomerID"))
            throw new Exception("Invoices select should have a @CustomerID variable");
        if (!Registry.InvoiceLines.Read.Mapper.ContainsKey("@InvoiceID"))
            throw new Exception("InvoiceLines select should have a @InvoiceID variable");
        if (!Registry.Customers.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Customers delete should have a @ID variable");
        if (!Registry.Customers.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Customers update should have a @ID variable");
    }
}
public class InvoiceModule : IApiModule<Invoice> {
    public static string Name => "invoice";
    public static async Task<Invoice> Create(Invoice i) {
        using var db = Registry.GetConnection();
        return i with {
            ID = await Registry.Invoices.Create.ExecuteScalarAsync<int>(db, i)
        };
    }
    public static int GetID(Invoice i) => i.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        db.Open();
        using var tx = db.BeginTransaction();
        try {
            await Registry.InvoiceLines.Delete.ExecuteAsync(db, new { InvoiceId = id });
            var rows = await Registry.Invoices.Delete.ExecuteAsync(db, new { ID = id });
            tx.Commit();
            return rows > 0;
        }
        catch {
            tx.Rollback();
            throw;
        }
    }
    public static IAsyncEnumerable<Invoice> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Invoices);
    public static async Task<Invoice?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.Invoices.Read.QueryOneAsync<Invoice>(db, new { ID = id })
            .ExecuteDBActionsAsync(db, ["Lines"]);
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.Invoices.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.Invoices.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("Invoices select should have a @ID variable");
        if (!Registry.InvoiceLines.Read.Mapper.ContainsKey("@InvoiceID"))
            throw new Exception("InvoiceLines select should have a @InvoiceID variable");
        if (!Registry.Invoices.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("Invoices delete should have a @ID variable");
        if (!Registry.Invoices.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("Invoices update should have a @ID variable");
    }
}
public class InvoiceLineModule : IApiModule<InvoiceLine> {
    public static string Name => "invoiceline";
    public static async Task<InvoiceLine> Create(InvoiceLine il) {
        using var db = Registry.GetConnection();
        return il with {
            ID = await Registry.InvoiceLines.Create.ExecuteScalarAsync<int>(db, il)
        };
    }
    public static int GetID(InvoiceLine il) => il.ID;
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        return await Registry.InvoiceLines.Delete.ExecuteAsync(db, new { ID = id }) > 0;
    }
    public static IAsyncEnumerable<InvoiceLine> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.InvoiceLines);
    public static async Task<InvoiceLine?> GetOne(int id) {
        using var db = Registry.GetConnection();
        return await Registry.InvoiceLines.Read.QueryOneAsync<InvoiceLine>(db, new { ID = id });
    }
    public static async Task<bool> Update(int id, HttpContext context) {
        using var db = Registry.GetConnection();
        var b = Registry.InvoiceLines.Update.StartBuilder();
        b.Use("@ID", id);
        foreach (var (key, value) in context.Request.Form)
            b.Use('@', key, value);
        return await b.ExecuteAsync(db) > 0;
    }
    public static void Validate() {
        if (!Registry.InvoiceLines.Read.Mapper.ContainsKey("@ID"))
            throw new Exception("InvoiceLines select should have a @ID variable");
        if (!Registry.InvoiceLines.Delete.Mapper.ContainsKey("@ID"))
            throw new Exception("InvoiceLines delete should have a @ID variable");
        if (!Registry.InvoiceLines.Update.Mapper.ContainsKey("@ID"))
            throw new Exception("InvoiceLines update should have a @ID variable");
    }
}