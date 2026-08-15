using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// The result wrapper shapes convert to and from their value through implicit operators, and report
/// presence through HasValue.
/// </summary>
public class WrapperStructTests {
    [Fact]
    public void Optional_wraps_and_unwraps_a_reference() {
        Optional<string> some = "x";
        Assert.True(some.HasValue);
        string? back = some;
        Assert.Equal("x", back);
        Optional<string> none = (string?)null;
        Assert.False(none.HasValue);
        Assert.Null((string?)none);
        Assert.Equal("m", Optional<string>.Make("m").Value);
    }

    [Fact]
    public void OptionalStruct_wraps_and_unwraps_a_value() {
        OptionalStruct<int> some = (int?)5;
        Assert.True(some.HasValue);
        int? back = some;
        Assert.Equal(5, back);
        OptionalStruct<int> none = (int?)null;
        Assert.False(none.HasValue);
        Assert.Null((int?)none);
        Assert.Equal(6, OptionalStruct<int>.Make(6).Value);
    }

    [Fact]
    public void OptionalNullable_flattens_missing_and_null() {
        OptionalNullable<string> some = "x";
        Assert.True(some.HasValue);
        Assert.Equal("x", (string?)some);
        OptionalNullable<string> none = (string?)null;
        Assert.False(none.HasValue);
        Assert.Equal("m", OptionalNullable<string>.Make("m").Value);
    }

    [Fact]
    public void OptionalNullableStruct_flattens_missing_and_null() {
        OptionalNullableStruct<int> some = 5;
        Assert.True(some.HasValue);
        Assert.Equal(5, (int?)some);
        OptionalNullableStruct<int> none = (int?)null;
        Assert.False(none.HasValue);
        Assert.Null((int?)none);
        Assert.Equal(6, OptionalNullableStruct<int>.Make(6).Value);
    }

    [Fact]
    public void SingleOrDefault_wraps_and_unwraps_a_reference() {
        SingleOrDefault<string> value = "x";
        Assert.True(value.HasValue);
        Assert.Equal("x", (string?)value);
        Assert.False(default(SingleOrDefault<string>).HasValue);
    }

    [Fact]
    public void SingleOrDefaultStruct_wraps_and_unwraps_a_value() {
        SingleOrDefaultStruct<int> value = 5;
        Assert.True(value.HasValue);
        Assert.Equal(5, (int?)value);
        Assert.False(default(SingleOrDefaultStruct<int>).HasValue);
    }

    [Fact]
    public void nullable_SingleOrDefault_shapes_flatten_missing_and_null() {
        SingleOrDefaultNullable<string> reference = (string?)null;
        SingleOrDefaultNullableStruct<int> value = (int?)null;
        Assert.False(reference.HasValue);
        Assert.False(value.HasValue);
        Assert.Null((string?)reference);
        Assert.Null((int?)value);
    }

    [Fact]
    public void MaybeNull_carries_a_nullable_reference() {
        MaybeNull<string> some = "x";
        Assert.True(some.HasValue);
        Assert.Equal("x", (string?)some);
        MaybeNull<string> none = (string?)null;
        Assert.False(none.HasValue);
        Assert.Null((string?)none);
    }

    [Fact]
    public void Single_holds_exactly_its_value() {
        Single<int> one = 3;
        Assert.Equal(3, (int)one);
        Assert.Equal(3, one.Value);
        Assert.Equal(4, Single<int>.Make(4).Value);
    }
}
