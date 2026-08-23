# Parameter binding

## Convert an application value

```csharp
public readonly record struct Names(IReadOnlyList<string> Items);

sealed class NamesParamInfo : ConvertedDbParamInfo<Names>
{
    protected override object ConvertValue(Names value) => string.Join(',', value.Items);

    protected override void ConfigureParameter(IDbDataParameter parameter)
        => parameter.DbType = DbType.String;
}
```

The command can hold that parameter strategy.

```csharp
static readonly QueryCommand SaveSearch = new("INSERT INTO saved_searches (Names) VALUES (@names)");

SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
SaveSearch.Execute(cnn, new { names = new Names(["Blue", "Live"]) });
// @names contains Blue,Live.
```

[Parameter metadata](../running-queries/parameter-metadata.md)

## Learn metadata from an existing command

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

using DbCommand providerCommand = cnn.CreateCommand();
providerCommand.CommandText = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";

DbParameter albumId = providerCommand.CreateParameter();
albumId.ParameterName = "@albumId";
albumId.DbType = DbType.Int32;
providerCommand.Parameters.Add(albumId);

DbParameter title = providerCommand.CreateParameter();
title.ParameterName = "@title";
title.DbType = DbType.String;
title.Size = 200;
providerCommand.Parameters.Add(title);

UpdateAlbum.UpdateCache(providerCommand);
```

`UpdateCache` reads parameter metadata through the registered parameter metadata getters.

A provider can add another metadata reader through [`IDbParamInfoGetter.ParamGetterMakers`](xref:Rinku.Querying.IDbParamInfoGetter.ParamGetterMakers).

## Change exposed parameter names

```csharp
public sealed class EmployeeArgs
{
    [ParameterName("EmployeeName")]
    [ParameterAlias("NameForSearch")]
    public string? Name { get; init; }

    [ParameterIgnore]
    public string? DebugNote { get; init; }
}
```

## Flatten another parameter object

```csharp
public sealed class UpdateArgs
{
    public int EmployeeId { get; init; }

    [NestedParameters("Employee")]
    public EmployeeArgs Employee { get; init; } = new();
}
```

```csharp
static readonly QueryCommand UpdateEmployee = new("UPDATE employees SET Name = @EmployeeName WHERE EmployeeId = @EmployeeId");

UpdateEmployee.Execute(cnn, new UpdateArgs
{
    EmployeeId = 12,
    Employee = new EmployeeArgs { Name = "Ana" }
});
```

## Same priority conflict

```csharp
public sealed class SearchTermA
{
    public string? Value { get; init; }
}

public sealed class SearchTermB
{
    public string? Value { get; init; }
}

[ParameterConflict(ParameterConflictBehavior.TakeOne)]
public sealed class SearchArgs
{
    [NestedParameters]
    public SearchTermA Primary { get; init; } = new();

    [NestedParameters]
    public SearchTermB Secondary { get; init; } = new();
}
```

```csharp
var search = new QueryCommand("SELECT @Value").StartBuilder();

search.UseWith(new SearchArgs
{
    Primary = new() { Value = "blue" },
    Secondary = new() { Value = "green" }
});

object? value = search["@Value"];
// TakeOne allows either value at the same priority.
```

A direct member has priority over a flattened member.

[Parameter member rules](parameter-members.md)

## Provider metadata reader

[`IDbParamInfoGetter.ParamGetterMakers`](xref:Rinku.Querying.IDbParamInfoGetter.ParamGetterMakers) contains the provider metadata readers tried for a command.

[`DbParameterDefaults`](xref:Rinku.Querying.DbParameterDefaults) supplies the application wide fallback when no provider reader claims the command. Its contract is [`IDbParameterDefaults`](xref:Rinku.Querying.IDbParameterDefaults).

## Nested custom parameter access

[`PathAccessorEmitterBase`](xref:Rinku.Querying.Parameters.PathAccessorEmitterBase) keeps a custom member rule path aware when the member is reached through `NestedParameters`. The lower level contract is [`IPathAccessorEmitter`](xref:Rinku.Querying.Parameters.IPathAccessorEmitter).

Command specific binding remains on [`DbParamInfo`](xref:Rinku.Querying.DbParamInfo).
