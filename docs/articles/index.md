# Documentation

RinkuLib is a micro-ORM made of four capabilities: mapping, conditional SQL, code generation, and tracking. Reach for one on its own or several together.

## Start here

- [Installation](getting-started/installation.md)
- [Quick start](getting-started/quick-start.md). From a SQL string to mapped objects.

## Running queries

- [Running queries](running-queries/index.md). Every way to run a command, by example.
- [Result shapes](running-queries/result-shapes.md). The type argument decides what you get back.
- [Supplying values](running-queries/parameters.md). Parameter objects and builders.
- [Multiple result sets](running-queries/multiple-results.md), [any DbCommand](running-queries/direct-dbcommand.md), [parameter metadata](running-queries/parameter-metadata.md).

## Mapping

How rows become objects.

- [Mapping](mapping/index.md), [objects and nesting](mapping/objects.md), [nullability](mapping/nullability.md), [DynaObject](mapping/dynaobject.md).
- [Registration](mapping/registration.md), [construction paths](mapping/construction-paths.md), [names](mapping/names.md), [reading order](mapping/reading-order.md), and [parsers](mapping/parsers.md), where you steer or replace the defaults.

## Conditional SQL

One template that adapts to the values you pass.

- [Conditional SQL](conditional-sql/index.md), [optional variables](conditional-sql/optional-variables.md), [conditional markers](conditional-sql/conditional-markers.md).
- [Dynamic projection](conditional-sql/dynamic-projection.md), [handlers](conditional-sql/handlers.md), and the [cheat sheet](conditional-sql/cheatsheet.md).

## More

- [Code generation](codegen/index.md). RinkuPowerTools generates ready-to-run `DbCommand`s at design time.
- [Tracking](tracking/index.md). Edit, commit, and revert over an `IEnumerable`.
- [Coming from Dapper](reference/dapper.md), [performance](reference/performance.md), [FAQ](reference/faq.md).
