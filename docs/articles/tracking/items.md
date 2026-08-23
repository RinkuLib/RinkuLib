# Editable items

## Accepted and edited values

```csharp
Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

string before = edit.Get<string>(nameof(Album.Title));

edit.Set(nameof(Album.Title), "Kind of Blue");
string after = edit.Get<string>(nameof(Album.Title));
```

Reading a member does not create edit state. Setting a tracked member creates it when needed.

## Edit state

```csharp
Console.WriteLine(edit.IsEditing);

edit.EnsureEditing();

Console.WriteLine(edit.IsEditing);
```

`EnsureEditing()` creates the edit snapshot without changing a member.

## Accepted original

```csharp
if (edit.TryGetOriginal(out Album accepted))
    Console.WriteLine(accepted.Title);
```

## Current differences

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

`GetChanges()` compares current edited values with accepted values. It is not assignment history.

Setting a value back removes that difference.

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");
edit.Set(nameof(Album.Title), "Blue");

bool changed = edit.HasChanges();
// false
```

## Member indexes

```csharp
int title = edit.GetIndex(nameof(Album.Title));

string value = edit.Get<string>(title);
edit.Set(title, "Kind of Blue");
```

Name access and index access target the same runtime member surface.

## Cancel

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");
edit.CancelEdit();

string title = edit.Get<string>(nameof(Album.Title));
// Blue
```

`CancelEdit()` discards the current snapshot.

## Confirm

```csharp
edit.Set(nameof(Album.Title), "Kind of Blue");
edit.ConfirmEdit();

if (edit.TryGetOriginal(out Album accepted))
    Console.WriteLine(accepted.Title);
// Kind of Blue
```

[Persistence](persistence.md)
