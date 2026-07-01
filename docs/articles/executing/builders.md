# Builders

*Per-call state you set in C#, for dynamic SQL and command reuse.*

A builder is the other way to supply a run's values. The convenient `cmd.Query<T>(cnn, paramObj)` reads them off an object; a builder lets you set them yourself in C#, useful when logic decides which optional ([conditional SQL](../conditional-sql/overview.md)) segments are active, or when you reuse one `DbCommand` across a batch. Both build the command and run it. A builder holds that state and is created from a [`QueryCommand`](query-command.md) with `StartBuilder()`.

There are two, for two needs.

## QueryBuilder, the single trip

A small struct for one database trip, when you are toggling conditions in code.

```csharp
var builder = SearchCmd.StartBuilder();
builder.Use("@minPrice", 0.99m);
if (onlyByArtist) builder.Use("@artistName", "Queen");   // condition decided in C#
var tracks = builder.Query<List<Track>>(cnn);
```

`Use` does two things. It marks the key active, keeping its template footprint, and stores the value. A required `@` variable that is recorded but never lands in the final SQL throws, which catches mistakes early. With static SQL there are no optional keys, so `Use` is just "here is a value." `Use` also returns a `bool` for whether the bind landed, so you can verify it.

```csharp
if (!builder.Use("@id", id)) throw new InvalidOperationException("No @id in this template");
```

## QueryBuilderCommand, reuse across calls

When you run the same command many times, a batch insert, a loop of lookups, rebuilding a `DbCommand` each time is wasteful. `QueryBuilderCommand<T>` keeps one alive and reconfigures only what changes.

```csharp
using var sqlCmd = new SqlCommand();
var builder = InsertPlaylist.StartBuilder(sqlCmd);

foreach (var name in names) {
    builder.Use("@name", name);
    builder.Execute(cnn);   // reuses the internal command
}
```

Its execution methods take **no** context parameters (except `ct` on async) because the builder owns and manages the `DbCommand`.

## One-step building

Instead of a series of `Use` calls, pass an object whose members carry the state. Members map to SQL variables by name. The convenient `Query<T>(cnn, obj)` call reads an object the same way.

```csharp
public record class TrackSearch(
    int? AlbumId,
    string? Name,
    [property: NotNullOrWhitespace] string? Composer)
{
    public int OtherField = 32;
    [ForBoolCond] public bool IncludeArtist;
}

var tracks = SearchCmd.Query<List<Track>>(cnn, new TrackSearch(1, "Black", "  ") { IncludeArtist = true });
```

How members map:

- **By name to variables.** `AlbumId` maps to `@AlbumId`.
- **To boolean conditions.** Mark a `bool` member `[ForBoolCond]` to drive a condition key (for example an `/*IncludeArtist*/` segment) instead of a parameter.
- **Usage/value attributes** (inheriting `AccessorEmiterHandler`) adjust *when* a member is used or *what* value is read.
  - `[NotNullOrWhitespace]` (strings) uses the value only when it is neither null nor whitespace.
  - `[NotDefault]` uses the value only when it is not the type's default.

The parameter-object overloads come in three shapes. `Query<T>(cnn, object?)` reads the object reflectively (anonymous objects welcome), `Query<T, TObj>(cnn, TObj)` avoids boxing, and a `ref` variant is there for large structs. You can also feed an object into a builder and then tweak before executing.

```csharp
var builder = SearchCmd.StartBuilder();   // or .StartBuilder(sqlCmd)
builder.UseWith(trackSearch);
builder.Use("IncludeArtist");
var tracks = builder.Query<IEnumerable<Track>>(cnn);
```
