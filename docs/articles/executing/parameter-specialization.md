# Parameter specialization

*Learn-on-use parameter metadata, tuned for your provider.*

This is the "extend it" page for command building. A [`QueryCommand`](query-command.md) starts generic and **specializes** its parameters to your provider as it runs, so later calls bind types and sizes precisely (better plan reuse, less driver overhead). You rarely touch this, but it is a place you can plug into.

## Initial state

Every parameter starts as `InferedDbParamCache` (the engine seeds each slot with `InferedDbParamCache.Instance`).

- On first use, a plain `DbParameter` is created and the value assigned without specifying a type or size.
- The command runs, which lets the provider resolve what the parameter really needs.

## Learning the requirements

Right after execution, the command tries to capture each parameter's real metadata through a pluggable system.

```csharp
public interface IDbParamInfoGetter {
    public static readonly List<ParamInfoGetterMaker> ParamGetterMakers = [];
    DbParamInfo MakeInfoAt(int i);   // specialized info for parameter i
}

// A maker inspects the command type to decide if it can extract metadata.
public delegate bool ParamInfoGetterMaker(IDbCommand cmd, out IDbParamInfoGetter getter);
```

Resolution order.

1. **Type check.** A registered maker checks the command type (for example `if (cmd is SqlCommand)`).
2. **Specialized extraction.** If it matches, it returns a getter that reads provider-specific metadata into an accurate `DbParamInfo`.
3. **Fallback.** Otherwise `DefaultParamCache` reads the standard `DbParameter` properties (type, size) directly.

Register a provider-specific maker by adding to `IDbParamInfoGetter.ParamGetterMakers` at startup.

## Persistence and manual overrides

Once captured, a `DbParamInfo` is cached permanently on the command, and later calls skip the learning phase. You can also set it yourself, even before the first call.

```csharp
TrackCmd.UpdateParamCache("@Name", TypedDbParamCache.Get(DbType.AnsiStringFixedLength, 1000));
```
