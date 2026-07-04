# Parameter metadata

How a `DbParameter` gets its type and size. The default needs no configuration. It is documented because it is a point you can take over.

## What happens by default

1. On a variable's first use, a plain `DbParameter` is created with just the value. The provider infers the rest.
2. Right after execution, the command captures each parameter's resolved metadata (type, size) and caches it.
3. Every later call binds that parameter precisely, which helps plan reuse and driver overhead.

## Setting it yourself

Pin a parameter's metadata up front instead of letting it be learned.

```csharp
TrackCmd.UpdateParamCache("@Name", TypedDbParamCache.Get(DbType.AnsiStringFixedLength, 1000));
```

## Output parameters

Direction is part of the metadata. Pin an output parameter with a directional cache, run through an overload that hands you the command, and read the value once the read completes. `Execute`, `ExecuteScalar`, `Query`, and their async forms all take an `out DbCommand`, like the reader methods.

```csharp
static readonly QueryCommand Renumber = new("EXEC dbo.RenumberTracks @albumId, @moved OUTPUT");

Renumber.UpdateParamCache("@moved", new DirectionalSizedDbParamCache(ParameterDirection.Output, DbType.Int32));

List<Track> renumbered = Renumber.Query<List<Track>>(cnn, out DbCommand cmd, new { albumId = 1, moved = 0 });

int moved = (int)cmd.Parameters["@moved"].Value!;
cmd.Dispose();
```

The details that matter:

- The `out DbCommand` overloads leave the command alive and in your hands, dispose it when done. The overloads without it create and dispose their own command, so outputs are not reachable there.
- A parameter is only created for a supplied value, so give the output a placeholder (`moved = 0` above) to bring it into the command.
- Providers fill outputs when the reader closes. A buffered shape completes its read before returning; a streamed shape fills them only after enumeration finishes.
- A [builder bound to your own command](parameters.md#a-builder-bound-to-one-dbcommand) works the same way, its command is yours already.
- `DirectionalScaledDbParamCache` is the same with precision and scale, for decimals.

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
