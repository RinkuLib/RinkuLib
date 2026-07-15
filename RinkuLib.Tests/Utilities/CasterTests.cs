using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="Caster"/> converts values across types at runtime: numeric bridges, nullables, enums,
/// parsing from strings, and registered custom parsers.
/// </summary>
public class CasterTests {
    [Fact]
    public void Parse_returns_the_value_when_it_already_matches() {
        object value = "text";
        Assert.Equal("text", value.Parse<string>());
    }

    [Fact]
    public void Parse_null_and_DBNull_give_the_default() {
        object? nothing = null;
        Assert.Null(nothing.Parse<string>());
        Assert.Equal(0, nothing.Parse<int>());
        Assert.Null(nothing.Parse<int?>());
        object dbNull = DBNull.Value;
        Assert.Null(dbNull.Parse<string>());
        Assert.Equal(0, dbNull.Parse<int>());
        Assert.Null(dbNull.Parse<int?>());
    }

    [Fact]
    public void Parse_converts_between_numeric_types() {
        object longValue = 42L;
        Assert.Equal(42, longValue.Parse<int>());
        Assert.Equal(42L, longValue.Parse<long>());
        Assert.Equal(42L, longValue.Parse<long?>());
        Assert.Equal(42u, longValue.Parse<uint>());
        Assert.Equal(42m, longValue.Parse<decimal>());
        object doubleValue = 1.5;
        Assert.Equal(1.5m, doubleValue.Parse<decimal>());
    }

    [Fact]
    public void Parse_converts_into_nullable_targets() {
        object longValue = 42L;
        Assert.Equal(42, longValue.Parse<int?>());
    }

    [Fact]
    public void Parse_builds_enums_from_integers_and_floats_as_object() {
        object intValue = 2;
        Assert.Equal(CastColor.Green, intValue.Parse<CastColor>());
        object doubleValue = 3.0;
        Assert.Equal(CastColor.Blue, doubleValue.Parse<CastColor>());
        object longValue = 1L;
        Assert.Equal(CastColor.Red, longValue.Parse<CastColor?>());
    }

    [Fact]
    public void Parse_builds_enums_from_integers_and_floats() {
        Assert.True(Caster.TryCast(2, out CastColor green));
        Assert.Equal(CastColor.Green, green);
        Assert.True(Caster.TryCast(3.0, out CastColor blue));
        Assert.Equal(CastColor.Blue, blue);
        Assert.True(Caster.TryCast(1L, out CastColor? red));
        Assert.Equal(CastColor.Red, red);
        Assert.True(Caster.TryCast(CastColor2.Red2, out CastColor? redr));
        Assert.Equal(CastColor.Red, redr);
    }

    [Fact]
    public void Parse_converts_strings_through_ChangeType() {
        object text = "123";
        Assert.Equal(123, text.Parse<int>());
    }

    [Fact]
    public void Parse_uses_a_registered_custom_parser() {
        Caster.AddParser((object raw, out CastWrapped r) => { r = new CastWrapped(raw.ToString()!); return true; });
        object text = "abc";
        Assert.Equal(new CastWrapped("abc"), text.Parse<CastWrapped>());
    }

    [Fact]
    public void TryCast_between_equal_types_copies() {
        Assert.True(Caster.TryCast<int, int>(5, out var same));
        Assert.Equal(5, same);
    }

    [Fact]
    public void TryCast_between_numeric_types_truncates() {
        Assert.True(Caster.TryCast<long, int>(300L, out var toInt));
        Assert.Equal(300, toInt);
        Assert.True(Caster.TryCast<double, int>(1.9, out var truncated));
        Assert.Equal(1, truncated);
        Assert.True(Caster.TryCast<int, byte>(257, out var wrapped));
        Assert.Equal((byte)1, wrapped);
    }

    [Fact]
    public void TryCast_wraps_and_unwraps_nullables() {
        Assert.True(Caster.TryCast<int, int?>(5, out var wrapped));
        Assert.Equal(5, wrapped);
        Assert.True(Caster.TryCast<int?, int>(5, out var unwrapped));
        Assert.Equal(5, unwrapped);
        Assert.False(Caster.TryCast<int?, int>(null, out _));
        Assert.True(Caster.TryCast<int?, int?>(null, out var res));
        Assert.Equal(default, res);
        Assert.True(Caster.TryCast<long?, int?>(1L, out res));
        Assert.Equal(1, res);
    }

