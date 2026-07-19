using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
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

        var parser = TypeParser.GetTypeParser<SimpleUser>(ref columns);

        reader.Read();
        var (canContinue, user1) = parser.Parse(reader);
        Assert.Equal(1, user1.Id);
        Assert.Equal("John Doe", user1.Name);
        Assert.True(canContinue);

        var (canContinue2, user2) = parser.Parse(reader);
        Assert.Equal(3, user2.Id);
        Assert.Equal("Jane Smith", user2.Name);
        Assert.False(canContinue2);
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

        var parser = TypeParser.GetTypeParser<(int ID, string Name)>(ref columns);

        reader.Read();
        var (ID, Name) = parser.Parse(reader).Result;
        Assert.Equal(1, ID);
        Assert.Equal("John Doe", Name);

        var user2 = parser.Parse(reader).Result;
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

        var parser = TypeParser.GetTypeParser<(int ID, int ID2)>(ref columns);

        reader.Read();
        var (ID, ID2) = parser.Parse(reader).Result;
        Assert.Equal(1, ID);
        Assert.Equal(2, ID2);

    }
    [Fact]
    public void Using_ValueTuple_Same_Complex() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ID", typeof(int), false),
            new("name", typeof(string), false),
            new("Other", typeof(string), true),
        ];

        using var reader = CreateReader(columns, [
            [1, "Test1", 2, "Test2", "Stop2"]
        ]);

        var parser = TypeParser.GetTypeParser<(TestStop, TestStop)>(ref columns);

        reader.Read();
        var (stop1, stop2) = parser.Parse(reader).Result;
        Assert.Equal(1, stop1.ID);
        Assert.Equal("Test1", stop1.Name);
        Assert.Null(stop1.Other);
        Assert.Equal(2, stop2.ID);
        Assert.Equal("Test2", stop2.Name);
        Assert.Equal("Stop2", stop2.Other);
    }
    [Fact]
    public void Using_ValueTuple_Same_Complex_Look() {
        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ID", typeof(int), false),
            new("name", typeof(string), false),
            new("Other", typeof(string), true),
        ];

        using var reader = CreateReader(columns, [
            [1, "Test1", 2, "Test2", "Stop1"]
        ]);

        var parser = TypeParser.GetTypeParser<(TestStop2, TestStop3)>(ref columns);

        reader.Read();
        var (stop1, stop2) = parser.Parse(reader).Result;
        Assert.Equal(1, stop1.ID);
        Assert.Equal("Test1", stop1.Name);
        Assert.Equal("Stop1", stop1.Other);
        Assert.Equal(2, stop2.ID);
        Assert.Equal("Test2", stop2.Name);
        Assert.Null(stop2.Other);
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

        var parser = TypeParser.GetTypeParser<int>(ref columns);

        reader.Read();
        var id1 = parser.Parse(reader).Result;
        Assert.Equal(1, id1);

        var id2 = parser.Parse(reader).Result;
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

        var parser = TypeParser.GetTypeParser<EmployeeRecord>(ref columns);

        reader.Read();
        var emp = parser.Parse(reader).Result;

        Assert.Equal(badge, emp.BadgeId);
        Assert.Equal("Engineering", emp.Department);
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

        using var reader = CreateReader(columns, [
            [500, 12.5, true, 'A'],
            [501, DBNull.Value, false, 'B']
        ]);

        var parser = TypeParser.GetTypeParser<ProductStatus>(ref columns);

        reader.Read();
        var p1 = parser.Parse(reader).Result;
        Assert.Equal(12.5, p1.Weight);

        var p2 = parser.Parse(reader).Result;
        Assert.Null(p2.Weight);
        Assert.False(p2.IsInStock);
        Assert.Equal('B', p2.WarehouseZone);
    }
    [Fact]
    public void Test_The_Works_Recursion_Hydration_And_JumpIfNull() {
        ColumnInfo[] columns = [
            new("ShipmentID", typeof(int), false),
            
            new("ContentsTrackingID", typeof(int), true),
            new("ContentsWeight", typeof(double), true),
            
            new("RoutingServiceLevel", typeof(string), false),
            new("RoutingNotes", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [100, 555, 1.5, "Overnight", "Fragile"], 
            [200, DBNull.Value, 0.0, "Ground", DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<Shipment>(ref columns);

        reader.Read();
        var s1 = parser.Parse(reader).Result;
        Assert.Equal(100, s1.ShipmentId);
        Assert.Equal(555, s1.Contents!.Value.TrackingId);
        Assert.Equal(1.5, s1.Contents!.Value.Weight);
        Assert.Equal(555, s1.Contents.Value.TrackingId);
        Assert.Equal("Overnight", s1.Routing.ServiceLevel);
        Assert.Equal("Fragile", s1.Routing.Notes);

        var s2 = parser.Parse(reader).Result;
        Assert.Equal(200, s2.ShipmentId);
        Assert.Null(s2.Contents);
        Assert.Equal("Ground", s2.Routing.ServiceLevel);
        Assert.Null(s2.Routing.Notes);
    }
    [Fact]
    public void Test_With_Interface_Overload() {
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [[99, "DE123456789", "GENEDEBK"]]);

        var parser = TypeParser.GetTypeParser<Order>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;

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
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentExternalID", typeof(int), false)
        ];

        using var reader = CreateReader(columns, [[99, 14532]]);

        var parser = TypeParser.GetTypeParser<Order>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(99, result.OrderId);
        Assert.IsType<ExternalIDPayment>(result.Payment);
        var transfer = (ExternalIDPayment)result.Payment;
        Assert.Equal(14532, transfer.ExternalID);
    }
    [Fact]
    public void Test_Generic_Factory_Manual_Add() {
        TypeParsingInfo.GetOrAdd(typeof(Wrapped<>))
            .AddPossibleConstruction(typeof(WrappedFactory).GetMethod(nameof(WrappedFactory.Create))!);

        ColumnInfo[] intCols = [new("Value", typeof(int), false)];
        using (var reader = CreateReader(intCols, [[7]])) {
            reader.Read();
            Assert.Equal(7, TypeParser.GetTypeParser<Wrapped<int>>(ref intCols).Parse(reader).Result.Value);
        }

        ColumnInfo[] strCols = [new("Value", typeof(string), false)];
        using (var reader = CreateReader(strCols, [["hi"]])) {
            reader.Read();
            Assert.Equal("hi", TypeParser.GetTypeParser<Wrapped<string>>(ref strCols).Parse(reader).Result.Value);
        }
    }
    [Fact]
    public void Test_With_Interface_Overload_Reorder_Specificity() {
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentCardNumber", typeof(string), false),
            new("PaymentOwner", typeof(string), false)
        ];

        using var reader = CreateReader(columns, [[321, "1234 5678 9012 3456", "John Smith"]]);

        var parser = TypeParser.GetTypeParser<Order>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(321, result.OrderId);
        Assert.IsType<CardDetailed>(result.Payment);
        var transfer = (CardDetailed)result.Payment;
        Assert.Equal("1234 5678 9012 3456", transfer.CardNumber);
        Assert.Equal("John Smith", transfer.Owner);
    }
    [Fact]
    public void Test_With_Interface_Overload_DifferentMatch() {
        ColumnInfo[] columns = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [99, "DE123456789", "GENEDEBK"],
            [100, "1234 5678 9012 3456", DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<Order>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(99, result.OrderId);
        Assert.IsType<Transfer>(result.Payment);
        var transfer = (Transfer)result.Payment;
        Assert.Equal("DE123456789", transfer.Iban);
        Assert.Equal("GENEDEBK", transfer.Bic);
        result = parser.Parse(reader).Result;

        Assert.Equal(100, result.OrderId);
        Assert.IsType<Card>(result.Payment);
        var transfer2 = (Card)result.Payment;
        Assert.Equal("1234 5678 9012 3456", transfer2.CardNumber);
    }
    [Fact]
    public void Test_Generic_Recursive_Mapping_With_NotNullable_Struct_Null() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
        
            new("ListingPriceAmount", typeof(decimal), true),
            new("ListingPriceCurrency", typeof(byte), true),
        
            new("InfoValue", typeof(string), false),
            new("InfoSource", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [1, 99.99m, DBNull.Value, "Premium Grade", "Warehouse A"]
        ]);

        var parser = TypeParser.GetTypeParser<BoxedProduct<decimal, string>>(ref columns);

        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }

    [Fact]
    public void Test_Generic_Type_Switching() {
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

        var parser = TypeParser.GetTypeParser<BoxedProduct<double, int>>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.IsType<double>(result.ListingPrice!.Value.Amount);
        Assert.IsType<int>(result.Info.Value);
        Assert.Equal(12.50, result.ListingPrice.Value.Amount);
        Assert.Equal(42, result.Info.Value);
    }
    [Fact]
    public void Test_BoxedProduct_Comprehensive_Validation() {
        ColumnInfo[] columns = [
            new("ProductId", typeof(int), false),
            
            new("ListingPriceAmount", typeof(decimal), true),
            new("ListingPriceCurrency", typeof(int), true),
            
            new("InfoValue", typeof(int), false),
            new("InfoSource", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [
            [101, 99.50m, 1, 500, "Warehouse_Alpha"],
            [102, DBNull.Value, 2, 600, "Warehouse_Beta"],
            [103, 10.00m, 3, 700, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<BoxedProduct<decimal, int>>(ref columns);

        reader.Read();
        var p1 = parser.Parse(reader).Result;
        Assert.Equal(101, p1.ProductId);
        Assert.Equal(99.50m, p1.ListingPrice!.Value.Amount);
        Assert.Equal(CurrencyCode.CAD, p1.ListingPrice.Value.Currency);
        Assert.Equal(500, p1.Info.Value);
        Assert.Equal("Warehouse_Alpha", p1.Info.Source);

        var p2 = parser.Parse(reader).Result;
        Assert.Equal(102, p2.ProductId);
        Assert.Null(p2.ListingPrice);
        Assert.Equal(600, p2.Info.Value);
        Assert.Equal("Warehouse_Beta", p2.Info.Source);

        var p3 = parser.Parse(reader).Result;
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
            new("InfoValue", typeof(string), true)
        ];

        using var reader = CreateReader(columns, [[201, 15.0d, 2, "Trusted"], [202, 15.0, 1, DBNull.Value]]);

        var parser = TypeParser.GetTypeParser<BoxedProduct<double, string>>(ref columns);

        reader.Read();
        var p1 = parser.Parse(reader).Result;
        Assert.Equal(201, p1.ProductId);
        Assert.Equal(15.0d, p1.ListingPrice!.Value.Amount);
        Assert.Equal(CurrencyCode.EUR, p1.ListingPrice.Value.Currency);
        Assert.Equal("Trusted", p1.Info.Value);
        Assert.Null(p1.Info.Source);
        var refused = Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
        Assert.Contains("Value", refused.Message);
    }

    [Fact]
    public void Recursive_User() {
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

        var parser = TypeParser.GetTypeParser<User>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;
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
    public void Recursive_User_InvalidOnNull() {
        ColumnInfo[] columns = [
            new("ID", typeof(int), false),
            new("Name", typeof(string), false),
            new("SupervisorID", typeof(int), true),
            new("SupervisorName", typeof(string), true),
            new("SupervisorBossID", typeof(int), true),
            new("SupervisorBossName", typeof(string), true)
            ];

        using var reader = CreateReader(columns, [
            [3, "Roger", 2, "Victor", DBNull.Value, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<User2>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;
        Assert.Equal(3, result.ID);
        Assert.Equal("Roger", result.Name);
        var sup = result.Supervisor;
        Assert.NotNull(sup);
        Assert.Equal(2, sup.ID);
        Assert.Equal("Victor", sup.Name);
        var boss = sup.Supervisor;
        Assert.Null(boss);
    }
    [Fact]
    public void Multi_Level_Jump() {
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

        var parser = TypeParser.GetTypeParser<TestTop>(ref columns);

        reader.Read();
        var top = parser.Parse(reader).Result;

        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Equal(300, top.Middle.Bottom.ID);
        Assert.Equal("Name", top.Middle.Bottom.Name);

        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }
    [Fact]
    public void Multi_Level_Jump_Alt2() {
        ColumnInfo[] columns = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
            ];

        using var reader = CreateReader(columns, [
            [500, 400, DBNull.Value, "Name"]
        ]);

        var parser = TypeParser.GetTypeParser<TestTop2>(ref columns);

        reader.Read();
        var top = parser.Parse(reader).Result;

        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Null(top.Middle.Bottom);
    }
    [Fact]
    public void Multi_Level_Jump_Alt3() {
        ColumnInfo[] columns = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
            ];

        using var reader = CreateReader(columns, [
            [500, 400, DBNull.Value, "Name"]
        ]);

        var parser = TypeParser.GetTypeParser<TestTop3>(ref columns);

        reader.Read();
        var top = parser.Parse(reader).Result;

        Assert.Equal(500, top.ID);
        Assert.Null(top.Middle);
    }
    [Fact]
    public void DynaObject() {
        var badge = Guid.NewGuid();
        var joinDate = new DateTime(2023, 05, 10);
        ColumnInfo[] columns = [
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), true),
            new("JoinedAt", typeof(DateTime), true)
        ];
        using var reader = CreateReader(columns, [
            [badge, "Engineering", 95000.50m, joinDate],
            [badge, "Engineeringg", DBNull.Value, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<DynaObject>(ref columns);

        reader.Read();
        var emp = parser.Parse(reader).Result;

        Assert.Equal(badge, emp.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", emp.Get<string>("Department"));
        Assert.Equal(95000.50m, emp.Get<decimal>("Salary"));
        Assert.Equal(joinDate, emp.Get<DateTime>("JoinedAt"));
        emp = parser.Parse(reader).Result;

        Assert.Equal(badge, emp["BadgeId"]);
        Assert.Equal("Engineeringg", emp.Get<object>("Department"));
        Assert.Null(emp["Salary"]);
        Assert.Null(emp.Get<DateTime?>("JoinedAt"));
    }
    [Fact]
    public void DynaObject_InTuple() {
        var badge = Guid.NewGuid();
        var joinDate = new DateTime(2023, 05, 10);
        ColumnInfo[] columns = [
            new("ID", typeof(int), false),
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), true),
            new("JoinedAt", typeof(DateTime), true)
        ];
        using var reader = CreateReader(columns, [
            [1, badge, "Engineering", 95000.50m, joinDate],
            [2, badge, "Engineeringg", DBNull.Value, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<(int, DynaObject)>(ref columns);

        reader.Read();
        var (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(1, id);
        Assert.Equal(badge, emp.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", emp.Get<string>("Department"));
        Assert.Equal(95000.50m, emp.Get<decimal>("Salary"));
        Assert.Equal(joinDate, emp.Get<DateTime>("JoinedAt"));
        (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(2, id);
        Assert.Equal(badge, emp["BadgeId"]);
        Assert.Equal("Engineeringg", emp.Get<object>("Department"));
        Assert.Null(emp["Salary"]);
        Assert.Null(emp.Get<DateTime?>("JoinedAt"));
    }
    [Fact]
    public void DynaObject_NestedObj() {
        var badge = Guid.NewGuid();
        var joinDate = new DateTime(2023, 05, 10);
        ColumnInfo[] columns = [
            new("ID", typeof(int), false),
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), true),
            new("JoinedAt", typeof(DateTime), true)
        ];
        using var reader = CreateReader(columns, [
            [1, badge, "Engineering", 95000.50m, joinDate],
            [2, badge, "Engineeringg", DBNull.Value, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<DynaPair<int>>(ref columns);

        reader.Read();
        var (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(1, id);
        Assert.Equal(badge, emp.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", emp.Get<string>("Department"));
        Assert.Equal(95000.50m, emp.Get<decimal>("Salary"));
        Assert.Equal(joinDate, emp.Get<DateTime>("JoinedAt"));
        (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(2, id);
        Assert.Equal(badge, emp["BadgeId"]);
        Assert.Equal("Engineeringg", emp.Get<object>("Department"));
        Assert.Null(emp["Salary"]);
        Assert.Null(emp.Get<DateTime?>("JoinedAt"));
    }
    [Fact]
    public void DynaObject_NestedObj2() {
        var badge = Guid.NewGuid();
        var joinDate = new DateTime(2023, 05, 10);
        ColumnInfo[] columns = [
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), true),
            new("IDAnywhere", typeof(int), false),
            new("JoinedAt", typeof(DateTime), true)
        ];
        using var reader = CreateReader(columns, [
            [badge, "Engineering", 95000.50m, 1, joinDate],
            [badge, "Engineeringg", DBNull.Value, 2, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<DynaPair2<int>>(ref columns);

        reader.Read();
        var (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(1, id);
        Assert.Equal(badge, emp.Get<Guid>("BadgeId"));
        Assert.Equal("Engineering", emp.Get<string>("Department"));
        Assert.Equal(95000.50m, emp.Get<decimal>("Salary"));
        Assert.Equal(joinDate, emp.Get<DateTime>("JoinedAt"));
        (id, emp) = parser.Parse(reader).Result;

        Assert.Equal(2, id);
        Assert.Equal(badge, emp["BadgeId"]);
        Assert.Equal("Engineeringg", emp.Get<object>("Department"));
        Assert.Null(emp["Salary"]);
        Assert.Null(emp.Get<DateTime?>("JoinedAt"));
    }
    [Fact]
    public void DynaObject_Dup() {
        var badge = Guid.NewGuid();
        ColumnInfo[] columns = [
            new("BadgeId", typeof(Guid), false),
            new("BadgeID", typeof(Guid), true)
        ];

        using var reader = CreateReader(columns, [
            [badge, DBNull.Value]
        ]);

        var parser = TypeParser.GetTypeParser<DynaObject>(ref columns);

        reader.Read();
        var emp = parser.Parse(reader).Result;

        Assert.Equal(badge, emp.Get<Guid>("BadgeId"));
        Assert.Null(emp.Get<Guid?>("BadgeID#2"));
        object badge2 = Guid.NewGuid();
        emp.Set("BadgeID", badge2);
        Assert.Equal(badge2, emp[0]);
    }
    [Fact]
    public void DynaObject_ImplicitOP() {
        ColumnInfo[] columns = [
            new("Nb1", typeof(int), false),
            new("Nb2", typeof(long), true)
        ];

        using var reader = CreateReader(columns, [
            [1, 1]
        ]);

        var parser = TypeParser.GetTypeParser<DynaObject>(ref columns);

        reader.Read();
        var emp = parser.Parse(reader).Result;

        Assert.Equal(new MM(1), emp.Get<MM>("Nb1"));
        Assert.Equal(new MM(1), emp.Get<MM>("Nb2"));
    }
    [Fact]
    public void Tuple_ImplicitOP() {
        ColumnInfo[] columns = [
            new("Nb1", typeof(int), false),
            new("Nb2", typeof(long), true)
        ];

        using var reader = CreateReader(columns, [
            [1, 1]
        ]);

        var parser = TypeParser.GetTypeParser<(MM, MM)>(ref columns);

        reader.Read();
        var (nb1, nb2) = parser.Parse(reader).Result;

        Assert.Equal(new MM(1), nb1);
        Assert.Equal(new MM(1), nb2);
    }
    [Fact]
    public void Test_Casting() {
        ColumnInfo[] columns = [
            new("Amount", typeof(decimal), true),
            new("Currency", typeof(int), true),
        ];

        using var reader = CreateReader(columns, [
            [99.50m, 1],
            [DBNull.Value, 2],
            [10.00m, 3]
        ]);

        var parser = TypeParser.GetTypeParser<Price<decimal>?>(ref columns);

        reader.Read();
        var p1 = parser.Parse(reader).Result;
        Assert.True(p1.HasValue);
        Assert.Equal(99.50m, p1.Value.Amount);
        Assert.Equal(CurrencyCode.CAD, p1.Value.Currency);

        var p2 = parser.Parse(reader).Result;
        Assert.False(p2.HasValue);

        var p3 = parser.Parse(reader).Result;
        Assert.True(p3.HasValue);
        Assert.Equal(10.00m, p3.Value.Amount);
        Assert.Equal(CurrencyCode.GBP, p3.Value.Currency);
    }
    [Fact]
    public void DynaObjectInfinite() {
        ColumnInfo[] columns = [
            new("Col1", typeof(int), false),
            new("Col2", typeof(int), false),
            new("Col3", typeof(int), false),
            new("Col4", typeof(int), true),
            new("Col5", typeof(int), true),
            new("Col6", typeof(int), false),
            new("Col7", typeof(int), false),
            new("Col8", typeof(int), false),
            new("Col9", typeof(int), true),
            new("Col10", typeof(int), true),
            new("Col11", typeof(int), false),
            new("Col12", typeof(int), false),
            new("Col13", typeof(int), false),
            new("Col14", typeof(int), true),
            new("Col15", typeof(int), true),
        ];
        using var reader = CreateReader(columns, [
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]
        ]);

        var parser = TypeParser.GetTypeParser<DynaObject>(ref columns);

        reader.Read();
        var dyna = parser.Parse(reader).Result;

        Assert.Equal(1, dyna.Get<int>(0));
        Assert.Equal(2, dyna.Get<int>(1));
        Assert.Equal(3, dyna.Get<int>(2));
        Assert.Equal(4, dyna.Get<int>(3));
        Assert.Equal(5, dyna.Get<int>(4));
        Assert.Equal(6, dyna.Get<int>(5));
        Assert.Equal(7, dyna.Get<int>(6));
        Assert.Equal(8, dyna.Get<int>(7));
        Assert.Equal(9, dyna.Get<int>(8));
        Assert.Equal(10, dyna.Get<int>(9));
        Assert.Equal(11, dyna.Get<int>(10));
        Assert.Equal(12, dyna.Get<int>(11));
        Assert.Equal(13, dyna.Get<int>(12));
        Assert.Equal((sbyte)14, dyna.Get<sbyte>(13));
        Assert.Equal((long)15, dyna.Get<long>(14));
    }
    [Fact]
    public void Test_UpToKey() {
        ColumnInfo[] columns = [
            new("First", typeof(int), true),
            new("NotTooDeep", typeof(int), true),
            new("SuperDeep", typeof(int), true),
            new("TwoSemiDeep", typeof(int), true),
        ];

        using var reader = CreateReader(columns, [
            [1, 2, 3, 4]
        ]);

        var parser = TypeParser.GetTypeParser<LayerOne>(ref columns);

        reader.Read();
        var p1 = parser.Parse(reader).Result;
        Assert.Equal(1, p1.First);
        Assert.Equal(2, p1.Two.Second);
        Assert.Equal(3, p1.Two.Three.Third);
        Assert.Equal(4, p1.Two.Three.Deep);
    }
    [Fact]
    public void Test_UpToKey_Fail() {
        ColumnInfo[] columns = [
            new("First", typeof(int), true),
            new("NotTooDeep", typeof(int), true),
            new("SuperDeep", typeof(int), true),
            new("SemiDeep", typeof(int), true),
        ];

        using var reader = CreateReader(columns, [
            [1, 2, 3, 4]
        ]);

        Refusals.NoParserFor<LayerOne>(() => TypeParser.GetTypeParser<LayerOne>(ref columns));
    }
    [Fact]
    public void Test_User_Mapping_With_Nullable_And_Binary_Types() {
        var salt = new byte[] { 0x01, 0x02, 0x03 };
        var expiration = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var parser = TypeParser.GetTypeParser<UserSchema, UserSchema>(out _);

        var columns = new ColumnInfo[]
        {
            new("ID", typeof(int), false),
            new("Username", typeof(string), false),
            new("Email", typeof(string), false),
            new("Salt", typeof(byte[]), true),
            new("Token", typeof(string), true),
            new("ExpirationToken", typeof(DateTime), true),
            new("Valid", typeof(bool), false)
        };

        using var reader = CreateReader(columns, [[1, "JohnDoe", "john@example.com", salt, "secret-token", expiration, true]]);
        reader.Read();

        var result = parser.Parse(reader).Result;

        Assert.NotNull(result);
        Assert.Equal(1, result.ID);
        Assert.Equal("JohnDoe", result.Username);
        Assert.Equal(salt, result.Salt);
        Assert.Equal("secret-token", result.Token);
        Assert.Equal(expiration, result.ExpirationToken);
        Assert.True(result.Valid);
    }
    [Fact]
    public void UpdateNullColHandler_ByName_RejectsNull() {
        Assert.True(TypeParsingInfo.GetOrAdd<CfgTrackA>()
            .UpdateNullColHandler("Name", NotNullHandle.Instance));

        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Name", typeof(string), true)];
        using var reader = CreateReader(columns, [[1, DBNull.Value]]);
        var parser = TypeParser.GetTypeParser<CfgTrackA>(ref columns);

        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }
    [Fact]
    public void UpdateNullColHandler_Visitor_RejectsNull() {
        Assert.True(TypeParsingInfo.GetOrAdd<CfgTrackB>()
            .UpdateNullColHandler(slot => slot.Type == typeof(string) ? NotNullHandle.Instance : null));

        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Name", typeof(string), true)];
        using var reader = CreateReader(columns, [[1, DBNull.Value]]);
        var parser = TypeParser.GetTypeParser<CfgTrackB>(ref columns);

        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }
    [Fact]
    public void SetInvalidOnNull_Visitor_CollapsesObject() {
        Assert.True(TypeParsingInfo.GetOrAdd<CfgPackage>()
            .SetInvalidOnNull(slot => slot.Type == typeof(int) ? true : null));

        ColumnInfo[] columns = [
            new("Id", typeof(int), false),
            new("ContentsTrackingId", typeof(int), true),
            new("ContentsWeight", typeof(double), true)
        ];
        using var reader = CreateReader(columns, [[1, DBNull.Value, 2.5]]);
        var parser = TypeParser.GetTypeParser<CfgShipment>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;
        Assert.Equal(1, result.Id);
        Assert.Null(result.Contents);
    }
    [Fact]
    public void AddMember_ExternalSetter_FillsColumn() {
        Assert.True(TypeParsingInfo.GetOrAdd<CfgSecretTarget>()
            .AddMember(typeof(CfgSecretTarget).GetMethod(nameof(CfgSecretTarget.SetSecret))!));

        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Secret", typeof(string), false)];
        using var reader = CreateReader(columns, [[1, "xyz"]]);
        var parser = TypeParser.GetTypeParser<CfgSecretTarget>(ref columns);

        reader.Read();
        var result = parser.Parse(reader).Result;
        Assert.Equal(1, result.Id);
        Assert.Equal("xyz", result.Secret);
    }
}
public record LayerOne(int First, LayerTwo Two);
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")]int Third, [AltUpTo("SemiDeep", "Three")]int Deep) : IDbReadable;
public record struct DynaPair<T>([CanNotLookAnywhere] T ID, [NoName] DynaObject Object);
public record struct DynaPair2<T>(T IDAnywhere, [NoName] DynaObject Object);
public record class TestStop(int ID, string Name, string? Other = null);
public record class TestStop2(int ID, string Name, [CanLookAnywhere]string? Other = null);
public record class TestStop3([CanLookAnywhere]int ID, string Name, string? Other = null);
public record class User(int ID, string Name, [Alt("Boss")] User? Supervisor = null);
public record class User2([InvalidOnNull]int ID, string Name, [Alt("Boss")] User2? Supervisor = null);
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
public record UserSchema(int ID, string Username, string Email, byte[]? Salt, string? Token, DateTime? ExpirationToken, bool Valid);
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
public interface IPayment : IDbReadable {
    public static IPayment CreateCard(string cardNumber) => new Card(cardNumber);
    public static IPayment CreateCard(string cardNumber, string owner) => new CardDetailed(cardNumber, owner);
    public static IPayment Create([Alt("CardNumber")][Alt("Iban")]string cardNumberOrIban, string? bic) 
        => bic is null ? new Card(cardNumberOrIban) :new Transfer(cardNumberOrIban, bic);
    public static IPayment CreateTransfer(string iban, string bic) => new Transfer(iban, bic);
}

public record Card(string CardNumber) : IPayment;
public record CardDetailed(string CardNumber, string Owner) : IPayment;
public record Transfer(string Iban, string Bic) : IPayment;
public record ExternalIDPayment(int ExternalID) : IPayment;

public class Order {
    public int OrderId { get; }
    public IPayment Payment { get; }
    public Order(Order order) {
        OrderId = order.OrderId;
        Payment = order.Payment;
    }
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
public record class CfgTrackA(int Id, string Name) : IDbReadable;
public record class CfgTrackB(int Id, string Name) : IDbReadable;
public record struct CfgPackage(int TrackingId, double Weight) : IDbReadable;
public record class CfgShipment(int Id, CfgPackage? Contents) : IDbReadable;
public class CfgSecretTarget : IDbReadable {
    public int Id { get; set; }
    public string? Secret { get; private set; }
    public static void SetSecret(CfgSecretTarget target, string secret) => target.Secret = secret;
}

public record struct MM(int Amount) {
    public static implicit operator MM([NoName]int amount) => new(amount);
    public static implicit operator MM([NoName]long amount) => new((int)amount);
}
public class Wrapped<T> {
    internal Wrapped(T value) { Value = value; }
    public T Value { get; }
}
public static class WrappedFactory {
    public static Wrapped<T> Create<T>(T value) => new(value);
}