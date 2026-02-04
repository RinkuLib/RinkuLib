using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Tools;

public class LetterMapTests {

    [Fact]
    public void For_Test_Coverage() {
        var items = new[] { ('z', 100), ('a', 1), ('m', 50) };
        var map = new LetterMap<int>(items.Select(x => new ValueTuple<char, int>(x.Item1, x.Item2)).ToArray());
        var arr = new KeyValuePair<char, int>[map.Count];
        map.CopyTo(arr, 0);
        Assert.Equal('a', arr[0].Key);
        Assert.Equal(1, arr[0].Value);
        Assert.Equal('m', arr[1].Key);
        Assert.Equal(50, arr[1].Value);
        Assert.Equal('z', arr[2].Key);
        Assert.Equal(100, arr[2].Value);
        var i = 0;
        foreach (var item in map) {
            Assert.Equal(arr[i].Key, item.Key);
            Assert.Equal(arr[i].Value, item.Value);
            i++;
        }
#pragma warning disable xUnit2017
        Assert.True(map.Contains(new('M', 50)));
        Assert.True(map.Contains(new('a', 1)));
        Assert.False(map.Contains(new('a', 10)));
        Assert.False(map.Contains(new('b', 10)));
        map.Add(new('b', 10));
        map.Add('c', 10);
        Assert.True(map.Contains(new('b', 10)));
#pragma warning restore xUnit2017
        Assert.True(map.ContainsKey('c'));
        Assert.True(map.Remove(new KeyValuePair<char, int>('M', 50)));
        Assert.False(map.Remove(new KeyValuePair<char, int>('a', 50)));
    }

    #region 1. Initialization & Case Insensitivity

    [Fact]
    public void Constructor_Populates_Alphabetical_Order() {
        // Even if added out of order, iteration should be alphabetical
        var items = new[] { ('z', 100), ('a', 1), ('m', 50) };
        var map = new LetterMap<int>(items.Select(x => new ValueTuple<char, int>(x.Item1, x.Item2)).ToArray());

        Assert.Equal(3, map.Count);
        var keys = map.Keys.ToArray();
        Assert.Equal('a', keys[0]);
        Assert.Equal('m', keys[1]);
        Assert.Equal('z', keys[2]);
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('z', 'Z')]
    [InlineData('M', 'm')]
    public void Keys_Are_Case_Insensitive(char key1, char key2) {
        var map = new LetterMap<int>() {
            [key1] = 42
        };

        Assert.True(map.ContainsKey(key2));
        Assert.Equal(42, map[key2]);
    }

    #endregion

    #region 2. Bitmask & Rank Integrity (The Core Logic)

    [Fact]
    public void Adding_Middle_Letter_Shifts_Internal_Values_Correctly() {
        // Initial: A and Z
        var map = new LetterMap<string>([('a', "Apple"), ('z', "Zebra")]) {
            // Add 'M' in the middle. Rank logic must correctly shift 'Zebra' in the array.
            ['m'] = "Mango"
        };

        Assert.Equal(3, map.Count);
        Assert.Equal("Apple", map['a']);
        Assert.Equal("Mango", map['m']);
        Assert.Equal("Zebra", map['z']);
    }

    [Fact]
    public void PresenceMap_Reflects_Correct_Bits() {
        var map = new LetterMap<int>([('a', 1), ('b', 2), ('z', 26)]);

        // Bit 0 (a), Bit 1 (b), Bit 25 (z)
        uint expectedMask = (1u << 0) | (1u << 1) | (1u << 25);
        Assert.Equal(expectedMask, map.PresenceMap);
    }

    #endregion

    #region 3. Boundary & Error Conditions

    [Theory]
    [InlineData('1')]
    [InlineData('!')]
    [InlineData('é')]
    [InlineData(' ')]
    public void Invalid_Characters_Throw_ArgumentOutOfRangeException(char invalidKey) {
        var map = new LetterMap<int>([]);
        Assert.Throws<ArgumentOutOfRangeException>(() => map[invalidKey] = 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.ContainsKey(invalidKey));
    }

    [Fact]
    public void Accessing_Missing_Key_Throws_KeyNotFoundException() {
        var map = new LetterMap<int>([('a', 1)]);
        Assert.Throws<KeyNotFoundException>(() => map['b']);
    }

    #endregion

    #region 4. Collection Mutation (Add/Remove/Clear)

    [Fact]
    public void Remove_Correctly_Collapses_Internal_Array() {
        var map = new LetterMap<int>([('a', 1), ('b', 2), ('c', 3)]);

        bool removed = map.Remove('b');

        Assert.True(removed);
        Assert.Equal(2, map.Count);
        Assert.False(map.ContainsKey('b'));
        Assert.Equal(1, map['a']);
        Assert.Equal(3, map['c']);

        // Verify PresenceMap bit 1 is cleared
        Assert.Equal(0u, (map.PresenceMap & (1u << 1)));
    }

    [Fact]
    public void ResetWith_Handles_Duplicates_By_Keeping_Last_Value() {
        var map = new LetterMap<int>([]);
        var items = new ValueTuple<char, int>[] { ('a', 1), ('b', 2), ('A', 10) };

        map.ResetWith(items);

        Assert.Equal(2, map.Count);
        Assert.Equal(10, map['a']); // The 'A' (10) overwrote 'a' (1)
    }

    [Fact]
    public void Clear_Resets_State_Completely() {
        var map = new LetterMap<int>([('x', 1), ('y', 2)]);
        map.Clear();

        Assert.Empty(map);
        Assert.Equal(0u, map.PresenceMap);
        Assert.Empty(map.Keys);
    }

    #endregion
}