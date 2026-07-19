using System.Collections.Immutable;
using System.Data;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using Xunit;


namespace RinkuLib.Tests.Tools;

public class MapperCreationTests {


    [Fact]
    public void Null_Entries_In_Span_Are_Refused() {
        Assert.Throws<ArgumentNullException>(() => Mapper.GetMapper(["A", null!, "B", null!, "a", "C"]));
    }
    public const char TypeEmpty = 'A';
    public const char TypeOne = 'B';
    public const char TypeTwo = 'C';
    public const char TypeDict = 'D';
    public const char TypeAscii = 'E';
    public const char TypeUnicode = 'F';
    private static void Verify(Span<string> input, Span<string> expected, char type)
        => Verify(Mapper.GetMapper(input), expected, type);
    private static void Verify(Mapper result, Span<string> expected, char type) {
        switch (type) {
            case TypeEmpty:
                Assert.Equal(Mapper.EmptyMapper, result);
                break;
            case TypeOne:
                Assert.Equal(1, result.Keys.Length);
                break;
            case TypeTwo:
                Assert.Equal(2, result.Keys.Length);
                break;
            case TypeDict:
                Assert.True(result.Keys.Length > 2);
                Assert.IsNotType<OptiMapper<AsciiStrategy>>(result);
                Assert.IsNotType<OptiMapper<UnicodeStrategy>>(result);
                break;
            case TypeAscii:
                Assert.IsType<OptiMapper<AsciiStrategy>>(result);
                break;
            case TypeUnicode:
                Assert.IsType<OptiMapper<UnicodeStrategy>>(result);
                break;
        }

        var keys = result.Keys;
        Assert.Equal(expected.Length, keys.Length);
        for (int i = 0; i < expected.Length; i++) {
            Assert.Equal(expected[i], keys[i]);
        }
    }

