using System.Collections.Immutable;
using System.Data;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using Xunit;


namespace RinkuLib.Tests.Tools;

public class MapperCreationTests {


    [Fact]
    public void Null_Entries_In_Span_Are_Refused() {
        Assert.Throws<NullReferenceException>(() => Mapper.GetMapper(["A", null!, "B", null!, "a", "C"]));
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
        // Assert Type Identity
        switch (type) {
            case TypeEmpty:
                Assert.IsType<Mapper.Empty>(result);
                break;
            case TypeOne:
                Assert.IsType<Mapper.One>(result);
                break;
            case TypeTwo:
                Assert.IsType<Mapper.Two>(result);
                break;
            case TypeDict:
                Assert.IsType<Mapper.DictMapper>(result);
                break;
            case TypeAscii:
                Assert.IsType<AsciiMapper<AsciiStrategy>>(result);
                break;
            case TypeUnicode:
                Assert.IsType<AsciiMapper<UnicodeStrategy>>(result);
                break;
        }

        // Assert Content Equality & Order
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
    // 1. THE EMPTY BOUNDARY
    // Tests the transition from absolute zero to minimal state.
    [Theory]
    [InlineData(new string[0], new string[0], TypeEmpty)]
    [InlineData(new[] { "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { "", "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { "", " " }, new[] { "", " " }, TypeTwo)]
    [InlineData(new[] { "", " ", "\t" }, new[] { "", " ", "\t" }, TypeAscii)]
    public void Empty_And_Whitespace_Identity(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    // 2. THE "NON-SEQUENTIAL" STABILITY TEST
    // Purpose: Force the factory to track multiple active states at once.
    // Unlike a simple stream, this ensures the constructor can handle 
    // unique items interleaved with duplicates of previous items.
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

    // 3. THE FIRST-APPARITION STABILITY (SINGLE COLLISION)
    // Tests that the "first" found casing is the one preserved, regardless of index.
    [Theory]
    [InlineData(new[] { "apple", "APPLE" }, new[] { "apple" }, TypeOne)]
    [InlineData(new[] { "APPLE", "apple" }, new[] { "APPLE" }, TypeOne)]
    [InlineData(new[] { "a", "b", "A" }, new[] { "a", "b" }, TypeTwo)]
    [InlineData(new[] { "A", "b", "a" }, new[] { "A", "b" }, TypeTwo)]
    public void OrdinalIgnoreCase_First_Wins_Permutations(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    // 4. THE GARGANTUAN STRING IDENTITY
    // Purpose: Test strings so large they likely live on the Large Object Heap (LOH).
    // This tests if the comparison logic or hashing can handle 2MB+ strings.
    [Fact]
    public void Massive_String_Deduplication() {
        // 1 million 'z's + a suffix
        string heavy1 = new string('z', 100_000) + "Content";
        string heavy2 = new string('Z', 100_000) + "content"; // Collision

        string[] input = [heavy1, heavy2];
        string[] expected = [heavy1];

        Verify(input, expected, TypeOne);
    }


    // 5. THE "REPETITIVE WAVE" (THE X****** PATTERN AT SCALE)
    // Purpose: Items appear once, then a massive gap of unique noise, 
    // then the duplicates reappear. Tests tracker persistence.
    [Fact]
    public void Long_Distance_Shadow_Deduplication() {
        var input = new List<string> { "Alpha", "Beta", "Gamma" };
        for (int i = 0; i < 1000; i++)
            input.Add($"Noise_{i}");
        input.Add("ALPHA"); // Duplicate of [0]
        input.Add("beta");  // Duplicate of [1]
        input.Add("GAMMA"); // Duplicate of [2]

        var span = CollectionsMarshal.AsSpan(input);

        Verify(span, span[..1003], TypeAscii);
    }

    // 6. THE "GARGANTUAN KEY" MEMORY ALIGNMENT
    // Purpose: If the constructor of X allocates buffers based on key length,
    // this test uses 100KB strings to ensure no truncation or buffer overruns.
    [Fact]
    public void Construction_With_Massive_Keys_Maintains_Data_Integrity() {
        string big1 = new string('1', 100_000) + "A";
        string big2 = new string('2', 100_000) + "B";
        string big3 = new string('1', 100_000) + "a"; // Duplicate of big1
        string big4 = new string('3', 100_000) + "C";

        string[] input = [big1, big2, big3, big4];
        string[] expected = [big1, big2, big4];

        // This should trigger TypeOther (3 unique items) and force large allocation logic.
        Verify(input, expected, TypeAscii);
    }

    // 7. THE "COMMON PREFIX" BRANCHING TORTURE
    // Purpose: If the constructor uses a Trie or prefix-based hashing,
    // identical prefixes of extreme length will force it into deeper logic branches.
    [Fact]
    public void Construction_With_Extremely_Similar_Prefix() {
        string prefix = new('z', 5000);
        string[] input = [
            prefix + "Alpha",
            prefix + "Beta",
            prefix + "ALPHA", // Collision
            prefix + "Gamma",
            prefix + "Delta"
        ];
        string[] expected = [input[0], input[1], input[3], input[4]];

        Verify(input, expected, TypeDict);
    }
    // 8. THE "COMMON PREFIX" BRANCHING TORTURE
    // Purpose: If the constructor uses a Trie or prefix-based hashing,
    // identical prefixes of extreme length will force it into deeper logic branches.
    [Fact]
    public void Construction_With_Extremely_Similar_Sufix() {
        string sufix = new('z', 5000);
        string[] input = [
             "Alpha" + sufix,
             "Beta" + sufix,
             "ALPHA" + sufix, // Collision
             "Gamma" + sufix,
             "Delta" + sufix
        ];
        string[] expected = [input[0], input[1], input[3], input[4]];

        Verify(input, expected, TypeAscii);
    }

    // 9. THE "MEMORY SLICE" CONSTRUCTOR ISOLATION
    // Purpose: Pass a Span that is a slice of a larger array containing "poison" data.
    // If the constructor peeks outside the Span, it will fail.
    [Fact]
    public void Constructor_Ignores_Poison_Data_Outside_Span_Bounds() {
        // "Poison" is a duplicate of a valid key, but exists outside the slice range.
        string[] buffer = ["POISON", "Key1", "Key2", "Key3IsLongerToTestLonger", "key1", "POISON"];

        // We slice to: ["Key1", "Key2", "Key3", "key1"]
        // The first "POISON" and any data after index 4 must be ignored.
        var slice = buffer.AsSpan(1, 4);
        string[] expected = ["Key1", "Key2", "Key3IsLongerToTestLonger"];

        Verify(slice, expected, TypeAscii);
    }
    // 10. THE "SURROGATE PAIR" COLLISION (UNICODE TORTURE)
    // Purpose: Non-BMP characters (like Emojis) are 2 chars long in UTF-16. 
    // If the tracker slices strings or uses char-based indexing incorrectly, it will fail.
    [Theory]
    [InlineData(
        new[] { "🪐", "🚀", "🛰️", "🪐", "🚀", "☄️" },
        new[] { "🪐", "🚀", "🛰️", "☄️" },
        TypeUnicode)]
    public void Unicode_Surrogate_Stability_During_Construction(string[] input, string[] expected, char type)
        => Verify(input, expected, type);

    // 11. THE "TURKISH I" & CULTURAL AMBIGUITY
    // Purpose: Under 'OrdinalIgnoreCase', 'i' (U+0069) and 'I' (U+0049) collide.
    // However, 'İ' (U+0130) must NOT collide with 'i' in Ordinal.
    // This ensures the factory doesn't accidentally use Culture-Sensitive logic.
    [Fact]
    public void Ordinal_Strictness_Prevents_Cultural_Collisions() {
        // i and I collide. İ and i do not.
        string[] input = ["i", "I", "İ", "j", "J"];
        string[] expected = ["i", "İ", "j"];

        Verify(input, expected, TypeUnicode);
    }

    // 12. THE "SAWTOOTH" GROWTH PATTERN
    // Purpose: Interleave 1-char strings with 10,000-char strings.
    // This stresses the memory allocator within the constructor if it tries to 
    // predict buffer sizes based on average lengths.
    [Fact]
    public void Sawtooth_Memory_Allocation_Pressure() {
        string big1 = new('z', 10_000);
        string big2 = new('y', 10_000);

        string[] input = ["a", big1, "A", "b", big2, big1.ToUpper(), "c"];
        string[] expected = ["a", big1, "b", big2, "c"];

        Verify(input, expected, TypeAscii);
    }

    // 13. THE "MAXIMUM ENTROPY" (ALL UNIQUE)
    // Purpose: 5,000 unique keys. Tests the constructor's efficiency in 
    // allocating its internal "Items" storage without over-allocating or leaking memory.
    [Fact]
    public void High_Volume_Unique_Construction_Efficiency() {
        var input = Enumerable.Range(0, 5000).Select(i => $"Key_{i:D4}").ToArray();
        Verify(input, input, TypeAscii);
    }

    // 14. THE "NULL-TERMINATED" STRING IDENTITY
    // Purpose: Verify the factory doesn't use C-style string comparison.
    // If "Alpha\0One" and "Alpha\0Two" are treated as "Alpha", the tool is broken.
    [Fact]
    public void Mid_String_Null_Terminators_Are_Distinct_Keys() {
        string s1 = "Alpha\0One";
        string s2 = "Alpha\0Two";
        string s3 = "Alpha\0one"; // Collision with s1
        string s4 = "Beta\0Three";

        string[] input = [s1, s2, s3, s4, "Gamma"];
        string[] expected = [s1, s2, s4, "Gamma"];

        Verify(input, expected, TypeAscii);
    }

    // 15. THE "HASH COLLISION" ATTACK
    // Purpose: Force the internal tracker to handle different strings with identical hash codes.
    // If the tool uses only HashCodes for deduplication, it will fail here.
    [Fact]
    public void Constructor_Handles_Deterministic_Hash_Collisions() {
        // "Aa" and "BB" have the same hash code in many legacy .NET hash implementations.
        // We use strings that are structurally similar to stress bucket-chaining.
        string s1 = "AaAaAaAa";
        string s2 = "BBBBBBBB"; // Potential collision with s1
        string s3 = "AaAaBBBB";
        string s4 = "BBBBaaaa"; // Should be distinct from s1 under OrdinalIgnoreCase

        string[] input = [s1, s2, s3, s4, "Unique"];

        // All these are actually distinct under OrdinalIgnoreCase. 
        // We test that the factory doesn't "swallow" unique items due to hash collisions.
        Verify(input, input, TypeAscii);
    }
    // 16. THE "REVERSED TWIN" STABILITY
    // Purpose: The second half of the input is the first half in reverse order with flipped casing.
    // This ensures that the "First Apparition" rule holds even when the set "flips" halfway.
    [Fact]
    public void Reversed_Mirror_With_Casing_Flip_Maintains_Original_Order() {
        string[] input = [ 
            "One", "Two", "Three", "Four", "Five",
            "FOUR", "THREE", "TWO", "ONE"]; // All duplicates
        Verify(input, input.AsSpan(0, 5), TypeAscii);
    }

    // 17. THE "BIT-FLIP" SIMILARITY (IDENTICAL LENGTH & ALIGNMENT)
    // Purpose: Every string is the exact same length and differs by only one bit.
    // This breaks optimizations that rely on length-bucketing or SIMD-aligned "quick skips."
    [Fact]
    public void Minimal_Difference_Keys_Force_Full_Memory_Comparison() {
        // All strings are 128 chars of '0', but one '1' moves across them.
        var input = Enumerable.Range(0, 11).Select(i => {
            char[] chars = new string('0', 128).ToCharArray();
            chars[i] = '1';
            return new string(chars);
        }).ToArray();

        // Add a duplicate of the 5th item
        input[10] = new string(input[4]).ToUpper();

        Verify(input, input.AsSpan(0, 10), TypeAscii);
    }
    // 18. THE "INVISIBLE CHARACTER" DIFFERENTIATION
    // Purpose: Non-printable characters like Zero-Width Space (U+200B) or 
    // Soft Hyphen (U+00AD) are technically distinct but visually identical.
    // Tests if X incorrectly "cleans" or "trims" keys during initialization.
    [Fact]
    public void Invisible_Characters_Are_Distinct_Keys() {
        string s1 = "Key";
        string s2 = "Key\u200B"; // Zero-width space
        string s3 = "K\u00ADey"; // Soft hyphen
        string s4 = "KEY";       // Collision with s1

        string[] input = [s1, s2, s3, s4, "KeyOther"];
        string[] expected = [s1, s2, s3, "KeyOther"];

        Verify(input, expected, TypeUnicode);
    }

    // 19. THE "WHITESPACE VARIANCE" IDENTITY
    // Purpose: Testing the intersection of space types (Tab, Space, NBSP).
    // In many normalization engines, these are collapsed. In Ordinal, they are not.
    [Fact]
    public void Various_Whitespace_Types_Are_Unique_Keys() {
        string[] input = [
            "Word",
            "Word\t",
            "Word ",
            "Word\u00A0", // Non-breaking space
            "WORD",      // Collision with [0]
            "Extra"
        ];
        string[] expected = ["Word", "Word\t", "Word ", "Word\u00A0", "Extra"];

        Verify(input, expected, TypeDict);
    }

    // 20. THE "STAIRCASE" LENGTH GROWTH (AGGRESSIVE)
    // Purpose: Keys grow exponentially: 1, 10, 100, 1000, 10000 chars.
    // Tests the constructor's ability to handle erratic memory requirements 
    // for each subsequent unique item it decides to track.
    [Fact]
    public void Exponential_Key_Length_Growth_Stability() {
        var input = new List<string>();
        for (int i = 0; i < 5; i++) {
            input.Add(new string((char)('A' + i), (int)Math.Pow(10, i)));
        }
        // Add a collision for the 1000-char string
        input.Add(new string('D', 1000).ToLower());

        var expected = input.Take(5).ToArray();

        Verify(input.ToArray(), expected, TypeAscii);
    }
    // 21. THE "BIRTHDAY PARADOX" VOLUME TEST
    // Purpose: 10,000 unique keys generated via GUIDs. 
    // This tests the constructor's ability to scale its internal tracking 
    // structure without collision or performance degradation.
    [Fact]
    public void Massive_Unique_Entry_Volume_Initialization() {
        var input = Enumerable.Range(0, 10000).Select(_ => Guid.NewGuid().ToString()).ToArray();

        // Verifies that TypeOther can hold and maintain 10k unique states.
        Verify(input, input, TypeAscii);
    }

    // 22. THE "MAX_STABILITY" WAVE
    // Purpose: Every item is duplicated 10 times in a row, then 10 times randomly.
    // [A,A,A, B,B,B, C,C,C, A,C,B, A,B,C]
    // Tests if the "already seen" logic is truly constant-time or if it degrades.
    [Fact]
    public void Dense_Repetitive_Waves_Verify_Constant_Time_Lookup() {
        string[] keys = ["Alpha", "Beta", "Gamma", "Delta"];
        var input = new List<string>();

        // 10 reps each
        foreach (var k in keys)
            input.AddRange(Enumerable.Repeat(k, 10));

        // Random interleaved duplicates
        input.AddRange(["ALPHA", "beta", "GAMMA", "delta", "Alpha"]);

        Verify(input.ToArray(), keys, TypeAscii);
    }
    // 23. THE "SELF-REFERENCING" SPAN TORTURE
    // Purpose: Pass a Span where multiple entries point to the EXACT same string object instance.
    // Tests if the constructor optimizes for reference equality (fast) 
    // while still correctly falling back for non-identical references (safe).
    [Fact]
    public void Object_Reference_Equality_ShortCircuit_Optimization() {
        string shared = "CommonKey";
        // All elements are the same memory address.
        string[] input = [shared, shared, shared, "Unique1", "Unique2"];
        string[] expected = [shared, "Unique1", "Unique2"];

        Verify(input, expected, TypeAscii);
    }

    // 24. THE "GC PRESSURE" PINNING TEST
    // Purpose: Use very large strings and trigger a GC collection (simulated) 
    // during the construction of X. 
    // Tests if X's constructor handles strings moving in memory (if it uses pointers).
    [Fact]
    public void Construction_Is_Resilient_To_Memory_Relocation() {
        string[] input = [
            new('A', 5000),
            new('B', 5000),
            new string('A', 5000).ToLower(), // Duplicate
            new('C', 5000),
            new('D', 5000)
        ];

        // This is a "smoke test" for pointer-based logic.
        // If the factory uses 'fixed' pointers but doesn't hold them correctly,
        // any heap shift would cause a crash or data corruption.
        Verify(input, [input[0], input[1], input[3], input[4]], TypeAscii);
    }

    // 25. THE "COW" (COPY-ON-WRITE) REFERENCE TEST
    // Purpose: If the constructor of X tries to be clever and doesn't actually copy 
    // the strings but just stores references, we ensure those references 
    // are stable even if the original array is cleared.
    [Fact]
    public void Result_X_Is_Independent_Of_Source_Array_Mutation() {
        string[] input = ["Alpha", "Beta", "Gamma", "Alpha"];
        var result = Mapper.GetMapper(input.AsSpan());

        // Mutate the original array
        input[0] = "POISON";
        input[1] = "POISON";

        // X.Items must still contain the original data
        Assert.Equal("Alpha", result.Keys[0]);
        Assert.Equal("Beta", result.Keys[1]);
        Assert.Equal("Gamma", result.Keys[2]);
    }
    // 26. THE "PARALLEL CONSTRUCTION" RACE
    // Purpose: Fire 100 threads at the factory simultaneously. 
    // If the factory uses a shared static buffer or a non-thread-safe pool 
    // to build X, this will cause cross-contamination or crashes.
    [Fact]
    public void ConcurrentFactoryCallsDoNotCrossContaminate() {
        Parallel.For(0, 100, i => {
            string uniqueSeed = $"Thread_{i}_";
            string[] input = [uniqueSeed + "1", uniqueSeed + "2", uniqueSeed + "1", uniqueSeed + "3"];
            string[] expected = [uniqueSeed + "1", uniqueSeed + "2", uniqueSeed + "3"];

            Verify(input, expected, TypeAscii);
        });
    }

    // 27. 
    [Fact]
    public void AllDifferentLength256() {
        var input = Enumerable.Range(0, 256).Select(i => {
            return new string('0', i);
        }).ToArray();

        Verify(input, input, TypeAscii);
    }
    // 28. 
    [Fact]
    public void AllDifferentLength257() {
        var input = Enumerable.Range(0, 257).Select(i => {
            return new string('0', i);
        }).ToArray();

        Verify(input, input, TypeDict);
    }
    /*
    // 1-4: The Null/Empty/Whitespace Matrix
    [Theory]
    [InlineData(new string[0], new string[0], TypeEmpty)]
    [InlineData(new[] { "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { "", "" }, new[] { "" }, TypeOne)]
    [InlineData(new[] { " ", "  ", "\t" }, new[] { " ", "  ", "\t" }, TypeBest)]
    public void Boundary_Tests(string[] input, string[] expected, char type) => Verify(input, expected, type);

    // 5-12: Casing & Collision Permutations (Ordinal)
    [Theory]
    [InlineData(new[] { "abc", "ABC" }, new[] { "abc" }, TypeOne)]
    [InlineData(new[] { "ABC", "abc" }, new[] { "ABC" }, TypeOne)]
    [InlineData(new[] { "aBc", "AbC", "ABC", "abc" }, new[] { "aBc" }, TypeOne)]
    [InlineData(new[] { "test", "TEST", "tEsT" }, new[] { "test" }, TypeOne)]
    [InlineData(new[] { "123", "123" }, new[] { "123" }, TypeOne)]
    [InlineData(new[] { "!!", "!!" }, new[] { "!!" }, TypeOne)]
    [InlineData(new[] { " { ", " { " }, new[] { " { " }, TypeOne)]
    [InlineData(new[] { "dot.net", "DOT.NET" }, new[] { "dot.net" }, TypeOne)]
    public void Casing_Collision_Permutations(string[] input, string[] expected, char type) => Verify(input, expected, type);

    // 13-24: Stability & Interleaving (The "First Apparition" Rule)
    [Theory]
    [InlineData(new[] { "a", "b", "a" }, new[] { "a", "b" }, 'B')]
    [InlineData(new[] { "b", "a", "b" }, new[] { "b", "a" }, 'B')]
    [InlineData(new[] { "a", "b", "c", "a", "b", "c" }, new[] { "a", "b", "c" }, 'C')]
    [InlineData(new[] { "x", "y", "Z", "X", "Y", "z" }, new[] { "x", "y", "Z" }, 'C')]
    [InlineData(new[] { "1", "2", "3", "2", "1" }, new[] { "1", "2", "3" }, 'C')]
    [InlineData(new[] { "first", "second", "FIRST" }, new[] { "first", "second" }, 'B')]
    [InlineData(new[] { "A", "B", "C", "D", "a", "b", "c", "d" }, new[] { "A", "B", "C", "D" }, 'D')]
    [InlineData(new[] { "keep", "ignore", "KEEP", "ignore" }, new[] { "keep", "ignore" }, 'C')]
    [InlineData(new[] { "a", "A", "A", "A" }, new[] { "a" }, 'A')]
    [InlineData(new[] { "A", "a", "a", "a" }, new[] { "A" }, 'A')]
    [InlineData(new[] { "unique1", "unique2", "unique3" }, new[] { "unique1", "unique2", "unique3" }, 'C')]
    [InlineData(new[] { "a", "b", "c", "d", "e", "f" }, new[] { "a", "b", "c", "d", "e", "f" }, 'D')]
    public void Stability_And_Ordering_Permutations(string[] input, string[] expected, char type) => Verify(input, expected, type);

    // 25-40: Unicode, Surrogates, and Turkish-I Matrix
    [Theory]
    [InlineData(new[] { "i", "I" }, new[] { "i" }, 'C')]
    [InlineData(new[] { "I", "i" }, new[] { "I" }, 'C')]
    [InlineData(new[] { "İ", "i" }, new[] { "İ", "i" }, 'D')] // Ordinal: İ (U+0130) != i
    [InlineData(new[] { "😀", "😀" }, new[] { "😀" }, 'D')]
    [InlineData(new[] { "😀", "😁" }, new[] { "😀", "😁" }, 'D')]
    [InlineData(new[] { "cafe\u0301", "café" }, new[] { "cafe\u0301", "café" }, 'D')] // Ordinal treats decomposed vs combined as different
    [InlineData(new[] { "æ", "Æ" }, new[] { "æ" }, 'D')]
    [InlineData(new[] { "ß", "SS" }, new[] { "ß", "SS" }, 'D')] // Ordinal: ß != SS
    public void Unicode_And_Culture_Edges(string[] input, string[] expected, char type) => Verify(input, expected, type);

    // 41-60: Structure & Slicing (Memory Safety)
    [Fact]
    public void Memory_Slicing_DoesNotLeakNeighbors() {
        string[] buffer = { "PRE", "DATA", "data", "POST" };
        Verify(buffer.AsSpan(1, 2).ToArray(), new[] { "DATA" }, 'A');
    }

    [Fact]
    public void External_Mutation_After_Creation_HasNoEffect() {
        string[] input = { "Original" };
        var res = GetX(input.AsSpan());
        input[0] = "Mutated";
        Assert.Equal("Original", res.Items[0]);
    }

    // 61-80: Large Scale & Concentration
    [Theory]
    [InlineData(100, 'D')]
    [InlineData(1000, 'D')]
    public void Large_Unique_Sets(int count, char type) {
        var input = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
        Verify(input, input, type);
    }

    [Theory]
    [InlineData(100, 'C')]
    [InlineData(1000, 'C')]
    public void Large_Duplicate_Sets(int count, char type) {
        var input = Enumerable.Range(0, count).Select(i => (i % 2 == 0 ? "A" : "B")).ToArray();
        Verify(input, new[] { "A", "B" }, type);
    }

    // 81-100: Pathological String Content
    [Theory]
    [InlineData(new[] { "a\0b", "a\0b" }, new[] { "a\0b" }, 'C')]
    [InlineData(new[] { "a\0b", "a\0B" }, new[] { "a\0b" }, 'C')]
    [InlineData(new[] { "long_string_prefix_12345", "long_string_prefix_12345" }, new[] { "long_string_prefix_12345" }, 'C')]
    [InlineData(new[] { " ", "  ", "   " }, new[] { " ", "  ", "   " }, 'C')]
    public void Pathological_Content(string[] input, string[] expected, char type) => Verify(input, expected, type);
    // --- 1. The "Massive Redundancy" Matrix ---
    [Theory]
    [InlineData(1000, "Same", 'C')]  // 1000 identical
    [InlineData(10000, "Same", 'D')] // 10000 identical (Testing buffer limits)
    public void Massive_Redundancy_Tests(int count, string value, char type) {
        var input = Enumerable.Repeat(value, count).ToArray();
        Verify(input, new[] { value }, type);
    }

    // --- 2. The "Nearly Identical" Matrix (The 'One-Off' cases) ---
    [Theory]
    [InlineData(1000, "Same", "Different", 0, 'C')] // Different at start
    [InlineData(1000, "Same", "Different", 500, 'C')] // Different in middle
    [InlineData(1000, "Same", "Different", 999, 'C')] // Different at end
    public void Almost_Identical_Tests(int count, string baseVal, string diffVal, int diffIndex, char type) {
        var input = Enumerable.Repeat(baseVal, count).ToArray();
        input[diffIndex] = diffVal;

        // Expected depends on where the diff is (stability)
        var expected = (diffIndex == 0) ? new[] { diffVal, baseVal } : new[] { baseVal, diffVal };
        Verify(input, expected, type);
    }

    // --- 3. The "Gargantuan String" Matrix ---
    [Theory]
    [InlineData(10000, 'D')] // 10k characters
    [InlineData(100000, 'D')] // 100k characters (LOH territory)
    public void Gargantuan_String_Tests(int length, char type) {
        string bigStringLower = new string('a', length);
        string bigStringUpper = new string('A', length);

        // Should treat as same under OrdinalIgnoreCase
        var input = new[] { bigStringLower, bigStringUpper };
        Verify(input, new[] { bigStringLower }, type);
    }

    // --- 4. The "High Entropy" Matrix (Every item is unique) ---
    [Theory]
    [InlineData(10, 'B')]
    [InlineData(100, 'C')]
    [InlineData(1000, 'D')]
    public void High_Entropy_Unique_Tests(int count, char type) {
        var input = Enumerable.Range(0, count).Select(i => Guid.NewGuid().ToString()).ToArray();
        Verify(input, input, type);
    }

    // --- 5. The "Casing Collision" Torture Test ---
    [Fact]
    public void Casing_Torture_Every_Permutation() {
        // "Ab" "aB" "AB" "ab"
        string[] input = { "Ab", "aB", "AB", "ab", "cd", "CD", "Cd" };
        string[] expected = { "Ab", "cd" };
        Verify(input, expected, 'C');
    }

    // --- 6. The "Non-Standard Whitespace" Matrix ---
    [Theory]
    [InlineData(new[] { "\u00A0", " " }, 'C')] // Non-breaking space vs regular
    [InlineData(new[] { "\r\n", "\n" }, 'C')]   // CRLF vs LF
    public void Whitespace_Distinct_Tests(string[] input, char type) {
        // These are NOT equal under OrdinalIgnoreCase
        Verify(input, input, type);
    }

    // --- 7. The "Embedded Control Character" Matrix ---
    [Theory]
    [InlineData("Data\0Key", "DATA\0KEY", 'C')]
    [InlineData("Data\u0001", "DATA\u0001", 'C')]
    public void Control_Character_Collision_Tests(string v1, string v2, char type) {
        Verify(new[] { v1, v2 }, new[] { v1 }, type);
    }

    // --- 8. The "Overlapping Reference" Matrix ---
    [Fact]
    public void SubSpan_Ordering_Stability() {
        // Testing that the internal index logic doesn't get confused by offsets
        string[] storage = { "Z", "A", "B", "A", "Z" };
        var input = storage.AsSpan(1, 3); // ["A", "B", "A"]
        Verify(input.ToArray(), new[] { "A", "B" }, 'B');
    }
    // --- 9. The "Diagonal Collision" Matrix (X******, *X*****, **X****) ---
    // Every row has one unique item, but its position shifts. 
    // Or, in your Span context: A sequence where the 'Anchor' is surrounded by noise.
    [Fact]
    public void Diagonal_Collision_Torture() {
        // Pattern: Only the 'Target' is a duplicate, but its position is shifted 
        // amongst unique noise to prevent simple look-ahead optimization.
        string[] input = {
        "Target", "Unique1", "Unique2",
        "Unique3", "TARGET", "Unique4",
        "Unique5", "Unique6", "target"
    };
        string[] expected = { "Target", "Unique1", "Unique2", "Unique3", "Unique4", "Unique5", "Unique6" };

        Verify(input, expected, 'C');
    }

    // --- 10. The "Staircase" Matrix ---
    // Growing string lengths to test allocation/copying logic transitions.
    [Fact]
    public void Staircase_Length_Tests() {
        var input = Enumerable.Range(1, 100)
            .Select(i => new string('a', i))
            .ToArray(); // ["a", "aa", "aaa" ...]

        // All unique, but tests the internal growth of the backing array in X
        Verify(input, input, 'D');
    }

    // --- 11. The "Alternating Current" Matrix ---
    // [A, B, A, B, A, B] - Tests high-frequency hash-table thrashing
    [Theory]
    [InlineData(1000, 'C')]
    public void Alternating_High_Frequency_Duplicates(int count, char type) {
        var input = Enumerable.Range(0, count)
            .Select(i => i % 2 == 0 ? "Toggle-A" : "toggle-a")
            .ToArray();

        // Should result in exactly one item because of OrdinalIgnoreCase
        Verify(input, new[] { "Toggle-A" }, type);
    }

    // --- 12. The "Bookend" Matrix ---
    // Very large unique data in the middle, but the first and last items are duplicates.
    [Fact]
    public void Bookend_Collision_With_Massive_Middle() {
        var middle = Enumerable.Range(0, 1000).Select(i => $"Unique{i}").ToList();
        var input = new List<string> { "Collision" };
        input.AddRange(middle);
        input.Add("COLLISION");

        var expected = new List<string> { "Collision" };
        expected.AddRange(middle);

        Verify(input.ToArray(), expected.ToArray(), 'D');
    }

    // --- 13. The "Palindrome" Matrix ---
    // [A, B, C, B, A]
    [Theory]
    [InlineData(new[] { "Alpha", "Beta", "Gamma", "BETA", "alpha" }, new[] { "Alpha", "Beta", "Gamma" }, 'B')]
    public void Palindrome_Collision_Tests(string[] input, string[] expected, char type) {
        Verify(input, expected, type);
    }

    // --- 14. The "Zero-Width / Invisible" Matrix ---
    // Checking if the deduplication treats invisible characters as distinct
    [Fact]
    public void Invisible_Character_Permutations() {
        // Regular Space, Zero-width space, Non-breaking space
        string[] input = { "word", "word\u200B", "word\u00A0", "WORD" };
        string[] expected = { "word", "word\u200B", "word\u00A0" };

        Verify(input, expected, 'C');
    }*/
}