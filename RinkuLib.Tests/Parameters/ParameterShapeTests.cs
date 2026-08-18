using System;
using System.Collections.Generic;
using Rinku;
using Rinku.Querying.Parameters;
using Xunit;

namespace Rinku.Querying.Tests;

public sealed class ParameterShapeTests {
    private sealed class RenamedArgs {
        [ParameterName("EmployeeName")] public string? Name { get; init; }
        [ParameterAlias("LegacyId")] public int Id { get; init; }
        [ParameterIgnore] public int Ignored { get; init; }
    }

    [Fact]
    public void Rename_alias_and_ignore_use_the_same_parameter_plan() {
        var command = new QueryCommand("SELECT @EmployeeName, @Id, @LegacyId, @Ignored");
        var builder = new QueryBuilder(command);
        builder.UseWith(new RenamedArgs { Name = "Ada", Id = 7, Ignored = 99 });

        Assert.Equal("Ada", builder["@EmployeeName"]);
        Assert.Equal(7, builder["@Id"]);
        Assert.Equal(7, builder["@LegacyId"]);
        Assert.Null(builder["@Ignored"]);
    }

    private sealed class CoreArgs {
        public int Id { get; init; }
        [ParameterName("DisplayName")] public string? Name { get; init; }
        [UseDbNull] public int? ParentId { get; init; }
    }

    private sealed class WrapperArgs {
        [NestedParameters] public required CoreArgs Core { get; init; }
        public int UserId { get; init; }
    }

    [Fact]
    public void Nested_members_are_flattened_and_keep_their_attributes() {
        var command = new QueryCommand("SELECT @Id, @DisplayName, @ParentId, @UserId");
        var builder = new QueryBuilder(command);
        builder.UseWith(new WrapperArgs {
            Core = new CoreArgs { Id = 4, Name = "Grace", ParentId = null },
            UserId = 12
        });

        Assert.Equal(4, builder["@Id"]);
        Assert.Equal("Grace", builder["@DisplayName"]);
        Assert.Equal(DBNull.Value, builder["@ParentId"]);
        Assert.Equal(12, builder["@UserId"]);
    }

    private sealed class PrefixedWrapper {
        [NestedParameters("Employee")] public required CoreArgs Core { get; init; }
    }

    [Fact]
    public void Nested_prefix_is_composed_with_nested_member_names() {
        var command = new QueryCommand("SELECT @EmployeeId, @EmployeeDisplayName");
        var builder = new QueryBuilder(command);
        builder.UseWith(new PrefixedWrapper { Core = new CoreArgs { Id = 5, Name = "Lin" } });

        Assert.Equal(5, builder["@EmployeeId"]);
        Assert.Equal("Lin", builder["@EmployeeDisplayName"]);
    }

    private sealed class ChildId { public int Id { get; init; } }
    private sealed class ParentWins {
        public int Id { get; init; }
        [NestedParameters] public required ChildId Child { get; init; }
    }

    [Fact]
    public void Direct_wrapper_member_wins_over_flattened_child() {
        var command = new QueryCommand("SELECT @Id");
        var builder = new QueryBuilder(command);
        builder.UseWith(new ParentWins { Id = 1, Child = new ChildId { Id = 2 } });
        Assert.Equal(1, builder["@Id"]);
    }

    private sealed class RedirectWins {
        [ParameterName("Id")] public int SourceId { get; init; }
        public int Id { get; init; }
    }

    [Fact]
    public void Explicit_name_wins_over_same_depth_convention() {
        var command = new QueryCommand("SELECT @Id");
        var builder = new QueryBuilder(command);
        builder.UseWith(new RedirectWins { SourceId = 8, Id = 2 });
        Assert.Equal(8, builder["@Id"]);
    }

    private sealed class AmbiguousChildA { public int Value { get; init; } }
    private sealed class AmbiguousChildB { public int Value { get; init; } }
    private sealed class AmbiguousWrapper {
        [NestedParameters] public required AmbiguousChildA A { get; init; }
        [NestedParameters] public required AmbiguousChildB B { get; init; }
    }

    [Fact]
    public void Same_depth_flattening_collision_fails_deterministically() {
        var command = new QueryCommand("SELECT @Value");
        var builder = new QueryBuilder(command);
        Assert.Throws<InvalidOperationException>(() => builder.UseWith(new AmbiguousWrapper {
            A = new AmbiguousChildA { Value = 1 }, B = new AmbiguousChildB { Value = 2 }
        }));
    }

