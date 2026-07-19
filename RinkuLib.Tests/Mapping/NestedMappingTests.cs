using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Complex members map from prefixed columns (<c>ContentsWeight</c> fills <c>Contents.Weight</c>),
/// and <c>[InvalidOnNull]</c> collapses a whole nested object to null when its key column is null.
/// </summary>
public class NestedMappingTests {
    private static readonly ColumnInfo[] ShipmentCols = [
        new("ShipmentID", typeof(int), false),
        new("ContentsTrackingID", typeof(int), true),
        new("ContentsWeight", typeof(double), true),
        new("RoutingServiceLevel", typeof(string), false),
        new("RoutingNotes", typeof(string), true),
    ];

    [Fact]
    public void Prefixed_columns_fill_nested_members() {
        var shipment = Rows.ParseOne<Delivery>(ShipmentCols, 100, 555, 1.5, "Overnight", "Fragile");
        Assert.Equal(100, shipment.ShipmentId);
        Assert.NotNull(shipment.Contents);
        Assert.Equal(555, shipment.Contents.Value.TrackingId);
        Assert.Equal(1.5, shipment.Contents.Value.Weight);
        Assert.Equal("Overnight", shipment.Routing.ServiceLevel);
        Assert.Equal("Fragile", shipment.Routing.Notes);
    }

    [Fact]
    public void InvalidOnNull_collapses_the_nested_struct_to_null() {
        var shipment = Rows.ParseOne<Delivery>(ShipmentCols, 200, DBNull.Value, 0.0, "Ground", DBNull.Value);
        Assert.Equal(200, shipment.ShipmentId);
        Assert.Null(shipment.Contents);
        Assert.Equal("Ground", shipment.Routing.ServiceLevel);
        Assert.Null(shipment.Routing.Notes);
    }

    [Fact]
    public void Recursive_type_maps_prefix_chains() {
        ColumnInfo[] cols = [
            new("ID", typeof(int), false),
            new("Name", typeof(string), false),
            new("SupervisorID", typeof(int), false),
            new("SupervisorName", typeof(string), false),
            new("SupervisorBossID", typeof(int), false),
            new("SupervisorBossName", typeof(string), false),
        ];
        var worker = Rows.ParseOne<Staff>(cols, 3, "Roger", 2, "Victor", 1, "Albert");
        Assert.Equal(3, worker.ID);
        Assert.Equal("Roger", worker.Name);
        Assert.NotNull(worker.Supervisor);
        Assert.Equal(2, worker.Supervisor.ID);
        Assert.Equal("Victor", worker.Supervisor.Name);
        Assert.NotNull(worker.Supervisor.Supervisor);
        Assert.Equal(1, worker.Supervisor.Supervisor.ID);
        Assert.Equal("Albert", worker.Supervisor.Supervisor.Name);
    }

    [Fact]
    public void Recursive_type_stops_where_the_key_is_null() {
        ColumnInfo[] cols = [
            new("ID", typeof(int), false),
            new("Name", typeof(string), false),
            new("SupervisorID", typeof(int), true),
            new("SupervisorName", typeof(string), true),
            new("SupervisorBossID", typeof(int), true),
            new("SupervisorBossName", typeof(string), true),
        ];
        var worker = Rows.ParseOne<GuardedStaff>(cols, 3, "Roger", 2, "Victor", DBNull.Value, DBNull.Value);
        Assert.Equal(3, worker.ID);
        Assert.NotNull(worker.Supervisor);
        Assert.Equal(2, worker.Supervisor.ID);
        Assert.Null(worker.Supervisor.Supervisor);
    }

