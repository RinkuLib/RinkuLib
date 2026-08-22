# Refresh generated code

Refresh a configuration after its SQL or database shape changes.

![Rinku Power Tools Refresh all command](../../images/codegen/project-menu.png)
![Rinku Power Tools Refresh command](../../images/codegen/refresh-menu.png)

## Refresh one selected configuration

Select a `rinkupt.json` or named `rinkupt` configuration and use `Refresh`.

The generated file is opened after generation.

## Configure and regenerate

Use `Configure` on a selected `rinkupt` file when the connection, output settings, or query list also need to change.

`Configure` saves the selected configuration and regenerates it when the changes require generation.

## Refresh from the project

The project context menu groups the project commands under `Rinku Power Tools`.

`Refresh all` regenerates every CodeGen configuration in the project.

Each configuration is loaded and generated separately.

Refresh one configuration from its selected `rinkupt` file context menu. `Refresh all` is the project-level operation.


## Generation failures

One failing query does not stop the remaining query entries from being generated.

```csharp
#error Query generation failed for method 'GetBrokenAlbums'

// Other valid generated methods can still appear below this failure.
```

The generated error block includes the method, query source, target, and exception message.

Fix the query or connection metadata and refresh again, then build the project.

## Result records after a refresh

When the discovered result shape is unchanged, CodeGen keeps the existing generated record text. When the shape changes, the record is generated again from the new columns.

Keep application members in another partial declaration so regeneration never owns them.

```csharp
public partial record GetAlbumsResult(int Id, string Title);

public partial record GetAlbumsResult
{
    public string Display => Title;
}
```

See [Generated commands](generated-code.md) for generated records and command methods.
