#if DEBUG
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="BinaryConverter"/> renders integers as space-grouped binary, one group per byte.
/// </summary>
public class BinaryConverterTests {
    [Fact]
    public void Byte_renders_eight_bits() {
        Assert.Equal("00000101", ((byte)5).ConvertBinary());
        Assert.Equal("11111111", byte.MaxValue.ConvertBinary());
        Assert.Equal("00000000", ((byte)0).ConvertBinary());
    }

    [Fact]
    public void Sbyte_reuses_the_byte_pattern() {
        Assert.Equal("11111111", ((sbyte)-1).ConvertBinary());
    }

    [Fact]
    public void Ushort_groups_two_bytes() {
        Assert.Equal("00000001 00000000", ((ushort)256).ConvertBinary());
        Assert.Equal("00000000 00000101", ((ushort)5).ConvertBinary());
    }

    [Fact]
    public void Short_and_char_reuse_the_ushort_pattern() {
        Assert.Equal("11111111 11111111", ((short)-1).ConvertBinary());
        Assert.Equal("00000000 01000001", 'A'.ConvertBinary());
    }

    [Fact]
    public void Uint_groups_four_bytes() {
        Assert.Equal("00000000 00000000 00000001 00000000", 256u.ConvertBinary());
    }

    [Fact]
    public void Int_reuses_the_uint_pattern() {
        Assert.Equal("11111111 11111111 11111111 11111111", (-1).ConvertBinary());
        Assert.Equal("00000000 00000000 00000000 00000101", 5.ConvertBinary());
    }

    [Fact]
    public void Ulong_groups_eight_bytes() {
        Assert.Equal("00000000 00000000 00000000 00000000 00000000 00000000 00000001 00000000", 256UL.ConvertBinary());
    }

    [Fact]
    public void Long_reuses_the_ulong_pattern() {
        Assert.Equal("11111111 11111111 11111111 11111111 11111111 11111111 11111111 11111111", (-1L).ConvertBinary());
    }
}
#endif
