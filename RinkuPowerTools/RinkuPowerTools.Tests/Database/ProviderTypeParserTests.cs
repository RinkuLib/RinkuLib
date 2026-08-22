using System.Data;

namespace RinkuPowerTools.Tests.Database;

public class ProviderTypeParserTests
{
    [Fact]
    public void SqlServerDecimal_PreservesPrecisionAndScale()
    {
        ProviderTypeInfo type = SqlServerTypeParser.Parse("decimal(18,2)");

        Assert.Equal(DbType.Decimal, type.DbType);
        Assert.Equal("decimal", type.CSharpType);
        Assert.Equal((byte)18, type.Precision);
        Assert.Equal((byte)2, type.Scale);
    }

    [Theory]
    [InlineData("timestamp(3) with time zone", DbType.DateTime, "DateTime", 3)]
    [InlineData("timestamp(6) without time zone", DbType.DateTime2, "DateTime", 6)]
    [InlineData("date", DbType.Date, "DateOnly", 0)]
    [InlineData("time(4) without time zone", DbType.Time, "TimeOnly", 4)]
    public void PostgreSqlTemporalTypes_AreParsed(string declaration, DbType dbType, string csharpType, byte scale)
    {
        ProviderTypeInfo type = PostgreSqlTypeParser.Parse(declaration);

        Assert.Equal(dbType, type.DbType);
        Assert.Equal(csharpType, type.CSharpType);
        Assert.Equal(scale, type.Scale);
    }

    [Fact]
    public void PostgreSqlArray_PreservesElementShapeWithoutForcingDbType()
    {
        ProviderTypeInfo type = PostgreSqlTypeParser.Parse("integer[]");

        Assert.Null(type.DbType);
        Assert.Equal("int[]", type.CSharpType);
        Assert.Equal(DatabaseType.PostgreSql, type.ProviderType?.Database);
        Assert.Equal("integer[]", type.ProviderType?.DataTypeName);
    }

    [Theory]
    [InlineData("jsonb", "jsonb")]
    [InlineData("timestamp with time zone", "timestamp with time zone")]
    [InlineData("serial", "integer")]
    public void PostgreSqlTypes_PreserveCanonicalNativeType(string declaration, string dataTypeName)
    {
        ProviderTypeInfo type = PostgreSqlTypeParser.Parse(declaration);

        Assert.Equal(DatabaseType.PostgreSql, type.ProviderType?.Database);
        Assert.Equal(dataTypeName, type.ProviderType?.DataTypeName);
    }

    [Fact]
    public void PostgreSqlCustomType_IsPreservedForRuntimeProviderTyping()
    {
        ProviderTypeInfo type = PostgreSqlTypeParser.Parse("app.album_state");

        Assert.Null(type.DbType);
        Assert.Equal("object", type.CSharpType);
        Assert.Equal(DatabaseType.PostgreSql, type.ProviderType?.Database);
        Assert.Equal("app.album_state", type.ProviderType?.DataTypeName);
    }

    [Theory]
    [InlineData("INTEGER", DbType.Int64, "long")]
    [InlineData("BOOLEAN", DbType.Boolean, "bool")]
    [InlineData("VARCHAR(40)", DbType.String, "string")]
    [InlineData("custom_int_value", DbType.Int64, "long")]
    public void SqliteDeclaredTypes_UseAffinityAndKnownMappings(string declaration, DbType dbType, string csharpType)
    {
        ProviderTypeInfo type = SqliteTypeParser.Parse(declaration);

        Assert.Equal(dbType, type.DbType);
        Assert.Equal(csharpType, type.CSharpType);
    }
}
