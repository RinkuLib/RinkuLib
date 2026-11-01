using Rinku.Tracking;
using Rinku.Tracking.Runtime;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public class ArrayValueTrackingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacement_arrays_are_isolated_and_compared_by_contents(bool attributes)
    {
        if (attributes)
        {
            Check(new AttributedRow { Values = [1, 2] }, RuntimeTracking.CreateOptions<AttributedRow>());
            return;
        }
        var options = RuntimeTracking.CreateOptions<Row>();
        options.Member<int[]>(nameof(Row.Values)).NestedEdit(NestedEditMode.Replacement);
        Check(new Row { Values = [1, 2] }, options);
    }

    private static void Check<T>(T original, RuntimeTrackingOptions<T> options) where T : Row
    {
        var item = options.GetRegistration<ITrackingItem<T>>().Create(original);
        Assert.True(item.EnsureEditing());
        Assert.NotSame(original.Values, item.Get<int[]>(nameof(Row.Values))!);
        Assert.False(item.HasChanges());
        item.Get<int[]>(nameof(Row.Values))![0] = 3;
        Assert.Equal([1, 2], original.Values);
        Assert.True(item.HasChanges());
        item.CancelEdit();
        Assert.Equal([1, 2], item.Get<int[]>(nameof(Row.Values))!);

        int[] replacement = [4, 5, 6];
        item.Set(nameof(Row.Values), replacement);
        replacement[0] = 99;
        Assert.Equal([4, 5, 6], item.Get<int[]>(nameof(Row.Values))!);
        int[] draft = item.Get<int[]>(nameof(Row.Values))!;
        item.ConfirmEdit();
        draft[0] = 99;
        Assert.Equal([4, 5, 6], original.Values);
        item.Set(nameof(Row.Values), new[] { 4, 5, 6 });
        Assert.False(item.HasChanges());
        item.Set(nameof(Row.Values), Array.Empty<int>());
        Assert.True(item.HasChanges());
        item.ConfirmEdit();
        Assert.Empty(original.Values);
    }

    public class Row
    {
        public virtual int[] Values { get; set; } = [];
    }
    public class AttributedRow : Row
    {
        [NestedEdit(NestedEditMode.Replacement)]
        public override int[] Values { get; set; } = [];
    }
}
