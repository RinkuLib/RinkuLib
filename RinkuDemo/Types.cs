using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using RinkuLib.DbParsing;
using RinkuLib.TypeAccessing;

namespace RinkuDemo;

public record Artist(int ID, string Name) : IDbReadable {
    public List<Album> Albums { get; set; } = [];
}

public record Album(int ID, string Title, Artist? Artist = null) : IDbReadable {
    public List<Track> Tracks { get; set; } = [];
}

public record Track(int ID, string Name, string Composer, int Milliseconds, int Bytes, decimal UnitPrice, Album? Album = null, Reference? MediaType = null, KeyValuePair<int, string>? Genre = null) : IDbReadable;

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Employee> ManagingEmployees { get; set; } = [];
}

public record Customer([InvalidOnNull]int ID, [NotNull]string FirstName, [NotNull]string LastName, string? Company, string? Address, string? City, string? State, string? Country, string? PostalCode, string? Phone, string? Fax, string? Email, Employee? SupportRep = null) : IDbReadable {
    public List<Invoice> Invoices { get; set; } = [];
}

public record Invoice(int ID, DateTime InvoiceDate, decimal Total, string BillingAddress, string BillingCity, string BillingState, string BillingCountry, string BillingPostalCode, Customer? Customer = null) : IDbReadable {
    public List<InvoiceLine> Lines { get; set; } = [];
}

public record InvoiceLine(int ID, decimal UnitPrice, int Quantity, int InvoiceID, int TrackID, string TrackName) : IDbReadable;

