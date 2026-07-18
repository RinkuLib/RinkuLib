using System.Reflection;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// How the engine picks a way to build <c>T</c>: constructor overloads and static factories compete
/// on the columns they can satisfy, and the registration API adds or adjusts candidates at runtime.
/// </summary>
public class ConstructionSelectionTests {
    [Fact]
    public void Factory_overload_is_chosen_by_the_columns_it_satisfies() {
        ColumnInfo[] cols = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), false),
        ];
        var order = Rows.ParseOne<Purchase>(cols, 99, "DE123456789", "GENEDEBK");
        Assert.Equal(99, order.OrderId);
        var transfer = Assert.IsType<WireTransfer>(order.Payment);
        Assert.Equal("DE123456789", transfer.Iban);
        Assert.Equal("GENEDEBK", transfer.Bic);
    }

    [Fact]
    public void More_specific_overload_wins_when_more_columns_match() {
        ColumnInfo[] cols = [
            new("OrderID", typeof(int), false),
            new("PaymentCardNumber", typeof(string), false),
            new("PaymentOwner", typeof(string), false),
        ];
        var order = Rows.ParseOne<Purchase>(cols, 321, "1234 5678 9012 3456", "John Smith");
        var card = Assert.IsType<NamedCard>(order.Payment);
        Assert.Equal("1234 5678 9012 3456", card.CardNumber);
        Assert.Equal("John Smith", card.Owner);
    }

    [Fact]
    public void One_factory_can_return_different_shapes_per_row() {
        ColumnInfo[] cols = [
            new("OrderID", typeof(int), false),
            new("PaymentIban", typeof(string), false),
            new("PaymentBic", typeof(string), true),
        ];
        using var reader = Rows.Reader(cols,
            [99, "DE123456789", "GENEDEBK"],
            [100, "1234 5678 9012 3456", DBNull.Value]);
        var parser = TypeParser.GetTypeParser<Purchase>(ref cols);
        reader.Read();

        var withBic = parser.Parse(reader).Result;
        Assert.IsType<WireTransfer>(withBic.Payment);

        var withoutBic = parser.Parse(reader).Result;
        var card = Assert.IsType<BareCard>(withoutBic.Payment);
        Assert.Equal("1234 5678 9012 3456", card.CardNumber);
    }

    [Fact]
    public void Manually_registered_constructor_becomes_a_candidate() {
        Assert.True(TypeParsingInfo.GetOrAdd<IPayKind>().AddPossibleConstruction(
            typeof(ExternalPayment).GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(int)])
            ?? throw new InvalidOperationException("constructor not found")));

        ColumnInfo[] cols = [
            new("OrderID", typeof(int), false),
            new("PaymentExternalID", typeof(int), false),
        ];
        var order = Rows.ParseOne<Purchase>(cols, 99, 14532);
        var external = Assert.IsType<ExternalPayment>(order.Payment);
        Assert.Equal(14532, external.ExternalID);
    }

    [Fact]
    public void Open_generic_factory_serves_every_closed_form() {
        TypeParsingInfo.GetOrAdd(typeof(Boxed<>))
            .AddPossibleConstruction(typeof(BoxedFactory).GetMethod(nameof(BoxedFactory.Create))!);

        ColumnInfo[] intCols = [new("Value", typeof(int), false)];
        Assert.Equal(7, Rows.ParseOne<Boxed<int>>(intCols, 7).Value);

        ColumnInfo[] strCols = [new("Value", typeof(string), false)];
        Assert.Equal("hi", Rows.ParseOne<Boxed<string>>(strCols, "hi").Value);
    }

    [Fact]
    public void UpdateNullColHandler_by_name_rejects_null() {
        Assert.True(TypeParsingInfo.GetOrAdd<StrictName>().UpdateNullColHandler("Name", NotNullHandle.Instance));
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), true)];
        Assert.Throws<NullValueAssignmentException>(() => Rows.ParseOne<StrictName>(cols, 1, DBNull.Value));
    }

    [Fact]
    public void UpdateNullColHandler_by_visitor_rejects_null() {
        Assert.True(TypeParsingInfo.GetOrAdd<StrictNameVisitor>()
            .UpdateNullColHandler(slot => slot.Type == typeof(string) ? NotNullHandle.Instance : null));
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), true)];
        Assert.Throws<NullValueAssignmentException>(() => Rows.ParseOne<StrictNameVisitor>(cols, 1, DBNull.Value));
    }

    [Fact]
    public void SetInvalidOnNull_by_visitor_collapses_the_object() {
        Assert.True(TypeParsingInfo.GetOrAdd<CollapsingParcel>()
            .SetInvalidOnNull(slot => slot.Type == typeof(int) ? true : null));
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("ContentsTrackingId", typeof(int), true),
            new("ContentsWeight", typeof(double), true),
        ];
        var shipment = Rows.ParseOne<CollapsingShipment>(cols, 1, DBNull.Value, 2.5);
        Assert.Equal(1, shipment.Id);
        Assert.Null(shipment.Contents);
    }

    [Fact]
    public void AddMember_registers_an_external_setter_for_a_column() {
        Assert.True(TypeParsingInfo.GetOrAdd<SecretHolder>()
            .AddMember(typeof(SecretHolder).GetMethod(nameof(SecretHolder.SetSecret))!));
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Secret", typeof(string), false)];
        var holder = Rows.ParseOne<SecretHolder>(cols, 1, "xyz");
        Assert.Equal(1, holder.Id);
        Assert.Equal("xyz", holder.Secret);
    }

    [Fact]
    public void Runtime_fallback_makes_a_required_slot_optional() {
        if (TypeParsingInfo.GetOrAdd<FallbackTrack>() is ICanProvideConstructions info) {
            var slots = info.PossibleConstructors[0].Parameters;
            var slot = slots[2];
            slots[2] = new ParamInfoPlus(slot.Type, slot.NullColHandler, slot.NameComparer,
                IColModifier.Nothing, DefaultValueFallback.Instance);
        }
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var track = Rows.ParseOne<FallbackTrack>(cols, 1, "x");
        Assert.Equal(1, track.Id);
        Assert.Equal("x", track.Name);
        Assert.Equal(0, track.Code);
    }

    [Fact]
    public void CtorTypeInfo_uses_the_marked_constructor_positionally() {
        TypeParsingInfo.AddOrSet<PickyBuilt>(CtorTypeInfo.Instance);
        ColumnInfo[] cols = [new("Anything", typeof(int), false)];
        var picked = Rows.ParseOne<PickyBuilt>(cols, 5);
        Assert.True(picked.CameFromMarkedCtor);
        Assert.Equal(5, picked.Id);
    }

    [Fact]
    public void CanCompleteWithMembers_hydrates_leftover_columns() {
        ColumnInfo[] cols = [
            new("Value", typeof(int), false),
            new("Source", typeof(string), true),
        ];
        var annotated = Rows.ParseOne<Annotated<int, string>>(cols, 3, "manual");
        Assert.Equal(3, annotated.Value);
        Assert.Equal("manual", annotated.Source);
    }
}

