using System.Reflection;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The properties every code must hold, checked over the whole set rather than one condition at a time.
/// A code is permanent, so a duplicate or a renumbering is a defect in the catalog itself.
/// </summary>
public class ErrorCodeTests {
    static readonly (string Name, string Code)[] All = [..
        typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))];

    [Fact]
    public void The_catalog_is_not_empty() => Assert.NotEmpty(All);

    [Fact]
    public void Every_code_is_unique() {
        var duplicates = All.GroupBy(c => c.Code).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} on {string.Join(" and ", g.Select(c => c.Name))}");
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_code_has_the_RINKU_shape() {
        foreach (var (name, code) in All) {
            Assert.StartsWith("RINKU", code);
            Assert.Equal(9, code.Length);
            Assert.True(code[5..].All(char.IsAsciiDigit), $"{name} is not four digits: {code}");
        }
    }

    [Fact]
    public void Every_code_sits_in_a_defined_band() {
        int[] bands = [1, 2, 3, 4, 5, 6, 9];
        foreach (var (name, code) in All)
            Assert.True(bands.Contains(int.Parse(code[5..]) / 1000), $"{name} is outside the defined bands: {code}");
    }

    [Fact]
    public void A_raised_condition_carries_its_code_and_a_help_link() {
        var refused = Refusals.Raises(ErrorCodes.QueryTooShort,
            () => new RinkuLib.Queries.QueryCommand("a"));
        Assert.StartsWith(ErrorCodes.QueryTooShort, refused.Message);
        Refusals.HasHelpLink(refused);
        Assert.Contains("errors", refused.HelpLink);
    }

    /// <summary>
    /// An internal invariant says plainly that reaching it is a library bug, so a user does not go looking
    /// for the mistake in their own code.
    /// </summary>
    [Fact]
    public void An_internal_invariant_names_itself_as_a_bug() {
        var internalFailure = new RinkuInternalException(ErrorCodes.InternalInvariant, "test invariant");
        Assert.Contains("bug in RinkuLib", internalFailure.Message);
        Assert.Equal(ErrorCodes.InternalInvariant, internalFailure.Code);
    }
}
