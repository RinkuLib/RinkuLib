# Parameter binding

Use a custom `DbParamInfo` to change a parameter's database value or metadata.

## Convert one application type

`ConvertedDbParamInfo<T>` keeps normal parameter reuse while converting the value before binding.

```csharp
public readonly record struct Names(IReadOnlyList<string> Items);

sealed class NamesParamInfo : ConvertedDbParamInfo<Names> {
    protected override object ConvertValue(Names value) => string.Join(',', value.Items);

    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.String;
}
```

Attach it to one command parameter during application setup.

```csharp
static readonly QueryCommand SaveSearch = new("INSERT INTO saved_searches (Names) VALUES (@names)");

SaveSearch.UpdateParamCache("@names", new NamesParamInfo());
```

```csharp
int inserted = SaveSearch.Execute(cnn, new { names = new Names(["Blue", "Live"]) });
```

```sql
INSERT INTO saved_searches (Names) VALUES (@names)
-- The provider receives Blue,Live as a string.
```

See [parameter metadata](../running-queries/parameter-metadata.md) for fixed sizes, directional values, and reset behavior. See [positional parameters](../running-queries/values.md#positional-parameters) for the included positional strategy.

## Inspect provider parameter metadata

An `IDbParamInfoGetter` reads metadata from one command.

```csharp
sealed class CommandParamInfoGetter(IDbCommand command)
    : IDbParamInfoGetter {

    public IEnumerable<KeyValuePair<string, int>> EnumerateParameters() => command.Parameters.Cast<IDataParameter>()
            .Select((parameter, index) => KeyValuePair.Create(parameter.ParameterName, index));

    public DbParamInfo MakeInfoAt(int index) {
        var parameter = (IDbDataParameter)command.Parameters[index]!;
        return MakeInfo(parameter);
    }

    public bool TryGetInfo(string name, out DbParamInfo info) {
        int index = command.Parameters.IndexOf(name);

        if (index < 0) {
            info = DbParameterDefaults.Current.Inferred;
            return false;
        }

        info = MakeInfoAt(index);
        return true;
    }

    static DbParamInfo MakeInfo(IDbDataParameter parameter) => parameter.DbType switch {
            DbType.String or
            DbType.AnsiString or
            DbType.Binary or
            DbType.Xml or
            DbType.AnsiStringFixedLength or
            DbType.StringFixedLength => TypedDbParamCache.Get(parameter.DbType, parameter.Size),
            _ => TypedDbParamCache.Get(parameter.DbType)
        };
}
```

Register a maker that claims the commands it understands.

```csharp
static bool MakeGetter(IDbCommand command, out IDbParamInfoGetter getter) {
    if (!SupportsProviderMetadata(command)) {
        getter = default!;
        return false;
    }

    getter = new CommandParamInfoGetter(command);
    return true;
}

IDbParamInfoGetter.ParamGetterMakers.Add(MakeGetter);
```

Makers are tried in list order until one returns true.

```text
maker 0 returns false -> try maker 1
maker 1 returns true  -> consume its getter
maker 2               -> not called
```

## Getter lifetime

The maker is invoked for every metadata-inspection operation.

```csharp
static bool MakeGetter(IDbCommand command, out IDbParamInfoGetter getter) {
    getter = new CommandParamInfoGetter(command);
    return SupportsProviderMetadata(command);
}
```

Rinku consumes the returned getter synchronously. It does not cache or dispose it.

```text
inspection starts -> maker receives current command
maker returns      -> getter is consumed synchronously
inspection ends   -> Rinku retains no getter reference
```

A maker may return a shared getter, but the getter methods do not receive the current command. Shared getters must therefore be stateless and thread-safe.

When metadata comes from `command.Parameters`, create a command-bound getter for each inspection. Rebinding one shared mutable getter is unsafe during concurrent execution.

## Replace the fallback inference rule

`IDbParameterDefaults` controls parameters that no registered getter claims.

```csharp
sealed class AppParameterDefaults : IDbParameterDefaults {
    readonly DefaultDbParameterServices shipped = new();

    public DbParamInfo Inferred => shipped.Inferred;

    public DbParamInfo MakeInfo(IDbDataParameter parameter) {
        if (parameter.DbType == DbType.String && parameter.Size == 0)
            return TypedDbParamCache.Get(DbType.String, 4000);

        return shipped.MakeInfo(parameter);
    }
}

DbParameterDefaults.Current = new AppParameterDefaults();
```

Set the default during application startup.

```text
parameter inferred afterward -> uses AppParameterDefaults
parameter reset afterward    -> uses AppParameterDefaults.Inferred
already learned strategy     -> unchanged
manually pinned strategy     -> unchanged
```

## Shape a parameter source

Use Core attributes to control the names and members exposed by a parameter object.

```csharp
public sealed class EmployeeArgs {
    [ParameterName("EmployeeName")]
    [ParameterAlias("NameForSearch")]
    public string? Name { get; init; }

    [ParameterIgnore]
    public string? DebugNote { get; init; }
}
```

Explicitly flatten nested values when the query expects a flat parameter surface.

```csharp
public sealed class UpdateArgs {
    [NestedParameters("Employee")]
    public EmployeeArgs Employee { get; init; } = new();
}
```

Structured members take precedence over dictionary fallbacks. Equal-priority collisions can use
`[ParameterConflict(ParameterConflictBehavior.TakeOne)]` when either winner is acceptable.
