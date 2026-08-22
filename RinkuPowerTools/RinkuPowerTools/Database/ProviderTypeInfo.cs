using System.Data;

namespace RinkuPowerTools;

public readonly record struct ProviderParameterType(
    DatabaseType Database,
    string DataTypeName);

public readonly record struct ProviderTypeInfo(
    DbType? DbType,
    string CSharpType,
    int Size = 0,
    byte Precision = 0,
    byte Scale = 0,
    ProviderParameterType? ProviderType = null);
