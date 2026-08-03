using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// Values written as parameters come back intact when read: the parameter binding and the result
/// conversion agree on each type's representation.
/// </summary>
public class RoundTripTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand Echo = new("SELECT @v AS V");

    [Fact]
    public void String_round_trips() {
        using var cnn = Db.GetConnection();
        Assert.Equal("héllo wörld", Echo.Query<string>(cnn, new { v = "héllo wörld" }));
    }

    [Fact]
    public void Integers_round_trip() {
        using var cnn = Db.GetConnection();
        Assert.Equal(42, Echo.Query<int>(cnn, new { v = 42 }));
        Assert.Equal(long.MaxValue, Echo.Query<long>(cnn, new { v = long.MaxValue }));
        Assert.Equal((short)-5, Echo.Query<short>(cnn, new { v = (short)-5 }));
        Assert.Equal((byte)255, Echo.Query<byte>(cnn, new { v = (byte)255 }));
    }

    [Fact]
    public void Floating_point_round_trips() {
        using var cnn = Db.GetConnection();
        Assert.Equal(0.1, Echo.Query<double>(cnn, new { v = 0.1 }));
        Assert.Equal(1.25f, Echo.Query<float>(cnn, new { v = 1.25f }));
    }

    [Fact]
    public void Bool_round_trips() {
        using var cnn = Db.GetConnection();
        Assert.True(Echo.Query<bool>(cnn, new { v = true }));
        Assert.False(Echo.Query<bool>(cnn, new { v = false }));
    }

    [Fact]
    public void Guid_round_trips() {
        var guid = Guid.NewGuid();
        using var cnn = Db.GetConnection();
        Assert.Equal(guid, Echo.Query<Guid>(cnn, new { v = guid }));
    }

    [Fact]
    public void Byte_array_round_trips() {
        var blob = new byte[] { 1, 2, 3, 255 };
        using var cnn = Db.GetConnection();
        Assert.Equal(blob, Echo.Query<byte[]>(cnn, new { v = blob }));
    }

    [Fact]
    public void DateTime_round_trips() {
        var stamp = new DateTime(2024, 5, 1, 13, 30, 15);
        using var cnn = Db.GetConnection();
        Assert.Equal(stamp, Echo.Query<DateTime>(cnn, new { v = stamp }));
    }

    [Fact]
    public void Decimal_round_trips() {
        using var cnn = Db.GetConnection();
        Assert.Equal(11.884m, Echo.Query<decimal>(cnn, new { v = 11.884m }));
    }

    [Fact]
    public void Enum_parameter_round_trips_to_its_numeric_value() {
        using var cnn = Db.GetConnection();
        Assert.Equal(RoundTripKind.Second, Echo.Query<RoundTripKind>(cnn, new { v = RoundTripKind.Second }));
    }

    [Fact]
    public void Nullable_parameter_with_a_value_round_trips() {
        using var cnn = Db.GetConnection();
        int? present = 7;
        Assert.Equal(7, Echo.Query<int?>(cnn, new { v = present }));
    }

    [Fact]
    public void Char_round_trips() {
        using var cnn = Db.GetConnection();
        Assert.Equal('ą', Echo.Query<char>(cnn, new { v = 'ą' }));
    }
}

public enum RoundTripKind { First = 1, Second = 2 }
