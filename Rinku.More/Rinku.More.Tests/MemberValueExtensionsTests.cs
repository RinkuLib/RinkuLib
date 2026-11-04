namespace Rinku.More.Tests;

public class MemberValueExtensionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Static_values_filter_by_read_visibility_and_keep_the_original_instances(bool? isPublic)
    {
        var values = typeof(Members).GetStaticMemberValues<IReadOnlyList<int>>(isPublic).ToDictionary(item => item.Name, item => item.Value);
        string[] expected = isPublic switch
        {
            true => [nameof(Members.PublicField), nameof(Members.PublicProperty)],
            false => ["PrivateField", "PrivateProperty", nameof(Members.PrivateGetter)],
            null => [nameof(Members.PublicField), nameof(Members.PublicProperty), "PrivateField", "PrivateProperty", nameof(Members.PrivateGetter)]
        };
        Assert.Equal(expected.Order(), values.Keys.Order());
        if (isPublic is not false)
        {
            Assert.Same(Members.PublicField, values[nameof(Members.PublicField)]);
            Assert.Same(Members.PublicProperty, values[nameof(Members.PublicProperty)]);
        }
        if (isPublic is not true)
        {
            Assert.Same(values["PrivateField"], values["PrivateProperty"]);
            Assert.Same(values["PrivateField"], values[nameof(Members.PrivateGetter)]);
        }
    }

    private class Members
    {
        public static readonly int[] PublicField = [1];
        private static readonly int[] PrivateField = [2];
        public static IReadOnlyList<int> PublicProperty => PublicField;
        private static IReadOnlyList<int> PrivateProperty => PrivateField;
        public static IReadOnlyList<int>? Empty => null;
        public static string Unrelated => throw new InvalidOperationException();
        public IReadOnlyList<int> Instance => throw new InvalidOperationException();
        public IReadOnlyList<int> this[int index] => throw new InvalidOperationException();
        public static IReadOnlyList<int> WriteOnly
        {
            set { }
        }
        public static IReadOnlyList<int> PrivateGetter
        {
            private get => PrivateField;
            set { }
        }
    }
}
