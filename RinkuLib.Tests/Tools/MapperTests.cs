using System.Runtime.CompilerServices;
using RinkuLib.Tools;
using Xunit;


namespace RinkuLib.Tests.Tools;

public class MapperTests {

    #region 1. Lookup & Indexing Logic

    [Theory]
    [InlineData("Alpha", 0)]
    [InlineData("BETA", 1)]
    [InlineData("gamma", 2)]
    [InlineData("Delta", -1)] // Not in set
    public void GetIndex_String_Returns_Correct_Index_Or_Negative_One(string key, int expected) {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);

        Assert.Equal(expected, mapper.GetIndex(key));
        Assert.Equal(expected, mapper[key]); // Indexer parity
    }

    [Fact]
    public void GetIndex_Span_Matches_String_Logic() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);
        ReadOnlySpan<char> span = "BETA".AsSpan();

        Assert.Equal(1, mapper.GetIndex(span));
        Assert.Equal(1, mapper[span]);
    }
    [Fact]
    public void GetIndex_Span_Matches_String_Logic_Poison() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);
        ReadOnlySpan<char> span = "PoisonBETAPoison".AsSpan(6, 4);

        Assert.Equal(1, mapper.GetIndex(span));
        Assert.Equal(1, mapper[span]);
    }
    [Fact]
    public void GetIndex_False_32_Upper() {
        using var mapper = Mapper.GetMapper(["Ａ", "ａ", "Ｂ", "\u1000"]);
        Assert.Equal(3, mapper.Count);
        Assert.Equal(-1, mapper.GetIndex("\u1010"));
        Assert.Equal(-1, mapper["\u1010"]);
        Assert.Equal(0, mapper.GetIndex("ａ"));
        Assert.Equal(0, mapper["ａ"]);
        Assert.Equal(1, mapper.GetIndex("ｂ"));
        Assert.Equal(1, mapper["ｂ"]);
    }
    [Fact]
    public void GetIndex_OutAscii_Upper() {
        using var mapper = Mapper.GetMapper(["Ａ", "ａ", "Ⴀ", "Ｂ", "\u1000"]);
        Assert.Equal(4, mapper.Count);
        Assert.Equal(1, mapper.GetIndex("ⴀ"));
        Assert.Equal(1, mapper["ⴀ"]);
    }

    [Fact]
    public void ContainsKey_Returns_True_Only_For_Valid_Keys() {
        using var mapper = Mapper.GetMapper(["One", "Two", "Three"]);

        Assert.True(mapper.ContainsKey("ONE"));
        Assert.True(mapper.ContainsKey("two".AsSpan()));
        Assert.False(mapper.ContainsKey("Four"));
    }

    [Fact]
    public void TryGetValue_Correctly_Outputs_Index() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);

        bool found = mapper.TryGetValue("B", out int index);
        bool found2 = mapper.TryGetValue("B".AsSpan(), out int index2);
        bool missing = mapper.TryGetValue("Z", out int missingIndex);

        Assert.True(found);
        Assert.Equal(1, index);
        Assert.True(found2);
        Assert.Equal(1, index2);
        Assert.False(missing);
        Assert.Equal(-1, missingIndex);
    }
    [Fact]
    public void AtFoo_And_At_ID_Span() {
        using var mapper = Mapper.GetMapper(["@foo", "@id"]);

        string name = "id";
        Span<char> nameSpan = stackalloc char[name.Length + 1];
        nameSpan[0] = '@';
        name.AsSpan().CopyTo(nameSpan[1..]);
        Assert.Equal(1, mapper.GetIndex(nameSpan));
    }

    #endregion

    #region 2. Canonical Reference Identity (The Performance Tip)

    [Fact]
    public void GetSameKey_Returns_Original_Instance_Memory_Address() {
        string original = "Canonical_Reference";
        // Create a new string instance with same content but different address
        string search = new("CANONICAL_REFERENCE".ToCharArray());

        using var mapper = Mapper.GetMapper([original, "Filler1", "Filler2"]);

        string result = mapper.GetSameKey(search);

        // Character check
        Assert.Equal(original, result);
        // Critical Reference Identity check (Reference Equals)
        Assert.Same(original, result);
        Assert.True(ReferenceEquals(original, result));
    }

    [Fact]
    public void GetKey_By_Index_Returns_Correct_Reference() {
        string target = "Target";
        using var mapper = Mapper.GetMapper(["A", "B", target]);

        string result = mapper.GetKey(2);

        Assert.Same(target, result);
    }

    #endregion

    #region 3. Memory & Pointer Access

    [Fact]
    public void KeysStartPtr_Allows_Unsafe_Add_Navigation() {
        string[] inputs = ["Key0", "Key1", "Key2"];
        using var mapper = Mapper.GetMapper(inputs.AsSpan());

        ref string start = ref mapper.KeysStartPtr;

        // Verify we can navigate the memory block linearly
        Assert.Same(inputs[0], start);
        Assert.Same(inputs[1], Unsafe.Add(ref start, 1));
        Assert.Same(inputs[2], Unsafe.Add(ref start, 2));
    }

    [Fact]
    public void Keys_Property_Returns_Valid_ReadOnlySpan() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);
        ReadOnlySpan<string> keysSpan = mapper.Keys;

        Assert.Equal(3, keysSpan.Length);
        Assert.Equal("A", keysSpan[0]);
    }

    #endregion

    #region 4. Collection & Enumeration

    [Fact]
    public void Enumerator_Yields_Correct_Pairs_In_Order() {
        string[] inputs = ["First", "Second", "Third"];
        using var mapper = Mapper.GetMapper(inputs.AsSpan());

        var pairs = mapper.ToList();

        Assert.Equal(3, pairs.Count);
        for (int i = 0; i < inputs.Length; i++) {
            Assert.Equal(inputs[i], pairs[i].Key);
            Assert.Equal(i, pairs[i].Value);
        }
    }

    private static readonly int[] expected = [0, 1, 2, 3];

    [Fact]
    public void Values_Enumerable_Contains_Sequential_Indices() {
        using var mapper = Mapper.GetMapper(["A", "B", "C", "D"]);

        var values = mapper.Values.ToList();

        Assert.Equal(expected, values);
    }

    #endregion

    #region 5. Lifecycle & Disposal (Sentinels)

    [Fact]
    public void Dispose_Triggers_DeadKeys_Sentinel_State() {
        var mapper = Mapper.GetMapper(["A", "B", "C"]);
        mapper.Dispose();

        // Count should reflect DeadKeys.Length (1)
        Assert.Single(mapper);
        // The first key in DeadKeys is null (per your DeadKeys = [null!] definition)
        Assert.Null(mapper.GetKey(0));
    }

    [Fact]
    public void Dispose_Is_ThreadSafe_And_Idempotent() {
        var mapper = Mapper.GetMapper(["X", "Y", "Z"]);

        // Concurrent disposal shouldn't throw or corrupt state
        Parallel.Invoke(
            mapper.Dispose,
            mapper.Dispose,
            mapper.Dispose
        );

        Assert.Single(mapper);
    }

    #endregion

    #region 6. Hostile / Edge Case Usage

    [Fact]
    public void Mapper_Handles_Empty_Or_Whitespace_Keys_Correctly() {
        using var mapper = Mapper.GetMapper(["", " ", "\t", "Valid"]);

        Assert.Equal(0, mapper.GetIndex(""));
        Assert.Equal(1, mapper.GetIndex(" "));
        Assert.Equal(3, mapper.GetIndex("VALID"));
    }

    [Fact]
    public void GetSameKey_Returns_Null_For_Missing_Input() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);

        Assert.Null(mapper.GetSameKey("NonExistent"));
        Assert.Null(mapper.GetSameKey("Z".AsSpan()));
    }

    #endregion
}