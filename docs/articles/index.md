# Rinku documentation

Rinku is split into a few main areas. Start with the [overview](overview.md) for a tour through the library, or jump directly to the part you need.

## Main modules

- Running queries, [execute SQL](running-queries/execution.md), [supply values](running-queries/values.md), [build from application logic](running-queries/builders.md), [choose result shapes](running-queries/result-shapes.md), [async execution](running-queries/async.md) and [streaming](running-queries/streaming.md)
- Mapping, [map rows to objects](mapping/objects.md), [choose construction paths](mapping/construction-paths.md), [map nested objects](mapping/nesting.md), [fill collections](mapping/collections.md), [adapt names](mapping/names.md) and [handle database NULL](mapping/nulls.md)
- Conditional SQL, [conditional variables](conditional-sql/variables.md), [collection expansion](conditional-sql/collections.md), [conditional markers](conditional-sql/markers.md), [dynamic projection](conditional-sql/dynamic-projection.md) and the [cheat sheet](conditional-sql/cheatsheet.md)
- Advanced customization, [overview](customization/index.md), [type registrations and defaults](customization/type-registration.md), [complete-result parsers](customization/result-parsers.md), [parameter binding](customization/parameters.md) and [cache ownership](customization/caches.md)
- Code generation, [RinkuPowerTools](codegen/index.md), [configuration](codegen/configuration.md) and [analyzers](codegen/analyzers.md)
- Tracking, [overview](tracking/index.md), [tracking items](tracking/items.md), [tracking lists](tracking/lists.md), [runtime tracking](tracking/runtime.md) and [validation and metadata](tracking/validation.md)

## Coming from Dapper

The [Coming from Dapper](reference/dapper.md) guide puts common Dapper operations next to their Rinku equivalents.

## Reference

Use [performance](reference/performance.md), [errors](reference/errors.md), the [FAQ](reference/faq.md) and the [API reference](../api/index.md) when you need details rather than a guided tour.