    [Fact]
    public void Null_deep_in_a_required_chain_throws() {
        ColumnInfo[] cols = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
        ];
        using var reader = Rows.Reader(cols, [500, 400, 300, "Name"], [500, 400, DBNull.Value, "Name"]);
        var parser = TypeParser.GetTypeParser<ChainTop>(ref cols);
        reader.Read();
        var top = parser.Parse(reader).Result;
        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Equal(300, top.Middle.Bottom.ID);
        Assert.Equal("Name", top.Middle.Bottom.Name);
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }

    [Fact]
    public void Null_key_collapses_only_the_nullable_level() {
        ColumnInfo[] cols = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
        ];
        var top = Rows.ParseOne<ChainTopNullableBottom>(cols, 500, 400, DBNull.Value, "Name");
        Assert.Equal(500, top.ID);
        Assert.Equal(400, top.Middle.ID);
        Assert.Null(top.Middle.Bottom);
    }

    [Fact]
    public void InvalidOnNull_deep_in_the_chain_collapses_upward() {
        ColumnInfo[] cols = [
            new("id", typeof(int), false),
            new("MiddleID", typeof(int), false),
            new("MIddleBottomID", typeof(int), true),
            new("MIddleBottomName", typeof(string), false),
        ];
        var top = Rows.ParseOne<ChainTopCollapsing>(cols, 500, 400, DBNull.Value, "Name");
        Assert.Equal(500, top.ID);
        Assert.Null(top.Middle);
    }

    [Fact]
    public void Generic_nested_types_map_with_their_closed_arguments() {
        ColumnInfo[] cols = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(double), false),
            new("ListingPriceCurrency", typeof(byte), false),
            new("InfoValue", typeof(int), false),
            new("InfoSource", typeof(string), true),
        ];
        var boxed = Rows.ParseOne<CrateOf<double, int>>(cols, 500, 12.50, (byte)Money.CAD, 42, "API");
        Assert.NotNull(boxed.ListingPrice);
        Assert.Equal(12.50, boxed.ListingPrice.Value.Amount);
        Assert.Equal(42, boxed.Info.Value);
        Assert.Equal("API", boxed.Info.Source);
    }

    [Fact]
    public void Generic_nested_struct_collapses_on_null_and_members_hydrate() {
        ColumnInfo[] cols = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(decimal), true),
            new("ListingPriceCurrency", typeof(int), true),
            new("InfoValue", typeof(int), false),
            new("InfoSource", typeof(string), true),
        ];
        using var reader = Rows.Reader(cols,
            [101, 99.50m, 1, 500, "Warehouse_Alpha"],
            [102, DBNull.Value, 2, 600, "Warehouse_Beta"],
            [103, 10.00m, 3, 700, DBNull.Value]);
        var parser = TypeParser.GetTypeParser<CrateOf<decimal, int>>(ref cols);
        reader.Read();

        var full = parser.Parse(reader).Result;
        Assert.Equal(101, full.ProductId);
        Assert.Equal(99.50m, full.ListingPrice!.Value.Amount);
        Assert.Equal(Money.CAD, full.ListingPrice.Value.Currency);
        Assert.Equal(500, full.Info.Value);
        Assert.Equal("Warehouse_Alpha", full.Info.Source);

        var collapsed = parser.Parse(reader).Result;
        Assert.Equal(102, collapsed.ProductId);
        Assert.Null(collapsed.ListingPrice);
        Assert.Equal(600, collapsed.Info.Value);

        var partial = parser.Parse(reader).Result;
        Assert.Equal(103, partial.ProductId);
        Assert.Equal(Money.GBP, partial.ListingPrice!.Value.Currency);
        Assert.Null(partial.Info.Source);
    }

    [Fact]
    public void NotNull_on_a_generic_parameter_rejects_null() {
        ColumnInfo[] cols = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(double), false),
            new("ListingPriceCurrency", typeof(int), false),
            new("InfoValue", typeof(string), true),
        ];
        using var reader = Rows.Reader(cols, [201, 15.0, 2, "Trusted"], [202, 15.0, 1, DBNull.Value]);
        var parser = TypeParser.GetTypeParser<CrateOf<double, string>>(ref cols);
        reader.Read();
        var ok = parser.Parse(reader).Result;
        Assert.Equal("Trusted", ok.Info.Value);
        Assert.Null(ok.Info.Source);
        var refused = Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
        Assert.Contains("Value", refused.Message);
    }

    [Fact]
    public void Non_nullable_generic_root_with_null_key_throws() {
        ColumnInfo[] cols = [
            new("ProductId", typeof(int), false),
            new("ListingPriceAmount", typeof(decimal), true),
            new("ListingPriceCurrency", typeof(byte), true),
            new("InfoValue", typeof(string), false),
            new("InfoSource", typeof(string), true),
        ];
        using var reader = Rows.Reader(cols, [1, 99.99m, DBNull.Value, "Premium", "A"]);
        var parser = TypeParser.GetTypeParser<CrateOf<decimal, string>>(ref cols);
        reader.Read();
        Assert.Throws<NullValueAssignmentException>(() => parser.Parse(reader));
    }

    [Fact]
    public void Nullable_struct_root_reads_value_or_null_per_row() {
        ColumnInfo[] cols = [
            new("Amount", typeof(decimal), true),
            new("Currency", typeof(int), true),
        ];
        using var reader = Rows.Reader(cols, [99.50m, 1], [DBNull.Value, 2], [10.00m, 3]);
        var parser = TypeParser.GetTypeParser<Cost<decimal>?>(ref cols);
        reader.Read();

        var first = parser.Parse(reader).Result;
        Assert.True(first.HasValue);
        Assert.Equal(99.50m, first.Value.Amount);
        Assert.Equal(Money.CAD, first.Value.Currency);

        var second = parser.Parse(reader).Result;
        Assert.False(second.HasValue);

        var third = parser.Parse(reader).Result;
        Assert.Equal(Money.GBP, third!.Value.Currency);
    }
}

