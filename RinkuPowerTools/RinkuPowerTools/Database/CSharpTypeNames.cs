using System.Data;

namespace RinkuPowerTools;

internal static class CSharpTypeNames {
    public static string FromType(Type type) {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            type = nullable;

        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(Guid)) return "Guid";
        if (type == typeof(DateTime)) return "DateTime";
        if (type == typeof(DateTimeOffset)) return "DateTimeOffset";
        if (type == typeof(DateOnly)) return "DateOnly";
        if (type == typeof(TimeOnly)) return "TimeOnly";
        if (type == typeof(TimeSpan)) return "TimeSpan";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(byte[])) return "byte[]";

        if (type.IsArray) {
            Type? elementType = type.GetElementType();
            if (elementType is not null) {
                string elementName = FromType(elementType);
                if (elementName != "object")
                    return elementName + "[]";
            }
        }

        return "object";
    }
    public static DbType? GetDbType(Type type) {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            type = nullable;

        if (type == typeof(string)) return DbType.String;
        if (type == typeof(int)) return DbType.Int32;
        if (type == typeof(long)) return DbType.Int64;
        if (type == typeof(short)) return DbType.Int16;
        if (type == typeof(byte)) return DbType.Byte;
        if (type == typeof(uint)) return DbType.UInt32;
        if (type == typeof(ulong)) return DbType.UInt64;
        if (type == typeof(ushort)) return DbType.UInt16;
        if (type == typeof(sbyte)) return DbType.SByte;
        if (type == typeof(bool)) return DbType.Boolean;
        if (type == typeof(Guid)) return DbType.Guid;
        if (type == typeof(DateTime)) return DbType.DateTime2;
        if (type == typeof(DateTimeOffset)) return DbType.DateTimeOffset;
        if (type == typeof(DateOnly)) return DbType.Date;
        if (type == typeof(TimeOnly) || type == typeof(TimeSpan)) return DbType.Time;
        if (type == typeof(decimal)) return DbType.Decimal;
        if (type == typeof(double)) return DbType.Double;
        if (type == typeof(float)) return DbType.Single;
        if (type == typeof(byte[])) return DbType.Binary;
        return null;
    }
}