public interface IPayKind : IDbReadable {
    public static IPayKind CreateCard(string cardNumber) => new BareCard(cardNumber);
    public static IPayKind CreateCard(string cardNumber, string owner) => new NamedCard(cardNumber, owner);
    public static IPayKind Create([Alt("CardNumber")][Alt("Iban")] string cardNumberOrIban, string? bic)
        => bic is null ? new BareCard(cardNumberOrIban) : new WireTransfer(cardNumberOrIban, bic);
    public static IPayKind CreateTransfer(string iban, string bic) => new WireTransfer(iban, bic);
}
public record BareCard(string CardNumber) : IPayKind;
public record NamedCard(string CardNumber, string Owner) : IPayKind;
public record WireTransfer(string Iban, string Bic) : IPayKind;
public record ExternalPayment(int ExternalID) : IPayKind;

public class Purchase {
    public int OrderId { get; }
    public IPayKind Payment { get; }
    public Purchase(Purchase other) {
        OrderId = other.OrderId;
        Payment = other.Payment;
    }
    public static Purchase Create(int orderId, IPayKind payment) => new(orderId, payment);
    private Purchase(int orderId, IPayKind payment) {
        OrderId = orderId;
        Payment = payment;
    }
}

public class Boxed<T> {
    internal Boxed(T value) { Value = value; }
    public T Value { get; }
}
public static class BoxedFactory {
    public static Boxed<T> Create<T>(T value) => new(value);
}

public record class StrictName(int Id, string Name) : IDbReadable;
public record class StrictNameVisitor(int Id, string Name) : IDbReadable;
public record struct CollapsingParcel(int TrackingId, double Weight) : IDbReadable;
public record class CollapsingShipment(int Id, CollapsingParcel? Contents) : IDbReadable;
public class SecretHolder : IDbReadable {
    public int Id { get; set; }
    public string? Secret { get; private set; }
    public static void SetSecret(SecretHolder target, string secret) => target.Secret = secret;
}
public record struct FallbackTrack(int Id, string Name, int Code);
public class PickyBuilt {
    public bool CameFromMarkedCtor;
    public int Id;
    public string? Name;
    public PickyBuilt(int id, string name) {
        Id = id;
        Name = name;
    }
    [DbConstructor]
    public PickyBuilt(int id) {
        Id = id;
        CameFromMarkedCtor = true;
    }
}
