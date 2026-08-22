# Parameter binding

Use a custom `DbParamInfo` when an application value needs a different database value or parameter metadata.

This example stores a list of names as one string.

```csharp
public readonly record struct Names(IReadOnlyList<string> Items);

sealed class NamesParamInfo : ConvertedDbParamInfo<Names>
{
    protected override object ConvertValue(Names value) => string.Join(',', value.Items);

    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.String;
}
```

Attach the strategy to the command once.

```csharp
static readonly QueryCommand SaveSearch = new("INSERT INTO saved_searches (Names) VALUES (@names)");

SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
```

Use the application type normally at execution time.

```csharp
int inserted = SaveSearch.Execute(cnn, new
{
    names = new Names(["Blue", "Live"])
});
```

```sql
INSERT INTO saved_searches (Names) VALUES (@names)
-- @names contains Blue,Live
```

See [Parameter metadata](../running-queries/parameter-metadata.md) when only `DbType`, size, direction, or another normal parameter setting needs to change.

## Shape a parameter object

Parameter source attributes change which members are exposed and which names they use.

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

Flatten a nested object when the SQL expects its values at the same level.

```csharp
public sealed class UpdateArgs
{
    [NestedParameters("Employee")]
    public EmployeeArgs Employee { get; init; } = new();
}
```

```csharp
UpdateEmployee.Execute(cnn, new UpdateArgs
{
    Employee = new EmployeeArgs
    {
        Name = "Ana"
    }
});
```

## Equal priority conflicts

Two flattened members at the same depth are ambiguous by default.

Use `ParameterConflictBehavior.TakeOne` only when either value is acceptable.

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
// Either value may be selected
```

A direct member still has priority over a flattened member.

## Provider metadata readers

A provider can add an `IDbParamInfoGetter` when Rinku cannot read its parameter metadata with the built in rules.

Register the getter maker during application startup.

```csharp
static bool MakeGetter(IDbCommand command, out IDbParamInfoGetter getter)
{
    getter = null!;
    return false;
}

IDbParamInfoGetter.ParamGetterMakers.Add(MakeGetter);
```

The maker should return `false` for command types it does not understand.

Use this extension point only for provider metadata discovery. Normal command specific metadata should use `UpdateParamCache`.
