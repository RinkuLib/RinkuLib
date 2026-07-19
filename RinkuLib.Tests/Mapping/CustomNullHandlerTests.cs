using System.Reflection.Emit;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// A null rule supplied from outside the library drives the emission choices the built-in rules never
/// reach: the long-form branch and the root jump target.
/// </summary>
public class CustomNullHandlerTests {
    sealed class LongBranchHandler : INullColHandler {
        public static readonly LongBranchHandler Instance = new();
        public bool IsBr_S(Type closedType) => false;
        public bool NeedNullJumpSetPoint(Type closedType) => false;
        public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
            var endLabel = generator.DefineLabel();
            DbItemParser.EmitDefaultValue(closedType, generator);
            generator.Emit(OpCodes.Br, endLabel);
            return endLabel;
        }
        public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) => this;
    }

    sealed class CollapsingHandler : INullColHandler {
        public static readonly CollapsingHandler Instance = new();
        public bool IsBr_S(Type closedType) => false;
        public bool NeedNullJumpSetPoint(Type closedType) => true;
        public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
            nullSetPoint.MakeNullJump(generator);
            return null;
        }
        public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) => this;
    }

    public record Pair(int Id, string Name) : IDbReadable;

    public record Collapsible([InvalidOnNull] int? Id, string Name) : IDbReadable;

    static readonly ColumnInfo[] PairCols = [new("Id", typeof(int), true), new("Name", typeof(string), true)];

    [Fact]
    public void A_scalar_read_under_a_custom_rule_emits_the_long_branch() {
        ColumnInfo[] cols = [new("V", typeof(int), true)];
        var parser = TypeParser.GetTypeParser<int>(ref cols, LongBranchHandler.Instance);
        using var reader = Rows.Reader(cols, [7]);
        reader.Read();
        Assert.Equal(7, parser.Parse(reader).Result);

        ColumnInfo[] nullCols = [new("V", typeof(int), true)];
        var nullParser = TypeParser.GetTypeParser<int>(ref nullCols, LongBranchHandler.Instance);
        using var nullReader = Rows.Reader(nullCols, [DBNull.Value]);
        nullReader.Read();
        Assert.Equal(0, nullParser.Parse(nullReader).Result);
    }

    [Fact]
    public void An_object_read_under_a_custom_rule_emits_the_long_branch() {
        var cols = PairCols;
        var parser = TypeParser.GetTypeParser<Pair>(ref cols, LongBranchHandler.Instance);
        using var reader = Rows.Reader(PairCols, [1, "a"]);
        reader.Read();
        var parsed = parser.Parse(reader).Result;
        Assert.Equal(1, parsed.Id);
        Assert.Equal("a", parsed.Name);
    }

    [Fact]
    public void A_root_marked_invalid_on_null_collapses_to_the_default() {
        var cols = PairCols;
        var parser = TypeParser.GetTypeParser<Collapsible>(ref cols, InvalidOnNullAndNullableHandle.Instance);
        using var reader = Rows.Reader(PairCols, [3, "c"]);
        reader.Read();
        Assert.Equal(3, parser.Parse(reader).Result!.Id);

        using var nullReader = Rows.Reader(PairCols, [DBNull.Value, "c"]);
        nullReader.Read();
        Assert.Null(parser.Parse(nullReader).Result);
    }

    [Fact]
    public void A_root_that_asks_for_a_jump_target_gets_one() {
        var cols = PairCols;
        var parser = TypeParser.GetTypeParser<Collapsible>(ref cols, CollapsingHandler.Instance);
        using var reader = Rows.Reader(PairCols, [2, "b"]);
        reader.Read();
        Assert.Equal(2, parser.Parse(reader).Result!.Id);

        using var nullReader = Rows.Reader(PairCols, [DBNull.Value, "b"]);
        nullReader.Read();
        Assert.Null(parser.Parse(nullReader).Result);
    }
}
