# Editable items

*Wrapping a value with a lazy editable copy.*

An editable item tracks an **original** value and supports a lazily-created **editable** copy of it. Two ready-made implementations exist.

- **`EditableClass<T>`** for reference types (`where T : class`).
- **`EditableStruct<T>`** for value types (`where T : struct`).

```csharp
var item  = EditableClass<Playlist>.FromOriginal(playlist);   // start from an existing value
var blank = EditableClass<Playlist>.CreateNew(newPlaylist);   // start directly in the edit state

if (item.EnsureIsEditing(out Playlist editable)) {
    editable.Name = "Updated";
}

bool dirty = item.HasChanges;
item.CommitEdit();   // edit value becomes the new original
// or
item.CancelEdit();   // discard the edit, keep the original
```

## The contracts

Editable items implement small interfaces so they compose with the [tracking lists](tracking-lists.md).

- **`ITrackingItem<T>`**, with `TryReattach(T value)` (claim a matching value as the original) and `HasOriginal(out T original)`.
- **`IEditableItem<T>`**, with `CurrentValue`, `EditableValue`, `EnsureIsEditing(out editable)`, `IsEditing`, `HasChanges`, `CommitEdit()` / `CommitEditAsync()`, and `CancelEdit()` / `CancelEditAsync()`.

`CurrentValue` returns the value as it stands, the edit value while editing, otherwise the original, with no mutability guarantee. Use `EditableValue` or `EnsureIsEditing` when you intend to modify it. The factory methods `FromOriginal` and `CreateNew` come from the `IEditableItemFromOriginal<,>` and `IEditableItemFromEdit<,>` interfaces, which is how the lists build items generically. A metadata-carrying variant (`EditableClass<T, TMetadata>` and `EditableStruct<T, TMetadata>`) also implements `IMetadata<TMetadata>` for validation results.
