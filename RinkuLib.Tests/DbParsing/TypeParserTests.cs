using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

public class TypeParserTests {
    private readonly ITestOutputHelper _output;

    public TypeParserTests(ITestOutputHelper output) {
        _output = output;
#if DEBUG
        //Generator.Write = output.WriteLine;
#endif
    }
    private static DataTableReader CreateReader(ColumnInfo[] columns, Span<object[]> rows) {
        DataTable table = new();
        foreach (var col in columns)
            table.Columns.Add(new DataColumn(col.Name, col.Type) { AllowDBNull = col.IsNullable });
        foreach (var row in rows)
            table.Rows.Add(row);
        return table.CreateDataReader();
    }

    [Fact]
    public void Test_SimpleUser_Mapping() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [
            [1, "John Doe"],
            [3, "Jane Smith"]
        ]);

        var parser = TypeParser<SimpleUser>.GetParserFunc(ref columns);

        reader.Read();
        var user1 = parser(reader);
        Assert.Equal(1, user1.Id);
        Assert.Equal("John Doe", user1.Name);

        reader.Read();
        var user2 = parser(reader);
        Assert.Equal(3, user2.Id);
        Assert.Equal("Jane Smith", user2.Name);
    }
    [Fact]
    public void Using_ValueTuple() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [
            [1, "John Doe"],
            [3, "Jane Smith"]
        ]);

        var parser = TypeParser<(int ID, string Name)>.GetParserFunc(ref columns);

        reader.Read();
        var (ID, Name) = parser(reader);
        Assert.Equal(1, ID);
        Assert.Equal("John Doe", Name);

        reader.Read();
        var user2 = parser(reader);
        Assert.Equal(3, user2.ID);
        Assert.Equal("Jane Smith", user2.Name);
    }
    [Fact]
    public void Using_ValueTuple_Same() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("ID", typeof(int), false)
        ];

        using var reader = CreateReader(columns, [
            [1, 2]
        ]);

        var parser = TypeParser<(int ID, int ID2)>.GetParserFunc(ref columns);

        reader.Read();
        var (ID, ID2) = parser(reader);
        Assert.Equal(1, ID);
        Assert.Equal(2, ID2);

    }
    [Fact]
    public void Scalar() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [
            [1, "John Doe"],
            [3, "Jane Smith"]
        ]);

        var parser = TypeParser<int>.GetParserFunc(ref columns);

        reader.Read();
        var id1 = parser(reader);
        Assert.Equal(1, id1);

        reader.Read();
        var id2 = parser(reader);
        Assert.Equal(3, id2);
    }

    [Fact]
    public void Test_EmployeeRecord_With_ValueTypes() {
        var badge = Guid.NewGuid();
        var joinDate = new DateTime(2023, 05, 10);
        ColumnInfo[] columns = [
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), false),
            new("JoinedAt", typeof(DateTime), false)
        ];

        using var reader = CreateReader(columns, [
            [badge, "Engineering", 95000.50m, joinDate]
        ]);

        var parser = TypeParser<EmployeeRecord>.GetParserFunc(ref columns);

        reader.Read();
        var emp = parser(reader);

        Assert.Equal(badge, emp.BadgeId);
        Assert.Equal(95000.50m, emp.Salary);
        Assert.Equal(joinDate, emp.JoinedAt);
    }

    [Fact]
    public void Test_ProductStatus_With_Nullables() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
            new("Weight", typeof(double), true),
            new("IsInStock", typeof(bool), false),
            new("WarehouseZone", typeof(char), false)
        ];

        // Row 1: Has weight, Row 2: Weight is NULL
        using var reader = CreateReader(columns, [
            [500, 12.5, true, 'A'],
            [501, DBNull.Value, false, 'B']
        ]);

        var parser = TypeParser<ProductStatus>.GetParserFunc(ref columns);

        reader.Read();
        var p1 = parser(reader);
        Assert.Equal(12.5, p1.Weight);

        reader.Read();
        var p2 = parser(reader);
        Assert.Null(p2.Weight);
        Assert.False(p2.IsInStock);
        Assert.Equal('B', p2.WarehouseZone);
    }
    [Fact]
    public void Test_The_Works_Recursion_Hydration_And_JumpIfNull() {
        ColumnInfo[] columns = [
            new("ShipmentID", typeof(int), false),
            
            // Package (contents) - Testing JumpIfNull on TrackingId
            new("ContentsTrackingID", typeof(int), true),
            new("ContentsWeight", typeof(double), true),
            
            // Label (routing) - Testing Property Hydration + NotNull
            new("RoutingServiceLevel", typeof(string), false),
            new("RoutingNotes", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [100, 555, 1.5, "Overnight", "Fragile"], 
            [200, DBNull.Value, 0.0, "Ground", DBNull.Value]
        ]);

        var parser = TypeParser<Shipment>.GetParserFunc(ref columns);

        // --- Execute Row 1 ---
        reader.Read();
        var s1 = parser(reader);
        Assert.Equal(100, s1.ShipmentId);
        Assert.Equal(555, s1.Contents!.Value.TrackingId);
        Assert.Equal(1.5, s1.Contents!.Value.Weight);
        Assert.Equal(555, s1.Contents.Value.TrackingId);
        Assert.Equal("Overnight", s1.Routing.ServiceLevel);
        Assert.Equal("Fragile", s1.Routing.Notes);

        // --- Execute Row 2 (JumpIfNull Test) ---
        reader.Read();
        var s2 = parser(reader);
        Assert.Equal(200, s2.ShipmentId);
        // TrackingId was null, so the Package struct should be null in the parent
        Assert.Null(s2.Contents);
        // Label should still hydrate normally
        Assert.Equal("Ground", s2.Routing.ServiceLevel);
        Assert.Null(s2.Routing.Notes);
    }
    [Fact]
    public void Test_With_Interface_Overload() {
        // We provide columns that satisfy Overload 2 of IPayment.Create
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [[99, "DE123456789", "GENEDEBK"]]);

        var parser = TypeParser<Order>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);

        // ASSERT
        Assert.Equal(99, result.OrderId);
        Assert.IsType<Transfer>(result.Payment);
        var transfer = (Transfer)result.Payment;
        Assert.Equal("DE123456789", transfer.Iban);
        Assert.Equal("GENEDEBK", transfer.Bic);
    }
    [Fact]
    public void Test_With_Interface_Overload_Manual_Add() {
        TypeParsingInfo.GetOrAdd<IPayment>()
            .AddPossibleConstruction(typeof(ExternalIDPayment)
            .GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(int)]) 
            ?? throw new Exception("method not found"));
        // We provide columns that satisfy Overload 2 of IPayment.Create
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentExternalID", typeof(int), false)
        ];

        using var reader = CreateReader(columns, [[99, 14532]]);

        var parser = TypeParser<Order>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);

        // ASSERT
        Assert.Equal(99, result.OrderId);
        Assert.IsType<ExternalIDPayment>(result.Payment);
        var transfer = (ExternalIDPayment)result.Payment;
        Assert.Equal(14532, transfer.ExternalID);
    }
    [Fact]
    public void Test_With_Interface_Overload_Reorder_Specificity() {
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentCardNumber", typeof(string), false),
            new("PaymentOwner", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [[321, "1234 5678 9012 3456", "John Smith"]]);

        var parser = TypeParser<Order>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);

        // ASSERT
        Assert.Equal(321, result.OrderId);
        Assert.IsType<CardDetailed>(result.Payment);
        var transfer = (CardDetailed)result.Payment;
        Assert.Equal("1234 5678 9012 3456", transfer.CardNumber);
        Assert.Equal("John Smith", transfer.Owner);
    }
    [Fact]
    public void Test_With_Interface_Overload_DifferentMatch() {
        // We provide columns that satisfy Overload 2 of IPayment.Create
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [99, "DE123456789", "GENEDEBK"],
            [100, "1234 5678 9012 3456", DBNull.Value]
        ]);

        var parser = TypeParser<Order>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);

        // ASSERT
        Assert.Equal(99, result.OrderId);
        Assert.IsType<Transfer>(result.Payment);
        var transfer = (Transfer)result.Payment;
        Assert.Equal("DE123456789", transfer.Iban);
        Assert.Equal("GENEDEBK", transfer.Bic);
        reader.Read();
        result = parser(reader);

        // ASSERT
        Assert.Equal(100, result.OrderId);
        Assert.IsType<Card>(result.Payment);
        var transfer2 = (Card)result.Payment;
        Assert.Equal("1234 5678 9012 3456", transfer2.CardNumber);
    }
    [Fact]
    public void Test_Generic_Recursive_Mapping_With_NotNullable_Struct_Null() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
        
        // Price<decimal> mapping: ListingPrice (param) + Amount/Currency
        new("ListingPriceAmount", typeof(decimal), true),
        new("ListingPriceCurrency", typeof(byte), true),
        
        // Metadata<string> mapping: Info (param) + Value/Source
        new("InfoValue", typeof(string), false),
        new("InfoSource", typeof(string), true)
        ];

        // Row 1: Fully populated with decimal and string
        // Row 2: Price Amount is null -> JumpIfNull should make ListingPrice null
        using var reader = CreateReader(columns, [
            [1, 99.99m, DBNull.Value, "Premium Grade", "Warehouse A"]
        ]);

        // Testing BoxedProduct with <decimal, string>
        var parser = TypeParser<BoxedProduct<decimal, string>>.GetParserFunc(ref columns);

        // --- Row 1 ---
        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser(reader));
    }

    [Fact]
    public void Test_Generic_Type_Switching() {
        // Same structure, different Generic types: <double, int>
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(double), false),
            new("ListingPriceCurrency", typeof(byte), false),
            new("InfoValue", typeof(int), false),
            new("InfoSource", typeof(string), true)
            ];

        using var reader = CreateReader(columns, [
            [500, 12.50, CurrencyCode.CAD, 42, "API"]
        ]);

        var parser = TypeParser<BoxedProduct<double, int>>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);

        Assert.IsType<double>(result.ListingPrice!.Value.Amount);
        Assert.IsType<int>(result.Info.Value);
        Assert.Equal(12.50, result.ListingPrice.Value.Amount);
        Assert.Equal(42, result.Info.Value);
    }
    [Fact]
    public void Test_BoxedProduct_Comprehensive_Validation() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
            
            // Price<decimal> - Parameters: Amount, Currency
            new("ListingPriceAmount", typeof(decimal), true),
            new("ListingPriceCurrency", typeof(int), true),
            
            // Metadata<int, string> - Ctor: Value, Prop: Source
            new("InfoValue", typeof(int), false),
            new("InfoSource", typeof(string), true)
        ];

        // Row 1: All present (Normal Case)
        // Row 2: Price Amount is NULL (JumpIfNull Case)
        // Row 3: Info Source is NULL (Optional Property Case)
        using var reader = CreateReader(columns, [
            [101, 99.50m, 1, 500, "Warehouse_Alpha"],
            [102, DBNull.Value, 2, 600, "Warehouse_Beta"],
            [103, 10.00m, 3, 700, DBNull.Value]
        ]);

        // Closing with <decimal, int>
        var parser = TypeParser<BoxedProduct<decimal, int>>.GetParserFunc(ref columns);

        // --- VALIDATE ROW 1: Normal Operation ---
        reader.Read();
        var p1 = parser(reader);
        Assert.Equal(101, p1.ProductId);
        Assert.Equal(99.50m, p1.ListingPrice!.Value.Amount);
        Assert.Equal(CurrencyCode.CAD, p1.ListingPrice.Value.Currency);
        Assert.Equal(500, p1.Info.Value);
        Assert.Equal("Warehouse_Alpha", p1.Info.Source);

        // --- VALIDATE ROW 2: JumpIfNull in Generic Price ---
        reader.Read();
        var p2 = parser(reader);
        Assert.Equal(102, p2.ProductId);
        Assert.Null(p2.ListingPrice);
        Assert.Equal(600, p2.Info.Value);
        Assert.Equal("Warehouse_Beta", p2.Info.Source);

        // --- VALIDATE ROW 3: Hybrid Hydration (Property is Null) ---
        reader.Read();
        var p3 = parser(reader);
        Assert.Equal(103, p3.ProductId);
        Assert.Equal(10.00m, p3.ListingPrice!.Value.Amount);
        Assert.Equal(CurrencyCode.GBP, p3.ListingPrice.Value.Currency);
        Assert.Equal(700, p3.Info.Value);
        Assert.Null(p3.Info.Source);
    }

    [Fact]
    public void Test_NotNull_Constraint_On_Generic_Parameter() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(double), false),
            new("ListingPriceCurrency", typeof(int), false),
            new("InfoValue", typeof(string), true) // Database allows NULL
        ];

        // Metadata.Value is [NotNull] T. If DB gives us NULL, it must fail.
        using var reader = CreateReader(columns, [[201, 15.0d, 2, "Trusted"], [202, 15.0, 1, DBNull.Value]]);

        var parser = TypeParser<BoxedProduct<double, string>>.GetParserFunc(ref columns);

        reader.Read();
        var p1 = parser(reader);
        Assert.Equal(201, p1.ProductId);
        Assert.Equal(15.0d, p1.ListingPrice!.Value.Amount);
        Assert.Equal(CurrencyCode.EUR, p1.ListingPrice.Value.Currency);
        Assert.Equal("Trusted", p1.Info.Value);
        Assert.Null(p1.Info.Source);
        reader.Read();
        // Should throw because Metadata.Value is marked [NotNull]
        Assert.ThrowsAny<Exception>(() => parser(reader));
    }

    [Fact]
    public void Recursive_User() {
        // Same structure, different Generic types: <double, int>
        ColumnInfo[] columns = [
            new("ID", typeof(int), false),
            new("Name", typeof(string), false),
            new("SupervisorID", typeof(int), false),
            new("SupervisorName", typeof(string), false),
            new("SupervisorBossID", typeof(int), false),
            new("SupervisorBossName", typeof(string), false)
            ];

        using var reader = CreateReader(columns, [
            [3, "Roger", 2, "Victor", 1, "Albert"]
        ]);

        var parser = TypeParser<User>.GetParserFunc(ref columns);

        reader.Read();
        var result = parser(reader);
        Assert.Equal(3, result.ID);
        Assert.Equal("Roger", result.Name);
        var sup = result.Supervisor;
        Assert.NotNull(sup);
        Assert.Equal(2, sup.ID);
        Assert.Equal("Victor", sup.Name);
        var boss = sup.Supervisor;
        Assert.NotNull(boss);
        Assert.Equal(1, boss.ID);
        Assert.Equal("Albert", boss.Name);
    }
    [Fact]
    public void Multi_Level_Jump() {
        // Same structure, different Generic types: <double, int>
        ColumnInfo[] columns = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
            ];

        using var reader = CreateReader(columns, [
            [500, 400, 300, "Name"],
            [500, 400, DBNull.Value, "Name"]
        ]);

        var parser = TypeParser<TestTop>.GetParserFunc(ref columns);

        reader.Read();
        var top = parser(reader);

        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Equal(300, top.Middle.Bottom.ID);
        Assert.Equal("Name", top.Middle.Bottom.Name);

        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser(reader));
    }
    [Fact]
    public void Multi_Level_Jump_Alt2() {
        // Same structure, different Generic types: <double, int>
        ColumnInfo[] columns = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
            ];

        using var reader = CreateReader(columns, [
            [500, 400, DBNull.Value, "Name"]
        ]);

        var parser = TypeParser<TestTop2>.GetParserFunc(ref columns);

        reader.Read();
        var top = parser(reader);

        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Null(top.Middle.Bottom);
    }
    [Fact]
    public void Multi_Level_Jump_Alt3() {
        // Same structure, different Generic types: <double, int>
        ColumnInfo[] columns = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
            ];

        using var reader = CreateReader(columns, [
            [500, 400, DBNull.Value, "Name"]
        ]);

        var parser = TypeParser<TestTop3>.GetParserFunc(ref columns);

        reader.Read();
        var top = parser(reader);

        Assert.Equal(500, top.ID);
        Assert.Null(top.Middle);
    }
}
public record class User(int ID, string Name, [Alt("Boss")]User? Supervisor = null);
public class SimpleUser {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class EmployeeRecord {
    public Guid BadgeId { get; set; }
    public string Department { get; set; } = "General";
    public decimal Salary { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class ProductStatus {
    public int ProductId { get; set; }
    public double? Weight { get; set; }
    public bool IsInStock { get; set; }
    public char WarehouseZone { get; set; }
}
public record struct Package(
    [InvalidOnNull] int TrackingId,
    double Weight
) : IDbReadable;

public class Label : IDbReadable {
    [NotNull]
    public string ServiceLevel { get; set; } = null!;
    public string? Notes { get; set; }
}

public class Shipment(int shipmentId, Package? contents, Label routing) : IDbReadable {
    public int ShipmentId { get; } = shipmentId;
    public Package? Contents { get; } = contents;
    public Label Routing { get; } = routing;
}
// The Sub-type Interface - Marker is required here
public interface IPayment : IDbReadable {
    public static IPayment CreateCard(string cardNumber) => new Card(cardNumber);
    public static IPayment CreateCard(string cardNumber, string owner) => new CardDetailed(cardNumber, owner);
    public static IPayment CreateTransfer(string iban, string bic) => new Transfer(iban, bic);
    public static IPayment Create([Alt("CardNumber")][Alt("Iban")]string cardNumberOrIban, string? bic) 
        => bic is null ? new Card(cardNumberOrIban) :new Transfer(cardNumberOrIban, bic);
}

public record Card(string CardNumber) : IPayment;
public record CardDetailed(string CardNumber, string Owner) : IPayment;
public record Transfer(string Iban, string Bic) : IPayment;
public record ExternalIDPayment(int ExternalID) : IPayment;

// The Root Type - NO marker interface needed here
public class Order {
    public int OrderId { get; }
    public IPayment Payment { get; }
    public static Order Create(int orderId, IPayment payment)
        => new(orderId, payment);
    private Order(int orderId, IPayment payment) {
        OrderId = orderId;
        Payment = payment;
    }
}
public enum CurrencyCode {
    CAD = 1,
    EUR = 2,
    GBP = 3
}
public record struct Price<T>([InvalidOnNull] T Amount, CurrencyCode Currency) : IDbReadable
    where T : struct;

[method: CanCompleteWithMembers]
public class Metadata<T, TSource>([NotNull] T Value) : IDbReadable where T : notnull {
    public T Value { get; } = Value;
    public TSource? Source { get; set; }
}

// Complex root using Generics
public class BoxedProduct<TAmount, TMeta>(int productId, Price<TAmount>? listingPrice, Metadata<TMeta, string> info) where TAmount : struct where TMeta : notnull {
    public int ProductId { get; } = productId;
    public Price<TAmount>? ListingPrice { get; } = listingPrice;
    public Metadata<TMeta, string> Info { get; } = info;
}
public record class TestTop(int ID, TestMiddle Middle) : IDbReadable;
public record class TestMiddle(int ID, TestBottom Bottom) : IDbReadable;
public record class TestTop2(int ID, TestMiddle2 Middle) : IDbReadable;
public record class TestMiddle2(int ID, TestBottom? Bottom) : IDbReadable;
public record class TestTop3(int ID, TestMiddle3 Middle) : IDbReadable;
public record class TestMiddle3(int ID, [InvalidOnNull] TestBottom Bottom) : IDbReadable;
public record struct TestBottom([InvalidOnNull]int ID, string Name) : IDbReadable;