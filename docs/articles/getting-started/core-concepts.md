# Core concepts

A call is made of two parts, the command and the run, plus a mapping step that stands apart from both. This page covers each.

## Two parts, the command and the run

```csharp
// 1. The command, built once. Parses the SQL, holds the caches, has no per-call state.
static readonly QueryCommand GetArtist =
    new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @id");

// 2. The run, per call. Supplies the connection and this call's values.
Artist a = GetArtist.Query<Artist>(cnn, new { id = 1 });
```

Because the command holds no per-call state, you build it once and share it for the life of the app. The things that change between calls, the connection and the parameter values, are passed to the run.

## The mapping stands on its own

Turning a result into your objects is a separate concern, with no dependence on `QueryCommand`. A parser, `ITypeParser<T>`, is essentially a `Func<DbDataReader, T>`. Give the engine the result columns and it builds the parser for `T`, which you can hold and reuse.

```csharp
ColumnInfo[] cols = reader.GetColumns();
ITypeParser<Artist> parser = TypeParser<Artist>.GetTypeParser(ref cols);
Artist a = parser.Parse(reader);
```

This is the piece everything else leans on. It is covered in full under [mapping](../mapping/index.md).

## A run is a wrapper over two jobs

With the parser in mind, a `QueryCommand` run is easy to describe. It does two things.

```text
1. build the DbCommand   (the template plus your values)
2. map the result        (the ITypeParser<T> for T)
```

You supply the values for job 1 in one of two ways, and they are peers. Neither is a shorthand for the other. Both build the command and then call the mapper.

```csharp
// an object, whose members are read for you
Artist a = GetArtist.Query<Artist>(cnn, new { id = 1 });

// a builder, whose values you set in C#
var b = GetArtist.StartBuilder();
b.Use("@id", 1);
Artist a2 = b.Query<Artist>(cnn);
```

Reach for the [builder](../executing/builders.md) when C# logic should decide what is active, or when you want to reuse one `DbCommand` across a batch. Otherwise the object form is the shorter one.

## Mapping a command you already have

Because job 2 is independent, you can hand it any `DbCommand`.

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = 1";
Artist c = cmd.Query<Artist>();
```

This is valid, but it is the slow path. With no parser in hand, the call has to read the result columns and derive the parser for that shape after the command runs, on every call.

```csharp
public T Query<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null);
```

When the shape is stable and the call is hot, pass a parser you built once, or a cache that holds one, and that per-call derivation goes away.

```csharp
Artist c = cmd.Query<Artist>(parser: artistParser);
```

A `QueryCommand` does this for you. It keeps the compiled parser keyed by result shape, so it looks the parser up instead of deriving it again. That is the payoff for defining the command once.

## Behavior lives in `T`, not in the method

`Query<T>` itself does very little. It runs the command and hands each result to the parser chosen for `T`. What happens on zero rows, many rows, or a `NULL` is decided by `T`. So you don't switch methods to change behavior, you ask for a different type.

```csharp
Artist one          = GetArtist.Query<Artist>(cnn, new { id = 1 });          // throws if absent
Optional<Artist> opt = GetArtist.Query<Optional<Artist>>(cnn, new { id = 1 }); // empty if absent
List<Artist> many    = GetArtists.Query<List<Artist>>(cnn);                   // all rows
```

`Optional<T>`, `List<T>`, `Single<T>` and the rest are not special methods, each is a small parser with its own rule for "no rows" and "many rows." You could write your own. See [choosing the result type](../mapping/simple-results.md).

## The capabilities are layers too

[Mapping](../mapping/index.md), [conditional SQL](../conditional-sql/overview.md), [code generation](../aot-codegen/overview.md), and [tracking](../tracking/overview.md) sit side by side. You pick the ones a task needs and ignore the rest. Conditional SQL produces a `DbCommand`. The mapping engine turns its rows into objects. Code generation can produce that `DbCommand` ahead of time instead. They meet at plain ADO.NET, so a choice in one doesn't lock the others.
