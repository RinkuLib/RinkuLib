using RinkuLib.Exceptions;
using Xunit;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// The refusals the library raises, asserted through one seam. Each helper checks the code that names the
/// condition rather than the wording of the message, so a rewrite of the text lands here instead of in
/// every test that expects one.
/// </summary>
public static class Refusals {
    /// <summary>Asserts <paramref name="run"/> was refused with <paramref name="code"/>.</summary>
    public static RinkuException Raises(string code, Action run) {
        var ex = Assert.ThrowsAny<Exception>(run);
        var rinku = Assert.IsAssignableFrom<RinkuException>(ex);
        Assert.Equal(code, rinku.Code);
        return rinku;
    }

    /// <inheritdoc cref="Raises(string, Action)"/>
    public static async Task<RinkuException> RaisesAsync(string code, Func<Task> run) {
        var ex = await Assert.ThrowsAnyAsync<Exception>(run);
        var rinku = Assert.IsAssignableFrom<RinkuException>(ex);
        Assert.Equal(code, rinku.Code);
        return rinku;
    }

    /// <summary>
    /// Asserts no parser could be built for <typeparamref name="T"/>, and that the refusal names the type
    /// it gave up on so the caller knows which shape failed.
    /// </summary>
    public static RinkuNoParserException NoParserFor<T>(Action build) {
        var ex = Assert.ThrowsAny<Exception>(build);
        var refused = Assert.IsType<RinkuNoParserException>(ex);
        Assert.Equal(ErrorCodes.NoParserForSchema, refused.Code);
        Assert.Equal(typeof(T), refused.TargetType);
        return refused;
    }

    /// <summary>Asserts a row held a null the target refuses, naming the slot that rejected it.</summary>
    public static RinkuException NullNotAllowed(string slotName, Action parse) {
        var refused = Raises(ErrorCodes.NullNotAllowed, parse);
        Assert.Contains(slotName, refused.Message);
        return refused;
    }

    /// <summary>Asserts every code carries a help link pointing at its documented entry.</summary>
    public static void HasHelpLink(RinkuException ex)
        => Assert.EndsWith(ex.Code.ToLowerInvariant(), ex.HelpLink);
}
