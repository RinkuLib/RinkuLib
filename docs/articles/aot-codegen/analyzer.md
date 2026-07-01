# Analyzer note

*What the bundled Roslyn analyzers do, and when they show up.*

The `Rinku` package ships a few analyzers (category **Rinku**) that light up on their own. They're mostly designed around the [code-generation](overview.md) workflow, though some can help outside it as well. You don't configure anything to get them, and you can tune or disable any of them in `.editorconfig`.

```ini
dotnet_diagnostic.RK0100.severity = none
```

## Invoke completion (RK0002)

Inside a method, constructor, or local function, if you reference a method without calling it, a hidden diagnostic offers an **"Invoke {method}"** Quick Action that turns the reference into a call and fills the arguments, matching each parameter by name and type against locals, fields, properties, and members in scope, and threading through anything it can't find as a new parameter. It was built for wiring up generated `DbCommand` methods, and it works just as well in everyday code.

## Keeping generated types in sync

These back the [PowerTools](power-tools.md) workflow, keeping a hand-written type aligned with its generated schema.

- **RK0001** (hidden). A type has no `<BasedOn>` link, offers to add one pointing at a generated schema.
- **RK0000** (hidden). Powers the *"Update BasedOn timestamp"* and *"Generate constructor from ..."* Quick Actions on a linked type.
- **RK0100** (warning). A `<BasedOn>` type is **older than the schema it came from** (the generated record's `<Schema LastUpdated>` is newer), so it may have drifted. Regenerate, or apply the timestamp fix once you have reconciled it.

They key off a generated schema, so without one in your project they stay quiet.
