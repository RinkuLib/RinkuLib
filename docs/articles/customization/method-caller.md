# Method caller

## Target method

```csharp
public static class AlbumCommands
{
    static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

    public static Task<int> SaveAlbum(DbConnection cnn, int albumId, string title, CancellationToken cancellationToken)
        => UpdateAlbum.ExecuteAsync(cnn, new { albumId, title }, ct: cancellationToken);
}
```

## Adapt the delegate

```csharp
public record SaveAlbumArgs(int AlbumId, string Title);

MethodInfo method = typeof(AlbumCommands).GetMethod(nameof(AlbumCommands.SaveAlbum))
    ?? throw new InvalidOperationException("SaveAlbum was not found.");

Func<SaveAlbumArgs, DbConnection, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, DbConnection, CancellationToken, Task<int>>>(
    method,
    CallerParameter<DbConnection>.ByType(),
    CallerParameter<CancellationToken>.ByType());

int affected = await save(new SaveAlbumArgs(12, "Blue"), cnn, cancellationToken);
```

The first delegate argument supplies mapped method parameters through the parameter source system. The later arguments supply parameters selected by the caller bindings.

[Parameter source rules](parameter-members.md)

## Match by type

```csharp
CallerParameter<CancellationToken>.ByType();
```

A type binding matches an otherwise unbound target parameter with the exact caller argument type.

## Match by name

```csharp
CallerParameter<int>.Named("userId");
```

A named binding targets the method parameter with that name.

## Mix direct and mapped inputs

```csharp
Func<SaveAlbumArgs, int, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, int, CancellationToken, Task<int>>>(
    method,
    CallerParameter<int>.Named("userId"),
CallerParameter<CancellationToken>.ByType());
```

Caller supplied bindings take precedence over a value that could also come from the mapped source.

## Return shape

```csharp
Func<SaveAlbumArgs, int> syncCall;
Func<SaveAlbumArgs, Task<int>> taskCall;
Func<SaveAlbumArgs, ValueTask<int>> valueTaskCall;
```

The delegate return type follows the wrapped method return shape.

[`MethodCaller`](xref:Rinku.MethodCaller)

## Binding details

A type binding uses the exact target parameter type. No match leaves that caller argument unused. More than one available exact match is ambiguous.

```csharp
CallerParameter<CancellationToken>.ByType();
```

A named binding takes precedence over type matching. A missing named target also leaves that caller argument unused.

```csharp
CallerParameter<int>.Named("userId");
```

A caller supplied binding takes precedence over a value that could also come from the mapped source.

```csharp
Func<SaveAlbumArgs, int, CancellationToken, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, int, CancellationToken, Task<int>>>(
    method,
    CallerParameter<int>.Named("userId"),
    CallerParameter<CancellationToken>.ByType());
```

The first delegate argument has a fixed compile time source type. Runtime derived types do not switch the parameter source mapping.

## Unusable mapped value

```csharp
Func<SaveAlbumArgs, Task<int>> save = MethodCaller.Create<Func<SaveAlbumArgs, Task<int>>>(method);
```

If a target parameter is assigned to the mapped source but the current source value is not usable, invocation fails. It is not silently replaced by another source.

## Constructors

Constructor calls use the same parameter mapping and generated IL as method calls. The first argument supplies mapped members; later arguments can override constructor parameters.

```csharp
var construct = MethodCaller.CreateConstructor<Func<IRuntimeMemberAccess, long, User>>(CallerParameter<long>.Named(nameof(User.Id)));
User accepted = construct(item, returnedId);
```

`CreateConstructor<TDelegate>()` requires exactly one public constructor on the delegate return type. When there are several, select one explicitly with `MethodCaller.Create<TDelegate>(constructorInfo, callerParameters)`; it never guesses a constructor.

## Output parameters

A custom delegate can expose target `out` parameters directly. They use the same exact-type or named matching as other caller arguments. `CallerParameter<T>` describes the output element type. An exposed output must have a matching target output; unexposed target outputs are discarded. Target and caller `ref`/`in` parameters remain unsupported.

```csharp
delegate DbCommand InsertCommand(IRuntimeMemberAccess source, DbConnection connection, out DbParameter id);
var insert = MethodCaller.Create<InsertCommand>(DbCommands.InsertUser);
DbCommand command = insert(item, connection, out DbParameter id);
```

The output is the factory's actual parameter handle. Its value is read after executing the command.
