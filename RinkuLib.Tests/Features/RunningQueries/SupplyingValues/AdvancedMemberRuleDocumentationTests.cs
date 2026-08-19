using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>Executable examples for docs/articles/running-queries/custom-member-rules.md.</summary>
public class AdvancedMemberRuleDocumentationTests {
    private static class SearchRules {
        public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class HasTextAttribute : AccessorEmitterHandler {
        private static readonly MethodConditionEmitter Emitter = new(
            typeof(SearchRules).GetMethod(nameof(SearchRules.HasText))!);

        public override IAccessorEmitter? GetMemberEmitter(
            char varChar, int index, Type type, MemberInfo member, Mapper mapper)
            => index < 0 ? null : Emitter;
    }

    private sealed class TrackSearch {
        [HasText] public string? Composer { get; init; }
    }

    private sealed class PositiveNumberEmitter : AccessorEmitterBase {
        protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member, int sourceArgument) {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt);
        }

        protected override void EmitValue(ILGenerator il, Type type, MemberInfo member, int sourceArgument)
            => AccessorEmitter.EmitMemberValue(il, type, member, sourceArgument);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class PositiveNumberAttribute : AccessorEmitterHandler {
        private static readonly PositiveNumberEmitter Emitter = new();

        public override IAccessorEmitter? GetMemberEmitter(
            char varChar, int index, Type type, MemberInfo member, Mapper mapper)
            => index < 0 ? null : Emitter;
    }

    private sealed class PriceSearch {
        [PositiveNumber] public int MinPrice { get; init; }
    }

    private sealed class NullAsDbNullEmitter : IAccessorEmitter {
        private static object ToDbValue(string? value) => (object?)value ?? DBNull.Value;
        private static readonly MethodInfo ToDbValueMethod = typeof(NullAsDbNullEmitter)
            .GetMethod(nameof(ToDbValue), BindingFlags.Static | BindingFlags.NonPublic)!;

        public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
            => AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue,
                condition => condition.Emit(OpCodes.Ldc_I4_1),
                value => EmitValue(value, type, member));

        public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
            => AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
                condition => condition.Emit(OpCodes.Ldc_I4_1),
                value => EmitValue(value, type, member, context.SourceArgument), context);

        public void Validate(Type type, MemberInfo member) {
            Type memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
            if (memberType != typeof(string))
                throw new InvalidOperationException("NullAsDbNull requires a string member");
        }

        private static void EmitValue(ILGenerator il, Type type, MemberInfo member, int sourceArgument = 0) {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            il.Emit(OpCodes.Call, ToDbValueMethod);
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class NullAsDbNullAttribute : AccessorEmitterHandler {
        private static readonly NullAsDbNullEmitter Emitter = new();
        public override IAccessorEmitter? GetMemberEmitter(
            char varChar, int index, Type type, MemberInfo member, Mapper mapper)
            => index < 0 ? null : Emitter;
    }

    private sealed class CustomNullFilter {
        [NullAsDbNull] public string? Name { get; init; }
    }

    [UseDbNull]
    private sealed class UpdateTrack {
        public string? Composer { get; init; }
        [NotNullOrWhitespace] public string? Name { get; init; }
    }

    [Fact]
    [DocumentationExample("custom-member-rules.md", "method-condition")]
    public void Method_condition_controls_direct_and_builder_usage() {
        using var query = new QueryCommand("SELECT * FROM tracks WHERE Composer = ?@Composer");

        var blank = Render.From(query, new TrackSearch { Composer = "  " });
        Render.AssertCommand(blank, "SELECT * FROM tracks");

        var present = query.StartBuilder();
        present.UseWith(new TrackSearch { Composer = "AC/DC" });
        Render.Expect(present, "SELECT * FROM tracks WHERE Composer = @Composer", ("@Composer", "AC/DC"));
    }

    [Fact]
    [DocumentationExample("custom-member-rules.md", "condition-emitter")]
    public void Custom_condition_emitter_uses_only_positive_values() {
        using var query = new QueryCommand("SELECT * FROM tracks WHERE Price >= ?@MinPrice");

        Render.AssertCommand(Render.From(query, new PriceSearch { MinPrice = 0 }), "SELECT * FROM tracks");
        Render.AssertCommand(Render.From(query, new PriceSearch { MinPrice = 10 }),
            "SELECT * FROM tracks WHERE Price >= @MinPrice", ("@MinPrice", 10));
    }

    [Fact]
    [DocumentationExample("custom-member-rules.md", "complete-emitter")]
    public void Complete_emitter_controls_direct_and_use_with_roads() {
        using var query = new QueryCommand("SELECT * FROM tracks WHERE Name = ?@Name");

        Render.AssertCommand(Render.From(query, new CustomNullFilter()),
            "SELECT * FROM tracks WHERE Name = @Name", ("@Name", DBNull.Value));

        var builder = query.StartBuilder();
        builder.UseWith(new CustomNullFilter());
        Assert.Equal(DBNull.Value, builder["@Name"]);
    }

    [Fact]
    [DocumentationExample("custom-member-rules.md", "type-member-rules")]
    public void Member_rule_overrides_the_type_default() {
        using var query = new QueryCommand("SELECT * FROM tracks WHERE Composer = ?@Composer AND Name = ?@Name");

        var command = Render.From(query, new UpdateTrack());

        Render.AssertCommand(command, "SELECT * FROM tracks WHERE Composer = @Composer", ("@Composer", DBNull.Value));
    }
}