    [Fact]
    public void Need_BigMask() {
        string[] buffer = ["`", "@", "A", "1", " ", "1`", "1@", "1A", "11", "2`", "2@", "2A", "21"];
        Verify(buffer, buffer, TypeAscii);
    }
    [Fact]
    public void UseEnumerable() {
        string[] buffer = ["Key1", "Key2", "KEY1", "Key3", "key2", "KEY3"];
        Verify(Mapper.GetMapper((IEnumerable<string>)buffer), ["Key1", "Key2", "Key3"], TypeAscii);
        Verify(Mapper.GetMapper(buffer.ToList()), ["Key1", "Key2", "Key3"], TypeAscii);
        Verify(Mapper.GetMapper(buffer.Select(k => k + '1')), ["Key11", "Key21", "Key31"], TypeAscii);
        Verify(Mapper.GetMapper(ImmutableArray.Create(buffer)), ["Key1", "Key2", "Key3"], TypeAscii);
    }
    [Theory]
    [InlineData(new string[0], new string[0], TypeEmpty)]
    [InlineData(new[] { "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { "", "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { "", " " }, new[] { "", " " }, TypeTwo)]
    [InlineData(new[] { "", " ", "\t" }, new[] { "", " ", "\t" }, TypeAscii)]
    public void Empty_And_Whitespace_Identity(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    [Theory]
    [InlineData(
        new[] { "Key1", "Key2", "KEY1", "Key3", "key2", "KEY3" },
        new[] { "Key1", "Key2", "Key3" },
        TypeAscii)]
    [InlineData(
        new[] { "A", "B", "C", "a", "b", "c", "D" },
        new[] { "A", "B", "C", "D" },
        TypeAscii)]
    [InlineData(
        new[] { "A", "B", "C", "a", "b", "c", "D", "A", "B", "C", "D" },
        new[] { "A", "B", "C", "D" },
        TypeAscii)]
    public void Interleaved_Keys_Force_State_Tracking_And_Order_Stability(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    [Theory]
    [InlineData(new[] { "apple", "APPLE" }, new[] { "apple" }, TypeOne)]
    [InlineData(new[] { "APPLE", "apple" }, new[] { "APPLE" }, TypeOne)]
    [InlineData(new[] { "a", "b", "A" }, new[] { "a", "b" }, TypeTwo)]
    [InlineData(new[] { "A", "b", "a" }, new[] { "A", "b" }, TypeTwo)]
    public void OrdinalIgnoreCase_First_Wins_Permutations(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    [Fact]
    public void Massive_String_Deduplication() {
        string heavy1 = new string('z', 100_000) + "Content";
        string heavy2 = new string('Z', 100_000) + "content";

        string[] input = [heavy1, heavy2];
        string[] expected = [heavy1];

        Verify(input, expected, TypeOne);
    }


    [Fact]
    public void Long_Distance_Shadow_Deduplication() {
        var input = new List<string> { "Alpha", "Beta", "Gamma" };
        for (int i = 0; i < 1000; i++)
            input.Add($"Noise_{i}");
        input.Add("ALPHA");
        input.Add("beta");
        input.Add("GAMMA");

        var span = CollectionsMarshal.AsSpan(input);

        Verify(span, span[..1003], TypeAscii);
    }

    [Fact]
    public void Construction_With_Massive_Keys_Maintains_Data_Integrity() {
        string big1 = new string('1', 100_000) + "A";
        string big2 = new string('2', 100_000) + "B";
        string big3 = new string('1', 100_000) + "a";
        string big4 = new string('3', 100_000) + "C";

        string[] input = [big1, big2, big3, big4];
        string[] expected = [big1, big2, big4];

        Verify(input, expected, TypeAscii);
    }

    [Fact]
    public void Construction_With_Extremely_Similar_Prefix() {
        string prefix = new('z', 5000);
        string[] input = [
            prefix + "Alpha",
            prefix + "Beta",
            prefix + "ALPHA",
            prefix + "Gamma",
            prefix + "Delta"
        ];
        string[] expected = [input[0], input[1], input[3], input[4]];

        Verify(input, expected, TypeDict);
    }
    [Fact]
    public void Construction_With_Extremely_Similar_Sufix() {
        string sufix = new('z', 5000);
        string[] input = [
             "Alpha" + sufix,
             "Beta" + sufix,
             "ALPHA" + sufix,
             "Gamma" + sufix,
             "Delta" + sufix
        ];
        string[] expected = [input[0], input[1], input[3], input[4]];

        Verify(input, expected, TypeAscii);
    }

    [Fact]
    public void Constructor_Ignores_Poison_Data_Outside_Span_Bounds() {
        string[] buffer = ["POISON", "Key1", "Key2", "Key3IsLongerToTestLonger", "key1", "POISON"];

        var slice = buffer.AsSpan(1, 4);
        string[] expected = ["Key1", "Key2", "Key3IsLongerToTestLonger"];

        Verify(slice, expected, TypeAscii);
    }
    [Theory]
    [InlineData(
        new[] { "🪐", "🚀", "🛰️", "🪐", "🚀", "☄️" },
        new[] { "🪐", "🚀", "🛰️", "☄️" },
        TypeUnicode)]
    public void Unicode_Surrogate_Stability_During_Construction(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    [Fact]
    public void Ordinal_Strictness_Prevents_Cultural_Collisions() {
        string[] input = ["i", "I", "İ", "j", "J"];
        string[] expected = ["i", "İ", "j"];

        Verify(input, expected, TypeUnicode);
    }

    [Fact]
    public void Sawtooth_Memory_Allocation_Pressure() {
        string big1 = new('z', 10_000);
        string big2 = new('y', 10_000);

        string[] input = ["a", big1, "A", "b", big2, big1.ToUpper(), "c"];
        string[] expected = ["a", big1, "b", big2, "c"];

        Verify(input, expected, TypeAscii);
    }

    [Fact]
    public void High_Volume_Unique_Construction_Efficiency() {
        var input = Enumerable.Range(0, 5000).Select(i => $"Key_{i:D4}").ToArray();
        Verify(input, input, TypeAscii);
    }

    [Fact]
    public void Mid_String_Null_Terminators_Are_Distinct_Keys() {
        string s1 = "Alpha\0One";
        string s2 = "Alpha\0Two";
        string s3 = "Alpha\0one";
        string s4 = "Beta\0Three";

        string[] input = [s1, s2, s3, s4, "Gamma"];
        string[] expected = [s1, s2, s4, "Gamma"];

        Verify(input, expected, TypeAscii);
    }

    [Fact]
    public void Constructor_Handles_Deterministic_Hash_Collisions() {
        string s1 = "AaAaAaAa";
        string s2 = "BBBBBBBB";
        string s3 = "AaAaBBBB";
        string s4 = "BBBBaaaa";

        string[] input = [s1, s2, s3, s4, "Unique"];
        Verify(input, input, TypeAscii);
    }
    [Fact]
    public void Reversed_Mirror_With_Casing_Flip_Maintains_Original_Order() {
        string[] input = [ 
            "One", "Two", "Three", "Four", "Five",
            "FOUR", "THREE", "TWO", "ONE"];
        Verify(input, input.AsSpan(0, 5), TypeAscii);
    }
    [Fact]
    public void Minimal_Difference_Keys_Force_Full_Memory_Comparison() {
        var input = Enumerable.Range(0, 11).Select(i => {
            char[] chars = new string('0', 128).ToCharArray();
            chars[i] = '1';
            return new string(chars);
        }).ToArray();

        input[10] = new string(input[4]).ToUpper();

        Verify(input, input.AsSpan(0, 10), TypeAscii);
    }
    [Fact]
    public void Invisible_Characters_Are_Distinct_Keys() {
        string s1 = "Key";
        string s2 = "Key\u200B";
        string s3 = "K\u00ADey";
        string s4 = "KEY";

        string[] input = [s1, s2, s3, s4, "KeyOther"];
        string[] expected = [s1, s2, s3, "KeyOther"];

        Verify(input, expected, TypeUnicode);
    }

    [Fact]
    public void Various_Whitespace_Types_Are_Unique_Keys() {
        string[] input = [
            "Word",
            "Word\t",
            "Word ",
            "Word\u00A0",
            "WORD",
            "Extra"
        ];
        string[] expected = ["Word", "Word\t", "Word ", "Word\u00A0", "Extra"];

        Verify(input, expected, TypeDict);
    }

    [Fact]
    public void Exponential_Key_Length_Growth_Stability() {
        var input = new List<string>();
        for (int i = 0; i < 5; i++)
            input.Add(new string((char)('A' + i), (int)Math.Pow(10, i)));
        input.Add(new string('D', 1000).ToLower());

        var expected = input.Take(5).ToArray();

        Verify(input.ToArray(), expected, TypeAscii);
    }
    [Fact]
    public void Massive_Unique_Entry_Volume_Initialization() {
        var input = Enumerable.Range(0, 10000).Select(_ => Guid.NewGuid().ToString()).ToArray();

        Verify(input, input, TypeAscii);
    }

    [Fact]
    public void Dense_Repetitive_Waves_Verify_Constant_Time_Lookup() {
        string[] keys = ["Alpha", "Beta", "Gamma", "Delta"];
        var input = new List<string>();

        foreach (var k in keys)
            input.AddRange(Enumerable.Repeat(k, 10));

        input.AddRange(["ALPHA", "beta", "GAMMA", "delta", "Alpha"]);

        Verify(input.ToArray(), keys, TypeAscii);
    }
    [Fact]
    public void Object_Reference_Equality_ShortCircuit_Optimization() {
        string shared = "CommonKey";
        string[] input = [shared, shared, shared, "Unique1", "Unique2"];
        string[] expected = [shared, "Unique1", "Unique2"];

        Verify(input, expected, TypeAscii);
    }

    [Fact]
    public void Construction_Is_Resilient_To_Memory_Relocation() {
        string[] input = [
            new('A', 5000),
            new('B', 5000),
            new string('A', 5000).ToLower(),
            new('C', 5000),
            new('D', 5000)
        ];

        Verify(input, [input[0], input[1], input[3], input[4]], TypeAscii);
    }

    [Fact]
    public void Result_X_Is_Independent_Of_Source_Array_Mutation() {
        string[] input = ["Alpha", "Beta", "Gamma", "Alpha"];
        var result = Mapper.GetMapper(input.AsSpan());

        input[0] = "POISON";
        input[1] = "POISON";

        Assert.Equal("Alpha", result.Keys[0]);
        Assert.Equal("Beta", result.Keys[1]);
        Assert.Equal("Gamma", result.Keys[2]);
    }
    [Fact]
    public void ConcurrentFactoryCallsDoNotCrossContaminate() {
        Parallel.For(0, 100, i => {
            string uniqueSeed = $"Thread_{i}_";
            string[] input = [uniqueSeed + "1", uniqueSeed + "2", uniqueSeed + "1", uniqueSeed + "3"];
            string[] expected = [uniqueSeed + "1", uniqueSeed + "2", uniqueSeed + "3"];

            Verify(input, expected, TypeAscii);
        });
    }

    [Fact]
    public void AllDifferentLength256() {
        var input = Enumerable.Range(0, 256).Select(i => {
            return new string('0', i);
        }).ToArray();

        Verify(input, input, TypeAscii);
    }
    [Fact]
    public void AllDifferentLength257() {
        var input = Enumerable.Range(0, 257).Select(i => {
            return new string('0', i);
        }).ToArray();

        Verify(input, input, TypeDict);
    }
}