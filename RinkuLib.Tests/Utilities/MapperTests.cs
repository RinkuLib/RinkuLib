using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="Mapper"/> numbers a set of keys once and answers name lookups case-insensitively, with
/// specialized shapes for zero, one, two, and many keys.
/// </summary>
public class MapperTests {
    [Fact]
    public void Empty_mapper_finds_nothing() {
        var mapper = Mapper.GetEmptyMapper();
        Assert.Empty(mapper);
        Assert.Equal(-1, mapper.GetIndex("anything"));
        Assert.Equal(-1, mapper.GetIndex("anything".AsSpan()));
    }

    [Fact]
    public void Empty_key_set_returns_the_empty_mapper() {
        using var mapper = Mapper.GetMapper([]);
        Assert.Empty(mapper);
    }

    [Fact]
    public void Single_key_mapper_matches_only_its_key() {
        using var mapper = Mapper.GetMapper(["Alpha"]);
        Assert.Single(mapper);
        Assert.Equal(0, mapper.GetIndex("Alpha"));
        Assert.Equal(0, mapper.GetIndex("ALPHA"));
        Assert.Equal(0, mapper.GetIndex("ALPHA".AsSpan()));
        Assert.Equal(-1, mapper.GetIndex("Beta"));
        Assert.Equal(-1, mapper.GetIndex("Beta".AsSpan()));
    }

    [Fact]
    public void Two_key_mapper_distinguishes_both() {
        using var mapper = Mapper.GetMapper(["First", "Second"]);
        Assert.Equal(0, mapper.GetIndex("first"));
        Assert.Equal(1, mapper.GetIndex("SECOND"));
        Assert.Equal(-1, mapper.GetIndex("Third"));
    }

