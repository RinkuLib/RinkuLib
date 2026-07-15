using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="LetterMap{T}"/> is a dictionary keyed on letters, case-insensitive, iterated in
/// alphabetical order, with a bit map of which letters are present.
/// </summary>
public class LetterMapTests {
    [Fact]
    public void Constructor_orders_entries_alphabetically() {
        var map = new LetterMap<int>([('z', 100), ('a', 1), ('m', 50)]);
        Assert.Equal(3, map.Count);
        Assert.Equal(['a', 'm', 'z'], map.Keys);
        Assert.Equal([1, 50, 100], map.Values);
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('z', 'Z')]
    [InlineData('M', 'm')]
    public void Keys_are_case_insensitive(char writeKey, char readKey) {
        var map = new LetterMap<int> { [writeKey] = 42 };
        Assert.Equal(42, map[readKey]);
        Assert.True(map.ContainsKey(readKey));
    }

    [Fact]
    public void Missing_key_throws_on_read() {
        var map = new LetterMap<int> { ['a'] = 1 };
        Assert.ThrowsAny<Exception>(() => map['b']);
    }

    [Fact]
    public void TryGetValue_reports_presence() {
        var map = new LetterMap<int> { ['a'] = 1 };
        Assert.True(map.TryGetValue('A', out var found));
        Assert.Equal(1, found);
        Assert.False(map.TryGetValue('b', out _));
    }

    [Fact]
    public void Indexer_set_overwrites_an_existing_letter() {
        var map = new LetterMap<int> { ['a'] = 1 };
        map['A'] = 9;
        Assert.Single(map);
        Assert.Equal(9, map['a']);
    }

    [Fact]
    public void Add_and_Remove_update_the_set() {
        var map = new LetterMap<int> {
            { 'b', 2 },
            new KeyValuePair<char, int>('c', 3)
        };
        Assert.Equal(2, map.Count);
        Assert.True(map.Remove('B'));
        Assert.False(map.Remove('z'));
        Assert.Single(map);
        Assert.False(map.ContainsKey('b'));
    }

    [Fact]
    public void Pair_removal_requires_a_matching_value() {
        var map = new LetterMap<int> { ['a'] = 1, ['m'] = 50 };
        Assert.True(map.Remove(new KeyValuePair<char, int>('M', 50)));
        Assert.False(map.Remove(new KeyValuePair<char, int>('a', 99)));
        Assert.True(map.ContainsKey('a'));
    }

    [Fact]
    public void Pair_containment_requires_a_matching_value() {
        var map = new LetterMap<int> { ['a'] = 1 };
        // LetterMap.Contains matches the key case-insensitively but still checks the value
#pragma warning disable xUnit2017
        Assert.True(map.Contains(new KeyValuePair<char, int>('A', 1)));
        Assert.False(map.Contains(new KeyValuePair<char, int>('a', 2)));
        Assert.False(map.Contains(new KeyValuePair<char, int>('b', 1)));
#pragma warning restore xUnit2017
    }

    [Fact]
    public void PresenceMap_sets_one_bit_per_letter() {
        var map = new LetterMap<int> { ['a'] = 1, ['c'] = 3 };
        Assert.Equal((1u << 0) | (1u << 2), map.PresenceMap);
    }

    [Fact]
    public void Clear_empties_the_map() {
        var map = new LetterMap<int> { ['a'] = 1, ['b'] = 2 };
        map.Clear();
        Assert.Empty(map);
        Assert.Equal(0u, map.PresenceMap);
    }

    [Fact]
    public void ResetWith_replaces_the_whole_content() {
        var map = new LetterMap<int> { ['a'] = 1 };
        map.ResetWith([('x', 10), ('y', 20)]);
        Assert.Equal(2, map.Count);
        Assert.False(map.ContainsKey('a'));
        Assert.Equal(10, map['x']);
        Assert.Equal(20, map['y']);
    }

    [Fact]
    public void CopyTo_and_enumeration_agree() {
        var map = new LetterMap<int>([('z', 100), ('a', 1), ('m', 50)]);
        var array = new KeyValuePair<char, int>[map.Count];
        map.CopyTo(array, 0);
        var enumerated = map.ToArray();
        Assert.Equal(array, enumerated);
        Assert.Equal('a', array[0].Key);
        Assert.Equal('z', array[2].Key);
    }
}
