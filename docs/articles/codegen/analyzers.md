# Analyzers

The `Rinku` package ships a few Roslyn analyzers (category **Rinku**). Nothing to configure, and any of them can be tuned or disabled in `.editorconfig`.

```ini
dotnet_diagnostic.RK0100.severity = none
```

## Invoke completion (RK0002)

Inside a method body, referencing a method without calling it offers an **"Invoke {method}"** Quick Action. It turns the reference into a call and fills the arguments by matching each parameter, by name and type, against locals, fields, and properties in scope, threading anything unresolved through as a new parameter. Built for wiring up generated `DbCommand` methods, useful anywhere.

## Keeping generated types in sync

These back the [code generation](index.md) workflow, keeping a hand-written type aligned with its generated schema. They key off a generated `<Schema>` tag, so without one in the project they stay quiet.

- **RK0001** (hidden). Offers to add a `<BasedOn>` link from a type to a generated schema.
- **RK0000** (hidden). Powers the *"Update BasedOn timestamp"* and *"Generate constructor from ..."* Quick Actions on a linked type.
- **RK0100** (warning). A `<BasedOn>` type is older than the schema it came from, so it may have drifted. Regenerate, or apply the timestamp fix once reconciled.