public record struct Parcel([InvalidOnNull] int TrackingId, double Weight) : IDbReadable;
public class RoutingLabel : IDbReadable {
    [NotNull] public string ServiceLevel { get; set; } = null!;
    public string? Notes { get; set; }
}
public class Delivery(int shipmentId, Parcel? contents, RoutingLabel routing) : IDbReadable {
    public int ShipmentId { get; } = shipmentId;
    public Parcel? Contents { get; } = contents;
    public RoutingLabel Routing { get; } = routing;
}
public record class Staff(int ID, string Name, [Alt("Boss")] Staff? Supervisor = null);
public record class GuardedStaff([InvalidOnNull] int ID, string Name, [Alt("Boss")] GuardedStaff? Supervisor = null);
public record class ChainTop(int ID, ChainMiddle Middle) : IDbReadable;
public record class ChainMiddle(int ID, ChainBottom Bottom) : IDbReadable;
public record class ChainTopNullableBottom(int ID, ChainMiddleNullable Middle) : IDbReadable;
public record class ChainMiddleNullable(int ID, ChainBottom? Bottom) : IDbReadable;
public record class ChainTopCollapsing(int ID, ChainMiddleCollapsing Middle) : IDbReadable;
public record class ChainMiddleCollapsing(int ID, [InvalidOnNull] ChainBottom Bottom) : IDbReadable;
public record struct ChainBottom([InvalidOnNull] int ID, string Name) : IDbReadable;

public enum Money { CAD = 1, EUR = 2, GBP = 3 }
public record struct Cost<T>([InvalidOnNull] T Amount, Money Currency) : IDbReadable where T : struct;
[method: CanCompleteWithMembers]
public class Annotated<T, TSource>([NotNull] T Value) : IDbReadable where T : notnull {
    public T Value { get; } = Value;
    public TSource? Source { get; set; }
}
public class CrateOf<TAmount, TMeta>(int productId, Cost<TAmount>? listingPrice, Annotated<TMeta, string> info)
    where TAmount : struct where TMeta : notnull {
    public int ProductId { get; } = productId;
    public Cost<TAmount>? ListingPrice { get; } = listingPrice;
    public Annotated<TMeta, string> Info { get; } = info;
}
