using RinkuLib.DbParsing;

namespace RinkuDemo;

public record Artist(int ID, string Name) : IDbReadable {
    public List<Album> Albums { get; set; } = [];
}

public record Album(int ID, string Title, Artist? Artist = null) : IDbReadable {
    public List<Track> Tracks { get; set; } = [];
}

public record Track(int ID, string Name, decimal UnitPrice, int Milliseconds, int Bytes, Album? Album = null, Reference? MediaType = null, KeyValuePair<int, string>? Genre = null) : IDbReadable;

public record struct Reference([Alt("Key")][InvalidOnNull]int ID, [Alt("Name")]string Value) : IDbReadable;

public record Employee(int ID, string LastName, string FirstName, string? Title = null, Employee? Manager = null) : IDbReadable {
    public List<Employee> ManagingEmployees { get; set; } = [];
}

public record Customer(int ID, string FirstName, string LastName, string Email, Employee? SupportRep = null) : IDbReadable {
    public List<Invoice> Invoices { get; set; } = [];
}

public record Invoice(int ID, DateTime InvoiceDate = default, decimal Total = 0, Customer? Customer = null) : IDbReadable {
    public List<InvoiceLine> Lines { get; set; } = [];
}

public record InvoiceLine(int ID, decimal UnitPrice = 0, int Quantity = 0, Invoice? Invoice = null, Track? Track = null) : IDbReadable;