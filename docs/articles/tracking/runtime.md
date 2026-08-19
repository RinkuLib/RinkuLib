# Runtime tracking

Runtime tracking generates an edit shape from `TOriginal` and caches the generated registration for reuse.

## Default dynamic shape

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

IRuntimeDynamicTrackingItem<Album> edit = new Album(12, "Blue").ToTrackingItem();

string title = edit.Get<string>(nameof(Album.Title));
edit.Set(nameof(Album.Title), "Kind of Blue");
```

`IRuntimeDynamicTrackingItem<TOriginal>` combines `IRuntimeTrackingItem<TOriginal>`, `INotifyPropertyChanged`, and `IRuntimeMemberAccess`. Member access is available by name or mapper index.

```csharp
int titleIndex = edit.GetIndex(nameof(Album.Title));
string title = edit.Get<string>(titleIndex);
edit.Set(titleIndex, "Kind of Blue");
```

## Configure the generated shape

`RuntimeTrackingOptions<TOriginal>` starts with the public members of the original type. Options can add or remove members, control dynamic access and notifications, expose members as query parameters, configure new-original creation, attach validation or metadata capabilities, and add custom type contributors.

The option tree freezes on first materialization. Use a new option tree for a different generated shape.

```csharp
var options = new RuntimeTrackingOptions<Album>();
options.Notifications();
options.DynamicAccess();
options.WithDefaultNewOriginal();

IRuntimeDynamicTrackingItem<Album> edit = new Album(12, "Blue").ToTrackingItem(options);
```

## Lists use the same registration

```csharp
TrackingList<IRuntimeDynamicTrackingItem<Album>> albums = originals.ToTrackingList(options);
```

Runtime list materialization uses the same generated registration for every row and configures binding metadata once for the list.
