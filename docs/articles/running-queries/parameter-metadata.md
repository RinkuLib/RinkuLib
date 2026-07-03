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
