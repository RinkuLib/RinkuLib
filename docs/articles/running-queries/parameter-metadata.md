# Parameter metadata

How a `DbParameter` gets its type and size. The default needs no configuration. It is documented because it is a point you can take over.

## What happens by default

1. On a variable's first use, a plain `DbParameter` is created with just the value. The provider infers the rest.
2. Right after execution, the command captures each parameter's resolved metadata (type, size) and caches it.
3. Every later call binds that parameter from the cache, which helps plan reuse and driver overhead.

A captured size is rounded up to 100, 500, 4000, or unbounded before it is cached, so a `varchar` learned at 50 binds at 100. Sizes group into a handful of buckets instead of one cache entry per length, and a plan is reused across calls whose values differ in length. Pin the size yourself, below, when a parameter needs an exact one.

## Setting it yourself

Pin a parameter's metadata up front instead of letting it be learned.

```csharp
TrackCmd.UpdateParamCache("@Name", TypedDbParamCache.Get(DbType.AnsiStringFixedLength, 1000));
```

Reset all learned and pinned metadata on one command through its parameter ledger. Every entry returns to the current inferred strategy, including metadata owned by special handlers, and later runs learn it again:

```csharp
TrackCmd.Parameters.Reset();
```

There is no separate named reset. `UpdateParamCache(name, DbParameterDefaults.Current.Inferred)` already provides that operation when an application needs it.

## Converting a custom parameter value

Use `ConvertedDbParamInfo<T>` when the value needs conversion but the normal parameter lifecycle is enough.

```csharp
sealed class NamesParam : ConvertedDbParamInfo<Names>
{
    protected override object ConvertValue(Names value)
        => string.Join(',', value.Items);

    protected override void ConfigureParameter(IDbDataParameter parameter)
        => parameter.DbType = DbType.String;
}

Search.UpdateParamCache("@names", new NamesParam());
```

The wrapper creates the parameter, updates it on reuse, removes it when the value becomes null, and supports
both command interfaces. Inherit directly from `DbParamInfo` when the parameter needs a different lifecycle.

## Output parameters

Direction is part of the metadata. Pin an output parameter with a directional cache, run through an overload that hands you the command, and read the value once the read completes. `Execute`, `ExecuteScalar`, `Query`, and their async forms all take an `out DbCommand`, like the reader methods.

```csharp
static readonly QueryCommand Renumber = new("EXEC dbo.RenumberTracks @albumId, @moved OUTPUT");

Renumber.UpdateParamCache("@moved", new DirectionalSizedDbParamCache(ParameterDirection.Output, DbType.Int32));

List<Track> renumbered = Renumber.Query<List<Track>>(cnn, out DbCommand cmd, new { albumId = 1, moved = 0 });

int moved = cmd.GetOutputValue<int>("@moved");
cmd.Dispose();
```

When a command exposes a return-value parameter, read it without relying on the name chosen by the provider.

```csharp
static readonly QueryCommand Renumber = QueryCommand.FromProc("dbo.RenumberTracks", cnn);

Renumber.Execute(cnn, out DbCommand cmd, new { albumId = 1, moved = 0 });
int moved = cmd.GetOutputValue<int>("@moved");
int returnValue = cmd.GetReturnValue<int>();
cmd.Dispose();
```

`FromProc` creates the provider-declared return-value parameter automatically. It is not a named input slot and
does not need a placeholder; named output parameters still do.

The details that matter:

- The `out DbCommand` overloads leave the command alive and in your hands, dispose it when done. The overloads without it create and dispose their own command, so outputs are not reachable there.
- A named output parameter is only created for a supplied value, so give it a placeholder (`moved = 0` above) to bring it into the command. A stored-procedure return value is added automatically.
- Providers fill outputs when the reader closes. A buffered shape completes its read before returning. A streamed shape fills them only after enumeration finishes.
- A [builder bound to your own command](parameters.md#a-builder-bound-to-one-dbcommand) works the same way, its command is yours already.
- `DirectionalScaledDbParamCache` is the same with precision and scale, for decimals.
- A command built by [`QueryCommand.FromProc`](index.md#stored-procedures) has this done for it. The procedure states the direction and the size, so output and return-value parameters need no pinning of their own and their declared metadata is retained. The return-value parameter is created automatically; named output parameters still use a placeholder value when running the command.

## Plugging in a provider

The capture step is pluggable. A maker inspects the command and, when it recognizes it, returns a getter that reads provider-specific metadata.

```csharp
IDbParamInfoGetter.ParamGetterMakers.Add((IDbCommand cmd, out IDbParamInfoGetter getter) => {
    if (cmd is MyProviderCommand mine) {
        getter = new MyProviderParamInfoGetter(mine);
        return true;
    }
    getter = null!;
    return false;
});
```

Register makers once at startup. When no maker matches, the default reads the standard `DbParameter` properties, which is what step 2 above does.

## Replacing the fallback

Provider getters are the narrow extension point. To replace the general fallback instead, assign an
`IDbParameterDefaults` implementation during startup:

```csharp
DbParameterDefaults.Current = new MyParameterDefaults();
```

It supplies the initial inferred `DbParamInfo` and turns a live `IDbDataParameter` into the strategy cached for
later calls. Query parsing and parameter arrays depend only on that contract. The shipped
`DefaultDbParameterServices` can be wrapped when only one decision needs changing.

This contract is resolved while a parameter ledger is created and while provider metadata is learned. Warm
rebinding keeps the selected `DbParamInfo` in the ledger and calls it directly; it does not resolve
`DbParameterDefaults.Current` for each parameter on each trip.

The literal, raw, numeric, and spread handlers are installed into `QueryFactory.BaseHandlerMapper` and
`SpecialHandler.SpecialHandlerGetter`. Those registries hold handler contracts, so a custom suffix is one
registration and replacing a shipped suffix does not require replacing query parsing or binding.