    [Fact]
    public void TryCast_from_object_dispatches_on_the_runtime_type() {
        Assert.True(Caster.TryCast<object, int>(42L, out var fromLong));
        Assert.Equal(42, fromLong);
        Assert.True(Caster.TryCast<object, int?>(42L, out var fromLong2));
        Assert.Equal(42, fromLong2);
        Assert.True(Caster.TryCast<object, string>("s", out var fromString));
        Assert.Equal("s", fromString);
    }

    [Fact]
    public void TryCast_parses_strings_into_parsable_types() {
        Assert.True(Caster.TryCast<string, int>("17", out var parsed));
        Assert.Equal(17, parsed);
        Assert.True(Caster.TryCast<string, Guid>("11111111-2222-3333-4444-555555555555", out var guid));
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), guid);
    }

    [Fact]
    public void TryCast_to_string_uses_ToString() {
        Assert.True(Caster.TryCast<int, string>(5, out var text));
        Assert.Equal("5", text);
    }

    [Fact]
    public void TryCast_uses_implicit_operators() {
        Assert.True(Caster.TryCast<int, CastAmount>(3, out var amount));
        Assert.Equal(new CastAmount(3), amount);
    }

    [Fact]
    public void TryCast_implicit_operator_from_a_nullable_source() {
        Assert.True(Caster.TryCast<int?, CastAmount>(3, out var amount));
        Assert.Equal(new CastAmount(3), amount);
        Assert.False(Caster.TryCast<int?, CastAmount>(null, out _));
    }

    [Fact]
    public void TryCast_implicit_operator_into_a_nullable_target() {
        Assert.True(Caster.TryCast<int, CastAmount?>(3, out var amount));
        Assert.Equal(new CastAmount(3), amount);
        Assert.True(Caster.TryCast<int?, CastAmount?>(null, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void TryCast_numeric_bridges_cross_nullability() {
        Assert.True(Caster.TryCast<long?, int>(7L, out var fromNullable));
        Assert.Equal(7, fromNullable);
        Assert.False(Caster.TryCast<long?, int>(null, out _));
        Assert.True(Caster.TryCast<long, int?>(7L, out var toNullable));
        Assert.Equal(7, toNullable);
        Assert.True(Caster.TryCast<long?, int?>(null, out var bothNull));
        Assert.Null(bothNull);
    }

    [Fact]
    public void TryCast_from_object_into_a_nullable_target() {
        Assert.True(Caster.TryCast<object, int?>(42L, out var value));
        Assert.Equal(42, value);
        Assert.True(Caster.TryCast<object, int?>(null!, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void TryCast_parses_strings_into_nullable_parsable_types() {
        Assert.True(Caster.TryCast<string, int?>("17", out var parsed));
        Assert.Equal(17, parsed);
        Assert.False(Caster.TryCast<string, int?>("nope", out _));
    }

    [Fact]
    public void TryCast_reference_upcast_keeps_the_instance() {
        var wrapped = new CastWrapped("x");
        Assert.True(Caster.TryCast<CastWrapped, object>(wrapped, out var upcast));
        Assert.Same(wrapped, upcast);
    }

    [Fact]
    public void TryCast_reference_upcast_of_null_succeeds() {
        Assert.True(Caster.TryCast<CastWrapped, object>(null!, out var upcast));
        Assert.Null(upcast);
    }

    [Fact]
    public void TryCast_with_no_conversion_available_fails() {
        Assert.False(Caster.TryCast<CastUnconvertible, int>(new CastUnconvertible(), out _));
    }
    [Fact]
    public void TryCast_parses_enums_from_strings() {
        Assert.True(Caster.TryCast<string, CastColor>("Red", out var color));
        Assert.Equal(CastColor.Red, color);
        Assert.True(Caster.TryCast<object, CastColor>("Red", out var red));
        Assert.Equal(CastColor.Red, red);
        Assert.True(Caster.TryCast<object, CastColor?>("Red", out var red2));
        Assert.Equal(CastColor.Red, red2);
        Assert.True(Caster.TryCast<string, CastColor>("green", out var green));
        Assert.Equal(CastColor.Green, green);
        Assert.True(Caster.TryCast<string, CastColor?>("green", out var greenn));
        Assert.Equal(CastColor.Green, greenn);
        Assert.False(Caster.TryCast<string, CastColor?>("notThere", out _));
    }
    [Fact]
    public void TryCast_uses_custom_parser_between_specific_types() {
        Caster.AddParser<CastAmount, string>((CastAmount a, out string r) => { r = $"Number_{a.Value}"; return true; });
        Assert.True(Caster.TryCast<CastAmount, string>(new CastAmount(42), out var result));
        Assert.Equal("Number_42", result);
    }

    [Fact]
    public void TryCast_uses_custom_parser_for_complex_types() {
        Caster.AddParser<CastWrapped, int>((CastWrapped w, out int r) => { r = w.Raw.Length; return true; });

        var wrapped = new CastWrapped("12345");
        Assert.True(Caster.TryCast<CastWrapped, int>(wrapped, out var length));
        Assert.Equal(5, length);
    }

    [Fact]
    public void TryCast_fails_on_invalid_enum_strings() {
        Assert.False(Caster.TryCast<string, CastColor>("NotAColor", out _));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData((byte)5, 5)]
    [InlineData('A', 65)]
    [InlineData("123", 123)]
    [InlineData((short)5, 5)]
    [InlineData(5, 5L)]
    [InlineData(5L, 5)]
    [InlineData(5f, 5)]
    [InlineData(5d, 5)]
    [InlineData((sbyte)5, 5)]
    [InlineData((ushort)5, 5)]
    [InlineData(5u, 5)]
    [InlineData(5ul, 5)]
    public void Object_dispatch_covers_primitive<T>(T expected, object input) {
        bool success = Caster.TryCast(input, out T? result);
        Assert.True(success, $"Failed to cast {input.GetType()} to {typeof(T)}");
        Assert.Equal(expected, result);
    }
    [Fact]
    public void Object_dispatch_covers_decimal() {
        const decimal expected = 5m;
        const int input = 5;
        bool success = Caster.TryCast(input, out decimal result);
        Assert.True(success);
        Assert.Equal(expected, result);
    }
    [Fact]
    public void Object_dispatch_covers_class_to_string() {
        const string expected = "string";
        object input = new Wrap("string");
        bool success = Caster.TryCast(input, out string? result);
        Assert.True(success);
        Assert.Equal(expected, result);
    }
    [Fact]
    public void Object_dispatch_covers_class_to_guid() {
        var expected = Guid.NewGuid();
        object input = new Wrap(expected.ToString());
        bool success = Caster.TryCast(input, out Guid result);
        Assert.True(success);
        Assert.Equal(expected, result);
    }
    class Wrap(string str) {
        public override string ToString() => str;
    }
    [Fact]
    public void Object_dispatch_to_DateTime_parses_from_a_string() {
        var stamp = new DateTime(2024, 5, 1);
        Assert.True(Caster.TryCast<object, DateTime>("2024-05-01", out var dt));
        Assert.Equal(stamp, dt);
    }

    [Fact]
    public void Object_dispatch_falls_back_to_ToType_for_an_unsupported_target() {
        Assert.False(Caster.TryCast<object, Int128>(5, out _));
    }

    [Fact]
    public void Object_dispatch_failure_is_swallowed() {
        Assert.False(Caster.TryCast<object, int>("abc", out _));
        Assert.False(Caster.TryCast<object, int?>("abc", out _));
    }

    [Fact]
    public void Object_to_non_convertible_target_fails() {
        Assert.False(Caster.TryCast<object, CastUnconvertible>(5, out _));
    }
    [Fact]
    public void Parse_throws_when_no_conversion_exists() {
        object value = 5;
        Assert.ThrowsAny<Exception>(value.Parse<CastUnconvertible>);
    }
    [Fact]
    public void Parse_guid_from_every_shape() {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, ((object)guid).Parse<Guid>());
        Assert.Equal(guid, ((object)guid.ToString()).Parse<Guid>());
        Assert.Equal(guid, ((object)guid.ToByteArray()).Parse<Guid>());
    }

    [Fact]
    public void Parse_guid_throws_exception_for_invalid_input() {
        object value = 123;
        Assert.ThrowsAny<Exception>(() => value.Parse<Guid>());
    }

    [Fact]
    public void Parse_datetimeoffset_and_timespan_from_strings() {
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero), ((object)"2024-05-01T00:00:00+00:00").Parse<DateTimeOffset>());
        Assert.Equal(TimeSpan.FromMinutes(90), ((object)"01:30:00").Parse<TimeSpan>());
    }

    [Fact]
    public void Parse_string_from_any_object_uses_ToString() {
        Assert.Equal("5", ((object)5).Parse<string>());
        Assert.Equal("text", ((object)"text").Parse<string>());
    }
    [Fact]
    public void Object_to_enum_from_a_floating_value() {
        Assert.True(Caster.TryCast<object, CastColor>(2.0, out var color));
        Assert.Equal(CastColor.Green, color);
        Assert.True(Caster.TryCast<object, CastColor>(3.0m, out var dec));
        Assert.Equal(CastColor.Blue, dec);
    }

    [Fact]
    public void Object_to_enum_from_an_invalid_string_fails() {
        Assert.False(Caster.TryCast<object, CastColor>("Nope", out _));
    }

    [Fact]
    public void Object_to_enum_from_an_incompatible_value_fails() {
        Assert.False(Caster.TryCast<object, CastColor>(DateTime.Now, out _));
    }

    [Fact]
    public void Object_to_nullable_enum_from_an_invalid_string_fails() {
        Assert.False(Caster.TryCast<object, CastColor?>("Nope", out _));
    }
    [Fact]
    public void Generic_math_types_are_treated_as_numbers() {
        Assert.True(Caster.TryCast<Half, int>((Half)3, out var toInt));
        Assert.Equal(3, toInt);
        Assert.True(Caster.TryCast<int, Half>(3, out var toHalf));
        Assert.Equal((Half)3, toHalf);
        Assert.True(Caster.TryCast<Int128, int>(3, out var fromI128));
        Assert.Equal(3, fromI128);
    }

    [Fact]
    public void A_value_type_that_shares_a_type_code_but_is_not_a_number_is_rejected() {
        Assert.False(Caster.TryCast<DateTime, int>(DateTime.Now, out _));
    }
    [Fact]
    public void A_plain_struct_with_no_conversion_is_rejected() {
        Assert.False(Caster.TryCast<CastPlain, int>(new CastPlain(), out _));
    }
    [Fact]
    public void String_to_a_non_parsable_non_enum_type_fails() {
        Assert.False(Caster.TryCast<string, CastWrapped>("x", out _));
    }
    [Fact]
    public void Operator_into_a_nullable_target_with_a_value() {
        Assert.True(Caster.TryCast<int?, CastAmount?>(3, out var amount));
        Assert.Equal(new CastAmount(3), amount);
    }
    [Fact]
    public void Value_type_boxes_up_to_object_and_to_an_implemented_interface() {
        Assert.True(Caster.TryCast<int, object>(5, out var boxed));
        Assert.Equal(5, boxed);
        Assert.True(Caster.TryCast<int, IComparable>(5, out var iface));
        Assert.Equal(5, iface);
    }
    [Fact]
    public void Non_numeric_struct_wraps_and_unwraps_its_nullable() {
        var guid = Guid.NewGuid();
        Assert.True(Caster.TryCast<Guid, Guid?>(guid, out var wrapped));
        Assert.Equal(guid, wrapped);
        Assert.True(Caster.TryCast<Guid?, Guid>(guid, out var unwrapped));
        Assert.Equal(guid, unwrapped);
        Assert.False(Caster.TryCast<Guid?, Guid>(null, out _));
    }
    [Fact]
    public void Custom_convertible_Should_Convert() {
        Assert.True(Caster.TryCast<SimpleConvertible, int>(new(), out var nb));
        Assert.Equal(123, nb);
    }
    [Fact]
    public void Non_matching_objects_should_fail() {
        Assert.False(Caster.TryCast<CastUnconvertible, List<int>>(new(), out _));
    }
}

public struct CastPlain;

public enum CastColor { Red = 1, Green = 2, Blue = 3 }
public enum CastColor2 { Red2 = 1 }
public record CastWrapped(string Raw);
public sealed class CastUnconvertible;
public record struct CastAmount(int Value) {
    public static implicit operator CastAmount(int value) => new(value);
}
public class SimpleConvertible : IConvertible {
    public TypeCode GetTypeCode() => TypeCode.Object;
    public bool ToBoolean(IFormatProvider? p) => false;
    public byte ToByte(IFormatProvider? p) => 0;
    public char ToChar(IFormatProvider? p) => 'a';
    public DateTime ToDateTime(IFormatProvider? p) => DateTime.MinValue;
    public decimal ToDecimal(IFormatProvider? p) => 0m;
    public double ToDouble(IFormatProvider? p) => 0.0;
    public short ToInt16(IFormatProvider? p) => 0;
    public int ToInt32(IFormatProvider? p) => 123;
    public long ToInt64(IFormatProvider? p) => 0;
    public sbyte ToSByte(IFormatProvider? p) => 0;
    public float ToSingle(IFormatProvider? p) => 0f;
    public string ToString(IFormatProvider? p) => "123";
    public ushort ToUInt16(IFormatProvider? p) => 0;
    public uint ToUInt32(IFormatProvider? p) => 0;
    public ulong ToUInt64(IFormatProvider? p) => 0;
    public object ToType(Type conversionType, IFormatProvider? p) => 123;
}