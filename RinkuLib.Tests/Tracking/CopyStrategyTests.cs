using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tracking;
using Xunit;

namespace RinkuLib.Tests.Tracking;

/// <summary>
/// Tracking keeps a copy of the original to compare against, so a tracked type has to be copyable. These
/// cover the refusals raised while building that copy strategy.
/// </summary>
public class CopyStrategyTests {
    class MissingMethod {
        [CopyUsingMethod("NoSuchMethod")] public string? Value;
    }

    class MethodTakesParameters {
        [CopyUsingMethod(nameof(Clone))] public string? Value;
        string Clone(int unused) => Value + unused;
    }

    class MethodReturnsWrongType {
        [CopyUsingMethod(nameof(Clone))] public string? Value;
        int Clone() => 0;
    }

    class CopiesItself {
        [CopyUsingMethod(nameof(Clone))] public string? Value;
        string? Clone() => Value;
    }

    /// <summary>
    /// <see cref="Copier{T}"/> builds its strategy in a static initializer, so a refusal reaches the caller
    /// wrapped in a <see cref="TypeInitializationException"/> and the code sits on the inner one. Reading
    /// through the wrapper keeps these passing either way, so moving the build out of static
    /// initialization would not break them.
    /// </summary>
    static void Refused(string code, Action run) {
        var raised = Assert.ThrowsAny<Exception>(run);
        var unwrapped = raised is TypeInitializationException t && t.InnerException is not null
            ? t.InnerException
            : raised;
        Assert.Equal(code, Assert.IsAssignableFrom<RinkuException>(unwrapped).Code);
    }

    [Fact]
    public void A_copy_method_that_does_not_exist_is_refused()
        => Refused(ErrorCodes.CopyMethodNotUsable, () => Copier<MissingMethod>.Copy(new MissingMethod()));

    [Fact]
    public void A_copy_method_that_takes_parameters_is_refused()
        => Refused(ErrorCodes.CopyMethodNotUsable,
            () => Copier<MethodTakesParameters>.Copy(new MethodTakesParameters()));

    [Fact]
    public void A_copy_method_returning_the_wrong_type_is_refused()
        => Refused(ErrorCodes.CopyMethodNotUsable,
            () => Copier<MethodReturnsWrongType>.Copy(new MethodReturnsWrongType()));

    /// <summary>The shape the refusals describe, taking no parameters and returning the field's type.</summary>
    [Fact]
    public void A_copy_method_of_the_right_shape_is_used() {
        var copy = Copier<CopiesItself>.Copy(new CopiesItself { Value = "x" });
        Assert.Equal("x", copy!.Value);
    }
}
