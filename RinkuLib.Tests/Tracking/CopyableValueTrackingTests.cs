using Rinku.Tracking;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public class CopyableValueTrackingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copyable_members_are_isolated_compared_and_rolled_back_without_type_specific_setup(bool binding)
    {
        var original = new Row { Value = new(1) };
        Row[] source = [original];
        TrackingList<IEdit> rows = binding ? source.ToBindingList<Row, IEdit>() : source.ToTrackingList<Row, IEdit>();
        var edit = rows[0];
        Assert.False(rows.HasChanges);
        ((IEditable)edit).EnsureEditing();
        Assert.NotSame(original.Value, edit.Value);
        Assert.False(rows.HasChanges);
        edit.Value!.Number = 2;
        Assert.Equal(1, original.Value.Number);
        Assert.True(rows.HasChanges);
        ((IEditable)edit).CancelEdit();
        Assert.Equal(1, edit.Value!.Number);
        Assert.False(rows.HasChanges);
        var supplied = new Value(3);
        edit.Value = supplied;
        supplied.Number = 4;
        Assert.Equal(3, edit.Value!.Number);
        var retained = edit.Value;
        Assert.True(rows.ConfirmEditAt(0));
        retained.Number = 5;
        Assert.Equal(3, original.Value.Number);
        Assert.False(rows.HasChanges);
        edit.Value = null;
        Assert.True(rows.HasChanges);
        ((IEditable)edit).CancelEdit();
        Assert.Equal(3, edit.Value!.Number);
    }

    public class Row
    {
        public Value? Value { get; set; }
    }
    public interface IEdit
    {
        Value? Value { get; set; }
    }
    public class Value(int number) : ITrackingValue<Value>
    {
        public int Number { get; set; } = number;
        public Value Copy() => new(Number);
        public bool Equals(Value? other) => other?.Number == Number;
        public override bool Equals(object? other) => other is Value value && Equals(value);
        public override int GetHashCode() => Number;
    }
}
