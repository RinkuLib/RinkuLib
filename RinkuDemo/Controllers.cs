using RinkuLib.Commands;
using RinkuLib.Queries;

namespace RinkuDemo;

public class ArtistModule : IApiModule<Artist> {
    public static string Name => "artist";
    public static async Task<int> Create(Artist a) {
        using var db = Registry.GetConnection();
        return await Registry.Artists.Create.ExecuteScalarAsync<int>(db, a);
    }
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
        var a = await Registry.Artists.Read.QueryOneAsync<Artist>(db, new { ID = id });
        if (a is null)
            return null;
        a.Albums = await Registry.Albums.Read.QueryAllBufferedAsync<Album>(db, new { ArtistID = id });
        if (a.Albums.Count > 0) {
            using var cmd = db.CreateCommand();
            var b = Registry.Tracks.Read.StartBuilder(cmd);
            foreach (var album in a.Albums) {
                b.Use("@AlbumID", album.ID);
                album.Tracks = await b.QueryAllBufferedAsync<Track>();
            }
        }
        return a;
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
    public static async Task<int> Create(Album a) {
        using var db = Registry.GetConnection();
        return await Registry.Albums.Create.ExecuteScalarAsync<int>(db, a);
    }
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
        var a = await Registry.Albums.Read.QueryOneAsync<Album>(db, new { ID = id });
        if (a is null)
            return null;
        a.Tracks = await Registry.Tracks.Read.QueryAllBufferedAsync<Track>(db, new { AlbumID = id });
        return a;
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
    public static async Task<int> Create(Track t) {
        using var db = Registry.GetConnection();
        return await Registry.Tracks.Create.ExecuteScalarAsync<int>(db, t);
    }
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
    public static async Task<int> Create(Reference mt) {
        using var db = Registry.GetConnection();
        return await Registry.MediaTypes.Create.ExecuteScalarAsync<int>(db, mt);
    }
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
    public static async Task<int> Create(KeyValuePair<int, string> g) {
        using var db = Registry.GetConnection();
        return await Registry.Genres.Create.ExecuteScalarAsync<int>(db, g);
    }
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
    public static async Task<int> Create(Employee e) {
        using var db = Registry.GetConnection();
        return await Registry.Employees.Create.ExecuteScalarAsync<int>(db, e);
    }
    public static async Task<bool> Delete(int id) {
        using var db = Registry.GetConnection();
        var rows = await Registry.Employees.Delete.ExecuteAsync(db, new { ID = id });
        return rows > 0;
    }
    public static IAsyncEnumerable<Employee> GetAll(HttpContext context)
        => Registry.Stream(context, Registry.Employees);
    public static async Task<Employee?> GetOne(int id) {
        using var db = Registry.GetConnection();
        var e = await Registry.Employees.Read.QueryOneAsync<Employee>(db, new { ID = id });
        if (e is null)
            return null;
        e.ManagingEmployees = await Registry.Employees.Read.QueryAllBufferedAsync<Employee>(db, new { ReportsTo = id });
        return e;
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
    }
}
public class CustomerModule : IApiModule<Customer> {
    public static string Name => "customer";
    public static async Task<int> Create(Customer c) {
        using var db = Registry.GetConnection();
        return await Registry.Customers.Create.ExecuteScalarAsync<int>(db, c);
    }
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
        var c = await Registry.Customers.Read.QueryOneAsync<Customer>(db, new { ID = id });
        if (c is null)
            return null;
        c.Invoices = await Registry.Invoices.Read.QueryAllBufferedAsync<Invoice>(db, new { CustomerID = id });
        using var cmd = db.CreateCommand();
        var b = Registry.InvoiceLines.Read.StartBuilder(cmd);
        foreach (var inv in c.Invoices) {
            b.Use("InvoiceID", inv.ID);
            inv.Lines = await Registry.InvoiceLines.Read.QueryAllBufferedAsync<InvoiceLine>(db, new { InvoiceID = inv.ID });
        }
        return c;
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
    }
}
public class InvoiceModule : IApiModule<Invoice> {
    public static string Name => "invoice";
    public static async Task<int> Create(Invoice i) {
        using var db = Registry.GetConnection();
        return await Registry.Invoices.Create.ExecuteScalarAsync<int>(db, i);
    }
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
        var inv = await Registry.Invoices.Read.QueryOneAsync<Invoice>(db, new { ID = id });
        if (inv is null)
            return null;
        inv.Lines = await Registry.InvoiceLines.Read.QueryAllBufferedAsync<InvoiceLine>(db, new { InvoiceID = id });
        return inv;
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
    }
}
public class InvoiceLineModule : IApiModule<InvoiceLine> {
    public static string Name => "invoiceline";
    public static async Task<int> Create(InvoiceLine il) {
        using var db = Registry.GetConnection();
        return await Registry.InvoiceLines.Create.ExecuteScalarAsync<int>(db, il);
    }
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
    }
}