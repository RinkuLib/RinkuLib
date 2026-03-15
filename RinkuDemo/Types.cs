using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using RinkuLib.DbParsing;
using RinkuLib.TypeAccessing;

namespace RinkuDemo;

public record Artist(int ID, [property: NotNullOrWhitespace] string Name) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public List<Album> Albums { get; set; } = [];
}

public record Album(int ID, [property: NotNullOrWhitespace] string Title, Artist? Artist = null) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public int? ArtistID => Artist?.ID;
    public List<Track> Tracks { get; set; } = [];
}

public record Track(int ID, [property: NotNullOrWhitespace] string Name, string Composer, int Milliseconds, int Bytes, decimal UnitPrice, Album? Album = null, Reference? MediaType = null, KeyValuePair<int, string>? Genre = null) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public int? AlbumID => Album?.ID;
    public int? ArtistID => Album?.Artist?.ID;
    public int? MediaTypeID => MediaType?.ID;
    public int? GenreID => Genre?.Key;
}

public record struct Reference([Alt("Key")][InvalidOnNull]int ID, [Alt("Name")]string Value) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public static implicit operator Reference(KeyValuePair<int, string> Kvp) => new(Kvp.Key, Kvp.Value);
    public static implicit operator KeyValuePair<int, string>(Reference Ref) => new(Ref.ID, Ref.Value);
}

public record Employee([InvalidOnNull]int ID, string FirstName, string LastName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), NotNullOrWhitespace] string? Title,
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
) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public int? ReportsTo => Manager?.ID;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Employee> ManagingEmployees { get; set; } = [];
}

public record Customer([InvalidOnNull]int ID, [NotNull][property: NotNullOrWhitespace] string FirstName, [NotNull][property: NotNullOrWhitespace] string LastName, string? Company, string? Address, string? City, string? State, string? Country, string? PostalCode, string? Phone, string? Fax, string? Email, Employee? SupportRep = null) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public int? SupportRepID => SupportRep?.ID;
    public List<Invoice> Invoices { get; set; } = [];
}

public record Invoice(int ID, DateTime InvoiceDate, decimal Total, string BillingAddress, string BillingCity, string BillingState, string BillingCountry, string BillingPostalCode, Customer? Customer = null) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
    public int? CustomerID => Customer?.ID;
    public List<InvoiceLine> Lines { get; set; } = [];
}

public record InvoiceLine(int ID, decimal UnitPrice, int Quantity, int InvoiceID, int TrackID, string TrackName) : IHasID<int> {
    [NotDefault]
    public int ID { get; set; } = ID;
}

