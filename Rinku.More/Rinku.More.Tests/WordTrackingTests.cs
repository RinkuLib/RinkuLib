using Rinku.More.Globalization;
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

namespace Rinku.More.Tests;

public class WordTrackingTests
{
    [Fact]
    public void Word_follows_the_standard_copy_and_equality_contracts()
    {
        var original = Word.FromKey("Original");
        original.Add("fr", "Avant");
        ITrackingValue<Word> value = original;
        Word copy = value.Copy();
        Assert.NotSame(original, copy);
        Assert.True(original.Equals(copy));
        Assert.Equal(original.GetHashCode(), copy.GetHashCode());
        Assert.Equal(original.Key, copy.Key);
        copy["fr"] = "Après";
        Assert.Equal("Avant", original["fr"]);
        Assert.False(original.Equals(copy));
    }

    [Fact]
    public void Word_mutation_is_owned_by_the_tracking_snapshot_and_revert_restores_it()
    {
        var original = new Row { Text = new Word { ["en"] = "Before" } };
        var rows = new[] { original }.ToTrackingList<Row, IEdit>();
        var edit = rows[0];
        ((IEditable)edit).EnsureEditing();
        Assert.NotSame(original.Text, edit.Text);
        Assert.False(rows.HasChanges);
        edit.Text["en"] = "After";
        Assert.Equal("Before", original.Text["en"]);
        Assert.True(rows.HasChanges);
        ((IEditable)edit).CancelEdit();
        Assert.Equal("Before", edit.Text["en"]);
        Assert.False(rows.HasChanges);
    }

    public class Row
    {
        public Word Text { get; set; } = new();
    }
    public interface IEdit
    {
        Word Text { get; set; }
    }
}
