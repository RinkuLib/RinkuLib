using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// The full enum conversion matrix through <see cref="Caster"/>: enum to/from other enums, to/from
/// numbers, to/from strings, each across the four nullability combinations.
/// </summary>
public class CasterEnumTests {

    [Fact]
    public void Enum_to_enum_by_underlying_value() {
        Assert.True(Caster.TryCast<ByteKind, IntKind>(ByteKind.Two, out var r));
        Assert.Equal(IntKind.Two, r);
    }

    [Fact]
    public void Enum_to_enum_across_nullability() {
        Assert.True(Caster.TryCast<ByteKind?, IntKind>(ByteKind.Two, out var fromNull));
        Assert.Equal(IntKind.Two, fromNull);
        Assert.False(Caster.TryCast<ByteKind?, IntKind>(null, out _));

        Assert.True(Caster.TryCast<ByteKind, IntKind?>(ByteKind.Two, out var toNull));
        Assert.Equal(IntKind.Two, toNull);

        Assert.True(Caster.TryCast<ByteKind?, IntKind?>(ByteKind.Two, out var bothValue));
        Assert.Equal(IntKind.Two, bothValue);
        Assert.True(Caster.TryCast<ByteKind?, IntKind?>(null, out var bothNull));
        Assert.Null(bothNull);
    }

    [Fact]
    public void Numeric_to_enum_by_value() {
        Assert.True(Caster.TryCast<int, IntKind>(2, out var r));
        Assert.Equal(IntKind.Two, r);
        Assert.True(Caster.TryCast<long, IntKind>(3L, out var fromLong));
        Assert.Equal(IntKind.Three, fromLong);
        Assert.True(Caster.TryCast<double, IntKind>(1.0, out var fromDouble));
        Assert.Equal(IntKind.One, fromDouble);
    }

    [Fact]
    public void Numeric_to_enum_across_nullability() {
        Assert.True(Caster.TryCast<int?, IntKind>(2, out var fromNull));
        Assert.Equal(IntKind.Two, fromNull);
        Assert.False(Caster.TryCast<int?, IntKind>(null, out _));

        Assert.True(Caster.TryCast<int, IntKind?>(2, out var toNull));
        Assert.Equal(IntKind.Two, toNull);

        Assert.True(Caster.TryCast<int?, IntKind?>(null, out var bothNull));
        Assert.Null(bothNull);
    }


    [Fact]
    public void Enum_to_numeric_by_value() {
        Assert.True(Caster.TryCast<IntKind, int>(IntKind.Two, out var toInt));
        Assert.Equal(2, toInt);
        Assert.True(Caster.TryCast<IntKind, long>(IntKind.Three, out var toLong));
        Assert.Equal(3L, toLong);
        Assert.True(Caster.TryCast<IntKind, byte>(IntKind.One, out var toByte));
        Assert.Equal((byte)1, toByte);
        Assert.True(Caster.TryCast<ByteKind, int>(ByteKind.Two, out var byteEnumToInt));
        Assert.Equal(2, byteEnumToInt);
    }

    [Fact]
    public void Enum_to_numeric_across_nullability() {
        Assert.True(Caster.TryCast<IntKind?, int>(IntKind.Two, out var fromNull));
        Assert.Equal(2, fromNull);
        Assert.False(Caster.TryCast<IntKind?, int>(null, out _));

        Assert.True(Caster.TryCast<IntKind, int?>(IntKind.Two, out var toNull));
        Assert.Equal(2, toNull);

        Assert.True(Caster.TryCast<IntKind?, int?>(IntKind.Two, out var bothValue));
        Assert.Equal(2, bothValue);
        Assert.True(Caster.TryCast<IntKind?, int?>(null, out var bothNull));
        Assert.Null(bothNull);
    }

    [Fact]
    public void String_to_enum_by_name_case_insensitive() {
        Assert.True(Caster.TryCast<string, IntKind>("Two", out var byName));
        Assert.Equal(IntKind.Two, byName);
        Assert.True(Caster.TryCast<string, IntKind>("three", out var lower));
        Assert.Equal(IntKind.Three, lower);
    }

    [Fact]
    public void String_to_enum_by_numeric_text() {
        Assert.True(Caster.TryCast<string, IntKind>("2", out var byNumber));
        Assert.Equal(IntKind.Two, byNumber);
    }

    [Fact]
    public void String_to_enum_into_nullable() {
        Assert.True(Caster.TryCast<string, IntKind?>("Two", out var value));
        Assert.Equal(IntKind.Two, value);
        Assert.False(Caster.TryCast<string, IntKind?>("nope", out _));
    }

    [Fact]
    public void String_to_enum_rejects_unknown_names() {
        Assert.False(Caster.TryCast<string, IntKind>("Nope", out _));
    }


    [Fact]
    public void Enum_to_string_yields_the_name() {
        Assert.True(Caster.TryCast<IntKind, string>(IntKind.Two, out var name));
        Assert.Equal("Two", name);
    }

    [Fact]
    public void Nullable_enum_to_string_yields_the_name_or_fails_on_null() {
        Assert.True(Caster.TryCast<IntKind?, string>(IntKind.Three, out var name));
        Assert.Equal("Three", name);
        Assert.True(Caster.TryCast<IntKind?, string>(null, out var str));
        Assert.Null(str);
    }


    [Fact]
    public void Object_to_enum_from_number_and_string() {
        Assert.Equal(IntKind.Two, ((object)2).Parse<IntKind>());
        Assert.Equal(IntKind.Two, ((object)2L).Parse<IntKind>());
        Assert.Equal(IntKind.Two, ((object)"Two").Parse<IntKind>());
        Assert.Equal(IntKind.Two, ((object)"2").Parse<IntKind>());
    }

    [Fact]
    public void Object_to_nullable_enum() {
        Assert.True(Caster.TryCast<object, IntKind?>(2, out var value));
        Assert.Equal(IntKind.Two, value);
        Assert.True(Caster.TryCast<object, IntKind?>(null!, out var none));
        Assert.Null(none);
    }

    /// <summary>
    /// Value-to-value conversions (numbers, enums, and their crossings, across nullability) must not box.
    /// The per-pair reflection happens once in the static ctor, so after warm-up the loop allocates nothing.
    /// </summary>
    [Fact]
    public void Value_conversions_never_allocate() {
        Convert();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            Convert();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);

        static void Convert() {
            Caster.TryCast<IntKind, int>(IntKind.Two, out _);       
            Caster.TryCast<int, IntKind>(2, out _);                 
            Caster.TryCast<IntKind, ByteKind>(IntKind.Two, out _);  
            Caster.TryCast<long, int>(5L, out _);                   
            Caster.TryCast<IntKind?, int>(IntKind.Two, out _);      
            Caster.TryCast<int, IntKind?>(2, out _);                
            Caster.TryCast<IntKind?, ByteKind?>(IntKind.Two, out _);
        }
    }
}

public enum IntKind { One = 1, Two = 2, Three = 3 }
public enum ByteKind : byte { One = 1, Two = 2, Three = 3 }
