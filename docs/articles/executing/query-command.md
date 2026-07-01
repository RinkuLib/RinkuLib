# The QueryCommand

*The long-lived blueprint that holds every variation of a command.*

A `QueryCommand` is created **once** from a SQL string and reused for the life of the app. Parsing and cache setup happen at construction, and it holds no per-call data, so put it in a `static readonly` field and share it.

```csharp
static readonly QueryCommand TrackCmd =
    new("SELECT * FROM tracks WHERE TrackId = ?@id AND UnitPrice > ?@minPrice");
```

## What it caches

- **`Mapper`.** Maps each parameter or condition name (`@id`, `IncludeArtist`) to a stable integer index. See [key mapping](#key-mapping).
- **`QueryText`.** The logic that builds the SQL string, with conditions referring to `Mapper` indices.
- **`QueryParameters`.** A `DbParamInfo` per variable, describing how to create each `DbParameter`. See [parameter specialization](parameter-specialization.md).
- **Parser cache.** Compiled row-to-object readers keyed by schema. See [below](#the-parser-cache).
- **Type-accessor cache.** Compiled readers that pull values from a [parameter object](builders.md#one-step-building) to build the command.

A `QueryCommand` is safe to share across threads. The blueprint is immutable, the lazy caches are guarded by shared static locks, and the read path does not lock. Per-call state lives in the builder or the parameter object, so the pattern is one shared command and fresh per-call state.

## Building the DbCommand

When you execute, the command takes the state a [builder](builders.md) or parameter object supplies and configures a `DbCommand` in two steps.

1. **Parameter injection.** For each active index, the matching `DbParamInfo` creates a correctly-typed `DbParameter`.
2. **SQL generation.** `QueryText` plus the state decide which segments to keep, producing the final SQL.

The result is a ready-to-run `DbCommand` with no string building on your part.

## The parser cache

The parser cache turns reader rows into objects. It is **lazy**, so it only fills when a result is actually parsed.

- For `INSERT`, `UPDATE`, and `DELETE` there is no result schema, so it is skipped and negotiation never runs.
- On the first returning call, the engine builds an IL reader for that schema and stores it.
- **Fixed projection** (the default) holds **one** reader, assuming a stable schema. **Dynamic projection** (a template flagged via [`?SELECT`](../conditional-sql/dynamic-projection.md)) holds **several**, one per unique schema.

## Key mapping

Compilation produces a `Mapper` that records every condition key once and assigns it a stable index (all comparisons are case-insensitive). Builders use the `Mapper` to place values in the right slots. Keys register in a fixed order.

1. Variables (required and optional)
2. Special-handler variables
3. Base-handler variables
4. Comment-based conditions without a variable (`/*Key*/`)

A `/*@Var*/` marker does **not** add a separate key. `@Var` is already registered as a variable.
