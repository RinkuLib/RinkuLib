# Editable items

A runtime tracking item reads accepted values until a member is changed.

```csharp
Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

string before = edit.Get<string>(nameof(Album.Title));

edit.Set(nameof(Album.Title), "Kind of Blue");

string after = edit.Get<string>(nameof(Album.Title));
```

Reading a member does not start an edit. Setting a tracked member creates edit state when needed.

## Check edit state

```csharp
Console.WriteLine(edit.IsEditing);

edit.EnsureEditing();

Console.WriteLine(edit.IsEditing);
```

`EnsureEditing()` creates the edit snapshot without changing a member.

## Read the original

```csharp
if (edit.TryGetOriginal(out Album accepted))
    Console.WriteLine(accepted.Title);
```

The original represents the accepted value. It is separate from the current edit snapshot.

## Inspect changed members

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
{
    Console.WriteLine(change.Name);
    Console.WriteLine(change.OriginalValue);
    Console.WriteLine(change.Value);
}
```

Only members whose current value differs from the accepted value are returned.

Use `HasChanges()` when only the presence of a change matters.

```csharp
bool saveEnabled = edit.HasChanges();
```

## Use member indexes

Resolve a name once when the same runtime member is read repeatedly.

```csharp
int title = edit.GetIndex(nameof(Album.Title));

string value = edit.Get<string>(title);
edit.Set(title, "Kind of Blue");
```

Name access and index access use the same runtime member surface.

## Cancel an edit

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");
edit.CancelEdit();

string title = edit.Get<string>(nameof(Album.Title));
// Blue
```

`CancelEdit()` discards the current snapshot and returns reads to the accepted value.

## Confirm an edit

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");
edit.ConfirmEdit();

if (edit.TryGetOriginal(out Album accepted))
    Console.WriteLine(accepted.Title);
```

`ConfirmEdit()` accepts the current edit. Confirm after the application has accepted the change, such as after a successful database update.

See [persistence](persistence.md) for database examples.
