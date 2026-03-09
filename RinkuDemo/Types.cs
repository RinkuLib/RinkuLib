using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using RinkuLib.DbParsing;
using RinkuLib.DbRegister;

namespace RinkuDemo;

public record Artist(int ID, string Name) : IDbReadable {
    [PopulateList("ID", "SELECT {0}AlbumId AS ID, Title FROM albums WHERE ArtistId {1}", "ArtistId")]
    public List<Album> Albums { get; set; } = [];
}

public record Album(int ID, string Title, Artist? Artist = null) : IDbReadable {
    public int? ArtistID => Artist?.ID;

    [PopulateList("ID", "SELECT {0}t.TrackId AS ID, t.Name, t.Composer, t.Milliseconds, t.Bytes, t.UnitPrice, t.MediaTypeId AS MediaTypeID, mt.Name AS MediaTypeName, t.GenreId AS GenreID, g.Name AS GenreName FROM tracks t INNER JOIN media_types mt ON t.MediaTypeId = mt.MediaTypeId INNER JOIN genres g ON t.GenreId = g.GenreId WHERE t.AlbumId {1}", "t.AlbumId")]
    public List<Track> Tracks { get; set; } = [];
}

public record Track(int ID, string Name, string Composer, int Milliseconds, int Bytes, decimal UnitPrice, Album? Album = null, Reference? MediaType = null, KeyValuePair<int, string>? Genre = null) : IDbReadable {
    public int? AlbumID => Album?.ID;
    public int? ArtistID => Album?.Artist?.ID;
    public int? MediaTypeID => MediaType?.ID;
    public int? GenreID => Genre?.Key;
}

public record struct Reference([Alt("Key")][InvalidOnNull]int ID, [Alt("Name")]string Value) : IDbReadable;

public record Employee([InvalidOnNull]int ID, string FirstName, string LastName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] DateTime BirthDate = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] DateTime HireDate = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Address = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? City = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Country = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PostalCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Phone = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Fax = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Email = null,
    Employee? Manager = null
) : IDbReadable {
    public int? ReportsTo => Manager?.ID;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [PopulateList("ID", "SELECT {0}EmployeeId AS ID, FirstName, LastName, Title, BirthDate, HireDate, Address, City, State, Country, PostalCode, Phone, Fax, Email FROM employees WHERE ReportsTo {1}", "ReportsTo")]
    public List<Employee> ManagingEmployees { get; set; } = [];
}

public record Customer([InvalidOnNull]int ID, [NotNull]string FirstName, [NotNull]string LastName, string? Company, string? Address, string? City, string? State, string? Country, string? PostalCode, string? Phone, string? Fax, string? Email, Employee? SupportRep = null) : IDbReadable {
    public int? SupportRepID => SupportRep?.ID;
    [PopulateList("ID", "SELECT {0}InvoiceId AS ID, InvoiceDate, Total, BillingAddress, BillingCity, BillingState, BillingCountry, BillingPostalCode FROM invoices WHERE CustomerId {1}", "CustomerId")]
    public List<Invoice> Invoices { get; set; } = [];
}

public record Invoice(int ID, DateTime InvoiceDate, decimal Total, string BillingAddress, string BillingCity, string BillingState, string BillingCountry, string BillingPostalCode, Customer? Customer = null) : IDbReadable {
    public int? CustomerID => Customer?.ID;
    [PopulateList("ID", "SELECT {0}ii.InvoiceLineId AS ID, ii.UnitPrice, ii.Quantity, ii.InvoiceId AS InvoiceID, ii.TrackId AS TrackID, t.Name AS TrackName FROM invoice_items ii INNER JOIN tracks t ON ii.TrackId = t.TrackId WHERE ii.InvoiceId {1}", "ii.InvoiceId")]
    public List<InvoiceLine> Lines { get; set; } = [];
}

public record InvoiceLine(int ID, decimal UnitPrice, int Quantity, int InvoiceID, int TrackID, string TrackName) : IDbReadable;

