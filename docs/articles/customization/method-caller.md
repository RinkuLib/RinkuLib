# Method caller

`MethodCaller` creates a delegate with the signature the caller wants around an existing method.

The first delegate argument can supply several method parameters through the normal Rinku parameter mapping rules. Later delegate arguments can supply selected method parameters directly.

```csharp
public record SaveAlbumArgs(int AlbumId, string Title);

MethodInfo method = typeof(AlbumStore).GetMethod(nameof(AlbumStore.SaveAlbum))
    ?? throw new InvalidOperationException();

Func<SaveAlbumArgs, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, CancellationToken, Task<int>>>(method, CallerParameter<CancellationToken>.ByType());

int affected = await save(new SaveAlbumArgs(12, "Blue"), cancellationToken);
```

For this example the mapped source supplies `albumId` and `title`. The second delegate argument supplies the `CancellationToken` directly.

See [parameter source rules](parameter-members.md) for the mapping rules used by the first delegate argument.

## Match a caller argument by type

Use `ByType()` when one otherwise unbound method parameter has the exact caller argument type.

```csharp
Func<SaveAlbumArgs, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, CancellationToken, Task<int>>>(method, CallerParameter<CancellationToken>.ByType());
```

A type match uses the exact type. No match leaves that caller argument unused. More than one available match is ambiguous.

## Match a caller argument by name

Use `Named` when the target parameter name is the useful distinction.

```csharp
Func<SaveAlbumArgs, int, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, int, Task<int>>>(method, CallerParameter<int>.Named("userId"));
```

A named caller binding takes precedence over type only matching.

A missing named target leaves that caller argument unused. This lets one delegate shape work with methods that do not all consume every caller supplied value.

## Mix mapped and caller supplied values

```csharp
Func<SaveAlbumArgs, int, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, int, CancellationToken, Task<int>>>(method, CallerParameter<int>.Named("userId"), CallerParameter<CancellationToken>.ByType());
```

Caller supplied parameters take precedence over values that could also come from the mapped source.

## Return values

The delegate return type follows the target method return shape.

```csharp
Func<SaveAlbumArgs, int> syncCall;
Func<SaveAlbumArgs, Task<int>> taskCall;
Func<SaveAlbumArgs, ValueTask<int>> valueTaskCall;
```

There is no separate asynchronous Method Caller model. Choose the delegate return type that matches the method being wrapped.

## Mapped values must be usable

If a target parameter is assigned to the mapped source but that value is not currently usable, invocation fails instead of silently replacing it.

```csharp
Func<SaveAlbumArgs, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, Task<int>>>(method);
```

The mapped source type is fixed by the first delegate argument. Method Caller does not switch mapping behavior from the runtime type of that argument.