    [Fact]
    public void TryGetValue_Correctly_Outputs_Index() {
        string[] keys = ["A", "B", "C"];
        using var mapper = Mapper.GetMapper(keys);

        bool found = mapper.TryGetValue("B", out int index);
        bool found2 = mapper.TryGetValue("B".AsSpan(), out int index2);
        bool missing = mapper.TryGetValue("Z", out int missingIndex);
        var i = 0;
        foreach (var (key, idx) in mapper) {
            Assert.Equal(i, idx);
            Assert.Same(keys[i], key);
            i++;
        }
        Assert.True(found);
        Assert.Equal(1, index);
        Assert.True(found2);
        Assert.Equal(1, index2);
        Assert.False(missing);
        Assert.Equal(-1, missingIndex);
    }
    [Theory]
    [InlineData("Alpha", 0)]
    [InlineData("BETA", 1)]
    [InlineData("gamma", 2)]
    [InlineData("Delta", -1)]
    public void Lookup_is_case_insensitive(string key, int expected) {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);
        Assert.Equal(expected, mapper.GetIndex(key));
        Assert.Equal(expected, mapper[key]);
        Assert.Equal(expected, mapper.GetIndex(key.AsSpan()));
        Assert.Equal(expected, mapper[key.AsSpan()]);
    }

    [Fact]
    public void Span_lookup_ignores_surrounding_text() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);
        ReadOnlySpan<char> span = "PoisonBETAPoison".AsSpan(6, 4);
        Assert.Equal(1, mapper.GetIndex(span));
    }

    [Fact]
    public void Input_order_becomes_the_index_order() {
        string[] keys = ["K5", "K1", "K9", "K3", "K7", "K2", "K8", "K0", "K4", "K6"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++) {
            Assert.Equal(i, mapper.GetIndex(keys[i]));
            Assert.Equal(keys[i], mapper.GetKey(i));
        }
        var idx = 0;
        foreach (var key in ((IReadOnlyDictionary<string, int>)mapper).Keys) {
            Assert.Equal(keys[idx], key);
            idx++;
        }
    }

    [Fact]
    public void Duplicate_keys_keep_the_first_occurrence() {
        using var mapper = Mapper.GetMapper(["One", "Two", "ONE"]);
        Assert.Equal(2, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("one"));
        Assert.Equal(1, mapper.GetIndex("Two"));
        using var mapper2 = Mapper.GetMapper(["One", "ONE"]);
        Assert.Single(mapper2);
        Assert.Equal(0, mapper2.GetIndex("ONE"));
        using var mapper3 = Mapper.GetMapper(["One", "ONE", "oNe"]);
        Assert.Single(mapper3);
        Assert.Equal(0, mapper3.GetIndex("OnE".AsSpan()));
    }

    [Fact]
    public void Non_ascii_keys_stay_case_insensitive() {
        using var mapper = Mapper.GetMapper(["Écran", "Naïve", "Straße"]);
        Assert.Equal(0, mapper.GetIndex("écran"));
        Assert.Equal(1, mapper.GetIndex("NAÏVE"));
        Assert.Equal(-1, mapper.GetIndex("Passé"));
    }

    [Fact]
    public void Fullwidth_letters_fold_together() {
        using var mapper = Mapper.GetMapper(["Ａ", "ａ", "Ｂ", "က"]);
        Assert.Equal(3, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("ａ"));
        Assert.Equal(1, mapper.GetIndex("ｂ"));
        Assert.Equal(-1, mapper.GetIndex("တ"));
    }

    [Fact]
    public void GetKey_returns_the_original_casing() {
        using var mapper = Mapper.GetMapper(["CamelCase", "UPPER", "lower"]);
        Assert.Equal("CamelCase", mapper.GetKey(0));
        Assert.Equal("UPPER", mapper.GetKey(1));
        Assert.Equal("lower", mapper.GetKey(2));
    }

    [Fact]
    public void GetSameKey_returns_the_stored_string_instance() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta"]);
        var stored = mapper.GetKey(1);
        Assert.Same(stored, mapper.GetSameKey("BETA"));
        Assert.Same(stored, mapper.GetSameKey("beta".AsSpan()));
    }

    [Fact]
    public void GetSameKey_returns_null_when_not_present() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta"]);
        var stored = mapper.GetKey(1);
        Assert.Null(mapper.GetSameKey("omega"));
        Assert.Null(mapper.GetSameKey("OMEGA".AsSpan()));
    }

    [Fact]
    public void GetIndex_with_a_prepend_char_matches_the_composed_key() {
        using var mapper = Mapper.GetMapper(["@Name", "@Other"]);
        Assert.Equal(0, mapper.GetIndex('@', "Name"));
        Assert.Equal(1, mapper.GetIndex('@', "Other"));
        Assert.Equal(-1, mapper.GetIndex('@', "Missing"));
    }

    [Fact]
    public void Dictionary_interface_works() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);
        Assert.True(mapper.ContainsKey("b"));
        Assert.False(mapper.ContainsKey("Z"));
        Assert.True(mapper.ContainsKey("b".AsSpan()));
        Assert.True(mapper.TryGetValue("C", out var index));
        Assert.Equal(2, index);
        Assert.False(mapper.TryGetValue("Z", out _));
        Assert.Equal([0, 1, 2], mapper.Values);
        Assert.Equal(["A", "B", "C"], mapper.Select(kv => kv.Key));
    }

    [Fact]
    public void Keys_span_exposes_every_key() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);
        Assert.Equal(["A", "B", "C"], mapper.Keys.ToArray());
    }

    [Fact]
    public void Long_keys_and_many_keys_still_resolve() {
        var keys = Enumerable.Range(0, 60).Select(i => $"Column_With_A_Long_Name_{i}").ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(60, mapper.Count);
        Assert.Equal(37, mapper.GetIndex("column_with_a_long_name_37"));
        Assert.Equal(-1, mapper.GetIndex("Column_With_A_Long_Name_60"));
    }

    [Fact]
    public void Enumerable_input_builds_the_same_mapper() {
        using var mapper = Mapper.GetMapper(Enumerable.Range(0, 5).Select(i => $"K{i}"));
        Assert.Equal(5, mapper.Count);
        Assert.Equal(3, mapper.GetIndex("K3"));
    }

    [Fact]
    public void Array_input_builds_the_same_mapper() {
        string[] strings = [.. Enumerable.Range(0, 5).Select(i => $"K{i}")];
        using var mapper = Mapper.GetMapper((IEnumerable<string>)strings);
        Assert.Equal(5, mapper.Count);
        Assert.Equal(3, mapper.GetIndex("K3"));
    }
    [Fact]
    public void List_input_builds_the_same_mapper() {
        using var mapper = Mapper.GetMapper(Enumerable.Range(0, 5).Select(i => $"K{i}").ToList());
        Assert.Equal(5, mapper.Count);
        Assert.Equal(3, mapper.GetIndex("K3"));
    }
    [Fact]
    public void ReadonlyCollection_input_builds_the_same_mapper() {
        using var mapper = Mapper.GetMapper(Array.AsReadOnly(Enumerable.Range(0, 5).Select(i => $"K{i}").ToArray()));
        Assert.Equal(5, mapper.Count);
        Assert.Equal(3, mapper.GetIndex("K3"));
    }
    

    [Fact]
    public void Non_generic_enumeration_yields_the_pairs() {
        using var mapper = Mapper.GetMapper(["A", "B", "C"]);
        var seen = new List<KeyValuePair<string, int>>();
        var e = ((System.Collections.IEnumerable)mapper).GetEnumerator();
        while (e.MoveNext())
            seen.Add((KeyValuePair<string, int>)e.Current!);
        Assert.Equal(["A", "B", "C"], seen.Select(p => p.Key));
        Assert.Equal([0, 1, 2], seen.Select(p => p.Value));
    }

    [Fact]
    public void One_and_two_key_factories_reject_null_keys() {
        Assert.Throws<NullReferenceException>(() => Mapper.GetOneKeyMapper(null!));
        Assert.Throws<NullReferenceException>(() => Mapper.GetTwoKeyMapper(null!, "b"));
        Assert.Throws<NullReferenceException>(() => Mapper.GetTwoKeyMapper("a", null!));
    }

    [Fact]
    public void Optimised_mapper_returns_minus_one_for_a_null_string_key() {
        using var mapper = Mapper.GetMapper(["Alpha", "Beta", "Gamma"]);
        Assert.Equal(-1, mapper.GetIndex((string)null!));
    }

    [Fact]
    public void Deeply_nested_keys_navigate_many_steps() {
        var keys = Enumerable.Range(4, 4).SelectMany(BinaryCombinations).ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
        Assert.Equal(-1, mapper.GetIndex("cccccc"));
    }

    [Fact]
    public void Long_key_misses_compare_the_full_candidate() {
        using var mapper = Mapper.GetMapper(["Alphabet_Long", "Betabet_Longer", "Gammabet_Longs"]);
        Assert.Equal(0, mapper.GetIndex("Alphabet_Long"));
        Assert.Equal(-1, mapper.GetIndex("Alphabet_LonZ"));
        Assert.Equal(-1, mapper.GetIndex("AlXXXbet_Long"));
        Assert.Equal(-1, mapper.GetIndex("Zlphabet_Long"));
    }

    [Fact]
    public void Short_key_misses_take_the_scalar_path() {
        using var mapper = Mapper.GetMapper(["@ab", "def", "ghi", "jkl"]);
        Assert.Equal(0, mapper.GetIndex("@ab"));
        Assert.Equal(-1, mapper.GetIndex("`ab"));
        Assert.Equal(-1, mapper.GetIndex("dez"));
        Assert.Equal(-1, mapper.GetIndex("xyz"));
    }

    [Theory]
    [InlineData(28)]
    [InlineData(60)]
    [InlineData(120)]
    public void Keys_spanning_many_distinct_lengths_build(int max) {
        var keys = Enumerable.Range(1, max).Select(n => new string('a', n - 1) + "Z" + n).ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void A_null_key_in_the_set_is_rejected() {
        Assert.Throws<NullReferenceException>(() => Mapper.GetMapper(["A", null!, "B", "C"]));
    }

    [Fact]
    public void An_empty_string_is_a_valid_distinct_key() {
        string[] keys = ["", "aX", "bX", "cX"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
        Assert.Equal(0, mapper.GetIndex(""));
        Assert.Equal(-1, mapper.GetIndex("zzz"));
    }

    [Fact]
    public void Keys_the_perfect_hash_cannot_separate_fall_back_to_a_dictionary() {
        var keys = new[] { "colı", "colµ", "colX", "colY", "colZ" };
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++) {
            Assert.Equal(i, mapper.GetIndex(keys[i]));
            Assert.Equal(i, mapper.GetIndex(keys[i].AsSpan()));
        }
        Assert.Equal(-1, mapper.GetIndex("colW"));
        Assert.Equal(-1, mapper.GetIndex("colW".AsSpan()));
        Assert.Equal(2, mapper.GetIndex("COLX"));
    }

    [Fact]
    public void Dictionary_fallback_collapses_when_duplicates_fold_away() {
        var keys = new[] { "colı", "colµ", "COLı" };
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(2, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("colı"));
        Assert.Equal(0, mapper.GetIndex("COLı"));
        Assert.Equal(1, mapper.GetIndex("colµ"));
    }

    [Fact]
    public void Long_key_misses_through_the_span_path() {
        using var mapper = Mapper.GetMapper(["Alphabet_Long", "Betabet_Longer", "Gammabet_Longs"]);
        Assert.Equal(0, mapper.GetIndex("Alphabet_Long".AsSpan()));
        Assert.Equal(-1, mapper.GetIndex("Alphabet_LonZ".AsSpan()));
        Assert.Equal(-1, mapper.GetIndex("AlXXXbet_Long".AsSpan()));
        Assert.Equal(-1, mapper.GetIndex("Alphabet".AsSpan()));
    }

    [Fact]
    public void A_wide_character_range_at_one_position_uses_a_big_mask() {
        string[] keys = ["`", "@", "A", "1", " ", "1`", "1@", "1A", "11", "2`", "2@", "2A", "21"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void A_wide_character_range_spanning_the_full_ascii_set_builds() {
        var keys = Enumerable.Range(32, 65).Select(i => "p" + (char)i + "s").ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void Every_distinct_length_up_to_255_builds() {
        var keys = Enumerable.Range(0, 256).Select(i => new string('0', i)).ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(256, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void More_distinct_lengths_than_a_step_table_can_hold_still_resolve() {
        var keys = Enumerable.Range(0, 257).Select(i => new string('0', i)).ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(257, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void An_extremely_long_shared_prefix_resolves() {
        string prefix = new('z', 5000);
        string[] keys = [prefix + "Alpha", prefix + "Beta", prefix + "Gamma", prefix + "Delta"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
        Assert.Equal(-1, mapper.GetIndex(prefix + "Zeta"));
    }

    [Fact]
    public void Interleaved_case_variants_dedup_to_the_first_occurrence() {
        string[] keys = ["Key1", "Key2", "KEY1", "Key3", "key2", "KEY3"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(3, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("key1"));
        Assert.Equal(1, mapper.GetIndex("KEY2"));
        Assert.Equal(2, mapper.GetIndex("key3"));
    }

    [Fact]
    public void Groups_that_differ_only_by_case_collapse() {
        string[] keys = ["Ab", "aB", "AB", "ab", "cd", "CD", "Cd"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(2, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("AB"));
        Assert.Equal(1, mapper.GetIndex("cD"));
    }

    [Fact]
    public void Repeated_string_instances_dedup_by_reference() {
        var shared = "CommonKey";
        string[] keys = [shared, shared, shared, "Unique1", "Unique2"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(3, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("commonkey"));
        Assert.Equal(1, mapper.GetIndex("Unique1"));
    }

    [Fact]
    public void A_letter_colliding_with_a_control_character_slot_falls_back() {
        string[] keys = ["pA", "p" + (char)0x81, "pB", "pC"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Fact]
    public void A_nested_group_the_builder_cannot_split_falls_back() {
        string[] keys = ["Aı", "Aµ", "AX", "Bı", "Bµ", "BX"];
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Distinct_length_counts_pick_each_length_comparer_tier(int count) {
        var keys = Enumerable.Range(0, count).Select(i => new string('0', i)).ToArray();
        using var mapper = Mapper.GetMapper(keys.AsSpan());
        Assert.Equal(count, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(i, mapper.GetIndex(keys[i]));
    }

    static string[] BinaryCombinations(int len) {
        int n = 1 << len;
        var keys = new string[n];
        for (int i = 0; i < n; i++) {
            var c = new char[len];
            for (int b = 0; b < len; b++)
                c[b] = ((i >> b) & 1) == 0 ? 'a' : 'b';
            keys[i] = new string(c);
        }
        return keys;
    }
}
