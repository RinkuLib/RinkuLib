using System.Reflection;
using RinkuLib.Commands;
using RinkuLib.Exceptions;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Queries;

public sealed class MethodConditionEmitterTests {
    private static readonly MethodConditionEmitter StringCondition = new(
        typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!);

    [Fact]
    public void A_method_condition_must_accept_the_member_type() {
        var command = new QueryCommand("SELECT * FROM Users WHERE ?@Value");
        var builder = command.StartBuilder();

        var error = Assert.Throws<RinkuConfigurationException>(() => builder.UseWith(new InvalidFilter { Value = 1 }));

        Assert.Equal(ErrorCodes.AttributeOnWrongMemberType, error.Code);
    }

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class StringConditionAttribute : AccessorEmitterHandler {
        public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper)
            => index < 0 ? null : StringCondition;
    }

    private sealed class InvalidFilter {
        [StringCondition]
        public int Value { get; init; }
    }
}