    [ParameterConflict(ParameterConflictBehavior.TakeOne)]
    private sealed class AmbiguousTakeOneWrapper {
        [NestedParameters] public required AmbiguousChildA A { get; init; }
        [NestedParameters] public required AmbiguousChildB B { get; init; }
    }

    [Fact]
    public void TakeOne_accepts_an_equal_priority_collision_without_defining_the_winner() {
        var command = new QueryCommand("SELECT @Value");
        var builder = new QueryBuilder(command);
        builder.UseWith(new AmbiguousTakeOneWrapper {
            A = new AmbiguousChildA { Value = 1 }, B = new AmbiguousChildB { Value = 2 }
        });

        Assert.Contains((int)builder["@Value"]!, new[] { 1, 2 });
    }

    [ParameterConflict(ParameterConflictBehavior.TakeOne)]
    private sealed class TakeOneStillUsesPriority {
        public int Id { get; init; }
        [NestedParameters] public required ChildId Child { get; init; }
    }

    [Fact]
    public void TakeOne_does_not_change_normal_parameter_priority() {
        var command = new QueryCommand("SELECT @Id");
        var builder = new QueryBuilder(command);
        builder.UseWith(new TakeOneStillUsesPriority { Id = 10, Child = new ChildId { Id = 20 } });
        Assert.Equal(10, builder["@Id"]);
    }

    [Fact]
    public void Root_dictionary_is_a_parameter_object() {
        var command = new QueryCommand("SELECT @Name, @Id");
        var builder = new QueryBuilder(command);
        var values = new Dictionary<string, object?> { ["Name"] = "A", ["@Id"] = 5 };
        builder.UseWith(values);

        Assert.Equal("A", builder["@Name"]);
        Assert.Equal(5, builder["@Id"]);
    }

    private sealed class DictionaryWrapper {
        public int Id { get; init; }
        [NestedParameters] public required IReadOnlyDictionary<string, object?> Extra { get; init; }
    }

    [Fact]
    public void Nested_dictionary_composes_with_regular_members() {
        var command = new QueryCommand("SELECT @Id, @ModifiedBy");
        var builder = new QueryBuilder(command);
        builder.UseWith(new DictionaryWrapper {
            Id = 4,
            Extra = new Dictionary<string, object?> { ["ModifiedBy"] = 12 }
        });

        Assert.Equal(4, builder["@Id"]);
        Assert.Equal(12, builder["@ModifiedBy"]);
    }

    private sealed class PrefixedDictionaryWrapper {
        [NestedParameters("Meta")] public required IReadOnlyDictionary<string, object?> Meta { get; init; }
    }

    [Fact]
    public void Nested_dictionary_can_be_prefixed() {
        var command = new QueryCommand("SELECT @MetaUserId");
        var builder = new QueryBuilder(command);
        builder.UseWith(new PrefixedDictionaryWrapper {
            Meta = new Dictionary<string, object?> { ["UserId"] = 14 }
        });
        Assert.Equal(14, builder["@MetaUserId"]);
    }

    private interface IBaseArgs { string? Name { get; } }
    private sealed class ConcreteArgs : IBaseArgs {
        public string? Name { get; init; }
        public int Id { get; init; }
    }

    [Fact]
    public void Generic_reference_parameter_sources_use_the_declared_generic_type() {
        var command = new QueryCommand("SELECT @Name, @Id");
        var builder = new QueryBuilder(command);
        IBaseArgs args = new ConcreteArgs { Name = "A", Id = 17 };
        builder.UseWith(args);

        Assert.Equal("A", builder["@Name"]);
        Assert.Null(builder["@Id"]);
    }

    [Fact]
    public void Object_parameter_sources_use_the_runtime_type() {
        var command = new QueryCommand("SELECT @Name, @Id");
        var builder = new QueryBuilder(command);
        IBaseArgs args = new ConcreteArgs { Name = "A", Id = 17 };
        builder.UseWith((object)args);

        Assert.Equal("A", builder["@Name"]);
        Assert.Equal(17, builder["@Id"]);
    }

    private sealed class NullableNestedWrapper {
        [NestedParameters] public CoreArgs? Core { get; init; }
        public int UserId { get; init; }
    }

    [Fact]
    public void Null_nested_object_supplies_no_nested_values() {
        var command = new QueryCommand("SELECT @Id, @DisplayName, @UserId");
        var builder = new QueryBuilder(command);
        builder.UseWith(new NullableNestedWrapper { Core = null, UserId = 9 });

        Assert.Null(builder["@Id"]);
        Assert.Null(builder["@DisplayName"]);
        Assert.Equal(9, builder["@UserId"]);
    }
}
