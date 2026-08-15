using System.Data;
using System.Data.Common;
using System.Reflection;
using Rinku.Mapping;
using Rinku.Mapping.Defaults;
using Rinku.Querying;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class CustomDatabaseTypeTests {
    [Fact]
    public void A_custom_value_type_can_take_over_reading_and_parameter_writing() {
        var date = new LocalDate(new DateTime(2024, 5, 1));
        Assert.Equal(date, Rows.ParseOne<LocalDate>([new("Value", typeof(DateTime), false)], date.Value));

        var command = new FakeCommand();
        object? current = date;
        var info = new LocalDateParam();
        Assert.True(info.SaveUse("@date", command, ref current));
        Assert.Equal(date.Value, command.BoundParameters[0].Value);
        Assert.True(info.Update(command, ref current, new LocalDate(new DateTime(2024, 5, 2))));
        Assert.Equal(new DateTime(2024, 5, 2), command.BoundParameters[0].Value);
    }

    [Fact]
    public void A_custom_parameter_info_plugs_into_a_query_command() {
        var date = new LocalDate(new DateTime(2024, 5, 1));
        var query = new QueryCommand("SELECT @date");
        Assert.True(query.UpdateParamCache("@date", new LocalDateParam()));

        Render.Expect(query, new { date }, "SELECT @date", ("@date", date.Value));
    }

    [Fact]
    public void A_converted_parameter_uses_the_normal_parameter_lifecycle() {
        var command = new FakeCommand();
        object current = new RegisteredNames(["Sam", "Kyro"]);
        var info = new RegisteredNamesParam();
        Assert.True(info.SaveUse("@names", command, ref current));
        Assert.Equal("Sam,Kyro", command.BoundParameters[0].Value);

        object? updateCurrent = current;
        Assert.True(info.Update(command, ref updateCurrent, new RegisteredNames(["Ada", "Grace"])));
        Assert.Equal("Ada,Grace", command.BoundParameters[0].Value);
        Assert.True(info.Update(command, ref updateCurrent, null));
        Assert.Empty(command.BoundParameters);
    }

    [Fact]
    [DocumentationExample("registration.md", "scalar-registration")]
    public void Registered_conversions_work_for_scalars_members_and_nullable_targets() {
        TypeParsingInfo.AddOrSet(typeof(RegisteredDate), RegisteredDateTypeInfo.Instance);
        TypeParsingInfo.AddOrSet(typeof(RegisteredNames), RegisteredNamesTypeInfo.Instance);

        Assert.Equal(new DateTime(2024, 5, 2), Rows.ParseOne<RegisteredDate>([new("Value", typeof(DateTime), false)], new DateTime(2024, 5, 1)).Value);
        Assert.Equal(new DateTime(2024, 5, 2), Rows.ParseOne<RegisteredDateContainer>([new("Date", typeof(DateTime), false)], new DateTime(2024, 5, 1)).Date.Value);
        Assert.Null(Rows.ParseOne<RegisteredDate?>([new("Value", typeof(DateTime), true)], DBNull.Value));
        Assert.Equal(new DateTime(2024, 5, 2), Rows.ParseOne<RegisteredDate?>([new("Value", typeof(DateTime), true)], new DateTime(2024, 5, 1))!.Value.Value);
        Assert.Equal(new DateTime(2024, 5, 4), Rows.ParseOne<RegisteredDate>([new("Value", typeof(ProviderDate), false)], new ProviderDate(new DateTime(2024, 5, 4))).Value);
        Assert.Equal(["Sam", "Kyro"], Rows.ParseOne<RegisteredNames>([new("Names", typeof(string), false)], "Sam,Kyro").Values);
        Assert.Equal(["Ada", "Grace"], Rows.ParseOne<RegisteredNamesContainer>([new("Names", typeof(string), false)], "Ada,Grace").Names.Values);
    }
}

public readonly record struct LocalDate(DateTime Value) : IDbReadable;
public readonly record struct RegisteredDate(DateTime Value) : IDbReadable;
public record RegisteredDateContainer(RegisteredDate Date) : IDbReadable;
public readonly record struct ProviderDate(DateTime Value);
public readonly record struct RegisteredNames(IReadOnlyList<string> Values);
public record RegisteredNamesContainer(RegisteredNames Names) : IDbReadable;

sealed class RegisteredDateTypeInfo : ScalarTypeParsingInfo<RegisteredDate> {
    internal static readonly RegisteredDateTypeInfo Instance = new();
    private static readonly MethodInfo FromDateTimeMethod = typeof(RegisteredDateTypeInfo).GetMethod(nameof(FromDateTime), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo FromProviderDateMethod = typeof(RegisteredDateTypeInfo).GetMethod(nameof(FromProviderDate), BindingFlags.Static | BindingFlags.NonPublic)!;

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal) {
        MethodInfo? method = column.Type == typeof(DateTime) ? FromDateTimeMethod : column.Type == typeof(ProviderDate) ? FromProviderDateMethod : null;
        if (method is null)
            return null;
        ITypeConverter converter = new MethodCallConverter(method);
        if (Nullable.GetUnderlyingType(targetType) is not null)
            converter = new NullableWrapperConverter(converter);
        return new ConvertedScalarPlan(parentType, converter, parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal);
    }

    private static RegisteredDate FromDateTime(DateTime value) => new(value.AddDays(1));
    private static RegisteredDate FromProviderDate(ProviderDate value) => new(value.Value);
}

sealed class RegisteredNamesTypeInfo : ScalarTypeParsingInfo<RegisteredNames> {
    internal static readonly RegisteredNamesTypeInfo Instance = new();
    private static readonly MethodInfo FromStringMethod = typeof(RegisteredNamesTypeInfo).GetMethod(nameof(FromString), BindingFlags.Static | BindingFlags.NonPublic)!;

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal)
        => column.Type == typeof(string)
            ? new ConvertedScalarPlan(parentType, new MethodCallConverter(FromStringMethod), parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal)
            : null;

    private static RegisteredNames FromString(string value) => new(value.Split(','));
}

sealed class LocalDateParam : DbParamInfo {
    public LocalDateParam() : base(true) { }
    public override bool SaveUse(string name, IDbCommand cmd, ref object value) { value = Add(name, cmd, (LocalDate)value); return true; }
    public override bool Update(IDbCommand cmd, ref object? current, object? newValue) { if (current is not IDbDataParameter parameter || newValue is not LocalDate date) return false; parameter.Value = date.Value; return true; }
    public override bool Use(string name, IDbCommand cmd, object value) { Add(name, cmd, (LocalDate)value); return true; }
    public override bool Use(string name, DbCommand cmd, object value) { Add(name, cmd, (LocalDate)value); return true; }
    public override void Remove(IDbCommand cmd, object current) => DbParamInfo.RemoveSingle(((IDbDataParameter)current).ParameterName, cmd);
    private static IDbDataParameter Add(string name, IDbCommand cmd, LocalDate value) { var parameter = (IDbDataParameter)cmd.CreateParameter(); parameter.ParameterName = name; parameter.DbType = DbType.DateTime; parameter.Value = value.Value; cmd.Parameters.Add(parameter); return parameter; }
}

sealed class RegisteredNamesParam : ConvertedDbParamInfo<RegisteredNames> {
    protected override object ConvertValue(RegisteredNames value) => string.Join(',', value.Values);
    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.String;
}
